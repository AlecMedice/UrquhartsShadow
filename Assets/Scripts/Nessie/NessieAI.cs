using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UrquhartsShadow.Boat;
using UrquhartsShadow.Config;
using UrquhartsShadow.Core;
using UrquhartsShadow.Player;

namespace UrquhartsShadow.Nessie
{
    /// <summary>
    /// The monster's brain. Server-authoritative state machine driven by a NessieDifficultyProfile.
    /// Other systems talk to it only through the public "On..." hooks; it never reaches into UI.
    /// Prefab: NessieAI + NessieBody + NetworkObject + NetworkTransform (server authority) + Animator + colliders.
    /// </summary>
    [RequireComponent(typeof(NessieBody))]
    public class NessieAI : NetworkBehaviour
    {
        private static NessieAI _instance;
        /// <summary>Returns a real null when the instance was destroyed (a stale static after Play, a scene unload).</summary>
        public static NessieAI Instance { get => _instance != null ? _instance : null; private set => _instance = value; }

        [Tooltip("Optional override; otherwise GameManager's selected difficulty profile is used.")]
        [SerializeField] private NessieDifficultyProfile profileOverride;
        [Tooltip("Debug: log state transitions.")]
        [SerializeField] private bool verbose;

        public readonly NetworkVariable<int> StateIdNet = new NetworkVariable<int>((int)NessieStateId.Dormant);

        public NessieBody Body { get; private set; }
        public NessieSenses Senses { get; private set; }
        public NessieDifficultyProfile Profile { get; private set; }
        public ResearchVessel Vessel => GameManager.Instance != null ? GameManager.Instance.Vessel : null;
        public Vector3 VesselPosition => Vessel != null ? Vessel.transform.position : Vector3.zero;
        public NessieStateId CurrentStateId => (NessieStateId)StateIdNet.Value;

        public event Action<NessieStateId, NessieStateId> StateChanged;

        private readonly Dictionary<NessieStateId, NessieState> _states = new Dictionary<NessieStateId, NessieState>();
        private NessieState _current;
        private float _nextBreachTime;
        private float _lastRamTime = -999f;
        private float _lastDeckStrikeTime = -999f;
        private float _nextDecisionTime;
        private bool _active;

        /// <summary>Aggression after night and evidence escalation, clamped 0..1.</summary>
        public float EffectiveAggression => Mathf.Clamp01((Profile != null ? Profile.Aggression : 0.4f) + (GameManager.Instance != null ? GameManager.Instance.NightAggressionBonus : 0f));
        public bool CanRam => Time.time - _lastRamTime > Profile.RamCooldownSeconds && Vessel != null;
        public bool CanDeckStrike => Time.time - _lastDeckStrikeTime > Profile.DeckStrikeCooldownSeconds && Vessel != null;

        private void Awake()
        {
            Instance = this;
            Body = GetComponent<NessieBody>();
            Senses = new NessieSenses(this);
        }

        public override void OnNetworkSpawn()
        {
            GameManager.Instance?.Register(this);
            Profile = profileOverride != null ? profileOverride : (GameManager.Instance != null ? GameManager.Instance.Profile : NessieDifficultyProfile.CreatePreset(DifficultyLevel.Wary));
            if (Profile == null) Profile = NessieDifficultyProfile.CreatePreset(DifficultyLevel.Wary);

            if (!IsServer) return;
            Register(new DormantState()); Register(new LurkState()); Register(new StalkState());
            Register(new InvestigateState()); Register(new SabotageState()); Register(new BreachState());
            Register(new DeckStrikeState()); Register(new RamState()); Register(new RetreatState()); Register(new FinaleState());
            ChangeState(NessieStateId.Dormant);
        }

        private void Register(NessieState s) { s.Init(this); _states[s.Id] = s; }

