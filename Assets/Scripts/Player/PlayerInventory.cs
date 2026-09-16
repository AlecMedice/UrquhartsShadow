using Unity.Netcode;
using UnityEngine;
using UrquhartsShadow.Config;

namespace UrquhartsShadow.Player
{
    /// <summary>
    /// What the player carries: spare batteries, one flashlight charge, phone charge.
    /// Simple counters; the lower deck SupplyStation refills them.
    /// </summary>
    public class PlayerInventory : NetworkBehaviour
    {
        public readonly NetworkVariable<int> SpareBatteries = new NetworkVariable<int>(1);
        public readonly NetworkVariable<float> FlashlightBatteryNet = new NetworkVariable<float>(100f);
        public readonly NetworkVariable<float> PhoneBatteryNet = new NetworkVariable<float>(100f);

        // Owner-side mirrors so drain is smooth locally; server value is authoritative for refills.
        public float FlashlightBattery { get; private set; } = 100f;
        public float PhoneBattery { get; private set; } = 100f;

        public override void OnNetworkSpawn()
        {
            FlashlightBattery = FlashlightBatteryNet.Value;
            PhoneBattery = PhoneBatteryNet.Value;
            FlashlightBatteryNet.OnValueChanged += (_, v) => FlashlightBattery = v;
            PhoneBatteryNet.OnValueChanged += (_, v) => PhoneBattery = v;
        }

        public void DrainFlashlight(float dt)
        {
            FlashlightBattery = Mathf.Max(0f, FlashlightBattery - GameConfig.Instance.Tools.FlashlightDrainPerSecond * dt);
        }

        public void DrainPhone(float dt)
        {
            PhoneBattery = Mathf.Max(0f, PhoneBattery - GameConfig.Instance.Tools.PhoneBatteryDrainPerSecond * dt);
        }

        /// <summary>Owner: swap a spare battery into the flashlight or phone.</summary>
        public bool TryUseSpare(bool forPhone)
        {
            if (SpareBatteries.Value <= 0) return false;
            UseSpareRpc(forPhone);
            return true;
        }

        [Rpc(SendTo.Server)]
        private void UseSpareRpc(bool forPhone)
        {
            if (SpareBatteries.Value <= 0) return;
            SpareBatteries.Value--;
            if (forPhone) PhoneBatteryNet.Value = GameConfig.Instance.Tools.BatteryCapacity;
            else FlashlightBatteryNet.Value = GameConfig.Instance.Tools.BatteryCapacity;
            SyncRpc(FlashlightBatteryNet.Value, PhoneBatteryNet.Value, RpcTarget.Owner);
        }

        [Rpc(SendTo.SpecifiedInParams)]
        private void SyncRpc(float flash, float phone, RpcParams p) { FlashlightBattery = flash; PhoneBattery = phone; }

        public void GiveBatteriesServer(int n)
        {
            if (!IsServer) return;
            SpareBatteries.Value += n;
        }
    }
}
