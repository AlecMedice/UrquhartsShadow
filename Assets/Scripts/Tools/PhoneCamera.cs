using Unity.Netcode;
using UnityEngine;
using UrquhartsShadow.Config;
using UrquhartsShadow.Core;
using UrquhartsShadow.Nessie;

namespace UrquhartsShadow.Tools
{
    /// <summary>
    /// Everyone's phone. Hold Record (right mouse / LT) to film. A clip counts once Nessie has been
    /// in frame for PhoneRecordSeconds; quality is lower than the 35mm and it drains the phone battery.
    /// </summary>
    public class PhoneCamera : HandheldTool
    {
        [SerializeField] private GameObject recordingIndicator;
        [SerializeField] private AudioSource beep;

        public override string ToolName => "Phone";
        public bool IsRecording { get; private set; }
        public float ClipProgress { get; private set; }
        public float LastFrameQuality { get; private set; }

        private float _onTarget;
        private float _qualityAccum;
        private float _cameraOnMeNotify;

        private void Update()
        {
            if (!OwnerCanUse) { StopRecording(); return; }
            var cfgT = GameConfig.Instance.Tools;
            var cfgE = GameConfig.Instance.Evidence;
            bool wants = Player.Input.RecordHeld && Player.Inventory.PhoneBattery > 0f;

            if (wants && !IsRecording) StartRecording();
            if (!wants && IsRecording) StopRecording();
            if (!IsRecording) return;

            Player.Inventory.DrainPhone(Time.deltaTime);
            LastFrameQuality = CameraEvidenceSensor.Evaluate(Player.Camera, cfgT.PhoneFov, cfgT.PhoneMaxRange, false);

            if (LastFrameQuality > 0f)
            {
                _onTarget += Time.deltaTime;
                _qualityAccum += LastFrameQuality * Time.deltaTime;
                if (Time.time > _cameraOnMeNotify) { _cameraOnMeNotify = Time.time + 0.25f; NotifyCameraRpc(); }
                ClipProgress = Mathf.Clamp01(_onTarget / cfgE.PhoneRecordSeconds);
                if (_onTarget >= cfgE.PhoneRecordSeconds)
                {
                    float q = Mathf.Clamp01(cfgT.PhoneBaseQuality * (_qualityAccum / _onTarget) / 0.65f);
                    SubmitClipRpc(q);
                    _onTarget = 0f; _qualityAccum = 0f; ClipProgress = 0f;
                }
            }
            else
            {
                // Losing her from frame decays the clip rather than resetting it outright.
                _onTarget = Mathf.Max(0f, _onTarget - Time.deltaTime * 2f);
                ClipProgress = Mathf.Clamp01(_onTarget / cfgE.PhoneRecordSeconds);
            }
        }

        private void StartRecording()
        {
            IsRecording = true;
            _onTarget = 0f; _qualityAccum = 0f;
            if (recordingIndicator) recordingIndicator.SetActive(true);
            if (beep) beep.Play();
        }

        private void StopRecording()
        {
            if (!IsRecording) return;
            IsRecording = false;
            ClipProgress = 0f;
            if (recordingIndicator) recordingIndicator.SetActive(false);
        }

        [Rpc(SendTo.Server)]
        private void NotifyCameraRpc() => NessieAI.Instance?.OnCameraPointedAtMe(Player.CameraRoot.position);

        [Rpc(SendTo.Server)]
        private void SubmitClipRpc(float clientQuality)
        {
            var nessie = NessieAI.Instance;
            if (nessie == null) return;
            // Server sanity: she must be surfaced (or just was) and within phone range of this player.
            float dist = Vector3.Distance(Player.CameraRoot.position, nessie.Body.Head.position);
            if (dist > GameConfig.Instance.Tools.PhoneMaxRange * 1.2f) return;
            float q = Mathf.Clamp01(clientQuality);
            var nt = GameManager.Instance?.NightCycle;
            Player.EvidenceBag.AddServer(new PendingEvidence(EvidenceType.PhoneVideo, q, "Phone clip", nt != null ? nt.NightElapsed : 0f));
            nessie.OnEvidenceCaptured(EvidenceType.PhoneVideo);
        }
    }
}
