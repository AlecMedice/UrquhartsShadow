using Unity.Netcode;
using UnityEngine;
using UrquhartsShadow.Config;
using UrquhartsShadow.Core;
using UrquhartsShadow.Player;

namespace UrquhartsShadow.Boat
{
    /// <summary>Hull patch point below decks. Hold Interact to shore up the hull; interrupts if you let go.</summary>
    public class RepairPoint : NetworkBehaviour, IInteractable
    {
        public float HoldSeconds => 2.5f;
        private float _lastRepair;

        public string GetPrompt(PlayerCharacter p)
        {
            var v = GameManager.Instance?.Vessel;
            return v != null ? $"Patch hull ({v.HullIntegrity.Value:0}%)" : "Patch hull";
        }
        public bool CanInteract(PlayerCharacter p)
        {
            var v = GameManager.Instance?.Vessel;
            return v != null && v.IntegrityFraction < 1f && !v.Sinking.Value;
        }
        public void Interact(PlayerCharacter p) => RepairRpc();

        [Rpc(SendTo.Server)]
        private void RepairRpc()
        {
            var v = GameManager.Instance?.Vessel;
            if (v == null) return;
            v.RepairServer(GameConfig.Instance.Vessel.RepairRatePerSecond * HoldSeconds);
            GameManager.Instance.Stats.HullRepairs++;
            // Hammering on the hull is loud underwater.
            GameManager.Instance.Nessie?.OnNoise(transform.position, 0.8f);
        }
    }
}
