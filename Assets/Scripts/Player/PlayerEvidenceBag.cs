using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UrquhartsShadow.Core;

namespace UrquhartsShadow.Player
{
    /// <summary>
    /// Evidence captured by this player but not yet deposited in the main-deck locker.
    /// Kept on the server (authoritative) and mirrored as a count for the HUD. Going overboard wipes it.
    /// </summary>
    public class PlayerEvidenceBag : NetworkBehaviour
    {
        public readonly NetworkVariable<int> PendingCount = new NetworkVariable<int>(0);
        private readonly List<PendingEvidence> _pending = new List<PendingEvidence>();

        public event Action<PendingEvidence> Captured;
        public event Action Lost;

        public IReadOnlyList<PendingEvidence> Pending => _pending;

        /// <summary>Server: add a capture. Tools call this after validating the capture on the server.</summary>
        public void AddServer(PendingEvidence e)
        {
            if (!IsServer) return;
            _pending.Add(e);
            PendingCount.Value = _pending.Count;
            var gm = GameManager.Instance;
            if (gm != null) gm.Stats.For(OwnerClientId).EvidenceCaptured++;
            CapturedRpc(e.Type, e.Quality, e.Label ?? string.Empty, RpcTarget.Owner);
        }

        [Rpc(SendTo.SpecifiedInParams)]
        private void CapturedRpc(EvidenceType type, float quality, string label, RpcParams p)
        {
            Captured?.Invoke(new PendingEvidence(type, quality, label, 0f));
        }

        /// <summary>Server: deposit everything into the locker. Returns how many were accepted.</summary>
        public int DepositAllServer(EvidenceManager locker)
        {
            if (!IsServer || locker == null) return 0;
            int accepted = 0;
            foreach (var e in _pending)
                if (locker.TrySave(e, OwnerClientId, out _)) accepted++;
            int rejected = _pending.Count - accepted;
            if (GameManager.Instance != null) GameManager.Instance.Stats.EvidenceLost += rejected;
            _pending.Clear();
            PendingCount.Value = 0;
            return accepted;
        }

        [Rpc(SendTo.Server)]
        public void LoseAllRpc()
        {
            if (_pending.Count == 0) return;
            if (GameManager.Instance != null) GameManager.Instance.Stats.EvidenceLost += _pending.Count;
            _pending.Clear();
            PendingCount.Value = 0;
            LostRpc(RpcTarget.Owner);
        }

        [Rpc(SendTo.SpecifiedInParams)]
        private void LostRpc(RpcParams p) => Lost?.Invoke();
    }
}
