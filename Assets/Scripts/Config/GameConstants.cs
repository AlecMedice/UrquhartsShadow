namespace UrquhartsShadow.Config
{
    /// <summary>
    /// Fixed, non-tunable identifiers used across systems: scene names, tags, layers,
    /// PlayerPrefs keys and hard design rules. Tunable numbers live in <see cref="GameConfig"/>.
    /// </summary>
    public static class GameConstants
    {
        // ---- Design rules (from the one-shot) ----
        public const int MaxPlayers = 5;
        public const int TotalNights = 5;
        public const int EvidenceToWin = 10;

        // ---- Scenes ----
        public const string SceneBootstrap = "Bootstrap";
        public const string SceneTitle = "Title";
        public const string SceneLoch = "LochNess";
        public const string SceneEnding = "Ending";

        // ---- Tags ----
        public const string TagPlayer = "Player";
        public const string TagNessie = "Nessie";
        public const string TagVessel = "Vessel";
        public const string TagDeployable = "Deployable";
        public const string TagWater = "Water";

        // ---- Layers (create these in Project Settings > Tags and Layers) ----
        public const string LayerPlayer = "Player";
        public const string LayerNessie = "Nessie";
        public const string LayerVessel = "Vessel";
        public const string LayerDeployable = "Deployable";
        public const string LayerInteractable = "Interactable";
        public const string LayerWater = "Water";

        // ---- Input action names (must match PlayerControls.inputactions) ----
        public const string MapPlayer = "Player";
        public const string MapROV = "ROV";
        public const string MapUI = "UI";
        public const string ActionMove = "Move";
        public const string ActionLook = "Look";
        public const string ActionJump = "Jump";
        public const string ActionSprint = "Sprint";
        public const string ActionCrouch = "Crouch";
        public const string ActionInteract = "Interact";
        public const string ActionPrimary = "Primary";      // left click / RT
        public const string ActionRecord = "Record";        // right click hold / LT
        public const string ActionFlashlight = "Flashlight";
        public const string ActionDrop = "Drop";
        public const string ActionPause = "Pause";
        public const string ActionSpectateNext = "SpectateNext";
        public const string ActionSpectatePrev = "SpectatePrev";
        public const string ActionSpectateToggleView = "SpectateToggleView";

        // ---- PlayerPrefs keys ----
        public const string PrefMouseSensitivity = "opt.mouseSens";
        public const string PrefGamepadSensitivity = "opt.padSens";
        public const string PrefInvertY = "opt.invertY";
        public const string PrefMasterVolume = "opt.masterVol";
        public const string PrefMusicVolume = "opt.musicVol";
        public const string PrefSfxVolume = "opt.sfxVol";
        public const string PrefPlayerName = "opt.playerName";
        public const string PrefDifficulty = "opt.difficulty";

        // ---- Resources paths ----
        public const string ResourceGameConfig = "GameConfig";
        public const string ResourceDifficultyFolder = "Difficulty";

        // ---- Multiplayer ----
        public const string SessionCodeLength = "6";
        public const string DefaultSessionName = "Urquhart Expedition";
    }
}
