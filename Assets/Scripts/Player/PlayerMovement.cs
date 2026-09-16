using UnityEngine;
using UrquhartsShadow.Config;
using UrquhartsShadow.Environment;

namespace UrquhartsShadow.Player
{
    /// <summary>
    /// Owner-authoritative first-person movement on a rocking deck. Uses CharacterController and
    /// "platform follow": while grounded on the vessel the player is carried with it, so walking
    /// across a pitching deck feels right without parenting NetworkObjects.
    /// Also handles swimming when the player ends up in the loch.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerMovement : MonoBehaviour
    {
        [SerializeField] private LayerMask groundMask = ~0;
        [SerializeField] private float groundProbe = 0.35f;

        private CharacterController _cc;
        private PlayerCharacter _player;
        private Vector3 _velocity;
        private bool _grounded;
        private Transform _platform;
        private Vector3 _platformLocalPos;
        private Quaternion _platformLocalRot;
        private float _standHeight;
        private bool _crouching;

        public bool IsGrounded => _grounded;
        public bool IsSprinting { get; private set; }
        public bool IsSwimming { get; private set; }
        public Vector3 HorizontalVelocity { get; private set; }
        public bool MovementLocked { get; set; }

        private void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _player = GetComponent<PlayerCharacter>();
            _standHeight = _cc.height;
        }

        private void OnEnable()
        {
            if (_player.Input != null) _player.Input.JumpPressed += OnJump;
        }
        private void OnDisable()
        {
            if (_player.Input != null) _player.Input.JumpPressed -= OnJump;
        }

        private void OnJump()
        {
            if (MovementLocked || !_grounded || IsSwimming || _crouching) return;
            var cfg = GameConfig.Instance.Player;
            _velocity.y = Mathf.Sqrt(cfg.JumpHeight * -2f * cfg.Gravity);
            _grounded = false;
        }

        private void Update()
        {
            if (!_player.Vitals.IsAlive || _player.IsSeated || MovementLocked)
            {
                FollowPlatform();
                return;
            }

            var cfg = GameConfig.Instance.Player;
            var input = _player.Input;
            var ocean = OceanSurface.Instance;

            // Swimming check: capsule centre below the water surface.
            IsSwimming = ocean != null && ocean.IsSubmerged(transform.position + Vector3.up * 0.9f);
            if (IsSwimming) { Swim(cfg, input, ocean); return; }

            FollowPlatform();
            ProbeGround();

            // Crouch
            bool wantCrouch = input.Crouch;
            if (wantCrouch != _crouching)
            {
                _crouching = wantCrouch;
                _cc.height = _crouching ? _standHeight * 0.55f : _standHeight;
                _cc.center = new Vector3(0f, _cc.height * 0.5f, 0f);
            }

            IsSprinting = input.Sprint && input.Move.y > 0.1f && !_crouching && _player.Vitals.CanSprint;
            float speed = _crouching ? cfg.CrouchSpeed : (IsSprinting ? cfg.SprintSpeed : cfg.WalkSpeed);
            if (_player.Vitals.IsExhausted) speed *= cfg.ExhaustedSpeedScale;
            speed *= _player.Vitals.SpeedMultiplier;

            Vector3 wish = transform.right * input.Move.x + transform.forward * input.Move.y;
            if (wish.sqrMagnitude > 1f) wish.Normalize();
            HorizontalVelocity = wish * speed;

            if (_grounded && _velocity.y < 0f) _velocity.y = -2f;
            _velocity.y += cfg.Gravity * Time.deltaTime;

            _cc.Move((HorizontalVelocity + Vector3.up * _velocity.y) * Time.deltaTime);
        }

        private void Swim(PlayerSettings cfg, InputReader input, OceanSurface ocean)
        {
            _platform = null;
            _grounded = false;
            Vector3 wish = transform.right * input.Move.x + transform.forward * input.Move.y;
            wish.y = 0f;
            if (wish.sqrMagnitude > 1f) wish.Normalize();
            // Bob to the surface.
            float surface = ocean.GetHeightAt(transform.position.x, transform.position.z);
            float targetY = surface - 1.2f;
            float dy = Mathf.Clamp(targetY - transform.position.y, -1f, 1f) * 2f;
            _velocity = new Vector3(0f, dy, 0f);
            HorizontalVelocity = wish * cfg.SwimSpeed;
            _cc.Move((HorizontalVelocity + _velocity) * Time.deltaTime);
        }

        private void ProbeGround()
        {
            Vector3 origin = transform.position + Vector3.up * 0.1f;
            if (Physics.SphereCast(origin, _cc.radius * 0.9f, Vector3.down, out var hit, groundProbe + 0.1f, groundMask, QueryTriggerInteraction.Ignore))
            {
                _grounded = true;
                var root = hit.collider.attachedRigidbody != null ? hit.collider.attachedRigidbody.transform : hit.collider.transform.root;
                if (root.CompareTag(GameConstants.TagVessel))
                {
                    if (_platform != root) _platform = root;
                    _platformLocalPos = _platform.InverseTransformPoint(transform.position);
                    _platformLocalRot = Quaternion.Inverse(_platform.rotation) * transform.rotation;
                }
                else _platform = null;
            }
            else
            {
                _grounded = _cc.isGrounded;
                if (!_grounded) _platform = null;
            }
        }

        /// <summary>Carry the player with the deck (position and yaw) before applying their own movement.</summary>
        private void FollowPlatform()
        {
            if (_platform == null) return;
            Vector3 targetPos = _platform.TransformPoint(_platformLocalPos);
            Quaternion targetRot = _platform.rotation * _platformLocalRot;
            Vector3 delta = targetPos - transform.position;
            // Only take the yaw from the platform; pitch/roll would tilt the player capsule.
            float yaw = targetRot.eulerAngles.y - transform.eulerAngles.y;
            transform.Rotate(0f, yaw, 0f, Space.World);
            _cc.Move(delta);
            _platformLocalPos = _platform.InverseTransformPoint(transform.position);
            _platformLocalRot = Quaternion.Inverse(_platform.rotation) * transform.rotation;
        }

        /// <summary>Server-driven shove (Nessie deck strike, storm lurch). Called through PlayerVitals RPC on owner.</summary>
        public void ApplyKnockback(Vector3 worldImpulse)
        {
            _platform = null;
            _grounded = false;
            _velocity.y = Mathf.Max(_velocity.y, worldImpulse.y);
            StartCoroutine(KnockRoutine(new Vector3(worldImpulse.x, 0f, worldImpulse.z)));
        }

        private System.Collections.IEnumerator KnockRoutine(Vector3 horizontal)
        {
            float t = 0f;
            while (t < 0.45f)
            {
                _cc.Move(horizontal * (1f - t / 0.45f) * Time.deltaTime);
                t += Time.deltaTime;
                yield return null;
            }
        }

        public void Teleport(Vector3 pos, Quaternion rot)
        {
            _cc.enabled = false;
            transform.SetPositionAndRotation(pos, rot);
            _cc.enabled = true;
            _velocity = Vector3.zero;
            _platform = null;
        }
    }
}
