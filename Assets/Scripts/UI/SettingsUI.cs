using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UrquhartsShadow.Settings;

namespace UrquhartsShadow.UI
{
    /// <summary>Settings panel shared by the title screen and the pause menu.</summary>
    public class SettingsUI : MonoBehaviour
    {
        [SerializeField] private Slider mouseSens;
        [SerializeField] private Slider padSens;
        [SerializeField] private Toggle invertY;
        [SerializeField] private Slider master;
        [SerializeField] private Slider music;
        [SerializeField] private Slider sfx;
        [SerializeField] private TMP_InputField playerName;
        [SerializeField] private Button backButton;
        [SerializeField] private TitleScreenUI title;

        private void OnEnable()
        {
            if (mouseSens) mouseSens.SetValueWithoutNotify(SettingsStore.MouseSensitivity);
            if (padSens) padSens.SetValueWithoutNotify(SettingsStore.GamepadSensitivity);
            if (invertY) invertY.SetIsOnWithoutNotify(SettingsStore.InvertY);
            if (master) master.SetValueWithoutNotify(SettingsStore.MasterVolume);
            if (music) music.SetValueWithoutNotify(SettingsStore.MusicVolume);
            if (sfx) sfx.SetValueWithoutNotify(SettingsStore.SfxVolume);
            if (playerName) playerName.SetTextWithoutNotify(SettingsStore.PlayerName);
        }

        private void Start()
        {
            if (mouseSens) mouseSens.onValueChanged.AddListener(v => SettingsStore.MouseSensitivity = v);
            if (padSens) padSens.onValueChanged.AddListener(v => SettingsStore.GamepadSensitivity = v);
            if (invertY) invertY.onValueChanged.AddListener(v => SettingsStore.InvertY = v);
            if (master) master.onValueChanged.AddListener(v => SettingsStore.MasterVolume = v);
            if (music) music.onValueChanged.AddListener(v => SettingsStore.MusicVolume = v);
            if (sfx) sfx.onValueChanged.AddListener(v => SettingsStore.SfxVolume = v);
            if (playerName) playerName.onEndEdit.AddListener(v => { if (!string.IsNullOrWhiteSpace(v)) SettingsStore.PlayerName = v.Trim(); });
            if (backButton) backButton.onClick.AddListener(() => { SettingsStore.Save(); if (title) title.ShowMain(); else gameObject.SetActive(false); });
        }

        private void OnDisable() => SettingsStore.Save();
    }
}
