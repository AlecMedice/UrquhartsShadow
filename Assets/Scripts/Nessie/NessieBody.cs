using System;
using Unity.Netcode;
using UnityEngine;
using UrquhartsShadow.Config;
using UrquhartsShadow.Environment;

namespace UrquhartsShadow.Nessie
{
    /// <summary>
    /// The physical plesiosaur: server-side steering, depth control, breach/dive, animation and audio hooks.
    /// NessieAI decides *what* to do; this does *how*. Replicated with NetworkTransform (server authority).
    /// Animator parameters: Speed (float), Breach (trigger), Bite (trigger), Ram (trigger), Roar (trigger), Surfaced (bool).
    /// </summary>
    public class NessieBody : NetworkBehaviour
    {
        [SerializeField] private Animator animator;
        [SerializeField] private AudioSource voice;
        [SerializeField] private AudioSource splash;
        [SerializeField] private AudioClip[] callClips;
        [SerializeField] private AudioClip[] breachClips;
        [SerializeField] private AudioClip roarClip;
        [SerializeField] private Transform head;
        [SerializeField] private float bodyLength = 14f;

        public readonly NetworkVariable<bool> Surfaced = new NetworkVariable<bool>(false);

        public event Action BreachStarted;
        public event Action BreachEnded;

        private Vector3 _velocity;
        private float _targetDepth;
        private static readonly int SpeedHash = Animator.StringToHash("Speed");
        private static readonly int SurfacedHash = Animator.StringToHash("Surfaced");

        public float BodyLength => bodyLength;
        public Transform Head => head != null ? head : transform;
        public Vector3 Velocity => _velocity;
        public float CurrentDepth => OceanSurface.Instance != null ? OceanSurface.Instance.DepthAt(transform.position) : -transform.position.y;
        public bool IsNearSurface => CurrentDepth < 4f;
        /// <summary>True while the animal is exposed above the water: the evidence window.</summary>
        public bool IsBreached => Surfaced.Value;

        private NessieDifficultyProfile P => NessieAI.Instance != null ? NessieAI.Instance.Profile : null;

        private void Update()
        {
            if (animator != null)
            {
                animator.SetFloat(SpeedHash, _velocity.magnitude);
                animator.SetBool(SurfacedHash, Surfaced.Value);
            }
        }

        /// <summary>Server: steer toward a world point at a given depth below the surface.</summary>
        public void SteerTowards(Vector3 target, float depth, float speed, float dt)
        {
            if (!IsServer) return;
            var ocean = OceanSurface.Instance;
            float surfaceY = ocean != null ? ocean.GetHeightAt(target.x, target.z) : 0f;
            float floorY = LochBounds.Instance != null ? -LochBounds.Instance.FloorDepth + 5f : -150f;
            target.y = Mathf.Max(floorY, surfaceY - depth);
            if (LochBounds.Instance != null) target = LochBounds.Instance.Clamp(target);

            Vector3 to = target - transform.position;
            float dist = to.magnitude;
            if (dist < 0.5f) { _velocity = Vector3.Lerp(_velocity, Vector3.zero, dt * 2f); return; }

            Vector3 desiredDir = to / dist;
            float turn = (P != null ? P.TurnRate : 40f) * dt;
            Vector3 dir = _velocity.sqrMagnitude > 0.01f
                ? Vector3.RotateTowards(_velocity.normalized, desiredDir, turn * Mathf.Deg2Rad, 0f)
                : desiredDir;
            float targetSpeed = Mathf.Min(speed, dist * 1.5f);
            _velocity = Vector3.Lerp(_velocity, dir * targetSpeed, dt * 1.5f);

            transform.position += _velocity * dt;
            if (_velocity.sqrMagnitude > 0.01f)
            {
                Quaternion look = Quaternion.LookRotation(_velocity.normalized, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, look, dt * 2.5f);
            }
        }

        /// <summary>Server: hold position, gently drifting, at the given depth.</summary>
        public void Hover(float depth, float dt)
        {
            if (!IsServer) return;
            var pos = transform.position;
            float surfaceY = OceanSurface.Instance != null ? OceanSurface.Instance.GetHeightAt(pos.x, pos.z) : 0f;
            float targetY = surfaceY - depth;
            _velocity = Vector3.Lerp(_velocity, new Vector3(0f, (targetY - pos.y) * 0.5f, 0f), dt * 2f);
            transform.position += _velocity * dt;
        }

        public float DistanceTo(Vector3 p) => Vector3.Distance(transform.position, p);
        public float FlatDistanceTo(Vector3 p) => Vector2.Distance(new Vector2(transform.position.x, transform.position.z), new Vector2(p.x, p.z));

        public void SetSurfacedServer(bool surfaced)
        {
            if (!IsServer || Surfaced.Value == surfaced) return;
            Surfaced.Value = surfaced;
            if (surfaced) { BreachStarted?.Invoke(); PlayFxRpc(0); }
            else { BreachEnded?.Invoke(); PlayFxRpc(1); }
        }

        public void TriggerAnimServer(string trigger) { if (IsServer) AnimRpc(trigger); }
        public void CallServer() { if (IsServer) PlayFxRpc(2); }
        public void RoarServer() { if (IsServer) PlayFxRpc(3); }

        [Rpc(SendTo.ClientsAndHost)]
        private void AnimRpc(string trigger) { if (animator != null) animator.SetTrigger(trigger); }

        [Rpc(SendTo.ClientsAndHost)]
        private void PlayFxRpc(int kind)
        {
            switch (kind)
            {
                case 0: if (animator) animator.SetTrigger("Breach"); PlayRandom(splash, breachClips); break;
                case 1: PlayRandom(splash, breachClips); break;
                case 2: PlayRandom(voice, callClips); break;
                case 3: if (animator) animator.SetTrigger("Roar"); if (voice && roarClip) voice.PlayOneShot(roarClip); break;
            }
        }

        private static void PlayRandom(AudioSource src, AudioClip[] clips)
        {
            if (src == null || clips == null || clips.Length == 0) return;
            src.PlayOneShot(clips[UnityEngine.Random.Range(0, clips.Length)]);
        }
    }
}
