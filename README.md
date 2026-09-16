# Urquhart's Shadow

A co-op analog-horror sim for 1–5 players. It is autumn 1972 on Loch Ness. Your team has five nights,
a research vessel, and a sponsor who wants ten pieces of evidence. The animal in the water has other plans.

**Stack:** Unity 6 (6000.0.x), C#, Universal Render Pipeline, Input System (KBM + Xbox/DualSense),
Netcode for GameObjects 2.x, Unity Multiplayer Services (Relay/Lobby via Sessions).

## Layout

```
Assets/
  Input/PlayerControls.inputactions   KBM + gamepad bindings
  Scripts/
    UrquhartsShadow.asmdef
    Config/      GameConstants (fixed rules/ids), GameConfig (ALL tunables), NessieDifficultyProfile (AI knobs)
    Core/        GameBootstrap (ENTRY POINT), GameManager (match state machine), NightCycleManager,
                 EvidenceManager, WeatherManager, GameState enums, EvidenceRecord, MatchStats
    Environment/ OceanSurface (Gerstner waves), Buoyancy, LochBounds (playable area, trenches, dock)
    Player/      PlayerCharacter (root), InputReader, PlayerMovement, PlayerLook, PlayerVitals,
                 PlayerInteractor, PlayerEvidenceBag, PlayerInventory, SpectatorCamera, IInteractable
    Nessie/      NessieAI (brain), NessieStates (Lurk/Stalk/Investigate/Sabotage/Breach/DeckStrike/Ram/Retreat/Finale),
                 NessieSenses (stimuli + memory), NessieBody (movement/anim/audio), INessieTarget
    Tools/       Station (seat base), HandheldTool, CameraEvidenceSensor, PhoneCamera, TelephotoCamera,
                 SideScanSonar, SonarBeacon, BeaconLauncher, DeployableDevice, HydrophoneArray,
                 EdnaSampler, ROV, ROVStation
    Boat/        ResearchVessel (hull, helm physics, sinking), Helm, SupplyStation, EvidenceLocker,
                 RepairPoint, Ladder, MonitorScreen
    Networking/  SessionManager (solo / host with join code / join), PlayerSpawner
    UI/          TitleScreenUI, MultiplayerMenuUI, HudUI, EndingUI, SettingsUI, PauseMenuUI
    Audio/       AudioManager (adaptive tension layer, stingers)
    Settings/    SettingsStore (PlayerPrefs)
    Editor/      ProjectSetupMenu (creates config assets, tags, layers)
docs/
  DESIGN.md    Game design document expanded from the one-shot
  SETUP.md     Step-by-step Unity scene/prefab wiring
  HANDOFF.md   What is done, what is not, and what to build next
```

## Quick start

1. Open the folder in Unity 6000.0.x. Packages resolve from `Packages/manifest.json`.
2. Run **Urquhart's Shadow > Setup > Create Config Assets** and **Create Tags And Layers**.
3. Follow `docs/SETUP.md` to build the four scenes and the prefabs (there are no binary assets in this repo yet).
4. Link the project to Unity Gaming Services (Edit > Project Settings > Services) for multiplayer; solo works without it.

## Design in one paragraph

Every night the team can do anything: ping sonar, drop beacons, listen on the hydrophones, sample eDNA over
trenches, pilot the ROV, or stand on deck with a phone. Evidence is only *captured* on the player; it only *counts*
once secured in the main-deck locker. Nessie hunts the gear (drags beacons down, gnaws the ROV tether), hunts the
crew (sweeps players off the rail, rams the hull), and gives brief 2-second surface breaches that are the only way
to get photos. Ten pieces before the end of night five and you win. Otherwise she cracks the hull.
