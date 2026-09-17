using System.Collections;
using UnityEngine;
using UrquhartsShadow.Core;
using UrquhartsShadow.Nessie;
using UrquhartsShadow.Settings;

namespace UrquhartsShadow.Audio
{
    /// <summary>
    /// Music and global stingers. Reacts to Nessie's state (tension layer rises when she stalks/attacks),
    /// weather, and match phase. Sits on the PersistentSystems prefab with two music AudioSources.
    /// </summary>
    public class AudioManager : MonoBehaviour
    {
        private static AudioManager _instance;
        /// <summary>Returns a real null when the instance was destroyed (a stale static after Play, a scene unload).</summary>
        public static AudioManager Instance { get => _instance != null ? _instance : null; private set => _instance = value; }

        [Header("Music layers")]
        [SerializeField] private AudioSource ambientLayer;
        [SerializeField] private AudioSource tensionLayer;
        [SerializeField] private AudioSource stingerSource;
        [SerializeField] private AudioClip titleTheme;
        [SerializeField] private AudioClip nightAmbient;
        [SerializeField] private AudioClip tensionLoop;
        [SerializeField] private AudioClip dawnTheme;
        [SerializeField] private AudioClip victoryTheme;
        [SerializeField] private AudioClip defeatTheme;
        [SerializeField] private AudioClip evidenceStinger;
        [SerializeField] private AudioClip hullHitStinger;
        [SerializeField] private AudioClip breachStinger;

        private float _tensionTarget;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void Start() => PlayTitle();

        private void Update()
        {
            float music = SettingsStore.MusicVolume;
            var nessie = NessieAI.Instance;
            if (nessie != null)
            {
                _tensionTarget = nessie.CurrentStateId switch
                {
                    NessieStateId.Stalk => 0.35f,
                    NessieStateId.DeckStrike or NessieStateId.Ram => 0.9f,
                    NessieStateId.Breach => 0.5f,
                    NessieStateId.Finale => 1f,
                    NessieStateId.Sabotage => 0.4f,
                    _ => 0.1f
                };
            }
            else _tensionTarget = 0f;

            if (tensionLayer != null)
            {
                tensionLayer.volume = Mathf.MoveTowards(tensionLayer.volume, _tensionTarget * music, Time.deltaTime * 0.25f);
                if (tensionLoop != null && !tensionLayer.isPlaying && tensionLayer.clip == tensionLoop) tensionLayer.Play();
            }
            if (ambientLayer != null) ambientLayer.volume = Mathf.MoveTowards(ambientLayer.volume, music * 0.6f, Time.deltaTime * 0.25f);
        }

        public void BindToMatch(GameManager gm)
        {
            if (gm == null) return;
            gm.PhaseChanged += OnPhase;
            gm.NightStarted += _ => Crossfade(nightAmbient, tensionLoop);
            if (gm.Evidence != null) gm.Evidence.EvidenceSavedEvent += _ => Stinger(evidenceStinger);
            if (gm.Vessel != null) gm.Vessel.HullHit += (_, __) => Stinger(hullHitStinger);
            if (gm.Nessie != null) gm.Nessie.Body.BreachStarted += () => Stinger(breachStinger);
        }

        private void OnPhase(GamePhase p)
        {
            switch (p)
            {
                case GamePhase.Dawn: Crossfade(dawnTheme, null); break;
                case GamePhase.Victory: Crossfade(victoryTheme, null); break;
                case GamePhase.Defeat: Crossfade(defeatTheme, null); break;
            }
        }

        public void PlayTitle() => Crossfade(titleTheme, null);

        public void Stinger(AudioClip clip)
        {
            if (clip != null && stingerSource != null) stingerSource.PlayOneShot(clip, SettingsStore.SfxVolume);
        }

        private void Crossfade(AudioClip ambient, AudioClip tension)
        {
            if (ambientLayer != null && ambientLayer.clip != ambient) StartCoroutine(Fade(ambientLayer, ambient));
            if (tensionLayer != null)
            {
                tensionLayer.clip = tension; tensionLayer.loop = true;
                if (tension != null) { tensionLayer.volume = 0f; tensionLayer.Play(); } else tensionLayer.Stop();
            }
        }

        private IEnumerator Fade(AudioSource src, AudioClip next)
        {
            float start = src.volume;
            for (float t = 0f; t < 1.5f; t += Time.deltaTime) { src.volume = Mathf.Lerp(start, 0f, t / 1.5f); yield return null; }
            src.clip = next; src.loop = true;
            if (next != null) src.Play();
        }
    }
}
