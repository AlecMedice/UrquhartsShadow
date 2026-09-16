using UnityEngine;
using UrquhartsShadow.Config;
using UrquhartsShadow.Nessie;

namespace UrquhartsShadow.Tools
{
    /// <summary>
    /// Launchable sonar beacon. Pings its own bubble of the loch and reports a contact range to the
    /// monitor screens on the main deck. Nessie hears every ping, so beacons are bait as much as sensors.
    /// </summary>
    public class SonarBeacon : DeployableDevice
    {
        public readonly Unity.Netcode.NetworkVariable<float> ContactRange = new Unity.Netcode.NetworkVariable<float>(-1f);
        public readonly Unity.Netcode.NetworkVariable<int> BeaconIndex = new Unity.Netcode.NetworkVariable<int>(0);

        private float _nextPing;

        public override float MaxBatterySeconds => GameConfig.Instance.Tools.BeaconBatterySeconds;
        public override string DeviceName => $"Sonar beacon {BeaconIndex.Value + 1}";

        protected override void ServerTick()
        {
            if (Time.time < _nextPing) return;
            var cfg = GameConfig.Instance.Tools;
            _nextPing = Time.time + cfg.SonarPingInterval;
            var nessie = NessieAI.Instance;
            if (nessie == null) { ContactRange.Value = -1f; return; }
            nessie.OnSonarPing(transform.position, cfg.BeaconRange);
            float d = Vector3.Distance(transform.position, nessie.transform.position);
            ContactRange.Value = d <= cfg.BeaconRange ? d : -1f;
        }
    }
}
