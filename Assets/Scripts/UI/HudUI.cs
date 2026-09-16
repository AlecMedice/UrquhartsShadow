using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UrquhartsShadow.Audio;
using UrquhartsShadow.Config;
using UrquhartsShadow.Core;
using UrquhartsShadow.Player;
using UrquhartsShadow.Tools;

namespace UrquhartsShadow.UI
{
    /// <summary>
    /// In-game HUD for the local player: vitals, night clock, evidence tally, interaction prompt,
    /// recording indicator, station overlays (exposure needle, hydrophone filter, ROV clip), and
    /// spectator info. Toasts for captures, rejections and losses.
    /// </summary>
    public class HudUI : MonoBehaviour
    {
        [Header("Vitals")]
        [SerializeField] private Image healthBar;
        [SerializeField] private Image energyBar;
        [SerializeField] private TMP_Text batteryText;
        [SerializeField] private GameObject coldWarning;

        [Header("Expedition")]
        [SerializeField] private TMP_Text nightText;
        [SerializeField] private TMP_Text clockText;
        [SerializeField] private TMP_Text evidenceText;
        [SerializeField] private TMP_Text pendingText;
        [SerializeField] private TMP_Text weatherText;
        [SerializeField] private TMP_Text hullText;

        [Header("Interaction")]
        [SerializeField] private TMP_Text promptText;
        [SerializeField] private Image holdProgress;

        [Header("Recording")]
        [SerializeField] private GameObject recIndicator;
        [Tooltip("Stretched Image using the AnalogOverlay shader; shown while any camera feed is active.")]
        [SerializeField] private Image analogOverlay;
        [SerializeField] private Image clipProgress;
        [SerializeField] private TMP_Text frameQualityText;

        [Header("Station overlays")]
        [SerializeField] private GameObject telephotoOverlay;
        [SerializeField] private Slider exposureSlider;
        [SerializeField] private RectTransform exposureIdealNeedle;
        [SerializeField] private TMP_Text filmText;
        [SerializeField] private GameObject hydrophoneOverlay;
        [SerializeField] private Slider filterSlider;
        [SerializeField] private Image signalLock;
        [SerializeField] private GameObject rovOverlay;
        [SerializeField] private TMP_Text rovText;

        [Header("Spectator")]
        [SerializeField] private GameObject spectatorPanel;
        [SerializeField] private TMP_Text spectatorText;

        [Header("Toasts")]
        [SerializeField] private TMP_Text toastText;
        [SerializeField] private CanvasGroup toastGroup;
        [SerializeField] private CanvasGroup damageFlash;
        [SerializeField] private CanvasGroup fadeToBlack;

        private PlayerCharacter _p;
        private float _toastUntil;
        private bool _bound;

        private void Update()
        {
            if (_p == null) { _p = PlayerCharacter.Local; if (_p == null) return; }
            if (!_bound) Bind();
            var gm = GameManager.Instance;
            var cfg = GameConfig.Instance;

            if (healthBar) healthBar.fillAmount = _p.Vitals.Health.Value / cfg.Player.MaxHealth;
            if (energyBar) energyBar.fillAmount = _p.Vitals.Energy.Value / cfg.Player.MaxEnergy;
            if (batteryText) batteryText.text = $"Torch {_p.Inventory.FlashlightBattery:0}%  Phone {_p.Inventory.PhoneBattery:0}%  Spares {_p.Inventory.SpareBatteries.Value}";
            if (coldWarning) coldWarning.SetActive(_p.Vitals.InWater.Value);

            if (gm != null)
            {
                if (nightText) nightText.text = gm.Phase.Value == GamePhase.Dawn ? "DAWN" : $"NIGHT {gm.CurrentNight.Value} / {GameConstants.TotalNights}";
                if (clockText && gm.NightCycle) clockText.text = gm.NightCycle.FormatClock();
                if (evidenceText && gm.Evidence) evidenceText.text = $"EVIDENCE {gm.Evidence.SavedCount} / {GameConstants.EvidenceToWin}";
                if (pendingText) pendingText.text = _p.EvidenceBag.PendingCount.Value > 0 ? $"{_p.EvidenceBag.PendingCount.Value} unsaved - get to the locker" : "";
                if (weatherText && gm.Weather) weatherText.text = gm.Weather.Current.Value.ToString();
                if (hullText && gm.Vessel) hullText.text = $"HULL {gm.Vessel.HullIntegrity.Value:0}%";
                if (fadeToBlack) fadeToBlack.alpha = gm.Phase.Value == GamePhase.Defeat ? Mathf.MoveTowards(fadeToBlack.alpha, 1f, Time.deltaTime * 0.3f) : 0f;
            }

            // Interaction prompt
            var target = _p.Interactor.Current;
            if (promptText) promptText.text = target != null ? target.GetPrompt(_p) : "";
            if (holdProgress) holdProgress.fillAmount = _p.Interactor.HoldProgress;

            // Recording
            var phone = _p.HeldTool as PhoneCamera;
            bool rec = phone != null && phone.IsRecording;
            if (recIndicator) recIndicator.SetActive(rec);
            if (clipProgress) clipProgress.fillAmount = phone != null ? phone.ClipProgress : 0f;
            if (frameQualityText) frameQualityText.text = rec && phone.LastFrameQuality > 0f ? $"SUBJECT IN FRAME  {phone.LastFrameQuality * 100f:0}%" : "";

            bool feedActive = rec || FindSeated<TelephotoCamera>() != null || FindSeated<ROVStation>() != null;
            if (analogOverlay) analogOverlay.enabled = feedActive;

            UpdateStationOverlays();

            // Spectator
            var spec = _p.Spectator;
            bool spectating = spec != null && spec.IsActive;
            if (spectatorPanel) spectatorPanel.SetActive(spectating);
            if (spectating && spectatorText)
                spectatorText.text = spec.Target != null ? $"Spectating {spec.Target.DisplayName}  ({(spec.IsThirdPerson ? "3rd person" : "POV")})" : "No one left to watch";

            if (toastGroup) toastGroup.alpha = Mathf.MoveTowards(toastGroup.alpha, Time.time < _toastUntil ? 1f : 0f, Time.deltaTime * 3f);
            if (damageFlash) damageFlash.alpha = Mathf.MoveTowards(damageFlash.alpha, 0f, Time.deltaTime * 1.5f);
        }

