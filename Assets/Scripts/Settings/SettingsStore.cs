using UnityEngine;
using UrquhartsShadow.Config;

namespace UrquhartsShadow.Settings
{
    /// <summary>Thin PlayerPrefs wrapper for user options. UI writes here; systems read here.</summary>
    public static class SettingsStore
    {
        public static float MouseSensitivity
        {
            get => PlayerPrefs.GetFloat(GameConstants.PrefMouseSensitivity, 1f);
            set => PlayerPrefs.SetFloat(GameConstants.PrefMouseSensitivity, value);
        }
        public static float GamepadSensitivity
        {
            get => PlayerPrefs.GetFloat(GameConstants.PrefGamepadSensitivity, 1f);
            set => PlayerPrefs.SetFloat(GameConstants.PrefGamepadSensitivity, value);
        }
        public static bool InvertY
        {
            get => PlayerPrefs.GetInt(GameConstants.PrefInvertY, 0) == 1;
            set => PlayerPrefs.SetInt(GameConstants.PrefInvertY, value ? 1 : 0);
        }
        public static float MasterVolume
        {
            get => PlayerPrefs.GetFloat(GameConstants.PrefMasterVolume, 1f);
            set { PlayerPrefs.SetFloat(GameConstants.PrefMasterVolume, value); AudioListener.volume = value; }
        }
        public static float MusicVolume
        {
            get => PlayerPrefs.GetFloat(GameConstants.PrefMusicVolume, 0.8f);
            set => PlayerPrefs.SetFloat(GameConstants.PrefMusicVolume, value);
        }
        public static float SfxVolume
        {
            get => PlayerPrefs.GetFloat(GameConstants.PrefSfxVolume, 1f);
            set => PlayerPrefs.SetFloat(GameConstants.PrefSfxVolume, value);
        }
        public static string PlayerName
        {
            get => PlayerPrefs.GetString(GameConstants.PrefPlayerName, "Researcher");
            set => PlayerPrefs.SetString(GameConstants.PrefPlayerName, value);
        }
        public static DifficultyLevel Difficulty
        {
            get => (DifficultyLevel)PlayerPrefs.GetInt(GameConstants.PrefDifficulty, (int)DifficultyLevel.Wary);
            set => PlayerPrefs.SetInt(GameConstants.PrefDifficulty, (int)value);
        }

        public static void ApplyAll()
        {
            AudioListener.volume = MasterVolume;
        }

        public static void Save() => PlayerPrefs.Save();
    }
}
