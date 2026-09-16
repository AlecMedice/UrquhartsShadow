using Unity.Netcode;
using UnityEngine;
using UrquhartsShadow.Config;
using UrquhartsShadow.Core;
using UrquhartsShadow.Nessie;
using UrquhartsShadow.Player;

namespace UrquhartsShadow.Tools
{
    /// <summary>
    /// Pilot chair on the main deck. Seated player sees the ROV camera feed and drives it:
    /// Move = thrust/strafe, Look X = yaw, Jump/Crouch = up/down, Record = film. Interact deploys/recovers.
    /// Footage on Nessie for RovRecordSeconds is evidence (she must be within the floodlight range).
    /// </summary>
    public class ROVStation : Station
    {
        [SerializeField] private ROV rov;
        [SerializeField] private Camera monitorCamera;

        public float ClipProgress { get; private set; }
        public float LastFrameQuality { get; private set; }
        public bool IsRecording { get; private set; }
        public ROV Rov => rov;

        private float _onTarget, _qualityAccum, _notify, _nextInputSend;
        private Camera _playerCam;
        private float _vertical;

        public override string GetPrompt(PlayerCharacter p) => LocalPlayerSeated ? "Leave ROV console" : (rov != null && rov.TetherCut.Value ? "ROV lost (recover at dawn)" : "Pilot ROV");
        public override bool CanInteract(PlayerCharacter p) => base.CanInteract(p) && rov != null && !rov.TetherCut.Value;

        protected override void OnSeatedLocal(PlayerCharacter p)
        {
            _playerCam = p.Camera;
            if (rov != null && rov.Camera != null) { rov.Camera.enabled = true; _playerCam.enabled = false; }
            p.Input.JumpPressed += Up; p.Input.PrimaryPressed += ToggleDeploy;
            DeployRpc(true);
        }

        protected override void OnLeftLocal(PlayerCharacter p)
        {
            if (rov != null && rov.Camera != null) { rov.Camera.enabled = false; _playerCam.enabled = true; }
            p.Input.JumpPressed -= Up; p.Input.PrimaryPressed -= ToggleDeploy;
            SendInputRpc(Vector2.zero, 0f, 0f);
        }

        protected override void OnVacatedServer(ulong id) { rov?.SetPilotInput(Vector2.zero, 0f, 0f); }

        private void Up() => _vertical = 1f;
        private void ToggleDeploy() => DeployRpc(!(rov != null && rov.Deployed.Value));

        private void Update()
        {
            if (!LocalPlayerSeated || Occupant == null || rov == null) { ClipProgress = 0f; return; }
            var input = Occupant.Input;
            float vertical = input.Crouch ? -1f : _vertical;
            _vertical = Mathf.MoveTowards(_vertical, 0f, Time.deltaTime * 3f);
            if (Time.time >= _nextInputSend)
            {
                _nextInputSend = Time.time + 0.05f;
                SendInputRpc(input.Move, vertical, Mathf.Clamp(input.Look.x * (input.UsingGamepad ? 1f : 0.1f), -1f, 1f));
            }

            var cfgT = GameConfig.Instance.Tools; var cfgE = GameConfig.Instance.Evidence;
            IsRecording = input.RecordHeld && rov.IsOperational;
            if (!IsRecording) { _onTarget = 0f; _qualityAccum = 0f; ClipProgress = 0f; return; }

            LastFrameQuality = CameraEvidenceSensor.Evaluate(rov.Camera, rov.Camera != null ? rov.Camera.fieldOfView : 70f, cfgT.RovLightRange * 1.5f, true, cfgT.RovLightRange);
            if (LastFrameQuality > 0f)
            {
                _onTarget += Time.deltaTime; _qualityAccum += LastFrameQuality * Time.deltaTime;
                if (Time.time > _notify) { _notify = Time.time + 0.25f; NotifyCameraRpc(); }
                ClipProgress = Mathf.Clamp01(_onTarget / cfgE.RovRecordSeconds);
                if (_onTarget >= cfgE.RovRecordSeconds)
                {
                    SubmitClipRpc(Mathf.Clamp01(_qualityAccum / _onTarget + 0.15f));
                    _onTarget = 0f; _qualityAccum = 0f; ClipProgress = 0f;
                }
            }
            else { _onTarget = Mathf.Max(0f, _onTarget - Time.deltaTime * 2f); ClipProgress = Mathf.Clamp01(_onTarget / cfgE.RovRecordSeconds); }
        }

        [Rpc(SendTo.Server)] private void SendInputRpc(Vector2 move, float vertical, float yaw) => rov?.SetPilotInput(move, vertical, yaw);
        [Rpc(SendTo.Server)] private void DeployRpc(bool deploy) => rov?.DeployServer(deploy);
        [Rpc(SendTo.Server)] private void NotifyCameraRpc() => NessieAI.Instance?.OnCameraPointedAtMe(rov != null ? rov.transform.position : transform.position);

        [Rpc(SendTo.Server)]
        private void SubmitClipRpc(float q)
        {
            var op = OccupantOnServer(); var nessie = NessieAI.Instance;
            if (op == null || nessie == null || rov == null || !rov.IsOperational) return;
            if (Vector3.Distance(rov.transform.position, nessie.Body.Head.position) > GameConfig.Instance.Tools.RovLightRange * 2f) return;
            var nt = GameManager.Instance?.NightCycle;
            op.EvidenceBag.AddServer(new PendingEvidence(EvidenceType.RovFootage, Mathf.Clamp01(q), "ROV footage", nt != null ? nt.NightElapsed : 0f));
            nessie.OnEvidenceCaptured(EvidenceType.RovFootage);
        }
    }
}