        private void Update()
        {
            if (!IsServer || _current == null) return;
            float dt = Time.deltaTime;
            Senses.Tick();

            // Grace period each night: stay dormant until it ends.
            var nc = GameManager.Instance?.NightCycle;
            if (_active && _current.Id == NessieStateId.Dormant && nc != null && !nc.GraceActive.Value && GameManager.Instance.IsNightActive)
                ChangeState(NessieStateId.Lurk);

            // Global interrupts (not during the finale).
            if (_current.Id != NessieStateId.Finale && _current.Id != NessieStateId.Dormant)
            {
                if (Senses.FlashedRecently(0.2f) && _current.Id != NessieStateId.Retreat && Body.IsNearSurface)
                { ChangeState(NessieStateId.Retreat, Profile.RetreatSecondsAfterFlash); }

                // A stealthy Nessie steers out of live sonar cones unless she is mid-attack.
                if (Profile.Stealth > 0.5f && (_current.Id == NessieStateId.Lurk || _current.Id == NessieStateId.Stalk)
                    && Senses.IsInsideActiveSonar(transform.position, out var ping) && UnityEngine.Random.value < Profile.Stealth * dt)
                {
                    ChangeState(NessieStateId.Retreat, 6f);
                }

                // Scheduled voluntary breach (the main way the team gets photos).
                if (Time.time >= _nextBreachTime && (_current.Id == NessieStateId.Lurk || _current.Id == NessieStateId.Stalk))
                {
                    ScheduleNextBreach();
                    ChangeState(NessieStateId.Breach);
                }
            }

            _current.Tick(dt);
        }

        /// <summary>Called by LurkState: choose what to do based on stimuli, targets and temperament.</summary>
        public void Decide()
        {
            if (Time.time < _nextDecisionTime) return;
            _nextDecisionTime = Time.time + Profile.ReactionSeconds;

            var stim = Senses.MostInteresting();
            if (stim.HasValue)
            {
                var inv = (InvestigateState)_states[NessieStateId.Investigate];
                inv.Target = stim.Value.Position; inv.Kind = stim.Value.Kind;
                ChangeState(NessieStateId.Investigate);
                return;
            }
            float r = UnityEngine.Random.value;
            if (r < Profile.SabotageDrive && Senses.ChooseSabotageTarget() != null) { ChangeState(NessieStateId.Sabotage); return; }
            if (r < Profile.SabotageDrive + EffectiveAggression * 0.7f) { ChangeState(NessieStateId.Stalk); return; }
            // Otherwise keep lurking.
        }

        public void ChangeState(NessieStateId id, float retreatDuration = -1f)
        {
            if (!IsServer || !_states.TryGetValue(id, out var next)) return;
            if (id == NessieStateId.Retreat && retreatDuration > 0f) ((RetreatState)next).Duration = retreatDuration;
            var prev = _current;
            prev?.Exit();
            _current = next;
            StateIdNet.Value = (int)id;
            _current.Enter();
            if (verbose) Debug.Log($"[Nessie] {(prev != null ? prev.Id.ToString() : "-")} -> {id}");
            StateChanged?.Invoke(prev != null ? prev.Id : NessieStateId.Dormant, id);
        }

        private void ScheduleNextBreach()
        {
            float min = Profile.BreachIntervalMin, max = Profile.BreachIntervalMax;
            // More evidence saved => she gets warier and breaches less.
            float scale = 1f + 0.1f * (GameManager.Instance?.Evidence?.SavedCount ?? 0);
            _nextBreachTime = Time.time + UnityEngine.Random.Range(min, max) * scale;
        }

        // ---------------- Hooks from GameManager ----------------
        public void OnNightBegin(int night)
        {
            if (!IsServer) return;
            _active = true;
            ScheduleNextBreach();
            _nextBreachTime = Mathf.Max(_nextBreachTime, Time.time + GameConfig.Instance.Night.GracePeriodSeconds + 20f);
            ChangeState(NessieStateId.Dormant);
            Debug.Log($"[Nessie] Night {night}: aggression {EffectiveAggression:0.00} ({Profile.Level})");
        }

        public void OnNightEnd() { if (IsServer) { _active = false; ChangeState(NessieStateId.Dormant); } }
        public void SetDormant() { if (IsServer) { _active = false; ChangeState(NessieStateId.Dormant); } }
        public void BeginFinale() { if (IsServer) { _active = true; ChangeState(NessieStateId.Finale); } }
        public void OnEvidenceSaved(int total) { /* aggression escalation is read live via EffectiveAggression */ }

