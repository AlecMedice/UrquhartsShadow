# Handoff Notes

Written at the end of the first scaffolding pass. The intent was to turn the one-shot into a complete,
compilable C# architecture with every tunable in one place so the next pass can focus on assets, scenes and feel.
**Nothing here has been compiled in an editor yet** (this pass ran without Unity). Expect a handful of
compile nits on first import; the API usage is Unity 6 / NGO 2.x / Input System 1.11 / Multiplayer Services 1.x.

## Done
- Project skeleton, package manifest, asmdefs (runtime + editor), .gitignore, input asset (KBM + gamepad).
- `GameConfig` ScriptableObject with every tunable grouped (Night, Player, Vessel, Evidence, Tools, Weather, Supplies, Audio). `GameConstants` for fixed rules. `NessieDifficultyProfile` with four presets.
- Match flow: Bootstrap → Title → LochNess → Ending. `GameManager` state machine, 5 nights + dawns, 10-evidence win, finale/sink loss, stats summary broadcast.
- Nessie AI: senses/memory, 10 states, difficulty scaling, escalation per night and per evidence, sabotage of beacons and ROV, deck strikes, rams, breaches with camera awareness, finale.
- All six tools with server-validated evidence, plus beacons/launcher, ROV vehicle + station.
- Vessel: hull, helm physics with buoyancy rocking, rails, flooding, repair, resupply, sinking sequence. Locker, galley, battery locker, ladder, CRT monitors.
- Player: owner-authoritative FPS on a moving deck, swimming, vitals, interaction (hold), evidence bag, inventory, spectator (POV / third person cycling).
- Networking: solo host, host with join code, join by code (UGS Sessions + Relay), spawner.
- UI controllers for title (video loops), multiplayer menu, HUD (with station overlays), ending stats, settings, pause, dawn shop. Audio manager with tension layer.
- `Assets/Shaders/LochWater.shader`: URP Gerstner water matching the CPU model (depth tint, moon specular, fresnel, foam).
- `Editor/GreyboxBuilder.cs`: one menu item builds all four scenes, every prefab, the water mesh/material, a URP asset and Build Settings from primitives, fully wired. This is the intended starting point for the next pass.

## Not done / needs the editor
1. **Real scenes and prefabs** — the greybox builder makes placeholder versions of everything; replace the primitives with models while keeping the component wiring (see SETUP.md for what each field expects).
2. **Water textures** — the shader exists; it needs a ripple normal map and a foam noise texture assigned on `Assets/Generated/LochWater.mat`, and Depth Texture enabled on the URP asset.
3. **Nessie model/animator** — plesiosaur mesh, animator with the listed params, "scary teeth".
4. **Title / ending videos** — VideoPlayer wiring exists; clips do not.
5. **Tool switching** — the phone is equipped on spawn; add a tool-switch (Drop key / number keys) if more handhelds are added.
6. **Settings panel widgets** — `SettingsUI` supports sliders/toggles but the greybox only creates a Back button.
7. **Lobby scene** — host currently loads the loch immediately. If you want a R.E.P.O.-style lobby where players gather first, add a Lobby scene and call `SessionManager.StartExpeditionFromLobby()`.
8. **Voice / proximity chat** — not started (Vivox is the usual choice with UGS).
9. **Nessie navigation** — steering is direct (no NavMesh); add a simple obstacle avoidance against the loch floor mesh if the terrain has islands/shallows.
10. **Verify NGO details on first compile**: `RpcTarget.Single(...)` usage, `NetworkList` construction in `Awake`, `NetworkTransform` Authority Mode = Owner on the player, `SessionOptions.WithRelayNetwork()` behaviour (it should start the host through the NGO integration; if it does not on your SDK version, call `NetworkManager.Singleton.StartHost()` after `CreateSessionAsync`).
11. **Analog horror layer** — film grain / VHS post-process on phone and ROV feeds, 35mm darkroom "develop" moment, grainy photo review UI. All hooks (quality values, labels) exist in `PendingEvidence`/`EvidenceRecord`.

## Suggested order for the next pass
1. Import, fix compile nits, run **Setup > Build Greybox (All)**, press Play from Bootstrap, choose Solo.
2. Play until night flow, evidence, and Nessie states log correctly (`NessieAI.verbose` is on in the greybox prefab). Shorten `GameConfig.Night` timings to iterate.
3. Verify breach → phone clip → locker → count, then each station in turn (sonar, hydrophone, 35mm, ROV, eDNA).
4. Multiplayer smoke test with two builds (host + join code).
5. Then art: water shader, vessel model, plesiosaur, weather FX, videos, audio.
