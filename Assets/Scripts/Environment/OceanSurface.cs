using UnityEngine;

namespace UrquhartsShadow.Environment
{
    /// <summary>
    /// CPU-side Gerstner wave model that every system samples for "where is the water here":
    /// buoyancy for the boat and ROV, Nessie's breach position, and whether a player is submerged.
    /// The visual water (URP shader / Shader Graph) should use the same four waves so they agree;
    /// see docs/SETUP.md for hooking a material to <see cref="PushToMaterial"/>.
    /// </summary>
    [ExecuteAlways]
    public class OceanSurface : MonoBehaviour
    {
        private static OceanSurface _instance;
        /// <summary>Returns a real null when the instance was destroyed (a stale static after Play, a scene unload).</summary>
        public static OceanSurface Instance { get => _instance != null ? _instance : null; private set => _instance = value; }

        [Tooltip("Overall wave height (metres). Set by WeatherManager.")]
        public float WaveHeight = 0.4f;
        [Tooltip("Base water level in world Y.")]
        public float SeaLevel = 0f;

        [System.Serializable]
        public struct Wave
        {
            public Vector2 Direction;
            public float Steepness; // 0..1
            public float Wavelength;
        }

        public Wave[] Waves =
        {
            new Wave { Direction = new Vector2(1f, 0.3f), Steepness = 0.25f, Wavelength = 18f },
            new Wave { Direction = new Vector2(-0.4f, 1f), Steepness = 0.18f, Wavelength = 11f },
            new Wave { Direction = new Vector2(0.7f, -0.6f), Steepness = 0.12f, Wavelength = 6f },
            new Wave { Direction = new Vector2(-0.9f, -0.2f), Steepness = 0.08f, Wavelength = 3f },
        };

        [SerializeField] private Material waterMaterial;
        private static readonly int WaveHeightId = Shader.PropertyToID("_WaveHeight");
        private static readonly int WaveAId = Shader.PropertyToID("_WaveA");
        private static readonly int WaveBId = Shader.PropertyToID("_WaveB");
        private static readonly int WaveCId = Shader.PropertyToID("_WaveC");
        private static readonly int WaveDId = Shader.PropertyToID("_WaveD");

        private void OnEnable() => Instance = this;
        private void OnDisable() { if (Instance == this) Instance = null; }

        private void Update() => PushToMaterial();

        /// <summary>World-space height of the water at (x, z).</summary>
        public float GetHeightAt(float x, float z) => GetHeightAt(x, z, Time.time);

        public float GetHeightAt(float x, float z, float time)
        {
            float y = SeaLevel;
            foreach (var w in Waves)
            {
                float k = 2f * Mathf.PI / Mathf.Max(0.01f, w.Wavelength);
                float c = Mathf.Sqrt(9.81f / k);
                Vector2 d = w.Direction.normalized;
                float f = k * (Vector2.Dot(d, new Vector2(x, z)) - c * time);
                float a = w.Steepness / k;
                y += a * Mathf.Sin(f) * WaveHeight;
            }
            return y;
        }

        /// <summary>Approximate surface normal via finite differences.</summary>
        public Vector3 GetNormalAt(float x, float z)
        {
            const float e = 0.5f;
            float hL = GetHeightAt(x - e, z), hR = GetHeightAt(x + e, z);
            float hD = GetHeightAt(x, z - e), hU = GetHeightAt(x, z + e);
            return new Vector3(hL - hR, 2f * e, hD - hU).normalized;
        }

        public bool IsSubmerged(Vector3 worldPos, float margin = 0f) =>
            worldPos.y + margin < GetHeightAt(worldPos.x, worldPos.z);

        public float DepthAt(Vector3 worldPos) => GetHeightAt(worldPos.x, worldPos.z) - worldPos.y;

        public void PushToMaterial()
        {
            if (waterMaterial == null || Waves.Length < 4) return;
            waterMaterial.SetFloat(WaveHeightId, WaveHeight);
            waterMaterial.SetVector(WaveAId, Pack(Waves[0]));
            waterMaterial.SetVector(WaveBId, Pack(Waves[1]));
            waterMaterial.SetVector(WaveCId, Pack(Waves[2]));
            waterMaterial.SetVector(WaveDId, Pack(Waves[3]));
        }

        private static Vector4 Pack(Wave w) => new Vector4(w.Direction.x, w.Direction.y, w.Steepness, w.Wavelength);
    }
}
