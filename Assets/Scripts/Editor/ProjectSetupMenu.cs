using System.IO;
using UnityEditor;
using UnityEngine;
using UrquhartsShadow.Config;

namespace UrquhartsShadow.Editor
{
    /// <summary>
    /// One-click creation of the config assets the runtime expects in Resources, plus the tags/layers
    /// GameConstants refers to. Run "Urquhart's Shadow > Setup > Create Config Assets" after opening the project.
    /// </summary>
    public static class ProjectSetupMenu
    {
        private const string ResourcesPath = "Assets/Resources";

        [MenuItem("Urquhart's Shadow/Setup/Create Config Assets")]
        public static void CreateConfigAssets()
        {
            Directory.CreateDirectory(ResourcesPath);
            Directory.CreateDirectory(Path.Combine(ResourcesPath, GameConstants.ResourceDifficultyFolder));

            string cfgPath = $"{ResourcesPath}/{GameConstants.ResourceGameConfig}.asset";
            if (AssetDatabase.LoadAssetAtPath<GameConfig>(cfgPath) == null)
            {
                AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<GameConfig>(), cfgPath);
                Debug.Log($"Created {cfgPath}");
            }

            foreach (DifficultyLevel level in System.Enum.GetValues(typeof(DifficultyLevel)))
            {
                string p = $"{ResourcesPath}/{GameConstants.ResourceDifficultyFolder}/{level}.asset";
                if (AssetDatabase.LoadAssetAtPath<NessieDifficultyProfile>(p) != null) continue;
                AssetDatabase.CreateAsset(NessieDifficultyProfile.CreatePreset(level), p);
                Debug.Log($"Created {p}");
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        /// <summary>
        /// Batch-mode entry for setup-windows.ps1, pass 1: things that need an editor restart to take effect
        /// (TextMeshPro resources, Input System handling). Pass 2 runs GreyboxBuilder.BuildAll.
        /// </summary>
        public static void FirstRunPrepare()
        {
            if (EditorApplication.isPlaying) { Debug.LogError("[Setup] Stop play mode first."); return; }
            SetInputHandlingToBoth();
            CreateTagsAndLayers();
            CreateConfigAssets();
            AssetDatabase.SaveAssets();
            // The TMP import is asynchronous; in batch mode we must keep the editor alive until it completes,
            // so this method does not rely on -quit. It exits the editor itself once the import is done (or times out).
            bool started = ImportTmpEssentials();
            if (!Application.isBatchMode) return;
            if (!started) { Debug.Log("[Setup] FirstRunPrepare complete."); EditorApplication.Exit(0); return; }
            double deadline = EditorApplication.timeSinceStartup + 180.0;
            void Finish(string why)
            {
                Debug.Log($"[Setup] FirstRunPrepare complete ({why}).");
                AssetDatabase.SaveAssets();
                EditorApplication.Exit(0);
            }
            AssetDatabase.importPackageCompleted += _ => Finish("TMP import completed");
            AssetDatabase.importPackageFailed += (_, msg) => Finish("TMP import failed: " + msg);
            AssetDatabase.importPackageCancelled += _ => Finish("TMP import cancelled");
            EditorApplication.update += () => { if (EditorApplication.timeSinceStartup > deadline) Finish("timeout waiting for TMP import"); };
        }

        [MenuItem("Urquhart's Shadow/Setup/Import TextMeshPro Essentials")]
        /// <summary>Returns true when an asynchronous import was started.</summary>
        public static bool ImportTmpEssentials()
        {
            if (Resources.Load("TMP Settings") != null) { Debug.Log("[Setup] TMP essentials already present."); return false; }
            if (EditorApplication.isPlaying) { Debug.LogError("[Setup] Stop play mode first."); return false; }
            // The essentials ship as a .unitypackage inside the uGUI package (Unity 6) or the legacy TMP package.
            string[] candidates =
            {
                "Packages/com.unity.ugui/Package Resources/TMP Essential Resources.unitypackage",
                "Packages/com.unity.textmeshpro/Package Resources/TMP Essential Resources.unitypackage",
            };
            foreach (var rel in candidates)
            {
                string full = Path.GetFullPath(rel);
                if (!File.Exists(full)) continue;
                AssetDatabase.ImportPackage(full, false);
                Debug.Log($"[Setup] Importing TextMeshPro essential resources from {rel}...");
                return true;
            }
            Debug.LogWarning("[Setup] TMP Essential Resources package not found. Use Window > TextMeshPro > Import TMP Essential Resources.");
            return false;
        }

        [MenuItem("Urquhart's Shadow/Setup/Set Input Handling (Both)")]
        public static void SetInputHandlingToBoth()
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
            if (assets == null || assets.Length == 0) return;
            var so = new SerializedObject(assets[0]);
            var prop = so.FindProperty("activeInputHandler");
            if (prop == null) { Debug.LogWarning("[Setup] activeInputHandler not found."); return; }
            if (prop.intValue != 2) { prop.intValue = 2; so.ApplyModifiedPropertiesWithoutUndo(); AssetDatabase.SaveAssets(); Debug.Log("[Setup] Active Input Handling set to Both (restart required)."); }
        }

        [MenuItem("Urquhart's Shadow/Setup/Create Tags And Layers")]
        public static void CreateTagsAndLayers()
        {
            foreach (var tag in new[] { GameConstants.TagPlayer, GameConstants.TagNessie, GameConstants.TagVessel, GameConstants.TagDeployable, GameConstants.TagWater })
                AddTag(tag);
            foreach (var layer in new[] { GameConstants.LayerPlayer, GameConstants.LayerNessie, GameConstants.LayerVessel, GameConstants.LayerDeployable, GameConstants.LayerInteractable, GameConstants.LayerWater })
                AddLayer(layer);
        }

        private static void AddTag(string tag)
        {
            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var tags = tagManager.FindProperty("tags");
            for (int i = 0; i < tags.arraySize; i++) if (tags.GetArrayElementAtIndex(i).stringValue == tag) return;
            tags.InsertArrayElementAtIndex(tags.arraySize);
            tags.GetArrayElementAtIndex(tags.arraySize - 1).stringValue = tag;
            tagManager.ApplyModifiedProperties();
        }

        private static void AddLayer(string layer)
        {
            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tagManager.FindProperty("layers");
            for (int i = 8; i < layers.arraySize; i++) if (layers.GetArrayElementAtIndex(i).stringValue == layer) return;
            for (int i = 8; i < layers.arraySize; i++)
            {
                var el = layers.GetArrayElementAtIndex(i);
                if (string.IsNullOrEmpty(el.stringValue)) { el.stringValue = layer; tagManager.ApplyModifiedProperties(); return; }
            }
            Debug.LogWarning($"No free layer slot for {layer}");
        }
    }
}
