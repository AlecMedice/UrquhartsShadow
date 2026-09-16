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
