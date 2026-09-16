using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UrquhartsShadow.Core;

namespace UrquhartsShadow.Networking
{
    /// <summary>
    /// Spawns a player object for each connected client when the loch scene loads, at the vessel's spawn
    /// points. Put this on a scene object in LochNess. Disable NetworkManager's auto player spawn so
    /// players are not created in the title scene.
    /// </summary>
    public class PlayerSpawner : NetworkBehaviour
    {
        [SerializeField] private GameObject playerPrefab;
        [SerializeField] private Transform[] spawnPoints;

        private readonly HashSet<ulong> _spawned = new HashSet<ulong>();

        public override void OnNetworkSpawn()
        {
            if (!IsServer) return;
            foreach (var id in NetworkManager.ConnectedClientsIds) Spawn(id);
            NetworkManager.OnClientConnectedCallback += Spawn;
            NetworkManager.OnClientDisconnectCallback += OnDisconnect;
        }

        public override void OnNetworkDespawn()
        {
            if (!IsServer || NetworkManager == null) return;
            NetworkManager.OnClientConnectedCallback -= Spawn;
            NetworkManager.OnClientDisconnectCallback -= OnDisconnect;
        }

        private void Spawn(ulong clientId)
        {
            if (_spawned.Contains(clientId) || playerPrefab == null) return;
            int i = _spawned.Count % Mathf.Max(1, spawnPoints != null ? spawnPoints.Length : 1);
            Vector3 pos = spawnPoints != null && spawnPoints.Length > 0 ? spawnPoints[i].position : transform.position;
            Quaternion rot = spawnPoints != null && spawnPoints.Length > 0 ? spawnPoints[i].rotation : Quaternion.identity;
            var go = Instantiate(playerPrefab, pos, rot);
            go.GetComponent<NetworkObject>().SpawnAsPlayerObject(clientId, true);
            _spawned.Add(clientId);
            Debug.Log($"[Spawner] Spawned player for client {clientId}");
        }

        private void OnDisconnect(ulong clientId)
        {
            _spawned.Remove(clientId);
            if (GameManager.Instance != null && GameManager.Instance.Players.TryGetValue(clientId, out var p) && p != null)
                GameManager.Instance.UnregisterPlayer(p);
        }
    }
}
