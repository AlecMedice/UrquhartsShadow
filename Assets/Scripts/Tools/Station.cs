using Unity.Netcode;
using UnityEngine;
using UrquhartsShadow.Player;

namespace UrquhartsShadow.Tools
{
    /// <summary>
    /// A seat on the vessel that one player occupies at a time: helm, 35mm mount, sonar console,
    /// hydrophone console, ROV pilot chair. Handles seating/leaving and ownership so only the
    /// seated player's input drives the station; the server tracks who is seated.
    /// </summary>
    public abstract class Station : NetworkBehaviour, IInteractable
    {
        [SerializeField] protected Transform seatPoint;
        [SerializeField] protected Transform viewPoint;
        [SerializeField] protected string stationName = "Station";

        public readonly NetworkVariable<ulong> OccupantId = new NetworkVariable<ulong>(ulong.MaxValue);
        public bool IsOccupied => OccupantId.Value != ulong.MaxValue;
        public bool LocalPlayerSeated => IsOccupied && NetworkManager.Singleton != null && OccupantId.Value == NetworkManager.Singleton.LocalClientId;
        public virtual float HoldSeconds => 0f;
        public string StationName => stationName;

        protected PlayerCharacter Occupant { get; private set; }
        private Vector3 _preSeatPos; private Quaternion _preSeatRot;

        public virtual string GetPrompt(PlayerCharacter p) => LocalPlayerSeated ? $"Leave {stationName}" : $"Use {stationName}";
        public virtual bool CanInteract(PlayerCharacter p) => !IsOccupied || OccupantId.Value == p.OwnerClientId;

        public void Interact(PlayerCharacter p)
        {
            if (LocalPlayerSeated) RequestLeaveRpc(); else RequestSitRpc();
        }

        [Rpc(SendTo.Server)]
        private void RequestSitRpc(RpcParams rpc = default)
        {
            ulong id = rpc.Receive.SenderClientId;
            if (IsOccupied) return;
            OccupantId.Value = id;
            SeatRpc(id, RpcTarget.Single(id, RpcTargetUse.Temp));
            OnOccupiedServer(id);
        }

        [Rpc(SendTo.Server)]
        private void RequestLeaveRpc(RpcParams rpc = default)
        {
            ulong id = rpc.Receive.SenderClientId;
            if (OccupantId.Value != id) return;
            OccupantId.Value = ulong.MaxValue;
            LeaveRpc(RpcTarget.Single(id, RpcTargetUse.Temp));
            OnVacatedServer(id);
        }

        [Rpc(SendTo.SpecifiedInParams)]
        private void SeatRpc(ulong id, RpcParams p)
        {
            var player = PlayerCharacter.Local;
            if (player == null) return;
            Occupant = player;
            player.IsSeated = true;
            _preSeatPos = player.transform.position; _preSeatRot = player.transform.rotation;
            if (seatPoint != null) player.Movement.Teleport(seatPoint.position, seatPoint.rotation);
            player.Look.SetPitch(0f);
            player.Interactor.ForceTarget(this);
            player.HeldTool?.Unequip();
            OnSeatedLocal(player);
        }

        [Rpc(SendTo.SpecifiedInParams)]
        private void LeaveRpc(RpcParams p)
        {
            var player = PlayerCharacter.Local;
            if (player == null) return;
            OnLeftLocal(player);
            player.IsSeated = false;
            player.Movement.Teleport(_preSeatPos, _preSeatRot);
            player.Interactor.ForceTarget(null);
            Occupant = null;
        }

        /// <summary>Server: force the occupant out (station destroyed, player died/overboard).</summary>
        public void EjectServer()
        {
            if (!IsServer || !IsOccupied) return;
            ulong id = OccupantId.Value;
            OccupantId.Value = ulong.MaxValue;
            LeaveRpc(RpcTarget.Single(id, RpcTargetUse.Temp));
            OnVacatedServer(id);
        }

        protected virtual void OnSeatedLocal(PlayerCharacter p) { }
        protected virtual void OnLeftLocal(PlayerCharacter p) { }
        protected virtual void OnOccupiedServer(ulong id) { }
        protected virtual void OnVacatedServer(ulong id) { }

        protected PlayerCharacter OccupantOnServer()
        {
            if (!IsOccupied || Core.GameManager.Instance == null) return null;
            return Core.GameManager.Instance.Players.TryGetValue(OccupantId.Value, out var p) ? p : null;
        }
    }
}
