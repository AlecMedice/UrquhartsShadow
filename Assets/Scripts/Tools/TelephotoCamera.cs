using Unity.Netcode;
using UnityEngine;
using UrquhartsShadow.Config;
using UrquhartsShadow.Core;
using UrquhartsShadow.Nessie;
using UrquhartsShadow.Player;

namespace UrquhartsShadow.Tools
{
    /// <summary>
    /// The one 35mm telephoto on the main deck mount. Better evidence than a phone, needs less time on
    /// target, but the photographer must dial exposure to match the light before the 2-second breach ends.
    /// Move (W/S or stick Y) adjusts exposure; Primary fires the shutter. Film is finite.
    /// </summary>
    public class TelephotoCamera : Station
    {
        [SerializeField] private Camera scopeCamera;
        [SerializeField] private AudioSource shutter;
        [SerializeField] private Light flashBulb;

        public readonly NetworkVariable<int> FilmRemaining = new NetworkVariable<int>(24);

        /// <summary>Photographer's current exposure setting 0..1.</summary>
        public float Exposure { get; private set; } = 0.5f;
        /// <summary>Ideal exposure for current conditions 0..1 (shown as a needle on the HUD).</summary>
        public float IdealExposure { get; private set; } = 0.5f;
        public float LastFrameQuality { get; private set; }
        public float ExposureProgress { get; private set; }

        private float _onTarget;
        private float _notify;
        private Camera _playerCam;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (IsServer) FilmRemaining.Value = GameConfig.Instance.Tools.FilmRollShots;
        }

        protected override void OnSeatedLocal(PlayerCharacter p)
        {
            _playerCam = p.Camera;
            if (scopeCamera != null) { scopeCamera.enabled = true; _playerCam.enabled = false; }
            p.Input.PrimaryPressed += Shoot;
        }

        protected override void OnLeftLocal(PlayerCharacter p)
        {
            if (scopeCamera != null) { scopeCamera.enabled = false; _playerCam.enabled = true; }
            p.Input.PrimaryPressed -= Shoot;
        }

        private void Update()
        {
            if (!LocalPlayerSeated || Occupant == null) return;
            var cfgT = GameConfig.Instance.Tools;
            var cfgE = GameConfig.Instance.Evidence;

            // Aim: look input pans the mount.
            Vector2 look = Occupant.Input.Look * (Occupant.Input.UsingGamepad ? Time.deltaTime * 40f : 0.05f);
            if (viewPoint != null)
            {
                viewPoint.Rotate(0f, look.x, 0f, Space.World);
                float pitch = viewPoint.localEulerAngles.x; if (pitch > 180f) pitch -= 360f;
                pitch = Mathf.Clamp(pitch - look.y, -20f, 30f);
                viewPoint.localEulerAngles = new Vector3(pitch, viewPoint.localEulerAngles.y, 0f);
            }

            // Exposure dial.
            Exposure = Mathf.Clamp01(Exposure + Occupant.Input.Move.y * cfgT.ExposureAdjustSpeed * Time.deltaTime);
            var w = GameManager.Instance?.Weather;
            IdealExposure = w != null ? Mathf.Lerp(0.85f, 0.35f, w.MoonVisibility) : 0.6f;

            var cam = scopeCamera != null ? scopeCamera : _playerCam;
            LastFrameQuality = CameraEvidenceSensor.Evaluate(cam, cfgT.TelephotoFov, cfgT.TelephotoMaxRange, false);
            if (LastFrameQuality > 0f)
            {
                _onTarget += Time.deltaTime;
                if (Time.time > _notify) { _notify = Time.time + 0.25f; NotifyCameraRpc(); }
            }
            else _onTarget = 0f;
            ExposureProgress = Mathf.Clamp01(_onTarget / cfgE.TelephotoExposureSeconds);
        }

        private void Shoot()
        {
            if (!LocalPlayerSeated || FilmRemaining.Value <= 0) return;
            if (shutter) shutter.Play();
            if (flashBulb) StartCoroutine(Flash());
            var cfgE = GameConfig.Instance.Evidence;
            var cfgT = GameConfig.Instance.Tools;
            float exposureError = Mathf.Abs(Exposure - IdealExposure);
            float exposureScore = 1f - Mathf.Clamp01(exposureError / (cfgT.ExposureTolerance * 2f));
            bool enoughTime = _onTarget >= cfgE.TelephotoExposureSeconds;
            float q = enoughTime ? Mathf.Clamp01((LastFrameQuality + cfgE.TelephotoQualityBonus) * exposureScore) : 0f;
            ShootRpc(q);
        }

        private System.Collections.IEnumerator Flash()
        {
            flashBulb.enabled = true;
            yield return new WaitForSeconds(0.06f);
            flashBulb.enabled = false;
        }

        [Rpc(SendTo.Server)]
        private void NotifyCameraRpc() => NessieAI.Instance?.OnCameraPointedAtMe(transform.position);

        [Rpc(SendTo.Server)]
        private void ShootRpc(float clientQuality)
        {
            if (FilmRemaining.Value <= 0) return;
            FilmRemaining.Value--;
            // The flash is a bright light; she notices.
            NessieAI.Instance?.OnBrightFlash(transform.position, GameConfig.Instance.Tools.TelephotoMaxRange * 0.5f);
            var occupant = OccupantOnServer();
            if (occupant == null || clientQuality <= 0f) return;
            var nessie = NessieAI.Instance;
            if (nessie == null || !nessie.Body.IsBreached) return;
            var nt = GameManager.Instance?.NightCycle;
            occupant.EvidenceBag.AddServer(new PendingEvidence(EvidenceType.TelephotoPhoto, Mathf.Clamp01(clientQuality), "35mm frame", nt != null ? nt.NightElapsed : 0f));
            nessie.OnEvidenceCaptured(EvidenceType.TelephotoPhoto);
        }

        public void ReloadFilmServer() { if (IsServer) FilmRemaining.Value = GameConfig.Instance.Tools.FilmRollShots; }
    }
}
