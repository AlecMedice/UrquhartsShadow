using UnityEngine;

namespace UrquhartsShadow.Config
{
    public enum DifficultyLevel { Docile = 0, Wary = 1, Cunning = 2, Ancient = 3 }

    /// <summary>
    /// Every knob that shapes how smart, aggressive and evasive the Nessie AI is.
    /// Create four assets (Docile / Wary / Cunning / Ancient) in Resources/Difficulty
    /// or call <see cref="CreatePreset"/> for sensible defaults.
    /// </summary>
    [CreateAssetMenu(fileName = "NessieProfile", menuName = "Urquhart's Shadow/Nessie Difficulty Profile")]
    public class NessieDifficultyProfile : ScriptableObject
    {
        public DifficultyLevel Level = DifficultyLevel.Wary;
        [TextArea] public string Description;

        [Header("Movement")]
        public float CruiseSpeed = 6f;
        public float HuntSpeed = 11f;
        public float BurstSpeed = 16f;
        public float TurnRate = 40f;
        public float PreferredDepth = 45f;
        public float MinDepth = 3f;

        [Header("Temperament (0..1)")]
        [Tooltip("How often Nessie chooses to engage the boat vs. lurk.")]
        [Range(0f, 1f)] public float Aggression = 0.4f;
        [Tooltip("How much Nessie avoids sonar cones, lights and cameras.")]
        [Range(0f, 1f)] public float Stealth = 0.4f;
        [Tooltip("Tactical intelligence: prioritising valuable equipment, baiting players, feinting.")]
        [Range(0f, 1f)] public float Cunning = 0.4f;
        [Tooltip("How curious Nessie is about sonar pings and hydrophone activity.")]
        [Range(0f, 1f)] public float Curiosity = 0.6f;
        [Tooltip("How long Nessie remembers player positions and equipment locations (seconds).")]
        public float MemorySeconds = 20f;

        [Header("Senses")]
        public float HearingRange = 220f;
        public float SightRangeUnderwater = 40f;
        public float SightRangeSurface = 120f;
        [Tooltip("Sonar pings within this range are heard and either investigated or avoided.")]
        public float SonarSensitivityRange = 300f;
        [Tooltip("Reaction delay before responding to a new stimulus.")]
        public float ReactionSeconds = 2.5f;

        [Header("Breaching (evidence opportunities)")]
        [Tooltip("Base seconds between voluntary breaches.")]
        public float BreachIntervalMin = 70f;
        public float BreachIntervalMax = 160f;
        [Tooltip("Multiplier on the config BreachWindowSeconds. Lower = harder to photograph.")]
        public float BreachDurationScale = 1f;
        [Tooltip("Chance Nessie dives early when it detects a camera pointed at it.")]
        [Range(0f, 1f)] public float CameraAwareness = 0.2f;
        [Tooltip("Minimum distance from the boat when breaching.")]
        public float BreachDistanceMin = 30f;
        public float BreachDistanceMax = 140f;

        [Header("Sabotage")]
        [Tooltip("Chance per decision tick to go after deployed equipment.")]
        [Range(0f, 1f)] public float SabotageDrive = 0.4f;
        public float BeaconDragSpeed = 4f;
        [Tooltip("Seconds Nessie must gnaw a tether before the ROV is cut loose.")]
        public float RovTetherCutSeconds = 6f;
        [Tooltip("Probability Nessie targets the beacon with the most battery (Cunning) vs nearest.")]
        [Range(0f, 1f)] public float TargetValuableGear = 0.3f;

        [Header("Assault")]
        public float RamDamageMultiplier = 1f;
        public float RamCooldownSeconds = 45f;
        [Tooltip("Chance to attempt to knock a player off the deck edge when close.")]
        [Range(0f, 1f)] public float DeckStrikeChance = 0.3f;
        public float DeckStrikeRange = 9f;
        public float DeckStrikeCooldownSeconds = 30f;
        [Tooltip("Nessie prefers players who are alone on deck / near rails.")]
        [Range(0f, 1f)] public float IsolationPreference = 0.5f;

