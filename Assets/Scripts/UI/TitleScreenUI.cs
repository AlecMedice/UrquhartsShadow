using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using UrquhartsShadow.Audio;
using UrquhartsShadow.Networking;

namespace UrquhartsShadow.UI
{
    /// <summary>
    /// Title scene controller. Loops background videos (the loch at dusk, the boat at the docks, the odd
    /// subtle wake) behind a sleek menu: Solo, Multiplayer, Settings, Credits, Quit.
    /// Build the Canvas in the editor and wire the buttons/panels here.
    /// </summary>
    public class TitleScreenUI : MonoBehaviour
    {
        [Header("Background")]
        [SerializeField] private VideoPlayer videoPlayer;
        [SerializeField] private VideoClip[] loopClips;
        [SerializeField] private float clipCrossfadeSeconds = 1.5f;
        [SerializeField] private CanvasGroup fadeOverlay;

        [Header("Panels")]
        [SerializeField] private GameObject mainPanel;
        [SerializeField] private GameObject multiplayerPanel;
        [SerializeField] private GameObject settingsPanel;
        [SerializeField] private GameObject creditsPanel;

        [Header("Buttons")]
        [SerializeField] private Button soloButton;
        [SerializeField] private Button multiplayerButton;
        [SerializeField] private Button settingsButton;
        [SerializeField] private Button creditsButton;
        [SerializeField] private Button quitButton;

        private int _clipIndex;

        private void Start()
        {
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            AudioManager.Instance?.PlayTitle();
            ShowMain();

            if (soloButton) soloButton.onClick.AddListener(() => SessionManager.Instance?.StartSolo());
            if (multiplayerButton) multiplayerButton.onClick.AddListener(() => Show(multiplayerPanel));
            if (settingsButton) settingsButton.onClick.AddListener(() => Show(settingsPanel));
            if (creditsButton) creditsButton.onClick.AddListener(() => Show(creditsPanel));
            if (quitButton) quitButton.onClick.AddListener(Application.Quit);

            if (videoPlayer != null && loopClips != null && loopClips.Length > 0)
            {
                videoPlayer.isLooping = false;
                videoPlayer.loopPointReached += _ => NextClip();
                _clipIndex = Random.Range(0, loopClips.Length);
                PlayClip(_clipIndex);
            }
        }

        private void NextClip()
        {
            _clipIndex = (_clipIndex + 1) % loopClips.Length;
            StartCoroutine(CrossfadeTo(_clipIndex));
        }

        private System.Collections.IEnumerator CrossfadeTo(int index)
        {
            if (fadeOverlay != null)
                for (float t = 0f; t < clipCrossfadeSeconds; t += Time.deltaTime) { fadeOverlay.alpha = t / clipCrossfadeSeconds; yield return null; }
            PlayClip(index);
            if (fadeOverlay != null)
                for (float t = 0f; t < clipCrossfadeSeconds; t += Time.deltaTime) { fadeOverlay.alpha = 1f - t / clipCrossfadeSeconds; yield return null; }
            if (fadeOverlay != null) fadeOverlay.alpha = 0f;
        }

        private void PlayClip(int i) { videoPlayer.clip = loopClips[i]; videoPlayer.Play(); }

        public void ShowMain() => Show(mainPanel);

        private void Show(GameObject panel)
        {
            foreach (var p in new[] { mainPanel, multiplayerPanel, settingsPanel, creditsPanel })
                if (p != null) p.SetActive(p == panel);
        }
    }
}
