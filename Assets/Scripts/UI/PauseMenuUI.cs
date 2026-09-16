using UnityEngine;
using UnityEngine.UI;
using UrquhartsShadow.Networking;
using UrquhartsShadow.Player;

namespace UrquhartsShadow.UI
{
    /// <summary>Escape / Start menu in the loch scene. Multiplayer never pauses time; it just frees the cursor.</summary>
    public class PauseMenuUI : MonoBehaviour
    {
        [SerializeField] private GameObject panel;
        [SerializeField] private GameObject settingsPanel;
        [SerializeField] private Button resumeButton;
        [SerializeField] private Button settingsButton;
        [SerializeField] private Button leaveButton;

        private PlayerCharacter _p;
        private bool _open;

        private void Start()
        {
            if (resumeButton) resumeButton.onClick.AddListener(Close);
            if (settingsButton) settingsButton.onClick.AddListener(() => settingsPanel?.SetActive(true));
            if (leaveButton) leaveButton.onClick.AddListener(() => SessionManager.Instance?.Leave());
            if (panel) panel.SetActive(false);
        }

        private void Update()
        {
            if (_p == null)
            {
                _p = PlayerCharacter.Local;
                if (_p != null) _p.Input.PausePressed += Toggle;
            }
        }

        private void Toggle() { if (_open) Close(); else Open(); }

        private void Open()
        {
            _open = true;
            if (panel) panel.SetActive(true);
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            _p?.Look.SetPitch(0f);
            if (_p != null) { _p.Look.LookLocked = true; _p.Movement.MovementLocked = true; }
        }

        private void Close()
        {
            _open = false;
            if (panel) panel.SetActive(false);
            if (settingsPanel) settingsPanel.SetActive(false);
            Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
            if (_p != null) { _p.Look.LookLocked = false; _p.Movement.MovementLocked = false; }
        }
    }
}
