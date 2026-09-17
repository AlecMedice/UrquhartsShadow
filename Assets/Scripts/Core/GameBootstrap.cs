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
        [Tooltip("Prefab containing NetworkManager, SessionManager, AudioManager (DontDestroyOnLoad). Falls back to Resources/PersistentSystems.")]
        [SerializeField] private GameObject persistentSystemsPrefab;

        public const string ResourcePersistentSystems = "PersistentSystems";

        /// <summary>
        /// Runs before the first scene loads, whichever scene that is, so pressing Play from Title or LochNess
        /// still has NetworkManager, SessionManager and AudioManager. No statics: it checks the live scene instead,
        /// which also makes it safe when Unity skips the domain reload between Play sessions.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void EnsureSystems()
        {
            Application.targetFrameRate = -1;
            QualitySettings.vSyncCount = 1;
            var cfg = GameConfig.Instance;
            Debug.Log($"[Bootstrap] Config loaded. Night length {cfg.Night.NightDurationSeconds}s, {GameConstants.TotalNights} nights, {GameConstants.EvidenceToWin} evidence to win.");
            Settings.SettingsStore.ApplyAll();
            SpawnSystemsIfMissing(null);
        }

        public static void SpawnSystemsIfMissing(GameObject prefab)
        {
            if (Object.FindFirstObjectByType<Networking.SessionManager>() != null) return;
            if (prefab == null) prefab = Resources.Load<GameObject>(ResourcePersistentSystems);
            if (prefab == null)
            {
                Debug.LogError("[Bootstrap] PersistentSystems prefab not found. Run Urquhart's Shadow > Setup > Build Greybox (All), or place it in Resources/PersistentSystems.");
                return;
            }
            var systems = Instantiate(prefab);
            systems.name = "PersistentSystems";
            DontDestroyOnLoad(systems);
        }

        private void Awake()
        {
            SpawnSystemsIfMissing(persistentSystemsPrefab);
        }

        private void Start()
        {
            SceneManager.LoadScene(GameConstants.SceneTitle);
        }
    }
}
