using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.SceneManagement;
using UrquhartsShadow.Config;
using UrquhartsShadow.Core;
using UrquhartsShadow.Settings;

namespace UrquhartsShadow.Networking
{
    /// <summary>
    /// Host / join flow in the style of R.E.P.O.: "Start a game" hosts and shows a 6-character code;
    /// "Join a game" takes a code. Uses Unity Multiplayer Services (Relay + Lobby via Sessions) so no port
    /// forwarding is needed. Solo play starts a local host with no relay.
    /// Lives on the PersistentSystems prefab next to NetworkManager.
    /// </summary>
    public class SessionManager : MonoBehaviour
    {
        private static SessionManager _instance;
        /// <summary>Returns a real null when the instance was destroyed (a stale static after Play, a scene unload).</summary>
        public static SessionManager Instance { get => _instance != null ? _instance : null; private set => _instance = value; }

        public enum Status { Offline, Initialising, Hosting, Joining, InSession, Error }
        public Status State { get; private set; } = Status.Offline;
        public string JoinCode { get; private set; }
        public string LastError { get; private set; }
        public bool IsSolo { get; private set; }
        public event Action<Status> StatusChanged;

        private ISession _session;
        private MatchSummary _lastSummary;
        public MatchSummary LastSummary => _lastSummary;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void Set(Status s) { State = s; StatusChanged?.Invoke(s); }

        private NetworkManager Net
        {
            get
            {
                var nm = NetworkManager.Singleton;
                if (nm == null) nm = GetComponent<NetworkManager>();
                if (nm == null) nm = FindFirstObjectByType<NetworkManager>();
                if (nm == null) { LastError = "No NetworkManager in the scene (PersistentSystems missing)."; Debug.LogError("[Session] " + LastError); Set(Status.Error); }
                return nm;
            }
        }

        private async Task EnsureServices()
        {
            if (UnityServices.State == ServicesInitializationState.Uninitialized)
                await UnityServices.InitializeAsync();
            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }

        // ---------------- Solo ----------------
        public void StartSolo()
        {
            IsSolo = true;
            JoinCode = null;
            var nm = Net;
            if (nm == null) return;
            var utp = nm.GetComponent<UnityTransport>();
            if (utp != null) utp.SetConnectionData("127.0.0.1", 7777);
            if (nm.StartHost())
            {
                HookSceneLoaded();
                Set(Status.InSession);
                nm.SceneManager.LoadScene(GameConstants.SceneLoch, LoadSceneMode.Single);
            }
            else { LastError = "Failed to start local host"; Set(Status.Error); }
        }

        // ---------------- Host ----------------
        public async void HostGame(bool isPrivate = false)
        {
            IsSolo = false;
            Set(Status.Initialising);
            try
            {
                await EnsureServices();
                Set(Status.Hosting);
                var options = new SessionOptions
                {
                    MaxPlayers = GameConstants.MaxPlayers,
                    IsPrivate = isPrivate,
                    IsLocked = false,
                    Name = $"{SettingsStore.PlayerName}'s expedition"
                }.WithRelayNetwork();
                _session = await MultiplayerService.Instance.CreateSessionAsync(options);
                JoinCode = _session.Code;
                Debug.Log($"[Session] Hosting. Join code {JoinCode}");
                HookSceneLoaded();
                Set(Status.InSession);
                // WithRelayNetwork() configures the transport and starts the host through NGO's session integration.
                var nm = Net; if (nm == null) return;
                if (!nm.IsListening) nm.StartHost();
                nm.SceneManager.LoadScene(GameConstants.SceneLoch, LoadSceneMode.Single);
            }
            catch (Exception e)
            {
                LastError = e.Message; Debug.LogException(e); Set(Status.Error);
            }
        }

        // ---------------- Join ----------------
        public async void JoinGame(string code)
        {
            IsSolo = false;
            Set(Status.Initialising);
            try
            {
                await EnsureServices();
                Set(Status.Joining);
                _session = await MultiplayerService.Instance.JoinSessionByCodeAsync(code.Trim().ToUpperInvariant());
                JoinCode = _session.Code;
                Set(Status.InSession);
                // Scene sync is handled by NGO's NetworkSceneManager once connected.
            }
            catch (Exception e)
            {
                LastError = e.Message; Debug.LogException(e); Set(Status.Error);
            }
        }

        private void HookSceneLoaded()
        {
            var nm = Net; if (nm == null || nm.SceneManager == null) return;
            nm.SceneManager.OnLoadEventCompleted -= OnLoadCompleted;
            nm.SceneManager.OnLoadEventCompleted += OnLoadCompleted;
        }

        private void OnLoadCompleted(string sceneName, LoadSceneMode mode, System.Collections.Generic.List<ulong> done, System.Collections.Generic.List<ulong> timedOut)
        {
            var nm = Net; if (nm == null) return;
            if (sceneName != GameConstants.SceneLoch || !nm.IsServer) return;
            // Give scene NetworkObjects a frame to register with GameManager, then start night 1.
            StartCoroutine(BeginAfterFrame());
        }

        private System.Collections.IEnumerator BeginAfterFrame()
        {
            yield return null;
            yield return null;
            GameManager.Instance?.BeginExpedition();
        }

        /// <summary>Host only: lobby "Start expedition" when players are gathered. Currently we load the loch immediately on host; keep for a lobby scene.</summary>
        public void StartExpeditionFromLobby()
        {
            var nm = Net; if (nm == null || !nm.IsServer) return;
            nm.SceneManager.LoadScene(GameConstants.SceneLoch, LoadSceneMode.Single);
        }

        // ---------------- Ending / leaving ----------------
        public void LoadEndingScene(MatchSummary summary)
        {
            _lastSummary = summary;
            var nm = Net;
            if (nm != null && nm.IsServer) nm.SceneManager.LoadScene(GameConstants.SceneEnding, LoadSceneMode.Single);
        }

        public async void Leave()
        {
            try
            {
                if (_session != null) { await _session.LeaveAsync(); _session = null; }
            }
            catch (Exception e) { Debug.LogWarning(e.Message); }
            var nm = NetworkManager.Singleton;
            if (nm != null && (nm.IsClient || nm.IsServer)) nm.Shutdown();
            JoinCode = null;
            Set(Status.Offline);
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            SceneManager.LoadScene(GameConstants.SceneTitle);
        }
    }
}
