using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UrquhartsShadow.Config;

namespace UrquhartsShadow.Player
{
    /// <summary>
    /// Wraps the PlayerControls input asset so gameplay code never touches bindings directly.
    /// Keyboard+mouse and gamepad (Xbox / DualSense) both feed the same actions.
    /// </summary>
    public class InputReader : MonoBehaviour
    {
        [SerializeField] private InputActionAsset actions;

        public Vector2 Move { get; private set; }
        public Vector2 Look { get; private set; }
        public bool Sprint { get; private set; }
        public bool Crouch { get; private set; }
        public bool RecordHeld { get; private set; }
        public bool PrimaryHeld { get; private set; }
        public bool UsingGamepad { get; private set; }

        public event Action JumpPressed;
        public event Action InteractPressed;
        public event Action InteractReleased;
        public event Action PrimaryPressed;
        public event Action FlashlightPressed;
        public event Action DropPressed;
        public event Action PausePressed;
        public event Action SpectateNext;
        public event Action SpectatePrev;
        public event Action SpectateToggleView;

        private InputActionMap _player;
        private InputAction _move, _look, _sprint, _crouch, _record, _primary;

        private void Awake()
        {
            if (actions == null)
            {
                Debug.LogError("[InputReader] Assign PlayerControls.inputactions.");
                enabled = false;
                return;
            }
            _player = actions.FindActionMap(GameConstants.MapPlayer, true);
            _move = _player.FindAction(GameConstants.ActionMove, true);
            _look = _player.FindAction(GameConstants.ActionLook, true);
            _sprint = _player.FindAction(GameConstants.ActionSprint, true);
            _crouch = _player.FindAction(GameConstants.ActionCrouch, true);
            _record = _player.FindAction(GameConstants.ActionRecord, true);
            _primary = _player.FindAction(GameConstants.ActionPrimary, true);

            Bind(GameConstants.ActionJump, () => JumpPressed?.Invoke());
            Bind(GameConstants.ActionInteract, () => InteractPressed?.Invoke(), () => InteractReleased?.Invoke());
            Bind(GameConstants.ActionPrimary, () => PrimaryPressed?.Invoke());
            Bind(GameConstants.ActionFlashlight, () => FlashlightPressed?.Invoke());
            Bind(GameConstants.ActionDrop, () => DropPressed?.Invoke());
            Bind(GameConstants.ActionPause, () => PausePressed?.Invoke());
            Bind(GameConstants.ActionSpectateNext, () => SpectateNext?.Invoke());
            Bind(GameConstants.ActionSpectatePrev, () => SpectatePrev?.Invoke());
            Bind(GameConstants.ActionSpectateToggleView, () => SpectateToggleView?.Invoke());
        }

        private void Bind(string name, Action performed, Action canceled = null)
        {
            var a = _player.FindAction(name, false);
            if (a == null) { Debug.LogWarning($"[InputReader] Missing action {name}"); return; }
            a.performed += ctx => { UsingGamepad = ctx.control.device is Gamepad; performed(); };
            if (canceled != null) a.canceled += _ => canceled();
        }

        private void OnEnable() => _player?.Enable();
        private void OnDisable() => _player?.Disable();

        private void Update()
        {
            if (_player == null) return;
            Move = _move.ReadValue<Vector2>();
            Look = _look.ReadValue<Vector2>();
            if (_look.activeControl != null) UsingGamepad = _look.activeControl.device is Gamepad;
            Sprint = _sprint.IsPressed();
            Crouch = _crouch.IsPressed();
            RecordHeld = _record.IsPressed();
            PrimaryHeld = _primary.IsPressed();
        }

        public void SetGameplayEnabled(bool on)
        {
            if (_player == null) return;
            if (on) _player.Enable(); else _player.Disable();
            Move = Vector2.zero; Look = Vector2.zero;
        }
    }
}