        [Header("Escalation")]
        [Tooltip("Aggression added per night (night 1 = +0).")]
        public float AggressionPerNight = 0.08f;
        [Tooltip("Aggression added per evidence piece the team has saved (she knows).")]
        public float AggressionPerEvidence = 0.03f;
        [Tooltip("Seconds to retreat after being caught on camera.")]
        public float RetreatSecondsAfterCapture = 40f;
        [Tooltip("Seconds to retreat after being flashed by a bright light.")]
        public float RetreatSecondsAfterFlash = 12f;

        [Header("Finale")]
        public float FinaleHullCrackSeconds = 20f;

        public static NessieDifficultyProfile CreatePreset(DifficultyLevel level)
        {
            var p = CreateInstance<NessieDifficultyProfile>();
            p.Level = level;
            switch (level)
            {
                case DifficultyLevel.Docile:
                    p.Description = "A curious animal. Breaches often, rarely attacks. Good for learning the tools.";
                    p.Aggression = 0.2f; p.Stealth = 0.15f; p.Cunning = 0.1f; p.Curiosity = 0.8f;
                    p.MemorySeconds = 10f; p.ReactionSeconds = 4f;
                    p.BreachIntervalMin = 45f; p.BreachIntervalMax = 90f; p.BreachDurationScale = 1.6f;
                    p.CameraAwareness = 0.05f; p.SabotageDrive = 0.2f; p.DeckStrikeChance = 0.1f;
                    p.RamDamageMultiplier = 0.6f; p.RamCooldownSeconds = 90f; p.AggressionPerNight = 0.05f;
                    break;
                case DifficultyLevel.Wary:
                    p.Description = "The intended experience. She hunts your gear and tests your nerve.";
                    break;
                case DifficultyLevel.Cunning:
                    p.Description = "Avoids sonar, dives when she sees a lens, drags your most valuable beacon first.";
                    p.Aggression = 0.6f; p.Stealth = 0.7f; p.Cunning = 0.7f; p.Curiosity = 0.5f;
                    p.MemorySeconds = 40f; p.ReactionSeconds = 1.5f;
                    p.BreachIntervalMin = 90f; p.BreachIntervalMax = 200f; p.BreachDurationScale = 0.8f;
                    p.CameraAwareness = 0.5f; p.SabotageDrive = 0.6f; p.TargetValuableGear = 0.7f;
                    p.DeckStrikeChance = 0.5f; p.RamDamageMultiplier = 1.3f; p.RamCooldownSeconds = 30f;
                    p.HuntSpeed = 13f; p.BurstSpeed = 19f;
                    break;
                case DifficultyLevel.Ancient:
                    p.Description = "Something older than the loch. Every mistake is punished. Ten pieces is a miracle.";
                    p.Aggression = 0.85f; p.Stealth = 0.9f; p.Cunning = 0.95f; p.Curiosity = 0.4f;
                    p.MemorySeconds = 90f; p.ReactionSeconds = 0.8f;
                    p.BreachIntervalMin = 120f; p.BreachIntervalMax = 260f; p.BreachDurationScale = 0.6f;
                    p.CameraAwareness = 0.8f; p.SabotageDrive = 0.8f; p.TargetValuableGear = 0.95f;
                    p.DeckStrikeChance = 0.7f; p.DeckStrikeCooldownSeconds = 18f; p.IsolationPreference = 0.9f;
                    p.RamDamageMultiplier = 1.8f; p.RamCooldownSeconds = 22f;
                    p.HuntSpeed = 15f; p.BurstSpeed = 22f; p.TurnRate = 60f;
                    p.AggressionPerNight = 0.05f; p.RetreatSecondsAfterCapture = 20f; p.RetreatSecondsAfterFlash = 5f;
                    break;
            }
            return p;
        }

        /// <summary>Loads a profile from Resources/Difficulty/{Level} or builds a preset if missing.</summary>
        public static NessieDifficultyProfile Load(DifficultyLevel level)
        {
            var asset = Resources.Load<NessieDifficultyProfile>($"{GameConstants.ResourceDifficultyFolder}/{level}");
            return asset != null ? asset : CreatePreset(level);
        }
    }
}
