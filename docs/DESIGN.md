# Urquhart's Shadow — Design Document

## Premise
Autumn, 1972. A privately funded expedition (inspired by Operation Deepscan) has five nights on Loch Ness
aboard the research vessel *R/V Urquhart*. Sponsors want ten verifiable pieces of evidence. The nights are long,
the water is black, and the animal is not passive.

## Win / Lose
- **Win:** the team secures **10 pieces of evidence** in the locker at any point across the 5 nights. Cut to the docks in daylight, drinks, stats overlay.
- **Lose:** night 5 ends with fewer than 10. Nessie enters the *Finale*: she circles at the surface, cracks the hull, the boat lists and sinks, fade to black.
- **Early loss:** hull integrity reaches 0 before the finale (configurable `CanSinkBeforeFinale`).

## The loop
1. **Title** — video loop of the loch / boat at the docks / subtle wake. Solo, Multiplayer (Start a game → join code; Join a game → enter code), Settings, Credits, Quit.
2. **Night (12 min default)** — grace period (60s) then Nessie activates. Free-form search. Weather rolls per night and may drift mid-night.
3. **Dawn (90s)** — supplies restock, film reloads, ROV is recovered, hull gets a small patch. Funding can buy extras.
4. Repeat to night 5 → **Ending**.

## Evidence
| Type | Source | Requirement | Notes |
|---|---|---|---|
| Sonar contact | Side-scan console | 6s continuous contact | Cheap but capped; every ping is heard by Nessie |
| Hydrophone call | Hydrophone console | Tune filter within tolerance of call frequency for 5s | Engine noise masks; turn engine off |
| 35mm photo | Telephoto mount (one) | 0.8s on target + exposure within tolerance, during a breach | Best quality; flash makes her dive |
| Phone clip | Everyone, RMB hold | 3s on target during a breach | Low quality, drains phone |
| eDNA sample | Rail sampler near trench | 25s; positive only if she passed within 45m in last 3 min | Blind but reliable if you track her |
| ROV footage | Pilot chair | 2.5s on target within floodlight range underwater | Only underwater evidence; tether can be cut |

Rules: minimum quality 0.35; max 3 per type (forces diversity); unsaved evidence is **lost if the player goes in the water**. Evidence must be walked to the locker on the main deck.

## Nessie
Server-authoritative state machine (`NessieAI` + `NessieStates`), driven by a `NessieDifficultyProfile`:

| State | What she does |
|---|---|
| Dormant | Deep and far; grace period, dawn, after victory |
| Lurk | Wander between trenches, occasional call (hydrophone opportunity), decide |
| Investigate | Go to a stimulus: sonar ping, engine, light, **splash (a player in the water — she bites)** |
| Stalk | Circle the vessel at depth; escalate to strike / ram / sabotage |
| Sabotage | Grab a beacon and drag it to a trench, or gnaw the ROV tether |
| Breach | Surface for ~2s at 30–140m: the photo window. Cunning profiles dive early when a lens is on her |
| DeckStrike | Surge beside the rail and sweep an isolated player overboard |
| Ram | Wind up to one side and hit the hull |
| Retreat | Dive away after being photographed, flashed, or after attacking |
| Finale | Night 5 loss: repeated hull hits until the boat sinks |

Difficulty presets: **Docile / Wary / Cunning / Ancient** (see `NessieDifficultyProfile.CreatePreset`). Knobs include aggression, stealth (avoids sonar cones), cunning (targets highest-value gear), curiosity, memory, reaction time, breach interval/duration, camera awareness, sabotage drive, ram damage, deck-strike chance, isolation preference, per-night and per-evidence escalation.

## The vessel
Main deck: helm, sonar console, hydrophone console, 35mm mount, ROV pilot chair, evidence locker, monitor CRTs (beacon net, ROV telemetry, vessel status).
Lower deck: galley (rations: +health/+energy), battery locker, hull repair points. Floods below 30% hull.
Rails have `RailPoints` that Nessie uses to find players near the edge. Boarding ladder for swimmers.

## Players
First person; walk/sprint/crouch/jump; phone in hand by default. Health, energy (sprint + cold), cold-shock in water (40s to live), shivering after. Dead/lost players spectate teammates (POV or third person, cycle with LMB/RMB or bumpers, V/Y toggles view).

## Weather
Clear (fog on the water, moon reflection), Overcast, Rain, Storm (lightning = giant flash that also scares her; heavy rocking). Fog reduces camera evidence quality with distance.

## Controls
| Action | KBM | Gamepad |
|---|---|---|
| Move / Look | WASD / Mouse | Left / Right stick |
| Jump / Crouch / Sprint | Space / Ctrl / Shift | A / B / L3 |
| Interact (hold for some) | E | X |
| Primary (shutter, engine toggle, ROV deploy) | LMB | RT |
| Record (phone/ROV) | RMB hold | LT hold |
| Flashlight / Drop | F / G | D-pad up / down |
| Pause | Esc | Start |
| Spectate next/prev/view | LMB / RMB / V | RB / LB / Y |

## Economy (light)
Funding starts at 500; +80 per secured evidence. Dawn purchases: rations 15, batteries 25, beacons 60, vials 30, film 40. All in `GameConfig.Supplies`.
