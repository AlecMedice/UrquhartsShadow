using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UrquhartsShadow.Config;
using UrquhartsShadow.Core;
using UrquhartsShadow.Environment;
using UrquhartsShadow.Tools;

namespace UrquhartsShadow.Boat
{
    /// <summary>
    /// The team's research vessel. Owns hull integrity, engine state, deck rail geometry (used by Nessie
    /// to find players near the edge), dawn resupply, and the night-5 sinking finale.
    /// Prefab: NetworkObject + NetworkTransform (server) + Rigidbody + Buoyancy + this, tagged "Vessel".
    /// Children: RailPoints (empties along the gunwale), Helm, Stations, SupplyStation, EvidenceLocker, RepairPoints.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class ResearchVessel : NetworkBehaviour
    {
        [Header("Wiring")]
        [SerializeField] private Transform[] railPoints;
        [SerializeField] private Buoyancy buoyancy;
        [SerializeField] private AudioSource engineAudio;
        [SerializeField] private AudioSource hullImpactAudio;
        [SerializeField] private AudioClip[] impactClips;
        [SerializeField] private ParticleSystem floodingFx;
        [SerializeField] private Transform waterlineMarker;
        [SerializeField] private TelephotoCamera telephoto;
        [SerializeField] private BeaconLauncher[] launchers;
        [SerializeField] private EdnaSampler[] samplers;
        [SerializeField] private ROV rov;
        [SerializeField] private SupplyStation supplyStation;
        [SerializeField] private SideScanSonar sonar;
        [SerializeField] private HydrophoneArray hydrophone;

        public readonly NetworkVariable<float> HullIntegrity = new NetworkVariable<float>(100f);
        public readonly NetworkVariable<bool> EngineOn = new NetworkVariable<bool>(true);
        public readonly NetworkVariable<float> Throttle = new NetworkVariable<float>(0f);   // -1..1
        public readonly NetworkVariable<float> Rudder = new NetworkVariable<float>(0f);     // -1..1
        public readonly NetworkVariable<bool> Sinking = new NetworkVariable<bool>(false);
        public readonly NetworkVariable<bool> LowerDeckFlooded = new NetworkVariable<bool>(false);

        public event Action<float, Vector3> HullHit;

        private Rigidbody _rb;
        public float EngineLoad => EngineOn.Value ? Mathf.Abs(Throttle.Value) : 0f;
        public float IntegrityFraction => HullIntegrity.Value / GameConfig.Instance.Vessel.MaxHullIntegrity;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            if (buoyancy == null) buoyancy = GetComponent<Buoyancy>();
        }

        public override void OnNetworkSpawn()
        {
            GameManager.Instance?.Register(this);
            if (IsServer) HullIntegrity.Value = GameConfig.Instance.Vessel.MaxHullIntegrity;
            if (buoyancy != null) buoyancy.Simulate = IsServer;
            _rb.isKinematic = !IsServer;
        }

        private void FixedUpdate()
        {
            if (!IsServer || Sinking.Value) return;
            var cfg = GameConfig.Instance.Vessel;
            if (EngineOn.Value)
            {
                float knotsToMs = 0.514f;
                Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
                Vector3 targetVel = forward * Throttle.Value * cfg.MaxSpeedKnots * knotsToMs;
                Vector3 vel = _rb.linearVelocity;
                Vector3 flat = new Vector3(vel.x, 0f, vel.z);
                Vector3 accel = (targetVel - flat) * cfg.ThrottleAcceleration;
                _rb.AddForce(accel, ForceMode.Acceleration);
                float turn = Rudder.Value * cfg.RudderTurnRate * Mathf.Clamp01(flat.magnitude / 1.5f);
                _rb.AddTorque(Vector3.up * turn * Mathf.Deg2Rad * _rb.mass * 0.2f, ForceMode.Force);
                if (Throttle.Value != 0f && UnityEngine.Random.value < Time.fixedDeltaTime * 0.3f)
                    GameManager.Instance?.Nessie?.OnNoise(transform.position, EngineLoad);
            }
            if (LochBounds.Instance != null && !LochBounds.Instance.Inside(transform.position, 20f))
            {
                Vector3 toCentre = (LochBounds.Instance.Center - transform.position); toCentre.y = 0f;
                _rb.AddForce(toCentre.normalized * 3f, ForceMode.Acceleration);
            }
            if (buoyancy != null)
            {
                var w = GameManager.Instance?.Weather;
                buoyancy.RockingScale = cfg.RockingAmplitude * (w != null && w.IsStorm ? cfg.StormRockingMultiplier : 1f);
            }
        }

        private void Update()
        {
            if (engineAudio) engineAudio.volume = Mathf.Lerp(0.15f, 0.7f, EngineLoad) * (EngineOn.Value ? 1f : 0f);
        }

