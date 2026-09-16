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
        public static SessionManager Instance { get; private set; }

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
            var utp = NetworkManager.Singleton.GetComponent<UnityTransport>();
            if (utp != null) utp.SetConnectionData("127.0.0.1", 7777);
            HookSceneLoaded();
            if (NetworkManager.Singleton.StartHost())
            {
                Set(Status.InSession);
                NetworkManager.Singleton.SceneManager.LoadScene(GameConstants.SceneLoch, LoadSceneMode.Single);
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
                NetworkManager.Singleton.SceneManager.LoadScene(GameConstants.SceneLoch, LoadSceneMode.Single);
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
            NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= OnLoadCompleted;
            NetworkManager.Singleton.SceneManager.OnLoadEventCompleted += OnLoadCompleted;
        }

        private void OnLoadCompleted(string sceneName, LoadSceneMode mode, System.Collections.Generic.List<ulong> done, System.Collections.Generic.List<ulong> timedOut)
        {
            if (sceneName != GameConstants.SceneLoch || !NetworkManager.Singleton.IsServer) return;
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
            if (!NetworkManager.Singleton.IsServer) return;
            NetworkManager.Singleton.SceneManager.LoadScene(GameConstants.SceneLoch, LoadSceneMode.Single);
        }

        // ---------------- Ending / leaving ----------------
        public void LoadEndingScene(MatchSummary summary)
        {
            _lastSummary = summary;
            if (NetworkManager.Singleton.IsServer)
                NetworkManager.Singleton.SceneManager.LoadScene(GameConstants.SceneEnding, LoadSceneMode.Single);
        }

        public async void Leave()
        {
            try
            {
                if (_session != null) { await _session.LeaveAsync(); _session = null; }
            }
            catch (Exception e) { Debug.LogWarning(e.Message); }
            if (NetworkManager.Singleton != null && (NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsServer))
                NetworkManager.Singleton.Shutdown();
            JoinCode = null;
            Set(Status.Offline);
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            SceneManager.LoadScene(GameConstants.SceneTitle);
        }
    }
}
