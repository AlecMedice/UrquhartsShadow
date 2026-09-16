using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UrquhartsShadow.Config;
using UrquhartsShadow.Networking;
using UrquhartsShadow.Settings;

namespace UrquhartsShadow.UI
{
    /// <summary>Multiplayer sub-menu: Start a game (shows join code) or Join a game (enter code). R.E.P.O.-style.</summary>
    public class MultiplayerMenuUI : MonoBehaviour
    {
        [SerializeField] private Button hostButton;
        [SerializeField] private Button joinButton;
        [SerializeField] private Button backButton;
        [SerializeField] private TMP_InputField joinCodeInput;
        [SerializeField] private TMP_InputField playerNameInput;
        [SerializeField] private TMP_Dropdown difficultyDropdown;
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private TMP_Text joinCodeDisplay;
        [SerializeField] private TitleScreenUI title;

        private void OnEnable()
        {
            if (playerNameInput) playerNameInput.text = SettingsStore.PlayerName;
            if (difficultyDropdown)
            {
                difficultyDropdown.ClearOptions();
                difficultyDropdown.AddOptions(new System.Collections.Generic.List<string> { "Docile", "Wary", "Cunning", "Ancient" });
                difficultyDropdown.value = (int)SettingsStore.Difficulty;
            }
            if (SessionManager.Instance != null) SessionManager.Instance.StatusChanged += OnStatus;
            OnStatus(SessionManager.Instance != null ? SessionManager.Instance.State : SessionManager.Status.Offline);
        }

        private void OnDisable()
        {
            if (SessionManager.Instance != null) SessionManager.Instance.StatusChanged -= OnStatus;
        }

        private void Start()
        {
            if (hostButton) hostButton.onClick.AddListener(Host);
            if (joinButton) joinButton.onClick.AddListener(Join);
            if (backButton) backButton.onClick.AddListener(() => title?.ShowMain());
        }

        private void SaveInputs()
        {
            if (playerNameInput && !string.IsNullOrWhiteSpace(playerNameInput.text)) SettingsStore.PlayerName = playerNameInput.text.Trim();
            if (difficultyDropdown) SettingsStore.Difficulty = (DifficultyLevel)difficultyDropdown.value;
            SettingsStore.Save();
        }

        private void Host() { SaveInputs(); SessionManager.Instance?.HostGame(); }
        private void Join()
        {
            SaveInputs();
            if (joinCodeInput == null || joinCodeInput.text.Trim().Length < 4) { if (statusText) statusText.text = "Enter a join code."; return; }
            SessionManager.Instance?.JoinGame(joinCodeInput.text);
        }

        private void OnStatus(SessionManager.Status s)
        {
            var sm = SessionManager.Instance;
            if (statusText) statusText.text = s switch
            {
                SessionManager.Status.Initialising => "Connecting to services...",
                SessionManager.Status.Hosting => "Creating expedition...",
                SessionManager.Status.Joining => "Joining expedition...",
                SessionManager.Status.InSession => "Connected.",
                SessionManager.Status.Error => $"Error: {sm?.LastError}",
                _ => ""
            };
            if (joinCodeDisplay) joinCodeDisplay.text = string.IsNullOrEmpty(sm?.JoinCode) ? "" : $"JOIN CODE  {sm.JoinCode}";
            bool busy = s == SessionManager.Status.Initialising || s == SessionManager.Status.Hosting || s == SessionManager.Status.Joining;
            if (hostButton) hostButton.interactable = !busy;
            if (joinButton) joinButton.interactable = !busy;
        }
    }
}
