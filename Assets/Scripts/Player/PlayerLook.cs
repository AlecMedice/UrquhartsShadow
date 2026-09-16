using UnityEngine;
using UrquhartsShadow.Config;
using UrquhartsShadow.Settings;

namespace UrquhartsShadow.Player
{
    /// <summary>Mouse / right-stick look. Yaw on the body, pitch on the camera root.</summary>
    public class PlayerLook : MonoBehaviour
    {
        [SerializeField] private Transform cameraRoot;
        [SerializeField] private float gamepadCurve = 1.6f;

        private PlayerCharacter _player;
        private float _pitch;
        public bool LookLocked { get; set; }
        public float FovScale { get; set; } = 1f;
        public float SensitivityScale { get; set; } = 1f;

        private void Awake()
        {
            _player = GetComponent<PlayerCharacter>();
            if (cameraRoot == null && _player.Camera != null) cameraRoot = _player.Camera.transform;
        }

        private void LateUpdate()
        {
            if (LookLocked || _player.IsSeated || !_player.Vitals.IsAlive) return;
            var cfg = GameConfig.Instance.Player;
            var input = _player.Input;
            Vector2 look = input.Look;
            float invert = SettingsStore.InvertY ? -1f : 1f;

            float dx, dy;
            if (input.UsingGamepad)
            {
                Vector2 curved = new Vector2(Mathf.Sign(look.x) * Mathf.Pow(Mathf.Abs(look.x), gamepadCurve),
                                             Mathf.Sign(look.y) * Mathf.Pow(Mathf.Abs(look.y), gamepadCurve));
                dx = curved.x * cfg.GamepadSensitivity * SettingsStore.GamepadSensitivity * Time.deltaTime;
                dy = curved.y * cfg.GamepadSensitivity * SettingsStore.GamepadSensitivity * Time.deltaTime;
            }
            else
            {
                dx = look.x * cfg.MouseSensitivity * SettingsStore.MouseSensitivity;
                dy = look.y * cfg.MouseSensitivity * SettingsStore.MouseSensitivity;
            }
            dx *= SensitivityScale; dy *= SensitivityScale;

            transform.Rotate(0f, dx, 0f, Space.Self);
            _pitch = Mathf.Clamp(_pitch - dy * invert, -cfg.MaxLookPitch, cfg.MaxLookPitch);
            if (cameraRoot != null) cameraRoot.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

        public void SetPitch(float pitch) { _pitch = pitch; if (cameraRoot != null) cameraRoot.localRotation = Quaternion.Euler(_pitch, 0f, 0f); }
    }
}
