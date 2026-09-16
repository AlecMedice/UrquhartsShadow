using Unity.Netcode;
using UnityEngine;
using UrquhartsShadow.Config;
using UrquhartsShadow.Core;
using UrquhartsShadow.Player;

namespace UrquhartsShadow.Boat
{
    /// <summary>
    /// Lower-deck galley and battery locker. Interact to eat a ration (health + energy) or take a spare battery.
    /// Flooded when hull integrity is low. Extra stock can be bought with funding at dawn.
    /// </summary>
    public class SupplyStation : NetworkBehaviour, IInteractable
    {
        public enum Kind { Rations, Batteries }
        [SerializeField] private Kind kind = Kind.Rations;

        public readonly NetworkVariable<int> Rations = new NetworkVariable<int>(0);
        public readonly NetworkVariable<int> Batteries = new NetworkVariable<int>(0);

        public float HoldSeconds => GameConfig.Instance.Supplies.ResupplyInteractSeconds;

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                Rations.Value = GameConfig.Instance.Supplies.StartingRations;
                Batteries.Value = GameConfig.Instance.Supplies.StartingBatteries;
            }
        }

        private bool Flooded => GameManager.Instance?.Vessel != null && GameManager.Instance.Vessel.LowerDeckFlooded.Value;

        public string GetPrompt(PlayerCharacter p)
        {
            if (Flooded) return "Flooded! Repair the hull first";
            return kind == Kind.Rations ? $"Eat ration ({Rations.Value} left)" : $"Take battery ({Batteries.Value} left)";
        }

        public bool CanInteract(PlayerCharacter p) => !Flooded && (kind == Kind.Rations ? Rations.Value > 0 : Batteries.Value > 0);
        public void Interact(PlayerCharacter p) => TakeRpc();

        [Rpc(SendTo.Server)]
        private void TakeRpc(RpcParams rpc = default)
        {
            if (Flooded) return;
            if (!GameManager.Instance.Players.TryGetValue(rpc.Receive.SenderClientId, out var player) || player == null) return;
            var s = GameConfig.Instance.Supplies;
            if (kind == Kind.Rations && Rations.Value > 0)
            {
                Rations.Value--;
                player.Vitals.HealServer(s.RationHealthRestore, s.RationEnergyRestore);
                GameManager.Instance.Stats.For(player.OwnerClientId).Rations++;
            }
            else if (kind == Kind.Batteries && Batteries.Value > 0)
            {
                Batteries.Value--;
                player.Inventory.GiveBatteriesServer(1);
                GameManager.Instance.Stats.For(player.OwnerClientId).Batteries++;
            }
        }

        public void RestockServer(int batteries, int rations)
        {
            if (!IsServer) return;
            Batteries.Value += batteries;
            Rations.Value += rations;
        }

        /// <summary>Dawn purchase with research funding. Called from the dawn UI via RPC.</summary>
        [Rpc(SendTo.Server)]
        public void BuyRpc(int rations, int batteries)
        {
            var s = GameConfig.Instance.Supplies;
            int cost = rations * s.RationCost + batteries * s.BatteryCost;
            var ev = GameManager.Instance?.Evidence;
            if (ev == null || !ev.TrySpendFunding(cost)) return;
            Rations.Value += rations; Batteries.Value += batteries;
        }
    }
}