        // ---------------- Hooks from the world (server only) ----------------
        public void OnSonarPing(Vector3 origin, float range)
        {
            if (!IsServer) return;
            if (Vector3.Distance(origin, transform.position) <= Profile.SonarSensitivityRange)
                Senses.Push(NessieSenses.StimulusKind.SonarPing, origin, range);
        }

        public void OnNoise(Vector3 origin, float loudness)
        {
            if (!IsServer) return;
            if (Vector3.Distance(origin, transform.position) <= Profile.HearingRange * loudness)
                Senses.Push(NessieSenses.StimulusKind.Noise, origin, loudness);
        }

        public void OnLightSource(Vector3 origin, float intensity)
        {
            if (!IsServer) return;
            if (Vector3.Distance(origin, transform.position) <= Profile.SightRangeSurface)
                Senses.Push(NessieSenses.StimulusKind.Light, origin, intensity);
        }

        /// <summary>A very bright flash (lightning, 35mm flash, ROV floods). Makes her dive.</summary>
        public void OnBrightFlash(Vector3 origin, float radius)
        {
            if (!IsServer) return;
            if (Vector3.Distance(origin, transform.position) <= radius)
                Senses.Push(NessieSenses.StimulusKind.Flash, origin, 2f);
        }

        public void OnPlayerInWater(PlayerCharacter p)
        {
            if (!IsServer || p == null) return;
            Senses.Push(NessieSenses.StimulusKind.Splash, p.transform.position, 1f);
        }

        /// <summary>Cameras call this while their frustum contains Nessie so a cunning one can react.</summary>
        public void OnCameraPointedAtMe(Vector3 cameraPos)
        {
            if (!IsServer) return;
            Senses.Push(NessieSenses.StimulusKind.CameraOnMe, cameraPos, 1f);
        }

        /// <summary>A piece of evidence of her was captured: retreat to make the team work for the next one.</summary>
        public void OnEvidenceCaptured(EvidenceType type)
        {
            if (!IsServer) return;
            if (_current.Id == NessieStateId.Finale || _current.Id == NessieStateId.Dormant) return;
            if (type == EvidenceType.TelephotoPhoto || type == EvidenceType.PhoneVideo || type == EvidenceType.RovFootage)
                ChangeState(NessieStateId.Retreat, Profile.RetreatSecondsAfterCapture);
        }

        public void RegisterTarget(INessieTarget t) { if (IsServer) Senses.RegisterTarget(t); }
        public void UnregisterTarget(INessieTarget t) { if (IsServer) Senses.UnregisterTarget(t); }

        // ---------------- Bookkeeping from states ----------------
        public void RegisterBreach() { if (GameManager.Instance != null) GameManager.Instance.Stats.NessieBreaches++; }
        public void MarkRam() => _lastRamTime = Time.time;
        public void MarkDeckStrike() => _lastDeckStrikeTime = Time.time;

        /// <summary>Hydrophones listen for this; the audio itself plays via NessieBody.</summary>
        public event Action<Vector3, float> HydrophoneCall;
        public void EmitHydrophoneCall() => HydrophoneCall?.Invoke(transform.position, Time.time);

        /// <summary>Recent trail for the eDNA sampler: positions with timestamps.</summary>
        private readonly List<(Vector3 pos, float time)> _trail = new List<(Vector3, float)>();
        private float _nextTrailSample;
        private void LateUpdate()
        {
            if (!IsServer || Time.time < _nextTrailSample) return;
            _nextTrailSample = Time.time + 5f;
            _trail.Add((transform.position, Time.time));
            float life = GameConfig.Instance.Tools.EdnaTraceLifetimeSeconds;
            _trail.RemoveAll(t => Time.time - t.time > life);
        }
        public bool HasRecentTraceNear(Vector3 p, float radius)
        {
            foreach (var t in _trail) if (Vector3.Distance(t.pos, p) <= radius) return true;
            return Vector3.Distance(transform.position, p) <= radius;
        }

        public PlayerCharacter NearestSwimmer(Vector3 from, float range)
        {
            PlayerCharacter best = null; float bd = range;
            if (GameManager.Instance == null) return null;
            foreach (var p in GameManager.Instance.AlivePlayers())
            {
                if (!p.Vitals.InWater.Value) continue;
                float d = Vector3.Distance(from, p.transform.position);
                if (d < bd) { bd = d; best = p; }
            }
            return best;
        }
    }
}
