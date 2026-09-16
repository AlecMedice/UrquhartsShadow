using Unity.Netcode;
using UnityEngine;
using UrquhartsShadow.Config;
using UrquhartsShadow.Core;
using UrquhartsShadow.Environment;
using UrquhartsShadow.Player;

namespace UrquhartsShadow.Tools
{
    /// <summary>Rail-mounted launcher on deck. Interact to fire a sonar beacon out into the water.</summary>
    public class BeaconLauncher : NetworkBehaviour, IInteractable
    {
        [SerializeField] private GameObject beaconPrefab;
        [SerializeField] private Transform muzzle;
        [SerializeField] private float launchDistance = 25f;

        public readonly NetworkVariable<int> Stock = new NetworkVariable<int>(0);
        public float HoldSeconds => 0.6f;

        private int _launched;

        public override void OnNetworkSpawn()
        {
            if (IsServer) Stock.Value = GameConfig.Instance.Tools.SonarBeaconStock;
        }

        public string GetPrompt(PlayerCharacter p) => Stock.Value > 0 ? $"Launch sonar beacon ({Stock.Value} left)" : "No beacons left";
        public bool CanInteract(PlayerCharacter p) => Stock.Value > 0;
        public void Interact(PlayerCharacter p) => LaunchRpc();

        [Rpc(SendTo.Server)]
        private void LaunchRpc()
        {
            if (Stock.Value <= 0 || beaconPrefab == null) return;
            Stock.Value--;
            Vector3 dir = muzzle != null ? muzzle.forward : transform.forward;
            Vector3 pos = (muzzle != null ? muzzle.position : transform.position) + dir * launchDistance;
            if (OceanSurface.Instance != null) pos.y = OceanSurface.Instance.GetHeightAt(pos.x, pos.z);
            var go = Instantiate(beaconPrefab, pos, Quaternion.identity);
            var no = go.GetComponent<NetworkObject>();
            no.Spawn(true);
            var beacon = go.GetComponent<SonarBeacon>();
            if (beacon != null) beacon.BeaconIndex.Value = _launched++;
            GameManager.Instance?.Nessie?.OnNoise(pos, 0.5f);
        }

        public void RestockServer(int n) { if (IsServer) Stock.Value += n; }
    }
}
