using System;
using Unity.Netcode;
using UnityEngine;
using UrquhartsShadow.Config;
using UrquhartsShadow.Environment;

namespace UrquhartsShadow.Core
{
    /// <summary>
    /// Random per-night weather with optional mid-night shifts. Drives fog, wave height, rain/lightning
    /// FX and how visible the moon is. Server rolls; clients replicate and blend visuals locally.
    /// </summary>
    public class WeatherManager : NetworkBehaviour
    {
        [Header("FX (optional)")]
        [SerializeField] private ParticleSystem rain;
        [SerializeField] private Light lightningLight;
        [SerializeField] private AudioSource windSource;
        [SerializeField] private AudioSource rainSource;
        [SerializeField] private AudioSource thunderSource;
        [SerializeField] private AudioClip[] thunderClips;
        [SerializeField] private OceanSurface ocean;

        public readonly NetworkVariable<WeatherType> Current = new NetworkVariable<WeatherType>(WeatherType.Clear);
        public event Action<WeatherType> WeatherChanged;

        /// <summary>0..1 how much the moon shows through (1 on a clear night).</summary>
        public float MoonVisibility { get; private set; } = 1f;
        public float FogDensity { get; private set; }
        public float WaveHeight { get; private set; }
        public bool IsStorm => Current.Value == WeatherType.Storm;

        private float _targetFog, _targetWave, _targetMoon;
        private float _nextChangeRoll;
        private float _nextLightning;

        public override void OnNetworkSpawn()
        {
            GameManager.Instance?.Register(this);
            Current.OnValueChanged += (_, w) => { ApplyTargets(w); WeatherChanged?.Invoke(w); };
            ApplyTargets(Current.Value);
            FogDensity = _targetFog; WaveHeight = _targetWave; MoonVisibility = _targetMoon;
            if (ocean == null) ocean = FindFirstObjectByType<OceanSurface>();
        }

        public void RollForNight(int night)
        {
            if (!IsServer) return;
            Current.Value = Roll();
            _nextChangeRoll = Time.time + GameConfig.Instance.Weather.WeatherChangeInterval;
            Debug.Log($"[Weather] Night {night}: {Current.Value}");
        }

        private WeatherType Roll()
        {
            var w = GameConfig.Instance.Weather.WeatherWeights;
            float total = 0f; foreach (var x in w) total += x;
            float r = UnityEngine.Random.value * total;
            for (int i = 0; i < w.Length; i++)
            {
                if (r < w[i]) return (WeatherType)Mathf.Min(i, 3);
                r -= w[i];
            }
            return WeatherType.Clear;
        }

        private void ApplyTargets(WeatherType w)
        {
            var c = GameConfig.Instance.Weather;
            switch (w)
            {
                case WeatherType.Clear: _targetFog = c.ClearNightFogDensity; _targetWave = c.ClearWaveHeight; _targetMoon = 1f; break;
                case WeatherType.Overcast: _targetFog = c.OvercastFogDensity; _targetWave = c.OvercastWaveHeight; _targetMoon = 0.35f; break;
                case WeatherType.Rain: _targetFog = c.RainFogDensity; _targetWave = c.RainWaveHeight; _targetMoon = 0.15f; break;
                case WeatherType.Storm: _targetFog = c.StormFogDensity; _targetWave = c.StormWaveHeight; _targetMoon = 0.05f; break;
            }
            bool wet = w == WeatherType.Rain || w == WeatherType.Storm;
            if (rain != null) { if (wet) rain.Play(); else rain.Stop(); }
            if (rainSource != null) rainSource.mute = !wet;
        }

        private void Update()
        {
            var c = GameConfig.Instance.Weather;
            if (IsServer && GameManager.Instance != null && GameManager.Instance.IsNightActive && Time.time >= _nextChangeRoll)
            {
                _nextChangeRoll = Time.time + c.WeatherChangeInterval;
                if (UnityEngine.Random.value < c.MidNightChangeChance)
                {
                    // Drift one step rather than jumping from Clear to Storm.
                    int cur = (int)Current.Value;
                    int next = Mathf.Clamp(cur + (UnityEngine.Random.value < 0.5f ? -1 : 1), 0, 3);
                    Current.Value = (WeatherType)next;
                }
            }

            float k = Time.deltaTime / Mathf.Max(0.01f, c.WeatherBlendSeconds);
            FogDensity = Mathf.MoveTowards(FogDensity, _targetFog, k * 0.05f);
            WaveHeight = Mathf.MoveTowards(WaveHeight, _targetWave, k * 2f);
            MoonVisibility = Mathf.MoveTowards(MoonVisibility, _targetMoon, k);

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = FogDensity;
            if (ocean != null) ocean.WaveHeight = WaveHeight;
            if (windSource != null) windSource.volume = Mathf.Lerp(0.2f, 1f, WaveHeight / Mathf.Max(0.01f, c.StormWaveHeight));

            if (IsStorm && Time.time >= _nextLightning) Lightning();
        }

        private void Lightning()
        {
            var c = GameConfig.Instance.Weather;
            _nextLightning = Time.time + UnityEngine.Random.Range(c.LightningMinInterval, c.LightningMaxInterval);
            if (lightningLight != null) StartCoroutine(Flash());
            if (thunderSource != null && thunderClips != null && thunderClips.Length > 0)
                thunderSource.PlayDelayed(UnityEngine.Random.Range(0.5f, 3f));
            // Lightning is a huge light: Nessie hates it and players briefly see the whole loch.
            GameManager.Instance?.Nessie?.OnBrightFlash(lightningLight != null ? lightningLight.transform.position : Vector3.zero, 9999f);
        }

        private System.Collections.IEnumerator Flash()
        {
            lightningLight.enabled = true;
            lightningLight.intensity = UnityEngine.Random.Range(4f, 9f);
            yield return new WaitForSeconds(0.08f);
            lightningLight.intensity *= 0.3f;
            yield return new WaitForSeconds(0.05f);
            lightningLight.intensity *= 3f;
            yield return new WaitForSeconds(0.1f);
            lightningLight.enabled = false;
        }

        /// <summary>Quality penalty applied to camera evidence for fog between two points.</summary>
        public float FogPenalty(float distance)
        {
            float visibility = Mathf.Exp(-FogDensity * FogDensity * distance * distance);
            return Mathf.Clamp01(1f - visibility) * GameConfig.Instance.Evidence.FogQualityPenalty;
        }
    }
}
