using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UrquhartsShadow.Core;
using UrquhartsShadow.Settings;
using UrquhartsShadow.Tools;

namespace UrquhartsShadow.Player
{
    /// <summary>
    /// Root of the player prefab. Aggregates the sub-systems, owns the network identity and
    /// enables/disables local-only components (camera, input) based on ownership.
    /// Prefab layout: PlayerCharacter (NetworkObject, NetworkTransform[Owner authority], CharacterController)
    ///   ├─ CameraRoot (PlayerLook target)  └─ Camera + AudioListener
    ///   ├─ HandSocket (held tool parent)
    ///   └─ Flashlight (Light)
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [RequireComponent(typeof(CharacterController))]
    public class PlayerCharacter : NetworkBehaviour
    {
        public static PlayerCharacter Local { get; private set; }

        [Header("Wiring")]
        [SerializeField] private Camera playerCamera;
        [SerializeField] private AudioListener listener;
        [SerializeField] private Transform handSocket;
        [SerializeField] private Light flashlight;
        [SerializeField] private GameObject[] localOnly;
        [SerializeField] private Renderer[] hideForOwner;

        public readonly NetworkVariable<FixedString32Bytes> PlayerName =
            new NetworkVariable<FixedString32Bytes>("Researcher", NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        public readonly NetworkVariable<bool> FlashlightOn =
            new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        public InputReader Input { get; private set; }
        public PlayerMovement Movement { get; private set; }
        public PlayerLook Look { get; private set; }
        public PlayerVitals Vitals { get; private set; }
        public PlayerInteractor Interactor { get; private set; }
        public PlayerEvidenceBag EvidenceBag { get; private set; }
        public PlayerInventory Inventory { get; private set; }
        public SpectatorCamera Spectator { get; private set; }
        public Camera Camera => playerCamera;
        public Transform HandSocket => handSocket;
        public Transform CameraRoot => playerCamera != null ? playerCamera.transform : transform;

        /// <summary>Tool currently in hand (phone by default). Null when seated at a station.</summary>
        public HandheldTool HeldTool { get; set; }
        public bool IsSeated { get; set; }

        private void Awake()
        {
            Input = GetComponent<InputReader>();
            Movement = GetComponent<PlayerMovement>();
            Look = GetComponent<PlayerLook>();
            Vitals = GetComponent<PlayerVitals>();
            Interactor = GetComponent<PlayerInteractor>();
            EvidenceBag = GetComponent<PlayerEvidenceBag>();
            Inventory = GetComponent<PlayerInventory>();
            Spectator = GetComponent<SpectatorCamera>();
        }

        public override void OnNetworkSpawn()
        {
            GameManager.Instance?.RegisterPlayer(this);
            bool owner = IsOwner;

            if (playerCamera != null) playerCamera.enabled = owner;
            if (listener != null) listener.enabled = owner;
            if (Input != null) Input.enabled = owner;
            // Toggle off then on so OnEnable subscriptions run after every Awake has completed.
            if (Movement != null) { Movement.enabled = false; Movement.enabled = owner; }
            if (Look != null) Look.enabled = owner;
            if (Interactor != null) { Interactor.enabled = false; Interactor.enabled = owner; }
            if (Spectator != null) Spectator.enabled = false;
            if (localOnly != null) foreach (var go in localOnly) if (go != null) go.SetActive(owner);
            if (hideForOwner != null && owner) foreach (var r in hideForOwner) if (r != null) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;

            if (owner)
            {
                Local = this;
                PlayerName.Value = SettingsStore.PlayerName;
                Input.FlashlightPressed += ToggleFlashlight;
                var phone = GetComponentInChildren<PhoneCamera>(true);
                if (phone != null) phone.Equip();
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            FlashlightOn.OnValueChanged += (_, on) => ApplyFlashlight(on);
            ApplyFlashlight(FlashlightOn.Value);
        }

        public override void OnNetworkDespawn()
        {
            GameManager.Instance?.UnregisterPlayer(this);
            if (Local == this) Local = null;
        }

        private void ToggleFlashlight()
        {
            if (!IsOwner || !Vitals.IsAlive) return;
            if (Inventory != null && Inventory.FlashlightBattery <= 0f && !FlashlightOn.Value) return;
            FlashlightOn.Value = !FlashlightOn.Value;
        }

        private void ApplyFlashlight(bool on)
        {
            if (flashlight != null) flashlight.enabled = on;
            if (on && IsServer) GameManager.Instance?.Nessie?.OnLightSource(flashlight != null ? flashlight.transform.position : transform.position, 0.3f);
        }

        private void Update()
        {
            if (!IsOwner) return;
            if (FlashlightOn.Value && Inventory != null)
            {
                Inventory.DrainFlashlight(Time.deltaTime);
                if (Inventory.FlashlightBattery <= 0f) FlashlightOn.Value = false;
            }
        }

        /// <summary>Server: called at dawn so per-night state resets (shivering, etc.).</summary>
        public void OnDawn()
        {
            Vitals?.OnDawnServer();
        }

        /// <summary>Server-side: the player was knocked or fell into the loch.</summary>
        public void EnterWaterServer()
        {
            Vitals?.SetInWaterServer(true);
        }

        public string DisplayName => PlayerName.Value.ToString();
    }
}
