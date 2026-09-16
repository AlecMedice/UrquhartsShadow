using UnityEngine;
using UrquhartsShadow.Core;
using UrquhartsShadow.Player;
using UrquhartsShadow.Tools;

namespace UrquhartsShadow.Boat
{
    /// <summary>Wheelhouse helm. Move Y = throttle, Move X = rudder, Primary toggles the engine (quiet = harder for her to find you).</summary>
    public class Helm : Station
    {
        private float _throttle, _rudder;
        private bool _engine = true;
        private float _nextSend;

        protected override void OnSeatedLocal(PlayerCharacter p) { p.Input.PrimaryPressed += ToggleEngine; }
        protected override void OnLeftLocal(PlayerCharacter p) { p.Input.PrimaryPressed -= ToggleEngine; SendRpc(_throttle, _rudder, _engine); }
        private void ToggleEngine() { _engine = !_engine; SendRpc(_throttle, _rudder, _engine); }

        private void Update()
        {
            if (!LocalPlayerSeated || Occupant == null) return;
            var m = Occupant.Input.Move;
            _throttle = Mathf.MoveTowards(_throttle, m.y, Time.deltaTime * 0.5f);
            _rudder = Mathf.MoveTowards(_rudder, m.x, Time.deltaTime * 1.2f);
            if (Time.time >= _nextSend) { _nextSend = Time.time + 0.1f; SendRpc(_throttle, _rudder, _engine); }
        }

        [Unity.Netcode.Rpc(Unity.Netcode.SendTo.Server)]
        private void SendRpc(float throttle, float rudder, bool engine) => GameManager.Instance?.Vessel?.SetHelmServer(throttle, rudder, engine);
    }
}
