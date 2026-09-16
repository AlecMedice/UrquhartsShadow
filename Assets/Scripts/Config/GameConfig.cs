using System;
using UnityEngine;

namespace UrquhartsShadow.Config
{
    /// <summary>
    /// Single home for every tunable number in the game. Create one via
    /// Assets > Create > Urquhart's Shadow > Game Config and place it in Resources/GameConfig.
    /// Systems read from <see cref="GameConfig.Instance"/>; nothing gameplay-related should hard-code values.
    /// </summary>
    [CreateAssetMenu(fileName = "GameConfig", menuName = "Urquhart's Shadow/Game Config")]
    public class GameConfig : ScriptableObject
    {
        private static GameConfig _instance;
        public static GameConfig Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = Resources.Load<GameConfig>(GameConstants.ResourceGameConfig);
                    if (_instance == null)
                    {
                        Debug.LogWarning("GameConfig not found in Resources; using defaults.");
                        _instance = CreateInstance<GameConfig>();
                    }
                }
                return _instance;
            }
        }

        public NightTuning Night = new NightTuning();
        public PlayerTuning Player = new PlayerTuning();
        public VesselTuning Vessel = new VesselTuning();
        public EvidenceTuning Evidence = new EvidenceTuning();
        public ToolTuning Tools = new ToolTuning();
        public WeatherTuning Weather = new WeatherTuning();
        public SupplyTuning Supplies = new SupplyTuning();
        public AudioTuning Audio = new AudioTuning();
    }

    [Serializable]
    public class NightTuning
    {
        [Tooltip("Real-time length of one night in seconds.")]
        public float NightDurationSeconds = 12f * 60f;
        [Tooltip("Dawn/resupply phase between nights in seconds.")]
        public float DawnDurationSeconds = 90f;
        [Tooltip("Seconds of calm at the start of each night before Nessie activates.")]
        public float GracePeriodSeconds = 60f;
        [Tooltip("Ambient light intensity at midnight (0..1 of the sun/moon light).")]
        [Range(0f, 1f)] public float MidnightLightScale = 0.08f;
        [Tooltip("Length of the hull-crack finale cinematic on night 5 loss, seconds.")]
        public float FinaleDurationSeconds = 45f;
    }

    [Serializable]
    public class PlayerTuning
    {
        public float WalkSpeed = 3.2f;
        public float SprintSpeed = 5.5f;
        public float CrouchSpeed = 1.6f;
        public float JumpHeight = 1.0f;
        public float Gravity = -19.6f;
        public float MouseSensitivity = 0.12f;
        public float GamepadSensitivity = 120f;
        public float MaxLookPitch = 85f;
        public float InteractRange = 2.6f;
        public float MaxHealth = 100f;
        public float MaxEnergy = 100f;
        [Tooltip("Energy drained per second while sprinting.")]
        public float SprintEnergyDrain = 6f;
        [Tooltip("Energy drained per second while idle/walking (cold, exhaustion).")]
        public float PassiveEnergyDrain = 0.35f;
        [Tooltip("Below this energy, movement speed is scaled by ExhaustedSpeedScale.")]
        public float ExhaustedEnergyThreshold = 15f;
        [Range(0.2f, 1f)] public float ExhaustedSpeedScale = 0.55f;
        [Tooltip("Health lost per second in the water (cold shock).")]
        public float WaterDamagePerSecond = 8f;
        [Tooltip("Seconds a player can survive in the water before they are lost.")]
        public float WaterSurvivalSeconds = 40f;
        [Tooltip("Health restored per second when back aboard after a dunk.")]
        public float ShiverRecoveryPerSecond = 2f;
        [Tooltip("How far a player is thrown when Nessie strikes the deck.")]
        public float KnockbackForce = 9f;
        [Tooltip("Force applied to a swimming player toward a ladder when they interact with it.")]
        public float SwimSpeed = 1.6f;
    }

    [Serializable]
    public class VesselTuning
    {
        public float MaxHullIntegrity = 100f;
        [Tooltip("Hull damage from a single Nessie ram at Normal difficulty (scaled by profile).")]
        public float RamDamage = 12f;
        [Tooltip("Hull integrity regained per second while a player is repairing.")]
        public float RepairRatePerSecond = 4f;
        [Tooltip("Below this integrity the lower deck floods, blocking supply stations.")]
        public float FloodingThreshold = 30f;
        [Tooltip("Boat sinks (immediate loss) if integrity reaches zero before night 5 finale.")]
        public bool CanSinkBeforeFinale = true;
        public float ThrottleAcceleration = 1.5f;
        public float MaxSpeedKnots = 6f;
        public float RudderTurnRate = 12f;
        public float EngineNoiseRadius = 120f;
        [Tooltip("How strongly the hull rolls with the waves.")]
        public float RockingAmplitude = 1.0f;
        [Tooltip("Extra rocking multiplier during a storm.")]
        public float StormRockingMultiplier = 2.4f;
        [Tooltip("Loch playable radius; the boat cannot leave this circle.")]
        public float LochBoundsRadius = 1400f;
    }

    [Serializable]
    public class EvidenceTuning
    {
        [Tooltip("Minimum quality (0..1) for a capture to count as evidence at all.")]
        [Range(0f, 1f)] public float MinimumQuality = 0.35f;
        [Tooltip("Cap per evidence type across the whole game so teams must diversify. 0 = uncapped.")]
        public int MaxPerType = 3;
        [Tooltip("Seconds Nessie is exposed on a surface breach (design: ~2s).")]
        public float BreachWindowSeconds = 2.0f;
        [Tooltip("Seconds of continuous phone recording on Nessie needed for a valid clip.")]
        public float PhoneRecordSeconds = 3.0f;
        [Tooltip("Seconds of 35mm exposure on Nessie needed for a valid shot (better than phone).")]
        public float TelephotoExposureSeconds = 0.8f;
        [Tooltip("Seconds of ROV footage on Nessie needed for a valid clip.")]
        public float RovRecordSeconds = 2.5f;
        [Tooltip("Quality bonus for 35mm vs phone.")]
        public float TelephotoQualityBonus = 0.3f;
        [Tooltip("Quality penalty per meter of fog density (0..1) between camera and target.")]
        public float FogQualityPenalty = 0.4f;
        [Tooltip("Unsaved evidence is lost if a player goes into the water.")]
        public bool LoseUnsavedEvidenceInWater = true;
    }

    [Serializable]
    public class ToolTuning
    {
        [Header("Batteries")]
        public float BatteryCapacity = 100f;
        public float FlashlightDrainPerSecond = 0.9f;

        [Header("Side-Scan Sonar")]
        public float SonarRange = 180f;
        public float SonarPingInterval = 2.5f;
        public float SonarDrainPerPing = 1.2f;
        [Tooltip("Sonar returns a blob for any body larger than this (m).")]
        public float SonarMinContactSize = 6f;
        [Tooltip("Continuous seconds of contact needed to log a sonar evidence hit.")]
        public float SonarContactSeconds = 6f;
        public float SonarNoiseAttractRadius = 200f;

        [Header("Sonar Beacons (deployable)")]
        public int SonarBeaconStock = 6;
        public float BeaconRange = 90f;
        public float BeaconBatterySeconds = 240f;
        public float BeaconTetherStrength = 1f;
        public float BeaconDeployRange = 6f;

        [Header("Hydrophone Array")]
        public float HydrophoneRange = 260f;
        public float HydrophoneDrainPerSecond = 0.25f;
        [Tooltip("Seconds of clean bio-acoustic signal required to log a hydrophone evidence hit.")]
        public float HydrophoneCleanSignalSeconds = 5f;
        [Tooltip("Engine noise mask strength; the player must filter below this to isolate calls.")]
        public float EngineMaskStrength = 0.7f;
        public float FilterSweepSpeed = 0.5f;
        [Tooltip("How close the filter band must be to the true call frequency (0..1).")]
        public float FilterTolerance = 0.08f;

        [Header("35mm Telephoto (one, main deck)")]
        public float TelephotoFov = 12f;
        public float TelephotoMaxRange = 350f;
        public float ExposureAdjustSpeed = 1.2f;
        [Tooltip("Ideal exposure changes with weather; tolerance to still get a usable shot.")]
        public float ExposureTolerance = 0.15f;
        public int FilmRollShots = 24;

        [Header("Phone Cameras (everyone)")]
        public float PhoneFov = 60f;
        public float PhoneMaxRange = 90f;
        public float PhoneBatteryDrainPerSecond = 1.5f;
        [Tooltip("Base quality of a phone clip before penalties.")]
        [Range(0f, 1f)] public float PhoneBaseQuality = 0.55f;

        [Header("eDNA Water Sampler")]
        public float SampleDurationSeconds = 25f;
        [Tooltip("Sampler only finds unknown sequences within this distance of a deep trench node.")]
        public float TrenchProximity = 60f;
        [Tooltip("Nessie must have passed within this radius in the last N seconds to leave eDNA.")]
        public float EdnaTraceRadius = 45f;
        public float EdnaTraceLifetimeSeconds = 180f;
        public int SampleVialStock = 8;

        [Header("ROV (one)")]
        public float RovSpeed = 3.5f;
        public float RovTurnRate = 70f;
        public float RovTetherLength = 220f;
        public float RovBatterySeconds = 300f;
        public float RovLightRange = 25f;
        public float RovMaxDepth = 180f;
    }

    [Serializable]
    public class WeatherTuning
    {
        [Tooltip("Probability weights for each night's weather: Clear, Overcast, Rain, Storm.")]
        public float[] WeatherWeights = { 0.35f, 0.3f, 0.2f, 0.15f };
        [Tooltip("Chance the weather changes mid-night (rolled every WeatherChangeInterval seconds).")]
        [Range(0f, 1f)] public float MidNightChangeChance = 0.25f;
        public float WeatherChangeInterval = 180f;
        public float ClearNightFogDensity = 0.012f;
        public float OvercastFogDensity = 0.02f;
        public float RainFogDensity = 0.03f;
        public float StormFogDensity = 0.045f;
        public float ClearWaveHeight = 0.25f;
        public float OvercastWaveHeight = 0.45f;
        public float RainWaveHeight = 0.7f;
        public float StormWaveHeight = 1.6f;
        public float WeatherBlendSeconds = 30f;
        [Tooltip("Moon brightness on a clear night (light intensity).")]
        public float ClearMoonIntensity = 0.35f;
        public float LightningMinInterval = 8f;
        public float LightningMaxInterval = 40f;
    }

    [Serializable]
    public class SupplyTuning
    {
        public int StartingBatteries = 8;
        public int BatteriesRestockedPerDawn = 4;
        public int StartingRations = 6;
        public int RationsRestockedPerDawn = 3;
        public float RationHealthRestore = 35f;
        public float RationEnergyRestore = 60f;
        public float ResupplyInteractSeconds = 1.5f;
        [Tooltip("Research funding at start; spent on restocking extras at dawn.")]
        public int StartingFunding = 500;
        public int BatteryCost = 25;
        public int RationCost = 15;
        public int BeaconCost = 60;
        public int VialCost = 30;
        public int FilmRollCost = 40;
        [Tooltip("Funding granted per piece of evidence saved (sponsors pay for results).")]
        public int FundingPerEvidence = 80;
    }

    [Serializable]
    public class AudioTuning
    {
        public float NessieCallMinInterval = 45f;
        public float NessieCallMaxInterval = 140f;
        [Tooltip("Distance at which underwater thumps against the hull are audible on deck.")]
        public float HullThumpAudibleRange = 60f;
    }
}
