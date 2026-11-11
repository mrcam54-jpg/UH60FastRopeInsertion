# Dependencies / Зависимости

Links and public metadata checked on **2026-11-09**. These are separate downloads,
not contents of our archive. Follow each author's COMPLETE install instructions.
Version references below are not claims that the new public UH60 build passed a raid.

| Component | Requirement | Version/reference | Purpose and link |
|---|---|---|---|
| SPT | Required | 4.1.6 | Offline Windows host; use its included BepInEx/Harmony |
| Manimal's Icebreaker Backport | Required for default presentation | 1.1.3 | [Aircraft, rope/hook, shaders and native motion reference](https://sp-mod.com/mod/2866/manimals-icebreaker-backport) |
| Tylevo's Tactical Service Control | Required | 1.3.13 | [Locally read flight controller, prefab and audio](https://sp-mod.com/mod/2791/tylevos-tactical-service-control) |
| Infil | Optional; recommended for LZ map | 2.1.0 | [Pre-raid entry selection](https://sp-mod.com/mod/3202/infil) |
| Dynamic Maps | Optional for UH60; required by Infil | 1.2.1 | [Interactive maps](https://sp-mod.com/mod/1431/dynamic-maps) |
| PIT Fireteam | Optional; needed for friendly-bot roster | Local adapter reference: 0.10.3; current public 1.1.1 UNVERIFIED | [Squad provider, formerly Please Invite Teammates](https://sp-mod.com/mod/2676/pit-fireteam) |
| DuckCam | Optional photo-mode integration | 1.0.0 interface reference; raid test pending | [Camera ownership, freeze and scaled scene time](https://sp-mod.com/mod/3245/duckcam) |

Without a working Infil map adapter: a random marked LZ on the seven configured
maps, trying another marked LZ if validation fails. The original Icebreaker default
is retained. Ground Zero is excluded. Without recognized PIT: solo insertion.
These paths have offline checks, but actual map geometry and runtime behaviour
still require a raid test.
Do not assume an older or newer version is supported merely because it loads.

## Required dependencies of the required mods

These are **transitive**, not direct UH60 code references. Do not strip Icebreaker
down to one bundle or omit its server files. Its full installation may affect profiles.

TSC 1.3.13 lists [UnityToolkit 2.0.2](https://sp-mod.com/mod/1426/unitytoolkit)
and [WTT CommonLib 3.0.6](https://sp-mod.com/mod/2310/wtt-commonlib).
Its page targets SPT 4.1.5/EFT 40743; our 4.1.6 target still requires a clean test.

Icebreaker 1.1.3 lists:

- [BigBrain 1.5.0](https://sp-mod.com/mod/902/bigbrain)
- [WTT CommonLib 3.0.6](https://sp-mod.com/mod/2310/wtt-commonlib)
- [MoreBotsAPI 2.1.1](https://sp-mod.com/mod/2426/morebotsapi)
- [WTT Black Division 1.3.1](https://sp-mod.com/mod/2511/wtt-black-division-redacted-home)
- [WTT Content Backport 2.0.2](https://sp-mod.com/mod/2512/wtt-content-backport)
- [Climbable Ladders 1.0.4](https://sp-mod.com/mod/2649/climbable-ladders)
- [Manimal's CS Gas 2.0.0](https://sp-mod.com/mod/2864/manimals-cs-gas)
- [Manimal's Interchange Expansion 1.0.5](https://sp-mod.com/mod/3025/manimals-10-interchange-expansion-backport)
- [UnityToolkit 2.0.2](https://sp-mod.com/mod/1426/unitytoolkit)

These are the upstream page's declared versions, not a flattened guarantee about
every later transitive dependency. Install their own requirements too. In particular,
newer MoreBotsAPI releases have changed requirements; do not mix versions blindly.
PIT's current page lists [Waypoints 1.9.0](https://sp-mod.com/mod/827/waypoints-expanded-navmesh)
and BigBrain 1.5.0. That does not certify our adapter against the new PIT version.

## Not required / not bundled

Black Division Alliance and the separate private BDA campaign are **not required**.
The WTT Black Division dependency of Icebreaker above is a DIFFERENT mod, not BDA.
Freecam is unnecessary: UH60 has its own F8 inspection camera. Smoothie/Smoother,
SAIN and other AI/graphics mods are not added as direct UH60 requirements.
Blender, Python, ffmpeg, uv and universal-modder are authoring tools, not player requirements.
F12's Configuration Manager is optional; configuration files can be edited while stopped.

No third-party DLL, loader, graphics library, map image, game bundle or animation
clip is repacked. Dynamic Maps' official package includes server/graphics components:
install the whole release, not just its client DLL. No Fika compatibility is claimed.