        // ---------------- Helm ----------------
        public void SetHelmServer(float throttle, float rudder, bool engineOn)
        {
            if (!IsServer) return;
            Throttle.Value = Mathf.Clamp(throttle, -1f, 1f);
            Rudder.Value = Mathf.Clamp(rudder, -1f, 1f);
            EngineOn.Value = engineOn;
        }

        // ---------------- Rails ----------------
        public float DistanceToRail(Vector3 worldPos) => Vector3.Distance(worldPos, NearestRailPoint(worldPos));

        public Vector3 NearestRailPoint(Vector3 worldPos)
        {
            if (railPoints == null || railPoints.Length == 0) return transform.position;
            Vector3 best = railPoints[0].position; float bd = float.MaxValue;
            foreach (var r in railPoints)
            {
                if (r == null) continue;
                float d = Vector3.Distance(worldPos, r.position);
                if (d < bd) { bd = d; best = r.position; }
            }
            return best;
        }

        // ---------------- Damage ----------------
        public void ApplyRamDamageServer(float amount, Vector3 worldPoint, Vector3 impactVelocity)
        {
            if (!IsServer || Sinking.Value) return;
            HullIntegrity.Value = Mathf.Max(0f, HullIntegrity.Value - amount);
            _rb.AddForceAtPosition(impactVelocity.normalized * amount * 40f, worldPoint, ForceMode.Impulse);
            if (GameManager.Instance != null) GameManager.Instance.Stats.TotalHullDamage += amount;
            HitRpc(amount, worldPoint);

            // Everyone on deck is thrown by the impact.
            foreach (var p in GameManager.Instance.AlivePlayers())
            {
                Vector3 away = (p.transform.position - worldPoint); away.y = 0f;
                p.Vitals.KnockbackServer(away.normalized * amount * 0.35f + Vector3.up * 2f);
            }

            var cfg = GameConfig.Instance.Vessel;
            if (HullIntegrity.Value <= cfg.FloodingThreshold && !LowerDeckFlooded.Value) LowerDeckFlooded.Value = true;
            if (HullIntegrity.Value <= 0f && cfg.CanSinkBeforeFinale && GameManager.Instance.Phase.Value != GamePhase.Finale)
            {
                BeginSinkingSequence(25f);
                Invoke(nameof(NotifySunk), 25f);
            }
        }

        private void NotifySunk() => GameManager.Instance?.OnVesselSunk();

        [Rpc(SendTo.ClientsAndHost)]
        private void HitRpc(float amount, Vector3 point)
        {
            if (hullImpactAudio && impactClips != null && impactClips.Length > 0)
                hullImpactAudio.PlayOneShot(impactClips[UnityEngine.Random.Range(0, impactClips.Length)]);
            HullHit?.Invoke(amount, point);
        }

        public void RepairServer(float amount)
        {
            if (!IsServer || Sinking.Value) return;
            var cfg = GameConfig.Instance.Vessel;
            HullIntegrity.Value = Mathf.Min(cfg.MaxHullIntegrity, HullIntegrity.Value + amount);
            if (HullIntegrity.Value > cfg.FloodingThreshold + 10f) LowerDeckFlooded.Value = false;
        }

        // ---------------- Dawn ----------------
        public void OnDawnResupply()
        {
            if (!IsServer) return;
            var s = GameConfig.Instance.Supplies;
            supplyStation?.RestockServer(s.BatteriesRestockedPerDawn, s.RationsRestockedPerDawn);
            telephoto?.ReloadFilmServer();
            rov?.RestoreServer();
            sonar?.RechargeServer();
            hydrophone?.RechargeServer();
            HullIntegrity.Value = Mathf.Min(GameConfig.Instance.Vessel.MaxHullIntegrity, HullIntegrity.Value + 15f);
        }

        // ---------------- Finale ----------------
        public void BeginSinkingSequence(float seconds)
        {
            if (!IsServer || Sinking.Value) return;
            Sinking.Value = true;
            EngineOn.Value = false;
            StartCoroutine(SinkRoutine(seconds));
            SinkFxRpc();
        }

        private IEnumerator SinkRoutine(float seconds)
        {
            if (buoyancy != null) buoyancy.Simulate = false;
            _rb.isKinematic = true;
            Vector3 start = transform.position; Quaternion startRot = transform.rotation;
            Quaternion listRot = startRot * Quaternion.Euler(12f, 0f, 25f);
            float t = 0f;
            while (t < seconds)
            {
                t += Time.deltaTime;
                float k = t / seconds;
                transform.position = start + Vector3.down * Mathf.SmoothStep(0f, 14f, k);
                transform.rotation = Quaternion.Slerp(startRot, listRot, Mathf.SmoothStep(0f, 1f, k));
                yield return null;
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void SinkFxRpc() { if (floodingFx) floodingFx.Play(); }
    }
}
