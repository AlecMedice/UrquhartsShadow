using System;
using UnityEngine;
using UrquhartsShadow.Config;

namespace UrquhartsShadow.Player
{
    /// <summary>Owner-only raycast interaction with hold-to-use support. Feeds the HUD prompt.</summary>
    public class PlayerInteractor : MonoBehaviour
    {
        [SerializeField] private LayerMask interactMask = ~0;

        private PlayerCharacter _player;
        private IInteractable _current;
        private float _holdTime;
        private bool _holding;

        public IInteractable Current => _current;
        public float HoldProgress => _current != null && _current.HoldSeconds > 0f ? Mathf.Clamp01(_holdTime / _current.HoldSeconds) : 0f;
        public event Action<IInteractable> TargetChanged;

        private void Awake() => _player = GetComponent<PlayerCharacter>();

        private void OnEnable()
        {
            _player.Input.InteractPressed += OnPressed;
            _player.Input.InteractReleased += OnReleased;
        }
        private void OnDisable()
        {
            _player.Input.InteractPressed -= OnPressed;
            _player.Input.InteractReleased -= OnReleased;
        }

        private void Update()
        {
            if (!_player.Vitals.IsAlive) { SetCurrent(null); return; }

            IInteractable found = null;
            if (!_player.IsSeated)
            {
                var cam = _player.CameraRoot;
                if (Physics.Raycast(cam.position, cam.forward, out var hit, GameConfig.Instance.Player.InteractRange, interactMask, QueryTriggerInteraction.Collide))
                {
                    found = hit.collider.GetComponentInParent<IInteractable>();
                    if (found != null && !found.CanInteract(_player)) found = null;
                }
            }
            else
            {
                // While seated the only interactable is the station itself (to stand up).
                found = _current;
            }
            SetCurrent(found);

            if (_holding && _current != null)
            {
                _holdTime += Time.deltaTime;
                if (_holdTime >= _current.HoldSeconds)
                {
                    _current.Interact(_player);
                    _holding = false;
                    _holdTime = 0f;
                }
            }
        }

        private void SetCurrent(IInteractable i)
        {
            if (ReferenceEquals(i, _current)) return;
            _current = i;
            _holdTime = 0f;
            _holding = false;
            TargetChanged?.Invoke(_current);
        }

        private void OnPressed()
        {
            if (_current == null) return;
            if (_current.HoldSeconds <= 0f) { _current.Interact(_player); return; }
            _holding = true;
            _holdTime = 0f;
        }

        private void OnReleased()
        {
            _holding = false;
            _holdTime = 0f;
        }

        /// <summary>Stations call this so the seated player can press Interact to leave.</summary>
        public void ForceTarget(IInteractable i) => SetCurrent(i);
    }
}
