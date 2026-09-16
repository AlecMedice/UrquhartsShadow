using System.Collections.Generic;
using UnityEngine;
using UrquhartsShadow.Core;

namespace UrquhartsShadow.Player
{
    /// <summary>
    /// When the local player is dead or incapacitated they can cycle through teammates and toggle
    /// between that teammate's first-person POV and a third-person orbit behind them.
    /// </summary>
    public class SpectatorCamera : MonoBehaviour
    {
        [SerializeField] private Vector3 thirdPersonOffset = new Vector3(0f, 1.6f, -3.2f);
        [SerializeField] private float followLerp = 8f;

        private PlayerCharacter _player;
        private Camera _cam;
        private int _index;
        private bool _thirdPerson = true;
        private bool _active;
        private Transform _originalParent;
        private Vector3 _originalLocalPos;
        private Quaternion _originalLocalRot;

        public PlayerCharacter Target { get; private set; }
        public bool IsThirdPerson => _thirdPerson;
        public bool IsActive => _active;

        private void Awake()
        {
            _player = GetComponent<PlayerCharacter>();
            _cam = _player.Camera;
        }

        public void Begin()
        {
            if (_active || _cam == null) return;
            _active = true;
            enabled = true;
            _originalParent = _cam.transform.parent;
            _originalLocalPos = _cam.transform.localPosition;
            _originalLocalRot = _cam.transform.localRotation;
            _cam.transform.SetParent(null, true);

            _player.Input.SpectateNext += Next;
            _player.Input.SpectatePrev += Prev;
            _player.Input.SpectateToggleView += ToggleView;
            _index = 0;
            PickTarget(0);
        }

        public void End()
        {
            if (!_active) return;
            _active = false;
            enabled = false;
            _player.Input.SpectateNext -= Next;
            _player.Input.SpectatePrev -= Prev;
            _player.Input.SpectateToggleView -= ToggleView;
            _cam.transform.SetParent(_originalParent, false);
            _cam.transform.localPosition = _originalLocalPos;
            _cam.transform.localRotation = _originalLocalRot;
            Target = null;
        }

        private List<PlayerCharacter> Candidates()
        {
            var list = new List<PlayerCharacter>();
            if (GameManager.Instance == null) return list;
            foreach (var p in GameManager.Instance.Players.Values)
                if (p != null && p != _player && p.Vitals != null && p.Vitals.IsAlive) list.Add(p);
            return list;
        }

        private void PickTarget(int delta)
        {
            var c = Candidates();
            if (c.Count == 0) { Target = null; return; }
            _index = ((_index + delta) % c.Count + c.Count) % c.Count;
            Target = c[_index];
        }

        private void Next() => PickTarget(1);
        private void Prev() => PickTarget(-1);
        private void ToggleView() => _thirdPerson = !_thirdPerson;

        private void LateUpdate()
        {
            if (!_active) return;
            if (Target == null || !Target.Vitals.IsAlive) PickTarget(0);
            if (Target == null)
            {
                // Nobody left: drift above the boat.
                var vessel = GameManager.Instance?.Vessel;
                if (vessel != null)
                {
                    Vector3 p = vessel.transform.position + Vector3.up * 25f + vessel.transform.forward * -30f;
                    _cam.transform.position = Vector3.Lerp(_cam.transform.position, p, Time.deltaTime * 2f);
                    _cam.transform.rotation = Quaternion.Slerp(_cam.transform.rotation, Quaternion.LookRotation(vessel.transform.position - _cam.transform.position), Time.deltaTime * 2f);
                }
                return;
            }

            if (_thirdPerson)
            {
                Transform t = Target.transform;
                Vector3 desired = t.TransformPoint(thirdPersonOffset);
                if (Physics.Linecast(t.position + Vector3.up * 1.6f, desired, out var hit, ~0, QueryTriggerInteraction.Ignore))
                    desired = hit.point + hit.normal * 0.2f;
                _cam.transform.position = Vector3.Lerp(_cam.transform.position, desired, Time.deltaTime * followLerp);
                _cam.transform.rotation = Quaternion.Slerp(_cam.transform.rotation, Quaternion.LookRotation(t.position + Vector3.up * 1.4f - _cam.transform.position), Time.deltaTime * followLerp);
            }
            else
            {
                Transform t = Target.CameraRoot;
                _cam.transform.position = t.position;
                _cam.transform.rotation = t.rotation;
            }
        }
    }
}
