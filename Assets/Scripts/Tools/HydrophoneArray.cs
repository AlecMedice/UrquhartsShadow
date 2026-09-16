using Unity.Netcode;
using UnityEngine;
using UrquhartsShadow.Config;
using UrquhartsShadow.Core;
using UrquhartsShadow.Nessie;
using UrquhartsShadow.Player;

namespace UrquhartsShadow.Tools
{
    /// <summary>
    /// Hydrophone console. The loch is full of noise: the vessel's engine, rain, distant boats. The operator
    /// sweeps a band-pass filter (Move X) to isolate a bio-acoustic call. When Nessie calls and the filter is
    /// within tolerance of the call frequency for HydrophoneCleanSignalSeconds, a recording is logged.
    /// Signal strength falls with distance and rises when the engine is off.
    /// </summary>
    public class HydrophoneArray : Station
    {
        [SerializeField] private AudioSource ambientHiss;
        [SerializeField] private AudioSource callPlayback;

        public readonly NetworkVariable<float> Battery = new NetworkVariable<float>(100f);
        /// <summary>The frequency (0..1) of the current call; -1 when nothing is calling.</summary>
        public readonly NetworkVariable<float> CallFrequency = new NetworkVariable<float>(-1f);
        public readonly NetworkVariable<float> CallStrength = new NetworkVariable<float>(0f);
        public readonly NetworkVariable<float> EngineMask = new NetworkVariable<float>(0f);

        /// <summary>Operator's filter centre 0..1 (owner-side, sent to server periodically).</summary>
        public float Filter { get; private set; } = 0.5f;
        public float CleanProgress { get; private set; }
        public bool Locked => CallFrequency.Value >= 0f && Mathf.Abs(Filter - CallFrequency.Value) <= GameConfig.Instance.Tools.FilterTolerance;

        private float _callEndsAt;
        private float _cleanSeconds;
        private float _nextSend;
        private NessieAI _subscribedTo;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (IsServer)
            {
                Battery.Value = GameConfig.Instance.Tools.BatteryCapacity;
                TrySubscribe();
            }
        }

        public override void OnNetworkDespawn()
        {
            if (_subscribedTo != null) _subscribedTo.HydrophoneCall -= OnNessieCall;
            _subscribedTo = null;
        }

        /// <summary>Nessie may spawn after this console; keep trying until she exists.</summary>
        private void TrySubscribe()
        {
            if (_subscribedTo != null || NessieAI.Instance == null) return;
            _subscribedTo = NessieAI.Instance;
            _subscribedTo.HydrophoneCall += OnNessieCall;
        }

        private void OnNessieCall(Vector3 pos, float time)
        {
            var cfg = GameConfig.Instance.Tools;
            float d = Vector3.Distance(pos, transform.position);
            if (d > cfg.HydrophoneRange) return;
            CallFrequency.Value = Random.Range(0.1f, 0.9f);
            CallStrength.Value = 1f - d / cfg.HydrophoneRange;
            _callEndsAt = Time.time + Random.Range(8f, 16f);
        }

        private void Update()
        {
            var cfg = GameConfig.Instance.Tools;
            if (IsServer)
            {
                TrySubscribe();
                if (IsOccupied && Battery.Value > 0f) Battery.Value = Mathf.Max(0f, Battery.Value - cfg.HydrophoneDrainPerSecond * Time.deltaTime);
                var vessel = GameManager.Instance?.Vessel;
                EngineMask.Value = vessel != null ? vessel.EngineLoad * cfg.EngineMaskStrength : 0f;
                if (CallFrequency.Value >= 0f && Time.time > _callEndsAt) { CallFrequency.Value = -1f; CallStrength.Value = 0f; }
            }

            if (!LocalPlayerSeated || Occupant == null) { CleanProgress = 0f; return; }
            Filter = Mathf.Clamp01(Filter + Occupant.Input.Move.x * cfg.FilterSweepSpeed * Time.deltaTime);

            bool audible = CallFrequency.Value >= 0f && CallStrength.Value > EngineMask.Value * 0.6f && Battery.Value > 0f;
            if (callPlayback) callPlayback.volume = audible && Locked ? CallStrength.Value : 0.05f;
            if (ambientHiss) ambientHiss.volume = Mathf.Lerp(0.3f, 0.9f, EngineMask.Value);

            if (audible && Locked)
            {
                _cleanSeconds += Time.deltaTime;
                CleanProgress = Mathf.Clamp01(_cleanSeconds / cfg.HydrophoneCleanSignalSeconds);
                if (_cleanSeconds >= cfg.HydrophoneCleanSignalSeconds)
                {
                    _cleanSeconds = 0f; CleanProgress = 0f;
                    LogRecordingRpc(Filter);
                }
            }
            else
            {
                _cleanSeconds = Mathf.Max(0f, _cleanSeconds - Time.deltaTime);
                CleanProgress = Mathf.Clamp01(_cleanSeconds / cfg.HydrophoneCleanSignalSeconds);
            }
        }

        [Rpc(SendTo.Server)]
        private void LogRecordingRpc(float filter)
        {
            if (CallFrequency.Value < 0f) return;
            float err = Mathf.Abs(filter - CallFrequency.Value);
            if (err > GameConfig.Instance.Tools.FilterTolerance * 1.5f) return;
            var op = OccupantOnServer();
            if (op == null) return;
            float q = Mathf.Clamp01(CallStrength.Value * (1f - EngineMask.Value) * (1f - err / GameConfig.Instance.Tools.FilterTolerance * 0.5f));
            var nt = GameManager.Instance?.NightCycle;
            op.EvidenceBag.AddServer(new PendingEvidence(EvidenceType.HydrophoneCall, q, "Hydrophone recording", nt != null ? nt.NightElapsed : 0f));
            CallFrequency.Value = -1f; CallStrength.Value = 0f;
        }

        public void RechargeServer() { if (IsServer) Battery.Value = GameConfig.Instance.Tools.BatteryCapacity; }
    }
}
