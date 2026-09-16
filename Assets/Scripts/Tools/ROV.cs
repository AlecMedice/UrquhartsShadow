using Unity.Netcode;
using UnityEngine;
using UrquhartsShadow.Config;
using UrquhartsShadow.Core;
using UrquhartsShadow.Environment;
using UrquhartsShadow.Nessie;

namespace UrquhartsShadow.Tools
{
    /// <summary>
    /// The one remotely operated vehicle. Tethered to the vessel, lit by floods, carries an underwater camera.
    /// Piloted from the ROVStation chair; movement is server-authoritative from the pilot's input.
    /// Nessie can gnaw the tether: after RovTetherCutSeconds the ROV is lost until dawn.
    /// </summary>
    public class ROV : NetworkBehaviour, INessieTarget
    {
        [SerializeField] private Camera rovCamera;
        [SerializeField] private Light floodLight;
        [SerializeField] private LineRenderer tether;
        [SerializeField] private Transform tetherAnchorOnVessel;
        [SerializeField] private AudioSource thrusters;

        public readonly NetworkVariable<float> BatterySeconds = new NetworkVariable<float>(0f);
        public readonly NetworkVariable<bool> Deployed = new NetworkVariable<bool>(false);
        public readonly NetworkVariable<bool> TetherCut = new NetworkVariable<bool>(false);
        public readonly NetworkVariable<bool> Held = new NetworkVariable<bool>(false);
        public readonly NetworkVariable<float> TetherHealth = new NetworkVariable<float>(1f);

        public Camera Camera => rovCamera;
        public bool IsOperational => Deployed.Value && !TetherCut.Value && BatterySeconds.Value > 0f;
        public float Depth => OceanSurface.Instance != null ? OceanSurface.Instance.DepthAt(transform.position) : 0f;

        // INessieTarget
        public float SabotageValue => IsOperational ? 1f : 0f;
        public bool CanBeSabotaged => IsOperational && !Held.Value;
        public float SabotageSeconds => NessieAI.Instance != null ? NessieAI.Instance.Profile.RovTetherCutSeconds : 6f;

        private Vector2 _move; private float _vertical; private float _yaw;
        private Vector3 _dockLocalPos;
        private float _held;
        private bool _registered;

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                BatterySeconds.Value = GameConfig.Instance.Tools.RovBatterySeconds;
                var vessel = GameManager.Instance?.Vessel;
                if (vessel != null) _dockLocalPos = vessel.transform.InverseTransformPoint(transform.position);
            }
            if (rovCamera) rovCamera.enabled = false;
        }

        public override void OnNetworkDespawn() { if (IsServer) NessieAI.Instance?.UnregisterTarget(this); }

        /// <summary>Server: pilot input from the station (forward/strafe, up/down, yaw).</summary>
        public void SetPilotInput(Vector2 move, float vertical, float yaw) { _move = move; _vertical = vertical; _yaw = yaw; }

        private void Update()
        {
            if (floodLight) floodLight.enabled = IsOperational;
            if (thrusters) thrusters.volume = IsOperational ? Mathf.Clamp01(_move.magnitude + Mathf.Abs(_vertical)) * 0.6f : 0f;
            DrawTether();
            if (!IsServer) return;
            if (!_registered && NessieAI.Instance != null) { NessieAI.Instance.RegisterTarget(this); _registered = true; }

            var cfg = GameConfig.Instance.Tools;
            var vessel = GameManager.Instance?.Vessel;
            if (vessel != null && _dockLocalPos == Vector3.zero) _dockLocalPos = vessel.transform.InverseTransformPoint(transform.position);

            if (!Deployed.Value)
            {
                if (vessel != null) transform.position = vessel.transform.TransformPoint(_dockLocalPos);
                return;
            }
            if (Held.Value) return;
            if (TetherCut.Value)
            {
                // Sinks slowly, lights dying.
                transform.position += Vector3.down * 0.8f * Time.deltaTime;
                return;
            }

            BatterySeconds.Value = Mathf.Max(0f, BatterySeconds.Value - Time.deltaTime);
            if (BatterySeconds.Value <= 0f) return;

            transform.Rotate(0f, _yaw * cfg.RovTurnRate * Time.deltaTime, 0f, Space.World);
            Vector3 vel = (transform.forward * _move.y + transform.right * _move.x) * cfg.RovSpeed + Vector3.up * _vertical * cfg.RovSpeed * 0.6f;
            Vector3 next = transform.position + vel * Time.deltaTime;

            // Stay below the surface, above the floor, within the tether.
            var ocean = OceanSurface.Instance;
            float surface = ocean != null ? ocean.GetHeightAt(next.x, next.z) : 0f;
            next.y = Mathf.Clamp(next.y, surface - cfg.RovMaxDepth, surface - 0.5f);
            if (vessel != null)
            {
                Vector3 anchor = tetherAnchorOnVessel != null ? tetherAnchorOnVessel.position : vessel.transform.position;
                Vector3 off = next - anchor;
                if (off.magnitude > cfg.RovTetherLength) next = anchor + off.normalized * cfg.RovTetherLength;
            }
            transform.position = next;
            // Thruster noise attracts her a little.
            if (vel.sqrMagnitude > 0.5f && Random.value < Time.deltaTime * 0.2f) NessieAI.Instance?.OnNoise(transform.position, 0.4f);
            if (Random.value < Time.deltaTime * 0.5f) NessieAI.Instance?.OnLightSource(transform.position, 0.5f);
        }

        private void DrawTether()
        {
            if (tether == null) return;
            bool show = Deployed.Value && !TetherCut.Value;
            tether.enabled = show;
            if (!show) return;
            Vector3 a = tetherAnchorOnVessel != null ? tetherAnchorOnVessel.position : (GameManager.Instance?.Vessel != null ? GameManager.Instance.Vessel.transform.position : transform.position);
            tether.positionCount = 2;
            tether.SetPosition(0, a);
            tether.SetPosition(1, transform.position);
        }

        public void DeployServer(bool deploy)
        {
            if (!IsServer) return;
            if (deploy && TetherCut.Value) return;
            Deployed.Value = deploy;
            if (deploy)
            {
                var vessel = GameManager.Instance?.Vessel;
                if (vessel != null) transform.position = vessel.transform.position + vessel.transform.right * 6f + Vector3.down * 2f;
            }
        }

        /// <summary>Dawn: a spare ROV is fitted (or the cut one is recovered) at a funding cost handled by the station.</summary>
        public void RestoreServer()
        {
            if (!IsServer) return;
            TetherCut.Value = false; Held.Value = false; Deployed.Value = false;
            TetherHealth.Value = 1f;
            BatterySeconds.Value = GameConfig.Instance.Tools.RovBatterySeconds;
        }

        public void OnSabotageBegin(NessieAI nessie) { Held.Value = true; _held = 0f; }
        public bool OnSabotageTick(NessieAI nessie, float dt)
        {
            _held += dt;
            TetherHealth.Value = 1f - Mathf.Clamp01(_held / SabotageSeconds);
            transform.position = nessie.Body.Head.position + Vector3.down * 1.5f;
            if (_held < SabotageSeconds) return false;
            TetherCut.Value = true; Held.Value = false; TetherHealth.Value = 0f;
            if (GameManager.Instance != null) GameManager.Instance.Stats.RovsLost++;
            Debug.Log("[ROV] Tether cut. ROV lost.");
            return true;
        }
        public void OnSabotageReleased(NessieAI nessie) { Held.Value = false; }
    }
}
