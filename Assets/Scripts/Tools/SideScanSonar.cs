using System;
using Unity.Netcode;
using UnityEngine;
using UrquhartsShadow.Config;
using UrquhartsShadow.Core;
using UrquhartsShadow.Nessie;
using UrquhartsShadow.Player;

namespace UrquhartsShadow.Tools
{
    /// <summary>
    /// Hull-mounted side-scan sonar console. Pings on an interval, draws the loch floor and any body
    /// bigger than SonarMinContactSize as a blob. Holding a contact continuously for SonarContactSeconds
    /// logs a sonar evidence hit for the operator. Every ping is a stimulus Nessie hears.
    /// </summary>
    public class SideScanSonar : Station
    {
        [SerializeField] private AudioSource pingSound;

        public readonly NetworkVariable<bool> Powered = new NetworkVariable<bool>(true);
        public readonly NetworkVariable<float> Battery = new NetworkVariable<float>(100f);
        /// <summary>Last known contact, relative to the vessel (x = starboard, y = ahead, z = depth). Zero = none.</summary>
        public readonly NetworkVariable<Vector3> Contact = new NetworkVariable<Vector3>(Vector3.zero);
        public readonly NetworkVariable<float> ContactHeld = new NetworkVariable<float>(0f);

        public event Action<Vector3> Pinged;

        private float _nextPing;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (IsServer) Battery.Value = GameConfig.Instance.Tools.BatteryCapacity;
        }

        private void Update()
        {
            if (!IsServer || !Powered.Value) return;
            if (!IsOccupied) { ContactHeld.Value = 0f; return; }
            var cfg = GameConfig.Instance.Tools;
            if (Time.time < _nextPing) return;
            _nextPing = Time.time + cfg.SonarPingInterval;
            Battery.Value = Mathf.Max(0f, Battery.Value - cfg.SonarDrainPerPing);
            if (Battery.Value <= 0f) { Powered.Value = false; return; }

            var nessie = NessieAI.Instance;
            nessie?.OnSonarPing(transform.position, cfg.SonarRange);
            PingFxRpc();

            bool hit = false;
            if (nessie != null && nessie.Body.BodyLength >= cfg.SonarMinContactSize)
            {
                Vector3 rel = transform.InverseTransformPoint(nessie.transform.position);
                float dist = rel.magnitude;
                if (dist <= cfg.SonarRange && nessie.Body.CurrentDepth > 1f)
                {
                    // Noise the reading a little; the operator sees a blob, not a dot.
                    rel += UnityEngine.Random.insideUnitSphere * Mathf.Lerp(2f, 12f, dist / cfg.SonarRange);
                    Contact.Value = new Vector3(rel.x, rel.z, nessie.Body.CurrentDepth);
                    hit = true;
                }
            }
            if (hit)
            {
                ContactHeld.Value += cfg.SonarPingInterval;
                if (ContactHeld.Value >= cfg.SonarContactSeconds)
                {
                    ContactHeld.Value = 0f;
                    var op = OccupantOnServer();
                    if (op != null)
                    {
                        float q = Mathf.Clamp01(0.5f + 0.5f * (1f - Contact.Value.magnitude / cfg.SonarRange));
                        var nt = GameManager.Instance?.NightCycle;
                        op.EvidenceBag.AddServer(new PendingEvidence(EvidenceType.SonarContact, q, "Sonar contact", nt != null ? nt.NightElapsed : 0f));
                    }
                }
            }
            else
            {
                Contact.Value = Vector3.zero;
                ContactHeld.Value = 0f;
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void PingFxRpc()
        {
            if (pingSound) pingSound.Play();
            Pinged?.Invoke(transform.position);
        }

        public void RechargeServer() { if (IsServer) { Battery.Value = GameConfig.Instance.Tools.BatteryCapacity; Powered.Value = true; } }
        public bool TryInsertBattery(PlayerCharacter p) => p != null && p.Inventory.SpareBatteries.Value > 0;
    }
}
