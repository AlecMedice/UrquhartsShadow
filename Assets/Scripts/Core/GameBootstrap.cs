using UnityEngine;
using UnityEngine.SceneManagement;
using UrquhartsShadow.Config;

namespace UrquhartsShadow.Core
{
    /// <summary>
    /// MAIN ENTRY POINT. Lives alone in the "Bootstrap" scene (scene index 0).
    /// Loads config, warms singletons, applies saved settings and hands off to the Title scene.
    /// Everything persistent (NetworkManager, AudioManager, SessionManager) is instantiated from
    /// the PersistentSystems prefab here so no other scene has to care about ordering.
    /// </summary>
    public class GameBootstrap : MonoBehaviour
    {
        [Tooltip("Prefab containing NetworkManager, SessionManager, AudioManager, GameManager (DontDestroyOnLoad).")]
        [SerializeField] private GameObject persistentSystemsPrefab;

        private static bool _booted;

        private void Awake()
        {
            if (_booted)
            {
                Destroy(gameObject);
                return;
            }
            _booted = true;
            DontDestroyOnLoad(gameObject);

            Application.targetFrameRate = -1;
            QualitySettings.vSyncCount = 1;

            // Touch the config so a missing asset is reported immediately, not mid-night.
            var cfg = GameConfig.Instance;
            Debug.Log($"[Bootstrap] Config loaded. Night length {cfg.Night.NightDurationSeconds}s, {GameConstants.TotalNights} nights, {GameConstants.EvidenceToWin} evidence to win.");

            if (persistentSystemsPrefab != null)
            {
                var systems = Instantiate(persistentSystemsPrefab);
                systems.name = "PersistentSystems";
                DontDestroyOnLoad(systems);
            }
            else
            {
                Debug.LogWarning("[Bootstrap] No PersistentSystems prefab assigned. Assign one containing NetworkManager, SessionManager, AudioManager and GameManager.");
            }

            Settings.SettingsStore.ApplyAll();
        }

        private void Start()
        {
            SceneManager.LoadScene(GameConstants.SceneTitle);
        }
    }
}
