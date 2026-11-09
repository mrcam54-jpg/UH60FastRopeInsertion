# UH-60 Fast-Rope Insertion

Author: **JozinZBazin** · version **0.1.0** · target: **offline SPT 4.1.6 / Windows**.

**Unpublished candidate. Do not describe this as a tested/stable public release.**
The publication build, direct dependency loading and performance sidecar still need
a clean-install raid test. Severe helicopter-lifetime slowdown was reported in the
previous local build; synthetic optimization tests do not prove it fixed.

Adds a pre-raid choice between standard spawning and a helicopter insertion:
approach, cabin presentation, rope deployment, sequential squad descent, control
handoff, rope retraction and departure. The optional map integration selects an LZ.
Maximum presentation roster: the player plus four recognized PIT followers.

## Requirements and recommended integrations

See [DEPENDENCIES.md](DEPENDENCIES.md) for versions, verified links and transitive dependencies.
The complete **Manimal's Icebreaker Backport 1.1.3** and **Tylevo's Tactical Service
Control 1.3.13** installations are required by this candidate. The aircraft,
materials, rope hardware and native animation references are read locally; they
are not included in this download. This is a standalone mod, not a dependency-free one.

**Infil 2.1.0 + Dynamic Maps 1.2.1** are recommended for selecting a landing zone;
without them the authored default sector remains available. **PIT Fireteam** is
optional for friendly-bot squad insertion; without a recognized roster only the
player is presented. The adapter was developed against local PIT 0.10.3; current
upstream 1.1.1 needs verification. BDA is not required. Fika/headless are not supported.

## Install

1. Close the game, server and launcher. Back up profiles and settings.
2. Install the complete required mods and their own dependencies separately,
   following their authors' instructions. Do not extract just their DLLs.
3. Remove any earlier/private UH60 installation from `BepInEx/plugins` to a backup
   outside that directory. Do not run two implementations together. The private
   BDA campaign hash adapter does not accept this differently identified public build.
4. Extract the candidate ZIP **into the SPT root**, preserving `BepInEx/plugins/`.
   Expected: `BepInEx/plugins/UH60FastRopeInsertion/UH60FastRopeInsertion.dll`,
   `UH60Performance.dll`, and `assets/cabin-flight.csv`.
5. Launch SPT normally. Select helicopter insertion on the location screen;
   select an LZ if the optional map pair is installed. Areas are candidates,
   not guarantees: obstacle/surface/NavMesh checks may refuse an unsafe insertion.

No setup script, resource download, personal drive path or extracted bundle is required.
Configuration is generated for this public GUID; private configuration files are not bundled.
F12 requires a BepInEx Configuration Manager already supplied by many SPT setups.
Without it, close the game and edit the generated BepInEx configuration file instead.

## Controls

- Mouse: limited cabin look; original control is restored at player touchdown.
- Ctrl+F9: manual repeat/test in a supported raid (not a normal progression landing).
- F8: inspection camera; mouse, WASD/QE, Shift; F8 returns to the actor.
- Ctrl+F6: reload a validated, data-only cabin CSV; Ctrl+F7: capture cabin poses.
- F12 / JozinZBazin-UH60FastRopeInsertion: deployment, timing and camera settings.
- F12 / JozinZBazin-UH60Performance: diagnostics; the temporary aircraft-hide probe
  is OFF by default and resets OFF on restart.

## Limitations and troubleshooting

Factory and Labs are not helicopter locations. New landing candidates, all roster
sizes, refusal/recovery, lighting, weapon restoration and FPS require live testing.
No automatic performance improvement or perfect cutscene equivalence is promised.
Native Tactical Services flight is reused, not a new flight simulator.

For a refusal or scene error, provide the shortest useful recording and
`BepInEx/LogOutput.log`. Remove personal information before sharing logs. For FPS,
include 10 seconds after helicopter cleanup. A missing dependency fails explicitly,
not by silently switching to a different helicopter or animation.

Uninstall while the game/server/launcher are closed: move only
`BepInEx/plugins/UH60FastRopeInsertion` out of the plugin tree and optionally remove
the two `com.jozinzbazin.uh60*.cfg` files. Do not delete profiles or other mods.
Separately installed dependencies may affect profiles; follow their own uninstall rules.

## Source, license and credits

The matching SOURCE archive contains the full authored code, build script and tests.
Public repository URL: **not created yet**. Build locally with a .NET 10 SDK:
`./build.ps1 -SptRoot <your-SPT-root>`; compile references are read from your game,
never redistributed. NuGet restores the pinned .NET Framework 4.7.2 reference package 1.0.3.
Outputs are written outside the game and never installed automatically.

Original code and authored animation data: MIT, © 2026 JozinZBazin. Foreign game
assets and dependency licenses are **not** relicensed under MIT.
See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

Developed iteratively with OpenAI Codex assistance for programming, animation tooling
and offline QA. JozinZBazin supplied direction, in-game tests and cabin poses.
No fal-generated asset is included in this candidate. Not affiliated with BSG,
SPT or the dependency authors.
