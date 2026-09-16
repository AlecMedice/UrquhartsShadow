using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UrquhartsShadow.Core;
using UrquhartsShadow.Tools;

namespace UrquhartsShadow.Boat
{
    /// <summary>
    /// A CRT on the main deck showing the status of every deployed beacon, the ROV and the hull.
    /// Purely presentational: reads replicated NetworkVariables, so it works on every client.
    /// </summary>
    public class MonitorScreen : MonoBehaviour
    {
        public enum Mode { Beacons, Rov, Vessel }
        [SerializeField] private Mode mode;
        [SerializeField] private TMP_Text text;
        [SerializeField] private float refreshSeconds = 0.5f;
        [SerializeField] private ROV rov;

        private float _next;
        private readonly System.Text.StringBuilder _sb = new System.Text.StringBuilder();

        private void Update()
        {
            if (text == null || Time.time < _next) return;
            _next = Time.time + refreshSeconds;
            _sb.Clear();
            switch (mode)
            {
                case Mode.Beacons: DrawBeacons(); break;
                case Mode.Rov: DrawRov(); break;
                case Mode.Vessel: DrawVessel(); break;
            }
            text.text = _sb.ToString();
        }

        private void DrawBeacons()
        {
            _sb.AppendLine("SONAR BEACON NET");
            var beacons = FindObjectsByType<SonarBeacon>(FindObjectsSortMode.None);
            if (beacons.Length == 0) { _sb.AppendLine("  no beacons deployed"); return; }
            foreach (var b in beacons)
            {
                string state = b.Lost.Value ? "LOST" : b.Held.Value ? "SIGNAL ERR" : b.BatterySeconds.Value <= 0f ? "DEAD" : "OK";
                string contact = b.ContactRange.Value >= 0f ? $"CONTACT {b.ContactRange.Value:0}m" : "--";
                _sb.AppendLine($"  B{b.BeaconIndex.Value + 1}  {state,-10} {b.BatterySeconds.Value / 60f:0.0}min  {contact}");
            }
        }

        private void DrawRov()
        {
            _sb.AppendLine("ROV TELEMETRY");
            if (rov == null) { _sb.AppendLine("  offline"); return; }
            string state = rov.TetherCut.Value ? "TETHER CUT" : rov.Held.Value ? "!!! LOAD ON TETHER !!!" : rov.Deployed.Value ? "DEPLOYED" : "DOCKED";
            _sb.AppendLine($"  {state}");
            _sb.AppendLine($"  depth {rov.Depth:0.0}m  batt {rov.BatterySeconds.Value / 60f:0.0}min  tether {rov.TetherHealth.Value * 100f:0}%");
        }

        private void DrawVessel()
        {
            var gm = GameManager.Instance;
            _sb.AppendLine("R/V URQUHART");
            if (gm == null || gm.Vessel == null) return;
            _sb.AppendLine($"  hull {gm.Vessel.HullIntegrity.Value:0}%  {(gm.Vessel.LowerDeckFlooded.Value ? "LOWER DECK FLOODING" : "")}");
            _sb.AppendLine($"  engine {(gm.Vessel.EngineOn.Value ? "ON" : "OFF")}  throttle {gm.Vessel.Throttle.Value * 100f:0}%");
            if (gm.Evidence != null) _sb.AppendLine($"  evidence {gm.Evidence.SavedCount}/{Config.GameConstants.EvidenceToWin}  funding £{gm.Evidence.Funding.Value}");
            if (gm.Weather != null) _sb.AppendLine($"  weather {gm.Weather.Current.Value}");
            if (gm.NightCycle != null) _sb.AppendLine($"  night {gm.CurrentNight.Value}/{Config.GameConstants.TotalNights}  {gm.NightCycle.FormatClock()}");
        }
    }
}
