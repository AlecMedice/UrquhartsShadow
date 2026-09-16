using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UrquhartsShadow.Config;

namespace UrquhartsShadow.Core
{
    /// <summary>
    /// The team's evidence locker. Captures are first "pending" on the player who made them
    /// (see PlayerEvidenceBag) and only count once deposited here via the EvidenceLocker on the
    /// main deck. Server validates quality, caps per type, awards funding and tells GameManager.
    /// </summary>
    public class EvidenceManager : NetworkBehaviour
    {
        public NetworkList<EvidenceRecord> Saved;
        public readonly NetworkVariable<int> Funding = new NetworkVariable<int>(0);

        public event Action<EvidenceRecord> EvidenceSavedEvent;
        public event Action<EvidenceType, string> EvidenceRejectedEvent;

        private int _nextId = 1;

        public int SavedCount => Saved != null ? Saved.Count : 0;
        public int Remaining => Mathf.Max(0, GameConstants.EvidenceToWin - SavedCount);

        private void Awake()
        {
            Saved = new NetworkList<EvidenceRecord>();
        }

        public override void OnNetworkSpawn()
        {
            GameManager.Instance?.Register(this);
            if (IsServer) Funding.Value = GameConfig.Instance.Supplies.StartingFunding;
            Saved.OnListChanged += OnListChanged;
        }

        public override void OnNetworkDespawn()
        {
            Saved.OnListChanged -= OnListChanged;
        }

        private void OnListChanged(NetworkListEvent<EvidenceRecord> e)
        {
            if (e.Type == NetworkListEvent<EvidenceRecord>.EventType.Add)
                EvidenceSavedEvent?.Invoke(e.Value);
        }

        public int CountOfType(EvidenceType t)
        {
            int n = 0;
            foreach (var r in Saved) if (r.Type == t) n++;
            return n;
        }

        /// <summary>Server-side validation and commit. Returns false (with reason) when rejected.</summary>
        public bool TrySave(PendingEvidence pending, ulong clientId, out string reason)
        {
            reason = null;
            if (!IsServer) { reason = "Not server"; return false; }
            var cfg = GameConfig.Instance.Evidence;

            if (pending.Quality < cfg.MinimumQuality)
            {
                reason = "Too blurry / inconclusive";
                RejectRpc(pending.Type, reason, RpcTarget.Single(clientId, RpcTargetUse.Temp));
                return false;
            }
            if (cfg.MaxPerType > 0 && CountOfType(pending.Type) >= cfg.MaxPerType)
            {
                reason = $"Sponsors already have enough {Describe(pending.Type)}";
                RejectRpc(pending.Type, reason, RpcTarget.Single(clientId, RpcTargetUse.Temp));
                return false;
            }

            var gm = GameManager.Instance;
            var rec = new EvidenceRecord
            {
                Id = _nextId++,
                Type = pending.Type,
                Quality = Mathf.Clamp01(pending.Quality),
                Night = gm != null ? gm.CurrentNight.Value : 0,
                NightTimeSeconds = pending.CapturedAtNightTime,
                CapturedByClientId = clientId,
                Label = string.IsNullOrEmpty(pending.Label) ? Describe(pending.Type) : pending.Label
            };
            Saved.Add(rec);
            Funding.Value += GameConfig.Instance.Supplies.FundingPerEvidence;

            if (gm != null)
            {
                gm.Stats.CountEvidence(rec.Type);
                gm.Stats.For(clientId).EvidenceSaved++;
                gm.OnEvidenceSaved(SavedCount);
            }
            Debug.Log($"[Evidence] Saved {rec}. Total {SavedCount}/{GameConstants.EvidenceToWin}");
            return true;
        }

        [Rpc(SendTo.SpecifiedInParams)]
        private void RejectRpc(EvidenceType type, string reason, RpcParams rpcParams)
        {
            EvidenceRejectedEvent?.Invoke(type, reason);
        }

        public bool TrySpendFunding(int amount)
        {
            if (!IsServer || Funding.Value < amount) return false;
            Funding.Value -= amount;
            if (GameManager.Instance != null) GameManager.Instance.Stats.FundingSpent += amount;
            return true;
        }

        public static string Describe(EvidenceType t) => t switch
        {
            EvidenceType.SonarContact => "sonar contacts",
            EvidenceType.HydrophoneCall => "hydrophone recordings",
            EvidenceType.TelephotoPhoto => "35mm photographs",
            EvidenceType.PhoneVideo => "phone footage",
            EvidenceType.EdnaSample => "eDNA samples",
            EvidenceType.RovFootage => "ROV footage",
            _ => "evidence"
        };
    }
}
