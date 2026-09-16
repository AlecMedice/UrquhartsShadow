using Unity.Netcode;
using UnityEngine;
using UrquhartsShadow.Config;
using UrquhartsShadow.Core;
using UrquhartsShadow.Environment;
using UrquhartsShadow.Nessie;
using UrquhartsShadow.Player;

namespace UrquhartsShadow.Tools
{
    /// <summary>
    /// Rail-mounted eDNA water sampler. Lower a vial (hold Interact) when the vessel is near a deep trench;
    /// after SampleDurationSeconds it tests positive only if Nessie passed nearby recently. A positive vial
    /// is evidence; negatives waste a vial. Nessie is drawn to the winch noise.
    /// </summary>
    public class EdnaSampler : NetworkBehaviour, IInteractable
    {
        [SerializeField] private AudioSource winch;
        [SerializeField] private Transform vialDropPoint;

        public readonly NetworkVariable<int> Vials = new NetworkVariable<int>(0);
        public readonly NetworkVariable<bool> Sampling = new NetworkVariable<bool>(false);
        public readonly NetworkVariable<float> SampleProgress = new NetworkVariable<float>(0f);
        public readonly NetworkVariable<bool> NearTrench = new NetworkVariable<bool>(false);

        public float HoldSeconds => 1.0f;
        private ulong _operator;
        private float _endsAt;

        public override void OnNetworkSpawn()
        {
            if (IsServer) Vials.Value = GameConfig.Instance.Tools.SampleVialStock;
        }

        public string GetPrompt(PlayerCharacter p)
        {
            if (Sampling.Value) return "Sampling...";
            if (Vials.Value <= 0) return "No vials left";
            return NearTrench.Value ? $"Lower eDNA sampler ({Vials.Value} vials)" : "Water too shallow for eDNA (find a trench)";
        }
        public bool CanInteract(PlayerCharacter p) => !Sampling.Value && Vials.Value > 0 && NearTrench.Value;
        public void Interact(PlayerCharacter p) => BeginSampleRpc();

        private void Update()
        {
            if (!IsServer) return;
            var lb = LochBounds.Instance;
            var trench = lb != null ? lb.NearestTrench(transform.position) : null;
            NearTrench.Value = trench != null && Vector3.Distance(new Vector3(trench.position.x, 0, trench.position.z), new Vector3(transform.position.x, 0, transform.position.z)) <= GameConfig.Instance.Tools.TrenchProximity;

            if (Sampling.Value)
            {
                float dur = GameConfig.Instance.Tools.SampleDurationSeconds;
                SampleProgress.Value = 1f - Mathf.Clamp01((_endsAt - Time.time) / dur);
                if (Time.time >= _endsAt) FinishSample();
            }
        }

        [Rpc(SendTo.Server)]
        private void BeginSampleRpc(RpcParams rpc = default)
        {
            if (Sampling.Value || Vials.Value <= 0 || !NearTrench.Value) return;
            Vials.Value--;
            Sampling.Value = true;
            _operator = rpc.Receive.SenderClientId;
            _endsAt = Time.time + GameConfig.Instance.Tools.SampleDurationSeconds;
            WinchRpc(true);
            NessieAI.Instance?.OnNoise(transform.position, 0.6f);
        }

        private void FinishSample()
        {
            Sampling.Value = false;
            SampleProgress.Value = 0f;
            WinchRpc(false);
            var nessie = NessieAI.Instance;
            var cfg = GameConfig.Instance.Tools;
            Vector3 samplePoint = vialDropPoint != null ? vialDropPoint.position : transform.position;
            bool positive = nessie != null && nessie.HasRecentTraceNear(samplePoint, cfg.EdnaTraceRadius);
            if (!positive) { ResultRpc(false, RpcTarget.Single(_operator, RpcTargetUse.Temp)); return; }
            if (GameManager.Instance != null && GameManager.Instance.Players.TryGetValue(_operator, out var op) && op != null)
            {
                var nt = GameManager.Instance.NightCycle;
                float q = Random.Range(0.6f, 0.95f);
                op.EvidenceBag.AddServer(new PendingEvidence(EvidenceType.EdnaSample, q, "eDNA: unknown sequence", nt != null ? nt.NightElapsed : 0f));
            }
            ResultRpc(true, RpcTarget.Single(_operator, RpcTargetUse.Temp));
        }

        public event System.Action<bool> SampleResult;

        [Rpc(SendTo.SpecifiedInParams)]
        private void ResultRpc(bool positive, RpcParams p) => SampleResult?.Invoke(positive);

        [Rpc(SendTo.ClientsAndHost)]
        private void WinchRpc(bool on) { if (winch) { if (on) winch.Play(); else winch.Stop(); } }

        public void RestockServer(int n) { if (IsServer) Vials.Value += n; }
    }
}
