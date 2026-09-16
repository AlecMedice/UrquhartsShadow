using System;
using Unity.Netcode;
using UnityEngine;
using UrquhartsShadow.Config;

namespace UrquhartsShadow.Core
{
    /// <summary>
    /// Drives the clock for one night and the dawn breather, and the lighting arc
    /// (dusk -> deep night -> pre-dawn). Server counts; clients get a replicated timer for the HUD.
    /// </summary>
    public class NightCycleManager : NetworkBehaviour
    {
        [Header("Scene lighting")]
        [SerializeField] private Light moonLight;
        [SerializeField] private Light sunLight;
        [SerializeField] private AnimationCurve nightLightCurve = AnimationCurve.EaseInOut(0, 0.6f, 1, 0.6f);
        [SerializeField] private Gradient skyGradient;

        public readonly NetworkVariable<float> TimeRemaining = new NetworkVariable<float>(0f);
        public readonly NetworkVariable<bool> IsDawn = new NetworkVariable<bool>(false);
        public readonly NetworkVariable<bool> GraceActive = new NetworkVariable<bool>(true);

        public event Action<int> OnNightBegan;
        public event Action OnDawnBegan;

        public int Night => GameManager.Instance != null ? GameManager.Instance.CurrentNight.Value : 0;
        public float NightDuration => GameConfig.Instance.Night.NightDurationSeconds;
        public float NightElapsed => IsDawn.Value ? NightDuration : NightDuration - TimeRemaining.Value;
        /// <summary>0 at dusk, 1 at dawn.</summary>
        public float NightProgress => Mathf.Clamp01(NightElapsed / NightDuration);

        private bool _running;

        public override void OnNetworkSpawn()
        {
            GameManager.Instance?.Register(this);
            if (moonLight == null) moonLight = RenderSettings.sun;
        }

        public void BeginNight(int night)
        {
            if (!IsServer) return;
            IsDawn.Value = false;
            GraceActive.Value = true;
            TimeRemaining.Value = NightDuration;
            _running = true;
            BroadcastNightBeganRpc(night);
        }

        public void BeginDawn()
        {
            if (!IsServer) return;
            IsDawn.Value = true;
            GraceActive.Value = false;
            TimeRemaining.Value = GameConfig.Instance.Night.DawnDurationSeconds;
            _running = true;
            BroadcastDawnBeganRpc();
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void BroadcastNightBeganRpc(int night) => OnNightBegan?.Invoke(night);

        [Rpc(SendTo.ClientsAndHost)]
        private void BroadcastDawnBeganRpc() => OnDawnBegan?.Invoke();

        private void Update()
        {
            if (IsServer && _running)
            {
                TimeRemaining.Value = Mathf.Max(0f, TimeRemaining.Value - Time.deltaTime);

                if (GraceActive.Value && !IsDawn.Value && NightElapsed >= GameConfig.Instance.Night.GracePeriodSeconds)
                    GraceActive.Value = false;

                if (TimeRemaining.Value <= 0f)
                {
                    _running = false;
                    if (IsDawn.Value) GameManager.Instance.OnDawnEnded();
                    else GameManager.Instance.OnNightEnded();
                }
            }

            UpdateLighting();
        }

        private void UpdateLighting()
        {
            float t = IsDawn.Value ? 1f : NightProgress;
            var cfg = GameConfig.Instance.Night;
            // Darkest in the middle of the night, a little lighter at dusk and pre-dawn.
            float darkness = Mathf.Sin(t * Mathf.PI);
            float scale = Mathf.Lerp(0.35f, cfg.MidnightLightScale, darkness);
            if (IsDawn.Value) scale = 0.6f;

            if (moonLight != null)
            {
                float weatherScale = GameManager.Instance?.Weather != null ? GameManager.Instance.Weather.MoonVisibility : 1f;
                moonLight.intensity = GameConfig.Instance.Weather.ClearMoonIntensity * nightLightCurve.Evaluate(t) * weatherScale;
                // Moon arcs across the sky over the night.
                moonLight.transform.rotation = Quaternion.Euler(Mathf.Lerp(15f, 165f, t), 210f, 0f);
            }
            if (sunLight != null)
            {
                sunLight.intensity = IsDawn.Value ? 0.9f : Mathf.Lerp(0.15f, 0f, Mathf.Clamp01(t * 6f));
                sunLight.transform.rotation = Quaternion.Euler(IsDawn.Value ? 8f : -5f, 30f, 0f);
            }
            RenderSettings.ambientIntensity = scale;
            if (skyGradient != null) RenderSettings.ambientSkyColor = skyGradient.Evaluate(t);
        }

        public string FormatClock()
        {
            // Nights run 20:00 -> 06:00 on the in-game clock for flavour.
            float hours = IsDawn.Value ? 6f : Mathf.Lerp(20f, 30f, NightProgress);
            int h = ((int)hours) % 24;
            int m = (int)((hours - Mathf.Floor(hours)) * 60f);
            return $"{h:00}:{m:00}";
        }
    }
}
