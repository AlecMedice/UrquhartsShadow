using Unity.Netcode;
using UnityEngine;
using UrquhartsShadow.Core;
using UrquhartsShadow.Environment;
using UrquhartsShadow.Nessie;

namespace UrquhartsShadow.Tools
{
    /// <summary>
    /// Base for equipment thrown into the loch: sonar beacons, hydrophone drops. Floats on the surface,
    /// runs a battery, feeds a monitor screen, and can be grabbed by Nessie and dragged to the bottom.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public abstract class DeployableDevice : NetworkBehaviour, INessieTarget
    {
        [SerializeField] protected Light beaconLight;
        [SerializeField] protected AudioSource loopAudio;

        public readonly NetworkVariable<float> BatterySeconds = new NetworkVariable<float>(0f);
        public readonly NetworkVariable<bool> Held = new NetworkVariable<bool>(false);
        public readonly NetworkVariable<bool> Lost = new NetworkVariable<bool>(false);

        public abstract float MaxBatterySeconds { get; }
        public abstract string DeviceName { get; }
        public virtual float SabotageSeconds => 12f;
        public virtual float SabotageValue => BatterySeconds.Value / Mathf.Max(1f, MaxBatterySeconds);
        public bool CanBeSabotaged => !Lost.Value && !Held.Value && BatterySeconds.Value > 0f;
        public bool IsActive => !Lost.Value && BatterySeconds.Value > 0f;

        private float _heldSeconds;
        private NessieAI _holder;
        private bool _registered;

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                BatterySeconds.Value = MaxBatterySeconds;
                if (NessieAI.Instance != null) { NessieAI.Instance.RegisterTarget(this); _registered = true; }
            }
            Lost.OnValueChanged += (_, l) => { if (l) OnLostVisual(); };
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer) NessieAI.Instance?.UnregisterTarget(this);
        }

        protected virtual void Update()
        {
            if (beaconLight) beaconLight.enabled = IsActive && !Held.Value;
            if (loopAudio) loopAudio.mute = !IsActive;
            if (!IsServer) return;
            if (!_registered && NessieAI.Instance != null) { NessieAI.Instance.RegisterTarget(this); _registered = true; }

            if (IsActive && !Held.Value)
            {
                BatterySeconds.Value = Mathf.Max(0f, BatterySeconds.Value - Time.deltaTime);
                var ocean = OceanSurface.Instance;
                if (ocean != null)
                {
                    var p = transform.position;
                    p.y = ocean.GetHeightAt(p.x, p.z) - 0.2f;
                    transform.position = p;
                    transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.FromToRotation(Vector3.up, ocean.GetNormalAt(p.x, p.z)), Time.deltaTime * 2f);
                }
                ServerTick();
            }
            else if (Held.Value && _holder != null)
            {
                transform.position = _holder.Body.Head.position + Vector3.down * 1f;
            }
        }

        protected abstract void ServerTick();
        protected virtual void OnLostVisual() { if (beaconLight) beaconLight.enabled = false; }

        // ---- INessieTarget ----
        public void OnSabotageBegin(NessieAI nessie)
        {
            _holder = nessie; Held.Value = true; _heldSeconds = 0f;
        }

        public bool OnSabotageTick(NessieAI nessie, float dt)
        {
            _heldSeconds += dt;
            if (_heldSeconds < SabotageSeconds) return false;
            Lost.Value = true; Held.Value = false; _holder = null;
            if (GameManager.Instance != null) GameManager.Instance.Stats.BeaconsLost++;
            Debug.Log($"[Deployable] {DeviceName} dragged to the bottom.");
            Invoke(nameof(DespawnServer), 30f);
            return true;
        }

        public void OnSabotageReleased(NessieAI nessie) { Held.Value = false; _holder = null; }

        private void DespawnServer() { if (IsServer && NetworkObject.IsSpawned) NetworkObject.Despawn(true); }
    }
}
