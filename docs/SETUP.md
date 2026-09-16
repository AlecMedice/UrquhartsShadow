# Unity Setup Guide

The repo contains code, the input asset and package manifest. Scenes, prefabs, models, materials and
audio must be created in the editor. This is the wiring checklist; every serialized field is documented in
the class headers.

## 0. Project
- Unity 6000.0.x, URP. Open the folder; wait for packages.
- **Urquhart's Shadow > Setup > Create Config Assets** → `Resources/GameConfig.asset` and `Resources/Difficulty/{Docile,Wary,Cunning,Ancient}.asset`.
- **Urquhart's Shadow > Setup > Create Tags And Layers**.
- Project Settings > Player > Active Input Handling: **Input System Package**.
- Project Settings > Services: link a UGS project, enable Authentication, Relay, Lobby (Multiplayer Services). Solo works without this.

## 1. Scenes (add to Build Settings in this order)
1. **Bootstrap** — one GameObject with `GameBootstrap`, `persistentSystemsPrefab` assigned.
2. **Title** — Canvas with `TitleScreenUI`, `MultiplayerMenuUI`, `SettingsUI`; a `VideoPlayer` on a full-screen RawImage (Render Mode: Render Texture) for the background loops.
3. **LochNess** — the game.
4. **Ending** — `EndingUI` with a VideoPlayer and a stats panel.

## 2. PersistentSystems prefab (DontDestroyOnLoad)
- `NetworkManager` + `UnityTransport`. Enable Scene Management. **Disable** "Auto Spawn Player Prefab" (PlayerSpawner does it in the loch). Add every network prefab (Player, Nessie, Vessel, SonarBeacon, ROV) to the Network Prefabs list.
- `SessionManager`
- `AudioManager` with three AudioSources (ambient, tension, stinger).
- Do **not** put `GameManager` here. It is a NetworkBehaviour and belongs in the LochNess scene as an in-scene NetworkObject (see §6); its `Instance` is set in Awake before the other scene systems register with it.

## 3. Player prefab
```
Player (Tag Player, Layer Player)
  NetworkObject, NetworkTransform (Authority Mode: Owner), CharacterController (h 1.8 r 0.35)
  PlayerCharacter, InputReader (assign PlayerControls.inputactions), PlayerMovement (groundMask: Vessel|Default),
  PlayerLook, PlayerVitals, PlayerInteractor (interactMask: Interactable|Vessel|Deployable),
  PlayerEvidenceBag, PlayerInventory, SpectatorCamera
  ├─ CameraRoot (y 1.6)  → Camera (URP, near 0.05) + AudioListener
  │     └─ HandSocket → Phone (PhoneCamera + view model; equipped by default: call Equip() on spawn or from PlayerCharacter)
  ├─ Flashlight (Spot Light, off)
  └─ Body mesh (Renderers listed in hideForOwner)
```
Wire `playerCamera`, `listener`, `handSocket`, `flashlight`, `localOnly` (HUD canvas if you put it on the player), `hideForOwner`.

## 4. Vessel prefab
```
Vessel (Tag Vessel, Layer Vessel) — NetworkObject, NetworkTransform (Server), Rigidbody (mass ~20000, no gravity? keep gravity; Buoyancy counters it),
  Buoyancy (4+ floater empties at the hull corners), ResearchVessel
  Hull colliders (non-trigger), deck colliders
  RailPoints/  6–12 empties along the gunwales → ResearchVessel.railPoints
  MainDeck/    Helm, SideScanSonar, HydrophoneArray, TelephotoCamera (with scopeCamera + viewPoint pivot), ROVStation (+ ROV prefab docked), EvidenceLocker, MonitorScreen x3 (TMP text on quads)
  Deck/        BeaconLauncher x2 (muzzle forward over the rail), EdnaSampler x1 (vialDropPoint over the side), Ladder (topPoint on deck)
  LowerDeck/   SupplyStation (Rations), SupplyStation (Batteries), RepairPoint x2
  Audio: engine loop, hull impact source
```
Every station: `seatPoint` (where the player stands), `viewPoint` (aim pivot for the 35mm), collider on layer Interactable.
Set the Vessel serialized refs (telephoto, launchers, samplers, rov, supplyStation, sonar, hydrophone).

## 5. Nessie prefab
```
Nessie (Tag Nessie, Layer Nessie) — NetworkObject, NetworkTransform (Server), NessieAI, NessieBody
  Animator (params: Speed float, Surfaced bool, triggers Breach/Bite/Ram/Roar), head transform, colliders (child of root so occlusion checks pass)
  AudioSources: voice, splash
```
Spawn it in the LochNess scene (in-scene NetworkObject) at depth near a trench.

## 6. LochNess scene objects
- `LochBounds` at the loch centre with trench markers as children (`trenchNodes`) and a `dockPosition`.
- `OceanSurface` with the water material assigned (see §7).
- `NightCycleManager` (NetworkObject) with the moon Directional Light and an optional sun light.
- `WeatherManager` (NetworkObject) with rain particles, lightning light, wind/rain/thunder sources, `ocean` ref.
- `EvidenceManager` (NetworkObject).
- `PlayerSpawner` (NetworkObject) with the Player prefab and spawn points **on the vessel deck**.
- `GameManager` (NetworkObject) if not on PersistentSystems.
- Vessel prefab instance, Nessie prefab instance, ROV prefab (docked, referenced by ROVStation).
- HUD Canvas with `HudUI`, `PauseMenuUI`.
- Fog enabled in Lighting settings (WeatherManager overrides density).

## 7. Water shader
`OceanSurface` pushes `_WaveHeight` and four `_WaveA.._WaveD` vectors (dir.x, dir.y, steepness, wavelength) to the material.
Build a Shader Graph that displaces vertices with the same Gerstner formula (`k = 2π/λ`, `c = sqrt(9.81/k)`, `f = k·(dot(d, xz) − c·t)`), plus normal reconstruction, depth-based colour, moon specular and a foam mask. A high-res plane (or clipmap) around the vessel is enough for the loch.

## 8. Difficulty
The host's selected difficulty (title screen dropdown) is stored in PlayerPrefs and replicated by `GameManager.DifficultyIndex`. Tune profiles in `Resources/Difficulty/*.asset`.

## 9. Testing solo
Press Play from Bootstrap → Title → Solo. Host starts locally; `PlayerSpawner` spawns you on deck. Set `NessieAI.verbose` to watch state transitions in the Console. Shorten `GameConfig.Night.NightDurationSeconds` and `GracePeriodSeconds` to iterate faster.
