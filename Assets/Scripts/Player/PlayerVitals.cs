using System;
using Unity.Netcode;
using UnityEngine;
using UrquhartsShadow.Config;
using UrquhartsShadow.Core;
using UrquhartsShadow.Environment;

namespace UrquhartsShadow.Player
{
    /// <summary>
    /// Server-authoritative health, energy, cold and death. Energy drains with sprinting and the cold night;
    /// rations from the lower deck restore both. A player in the water takes cold-shock damage and loses
    /// unsaved evidence; if they cannot reach a ladder in time they are lost for the expedition.
    /// </summary>
    public class PlayerVitals : NetworkBehaviour
    {
        public readonly NetworkVariable<float> Health = new NetworkVariable<float>(100f);
        public readonly NetworkVariable<float> Energy = new NetworkVariable<float>(100f);
        public readonly NetworkVariable<bool> InWater = new NetworkVariable<bool>(false);
        public readonly NetworkVariable<bool> Alive = new NetworkVariable<bool>(true);
        public readonly NetworkVariable<float> WaterTimer = new NetworkVariable<float>(0f);

        public event Action Died;
        public event Action<float> Damaged;

        private PlayerCharacter _player;
        private float _shiverTimer;

        public bool IsAlive => Alive.Value;
        public bool IsExhausted => Energy.Value <= GameConfig.Instance.Player.ExhaustedEnergyThreshold;
        public bool CanSprint => Energy.Value > 2f;
        /// <summary>Shivering after a dunk slows the player for a while.</summary>
        public float SpeedMultiplier => _shiverTimer > 0f ? 0.75f : 1f;

        private void Awake() => _player = GetComponent<PlayerCharacter>();

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                Health.Value = GameConfig.Instance.Player.MaxHealth;
                Energy.Value = GameConfig.Instance.Player.MaxEnergy;
            }
            Alive.OnValueChanged += (_, alive) => { if (!alive) Died?.Invoke(); };
        }

        private void Update()
        {
            if (_shiverTimer > 0f) _shiverTimer -= Time.deltaTime;
            if (!IsServer || !Alive.Value) return;

            var cfg = GameConfig.Instance.Player;
            var gm = GameManager.Instance;
            bool nightActive = gm != null && gm.IsNightActive;

            // Energy
            float drain = cfg.PassiveEnergyDrain * (nightActive ? 1f : 0.2f);
            if (_player.Movement != null && _player.Movement.IsSprinting) drain += cfg.SprintEnergyDrain;
            Energy.Value = Mathf.Max(0f, Energy.Value - drain * Time.deltaTime);

            // Water
            bool inWater = OceanSurface.Instance != null && OceanSurface.Instance.IsSubmerged(transform.position + Vector3.up * 0.9f);
            if (inWater != InWater.Value) SetInWaterServer(inWater);
            if (InWater.Value)
            {
                WaterTimer.Value += Time.deltaTime;
                Health.Value = Mathf.Max(0f, Health.Value - cfg.WaterDamagePerSecond * Time.deltaTime);
                if (gm != null) gm.Stats.For(OwnerClientId).SecondsInWater += Time.deltaTime;
                if (WaterTimer.Value >= cfg.WaterSurvivalSeconds || Health.Value <= 0f) KillServer("lost to the loch");
            }
            else if (Health.Value < cfg.MaxHealth && _shiverTimer > 0f)
            {
                Health.Value = Mathf.Min(cfg.MaxHealth, Health.Value + cfg.ShiverRecoveryPerSecond * Time.deltaTime);
            }
        }

        public void SetInWaterServer(bool inWater)
        {
            if (!IsServer) return;
            if (inWater && !InWater.Value)
            {
                WaterTimer.Value = 0f;
                var gm = GameManager.Instance;
                if (gm != null) { gm.Stats.PlayersOverboard++; gm.Stats.For(OwnerClientId).TimesOverboard++; }
                if (GameConfig.Instance.Evidence.LoseUnsavedEvidenceInWater) _player.EvidenceBag?.LoseAllRpc();
                gm?.Nessie?.OnPlayerInWater(_player);
            }
            if (!inWater && InWater.Value)
            {
                _shiverTimer = 60f;
                ShiverRpc();
            }
            InWater.Value = inWater;
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void ShiverRpc() => _shiverTimer = 60f;

        public void DamageServer(float amount, string cause)
        {
            if (!IsServer || !Alive.Value) return;
            Health.Value = Mathf.Max(0f, Health.Value - amount);
            DamagedRpc(amount);
            if (Health.Value <= 0f) KillServer(cause);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void DamagedRpc(float amount) => Damaged?.Invoke(amount);

        public void HealServer(float health, float energy)
        {
            if (!IsServer) return;
            var cfg = GameConfig.Instance.Player;
            Health.Value = Mathf.Min(cfg.MaxHealth, Health.Value + health);
            Energy.Value = Mathf.Min(cfg.MaxEnergy, Energy.Value + energy);
        }

        public void KillServer(string cause)
        {
            if (!IsServer || !Alive.Value) return;
            Alive.Value = false;
            var gm = GameManager.Instance;
            if (gm != null) { gm.Stats.PlayersLost++; gm.Stats.For(OwnerClientId).Died = true; }
            Debug.Log($"[Vitals] {_player.DisplayName} {cause}.");
            OnDiedRpc();
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void OnDiedRpc()
        {
            if (IsOwner)
            {
                _player.Spectator?.Begin();
            }
        }

        /// <summary>Server: knock the player with an impulse (Nessie strike, storm lurch).</summary>
        public void KnockbackServer(Vector3 impulse)
        {
            if (!IsServer) return;
            KnockbackRpc(impulse, RpcTarget.Owner);
        }

        [Rpc(SendTo.SpecifiedInParams)]
        private void KnockbackRpc(Vector3 impulse, RpcParams p) => _player.Movement?.ApplyKnockback(impulse);

        public void OnDawnServer()
        {
            if (!IsServer) return;
            // A little rest at dawn; the lower deck does the rest.
            Energy.Value = Mathf.Min(GameConfig.Instance.Player.MaxEnergy, Energy.Value + 20f);
        }
    }
}
