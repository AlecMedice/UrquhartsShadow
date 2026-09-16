using Unity.Netcode;
using UnityEngine;
using UrquhartsShadow.Core;
using UrquhartsShadow.Player;

namespace UrquhartsShadow.Boat
{
    /// <summary>
    /// Main-deck darkroom / evidence safe. Players deposit their pending captures here; only then do they
    /// count toward the 10. Nessie's whole game is stopping you between the rail and this box.
    /// </summary>
    public class EvidenceLocker : NetworkBehaviour, IInteractable
    {
        [SerializeField] private AudioSource depositSound;
        public float HoldSeconds => 1.2f;

        public string GetPrompt(PlayerCharacter p)
        {
            int n = p.EvidenceBag != null ? p.EvidenceBag.PendingCount.Value : 0;
            var ev = GameManager.Instance?.Evidence;
            string tally = ev != null ? $" [{ev.SavedCount}/{Config.GameConstants.EvidenceToWin}]" : "";
            return n > 0 ? $"Secure {n} piece(s) of evidence{tally}" : $"Evidence locker{tally}";
        }
        public bool CanInteract(PlayerCharacter p) => p.EvidenceBag != null && p.EvidenceBag.PendingCount.Value > 0;
        public void Interact(PlayerCharacter p) => DepositRpc();

        [Rpc(SendTo.Server)]
        private void DepositRpc(RpcParams rpc = default)
        {
            if (!GameManager.Instance.Players.TryGetValue(rpc.Receive.SenderClientId, out var player) || player == null) return;
            int accepted = player.EvidenceBag.DepositAllServer(GameManager.Instance.Evidence);
            if (accepted > 0) FxRpc();
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void FxRpc() { if (depositSound) depositSound.Play(); }
    }
}