        private void UpdateStationOverlays()
        {
            var tele = FindSeated<TelephotoCamera>();
            if (telephotoOverlay) telephotoOverlay.SetActive(tele != null);
            if (tele != null)
            {
                if (exposureSlider) exposureSlider.value = tele.Exposure;
                if (exposureIdealNeedle && exposureSlider) exposureIdealNeedle.anchorMin = exposureIdealNeedle.anchorMax = new Vector2(tele.IdealExposure, 0.5f);
                if (filmText) filmText.text = $"FILM {tele.FilmRemaining.Value}   {(tele.ExposureProgress >= 1f ? "READY" : tele.LastFrameQuality > 0f ? "FOCUSING" : "")}";
            }
            var hydro = FindSeated<HydrophoneArray>();
            if (hydrophoneOverlay) hydrophoneOverlay.SetActive(hydro != null);
            if (hydro != null)
            {
                if (filterSlider) filterSlider.value = hydro.Filter;
                if (signalLock) { signalLock.color = hydro.Locked ? Color.green : Color.gray; signalLock.fillAmount = hydro.CleanProgress; }
            }
            var rovS = FindSeated<ROVStation>();
            if (rovOverlay) rovOverlay.SetActive(rovS != null);
            if (rovS != null && rovText && rovS.Rov != null)
                rovText.text = $"DEPTH {rovS.Rov.Depth:0.0}m  BATT {rovS.Rov.BatterySeconds.Value / 60f:0.0}min  {(rovS.IsRecording ? "REC" : "")} {(rovS.ClipProgress > 0f ? $"{rovS.ClipProgress * 100f:0}%" : "")}";
        }

        private T FindSeated<T>() where T : Station
        {
            if (!_p.IsSeated) return null;
            foreach (var s in FindObjectsByType<T>(FindObjectsSortMode.None)) if (s.LocalPlayerSeated) return s;
            return null;
        }

        private void Bind()
        {
            var g = GameManager.Instance;
            if (_bound || g == null || g.Evidence == null || g.Vessel == null) return;
            _bound = true;
            _p.EvidenceBag.Captured += e => Toast($"Captured: {e.Label} ({e.Quality * 100f:0}%). Secure it in the locker!");
            _p.EvidenceBag.Lost += () => Toast("Your unsaved evidence is gone. The phone went in the water.");
            _p.Vitals.Damaged += _ => { if (damageFlash) damageFlash.alpha = 0.8f; };
            _p.Vitals.Died += () => Toast("You were lost to the loch. Spectating.");
            var gm = GameManager.Instance;
            if (gm != null)
            {
                if (gm.Evidence != null)
                {
                    gm.Evidence.EvidenceSavedEvent += r => Toast($"Evidence secured ({r.Type}). {gm.Evidence.SavedCount}/{GameConstants.EvidenceToWin}");
                    gm.Evidence.EvidenceRejectedEvent += (t, why) => Toast($"Rejected: {why}");
                }
                gm.NightStarted += n => Toast(n == 1 ? "Night 1. Find her." : $"Night {n}. She knows you're here.");
                gm.PhaseChanged += p => { if (p == GamePhase.Dawn) Toast("Dawn. Resupply below decks."); if (p == GamePhase.Finale) Toast("Something is hitting the hull."); };
                AudioManager.Instance?.BindToMatch(gm);
            }
        }

        public void Toast(string msg)
        {
            if (toastText) toastText.text = msg;
            _toastUntil = Time.time + 4f;
        }
    }
}
