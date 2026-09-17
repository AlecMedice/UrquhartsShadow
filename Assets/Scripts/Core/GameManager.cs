using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UrquhartsShadow.Boat;
using UrquhartsShadow.Config;
using UrquhartsShadow.Nessie;
using UrquhartsShadow.Networking;
using UrquhartsShadow.Player;

namespace UrquhartsShadow.Core
{
    /// <summary>
    /// Server-authoritative match state machine. Owns the phase, the night counter and the
    /// win/lose evaluation. Its static Instance is set in Awake so it
    /// spans the match; it is an in-scene NetworkObject in the LochNess scene, and the other scene systems (NightCycle, Weather, Evidence, Vessel, Nessie)
    /// register themselves with it when they spawn.
    /// </summary>
    public class GameManager : NetworkBehaviour
    {
        private static GameManager _instance;
        /// <summary>Returns a real null when the instance was destroyed (a stale static after Play, a scene unload).</summary>
        public static GameManager Instance { get => _instance != null ? _instance : null; private set => _instance = value; }

        public readonly NetworkVariable<GamePhase> Phase = new NetworkVariable<GamePhase>(GamePhase.Title);
        public readonly NetworkVariable<int> CurrentNight = new NetworkVariable<int>(0);
        public readonly NetworkVariable<int> DifficultyIndex = new NetworkVariable<int>((int)DifficultyLevel.Wary);

        public event Action<GamePhase> PhaseChanged;
        public event Action<int> NightStarted;
        public event Action<MatchSummary> MatchEnded;

        public MatchStats Stats { get; private set; } = new MatchStats();
        public DifficultyLevel Difficulty => (DifficultyLevel)DifficultyIndex.Value;
        public NessieDifficultyProfile Profile { get; private set; }

        // Scene-level systems register on spawn (server and clients).
        public NightCycleManager NightCycle { get; private set; }
        public EvidenceManager Evidence { get; private set; }
        public WeatherManager Weather { get; private set; }
        public ResearchVessel Vessel { get; private set; }
        public NessieAI Nessie { get; private set; }

        private readonly Dictionary<ulong, PlayerCharacter> _players = new Dictionary<ulong, PlayerCharacter>();
        public IReadOnlyDictionary<ulong, PlayerCharacter> Players => _players;

        private float _matchStartTime;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        public override void OnNetworkSpawn()
        {
            Phase.OnValueChanged += (_, p) => PhaseChanged?.Invoke(p);
            CurrentNight.OnValueChanged += (_, n) => { if (n > 0) NightStarted?.Invoke(n); };
            if (IsServer)
            {
                DifficultyIndex.Value = PlayerPrefs.GetInt(GameConstants.PrefDifficulty, (int)DifficultyLevel.Wary);
                Profile = NessieDifficultyProfile.Load(Difficulty);
                Stats = new MatchStats { Difficulty = Difficulty.ToString() };
            }
            else
            {
                Profile = NessieDifficultyProfile.Load(Difficulty);
            }
        }

        // ---------------- Registration ----------------
        public void Register(NightCycleManager m) => NightCycle = m;
        public void Register(EvidenceManager m) => Evidence = m;
        public void Register(WeatherManager m) => Weather = m;
        public void Register(ResearchVessel v) => Vessel = v;
        public void Register(NessieAI n) => Nessie = n;
        public void RegisterPlayer(PlayerCharacter p) => _players[p.OwnerClientId] = p;
        public void UnregisterPlayer(PlayerCharacter p) => _players.Remove(p.OwnerClientId);

        // ---------------- Flow (server only) ----------------

        /// <summary>Called by SessionManager once the loch scene has finished loading on the host.</summary>
        public void BeginExpedition()
        {
            if (!IsServer) return;
            _matchStartTime = Time.time;
            Stats = new MatchStats { Difficulty = Difficulty.ToString() };
            CurrentNight.Value = 0;
            StartNextNight();
        }

        public void StartNextNight()
        {
            if (!IsServer) return;
            CurrentNight.Value += 1;
            SetPhase(GamePhase.NightSearch);
            NightCycle?.BeginNight(CurrentNight.Value);
            Weather?.RollForNight(CurrentNight.Value);
            Nessie?.OnNightBegin(CurrentNight.Value);
            Debug.Log($"[GameManager] Night {CurrentNight.Value} begins.");
        }

        /// <summary>NightCycleManager calls this when the night timer expires.</summary>
        public void OnNightEnded()
        {
            if (!IsServer) return;
            Stats.NightsSurvived = CurrentNight.Value;
            Nessie?.OnNightEnd();

            if (CurrentNight.Value >= GameConstants.TotalNights)
            {
                // Night 5 ended without 10 pieces: the finale.
                BeginFinale();
                return;
            }

            SetPhase(GamePhase.Dawn);
            NightCycle?.BeginDawn();
            Vessel?.OnDawnResupply();
            foreach (var p in _players.Values) p.OnDawn();
        }

        /// <summary>NightCycleManager calls this when the dawn breather ends.</summary>
        public void OnDawnEnded()
        {
            if (!IsServer) return;
            StartNextNight();
        }

        /// <summary>EvidenceManager calls this whenever the saved count changes.</summary>
        public void OnEvidenceSaved(int total)
        {
            if (!IsServer) return;
            Nessie?.OnEvidenceSaved(total);
            if (total >= GameConstants.EvidenceToWin && Phase.Value != GamePhase.Victory)
                Win();
        }

        /// <summary>ResearchVessel calls this if the hull hits zero integrity before the finale.</summary>
        public void OnVesselSunk()
        {
            if (!IsServer) return;
            if (Phase.Value == GamePhase.Victory || Phase.Value == GamePhase.Defeat) return;
            Lose();
        }

        private void Win()
        {
            Stats.Won = true;
            SetPhase(GamePhase.Victory);
            Nessie?.SetDormant();
            EndMatch();
        }

        private void BeginFinale()
        {
            SetPhase(GamePhase.Finale);
            Nessie?.BeginFinale();
            Vessel?.BeginSinkingSequence(GameConfig.Instance.Night.FinaleDurationSeconds);
            Invoke(nameof(Lose), GameConfig.Instance.Night.FinaleDurationSeconds);
        }

        private void Lose()
        {
            Stats.Won = false;
            SetPhase(GamePhase.Defeat);
            EndMatch();
        }

        private void EndMatch()
        {
            Stats.TotalSeconds = Time.time - _matchStartTime;
            var summary = MatchSummary.From(Stats);
            summary.DifficultyLevel = DifficultyIndex.Value;
            BroadcastMatchEndRpc(summary);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void BroadcastMatchEndRpc(MatchSummary summary)
        {
            MatchEnded?.Invoke(summary);
            SessionManager.Instance?.LoadEndingScene(summary);
        }

        private void SetPhase(GamePhase p)
        {
            if (Phase.Value == p) return;
            Phase.Value = p;
        }

        // ---------------- Helpers ----------------
        public bool IsNightActive => Phase.Value == GamePhase.NightSearch || Phase.Value == GamePhase.Finale;

        public float NightAggressionBonus =>
            Profile == null ? 0f :
            Profile.AggressionPerNight * Mathf.Max(0, CurrentNight.Value - 1)
            + Profile.AggressionPerEvidence * (Evidence != null ? Evidence.SavedCount : 0);

        public IEnumerable<PlayerCharacter> AlivePlayers()
        {
            foreach (var p in _players.Values)
                if (p != null && p.Vitals != null && p.Vitals.IsAlive) yield return p;
        }
    }
}
