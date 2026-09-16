using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using UrquhartsShadow.Config;
using UrquhartsShadow.Core;
using UrquhartsShadow.Networking;

namespace UrquhartsShadow.UI
{
    /// <summary>
    /// Ending scene. Victory: looping daytime video of the team at the docks with drinks, stats overlay.
    /// Defeat: black water, slow circling silhouette, the same stats overlay in colder type.
    /// </summary>
    public class EndingUI : MonoBehaviour
    {
        [SerializeField] private VideoPlayer videoPlayer;
        [SerializeField] private VideoClip victoryLoop;
        [SerializeField] private VideoClip defeatLoop;
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text statsText;
        [SerializeField] private Button returnButton;
        [SerializeField] private CanvasGroup overlay;
        [SerializeField] private float overlayDelay = 3f;

        private void Start()
        {
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            var s = SessionManager.Instance != null ? SessionManager.Instance.LastSummary : default;

            if (videoPlayer) { videoPlayer.clip = s.Won ? victoryLoop : defeatLoop; videoPlayer.isLooping = true; videoPlayer.Play(); }
            if (titleText) titleText.text = s.Won ? "SHE IS REAL." : "URQUHART'S SHADOW";
            if (statsText) statsText.text = Build(s);
            if (overlay) { overlay.alpha = 0f; StartCoroutine(FadeIn()); }
            if (returnButton) returnButton.onClick.AddListener(() => SessionManager.Instance?.Leave());
        }

        private System.Collections.IEnumerator FadeIn()
        {
            yield return new WaitForSeconds(overlayDelay);
            for (float t = 0f; t < 2f; t += Time.deltaTime) { overlay.alpha = t / 2f; yield return null; }
            overlay.alpha = 1f;
        }

        private static string Build(MatchSummary s)
        {
            var mins = Mathf.FloorToInt(s.TotalSeconds / 60f);
            var diff = ((DifficultyLevel)s.DifficultyLevel).ToString();
            return
                $"{(s.Won ? "EXPEDITION SUCCESSFUL" : "EXPEDITION LOST")}\n" +
                $"Difficulty: {diff}\n" +
                $"Nights survived: {s.NightsSurvived} / {GameConstants.TotalNights}\n" +
                $"Evidence secured: {s.EvidenceSaved} / {GameConstants.EvidenceToWin}\n" +
                $"Evidence lost to the water: {s.EvidenceLost}\n" +
                $"Sightings (breaches): {s.Breaches}\n" +
                $"Crew overboard: {s.Overboard}   Crew lost: {s.PlayersLost}\n" +
                $"Beacons dragged down: {s.BeaconsLost}\n" +
                $"Hull damage taken: {s.TotalHullDamage:0}\n" +
                $"Time on the loch: {mins} min";
        }
    }
}
