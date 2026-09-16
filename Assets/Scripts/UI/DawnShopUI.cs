using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UrquhartsShadow.Boat;
using UrquhartsShadow.Config;
using UrquhartsShadow.Core;

namespace UrquhartsShadow.UI
{
    /// <summary>
    /// Shown during the Dawn phase: spend research funding on extra rations and batteries.
    /// Talks to the lower-deck SupplyStation through its BuyRpc. Any player can buy; funding is shared.
    /// </summary>
    public class DawnShopUI : MonoBehaviour
    {
        [SerializeField] private GameObject panel;
        [SerializeField] private TMP_Text fundingText;
        [SerializeField] private TMP_Text summaryText;
        [SerializeField] private Button buyRationButton;
        [SerializeField] private Button buyBatteryButton;
        [SerializeField] private Button closeButton;
        [SerializeField] private SupplyStation rationStation;
        [SerializeField] private SupplyStation batteryStation;

        private bool _shownThisDawn;

        private void Start()
        {
            if (panel) panel.SetActive(false);
            if (buyRationButton) buyRationButton.onClick.AddListener(() => Buy(1, 0));
            if (buyBatteryButton) buyBatteryButton.onClick.AddListener(() => Buy(0, 1));
            if (closeButton) closeButton.onClick.AddListener(Hide);
        }

        private void Update()
        {
            var gm = GameManager.Instance;
            if (gm == null) return;
            bool dawn = gm.Phase.Value == GamePhase.Dawn;
            if (dawn && !_shownThisDawn) { _shownThisDawn = true; Show(); }
            if (!dawn) { _shownThisDawn = false; if (panel && panel.activeSelf) Hide(); }
            if (!panel || !panel.activeSelf) return;

            var s = GameConfig.Instance.Supplies;
            int funding = gm.Evidence != null ? gm.Evidence.Funding.Value : 0;
            if (fundingText) fundingText.text = $"FUNDING  £{funding}";
            if (summaryText) summaryText.text =
                $"Ration £{s.RationCost}  (+{s.RationHealthRestore:0} health, +{s.RationEnergyRestore:0} energy)\n" +
                $"Battery £{s.BatteryCost}\n" +
                $"Night {gm.CurrentNight.Value} of {GameConstants.TotalNights} done. Evidence {gm.Evidence?.SavedCount ?? 0}/{GameConstants.EvidenceToWin}.";
            if (buyRationButton) buyRationButton.interactable = funding >= s.RationCost;
            if (buyBatteryButton) buyBatteryButton.interactable = funding >= s.BatteryCost;
        }

        private void Buy(int rations, int batteries)
        {
            var station = rations > 0 ? rationStation : batteryStation;
            if (station == null) station = rationStation != null ? rationStation : batteryStation;
            station?.BuyRpc(rations, batteries);
        }

        private void Show()
        {
            if (panel) panel.SetActive(true);
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            var p = Player.PlayerCharacter.Local;
            if (p != null) { p.Look.LookLocked = true; p.Movement.MovementLocked = true; }
        }

        private void Hide()
        {
            if (panel) panel.SetActive(false);
            var p = Player.PlayerCharacter.Local;
            if (p != null && p.Vitals.IsAlive)
            {
                Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
                p.Look.LookLocked = false; p.Movement.MovementLocked = false;
            }
        }
    }
}
