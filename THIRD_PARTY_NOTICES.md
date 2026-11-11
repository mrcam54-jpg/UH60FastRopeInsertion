# Authorship and third-party notices

## Original work

Original integration/choreography, data validation, UI, cable deformation,
camera/state management, performance module, build tooling and authored control/pose
data: © 2026 JozinZBazin, MIT. Development used OpenAI Codex assistance. User-edited
cabin poses are included as our animation data; no native clip curves are embedded.

## Separately installed resources — NOT included or relicensed

- **Battlestate Games / EFT**: game skeleton and cutscene art/animations remain the
  original owners' property. Runtime references are read from the user's installation.
- **Manimal1920 / Icebreaker Backport**: [project](https://github.com/danauraborealis/ManimalIcebreaker),
  [distribution](https://sp-mod.com/mod/2866/manimals-icebreaker-backport).
  Aircraft geometry, rope/connector surfaces, materials and native cabin reference
  animations are read locally. The mod's code license is not a license to redistribute BSG assets.
- **SamSWAT, Arys, Tylevo and contributors / Tactical Services**:
  [project](https://github.com/Tylevo/Tylevo.TacticalServicesControl),
  [distribution](https://sp-mod.com/mod/2791/tylevos-tactical-service-control).
  The authored flight/prefab/controller and aircraft audio are reused from the separate
  installed bundle. No TSC DLL, source or asset bundle is included. The current Forge
  listing labels CC BY-NC 3.0, while the locally inspected notice labels CC BY-NC 4.0.
  Neither is represented as MIT; this package grants no redistribution rights over it.
- **BepInEx, Harmony/HarmonyX, Unity, SPT and EFT APIs**: compile/runtime references
  from the user's installation; their binaries are not included.

## Optional integration references — not bundled

- **Hj / DuckCam 1.0.0**, proprietary, installed separately:
  https://sp-mod.com/mod/3245/duckcam and https://github.com/hjal-dev/DuckCam.
  Optional read-only camera/freeze-state interoperability; no DuckCam code or binary copied.

## Included user-supplied rope sound — not MIT

**Sound-Pack.net, Sound #5254, "Звук спуска по канату"**, supplied by the user:
https://sound-pack.net/sounds/sfx/construction/kanat_otpustili_sezzhaet_zvuk/89-1-0-5254

The source page (checked 2026-11-11) explicitly offers this sound for game voiceover,
among other uses. The supplied MP3 was converted to a quiet mono PCM sound embedded
in our plugin; its cover image and original MP3 are not included. Original rights
remain with the sound's owner. Our MIT license does not relicense this recording
or grant independent redistribution rights. This is not an AI-generated sound.

## Other optional integration references — not bundled

- **Liquidwarp / Infil 2.1.0**, MIT:
  https://github.com/liquidwarp/SPT-Infil/tree/34a6b45f2e6811a9ebeaed8e35c8dfcbb47416d3
- **Michael P. Starkweather, acidphantasm and contributors / Dynamic Maps 1.2.1**,
  MIT code: https://github.com/acidphantasm/SPT-DynamicMaps/tree/4944764f5f6c42d152dca6bd1b5371c4f6212a9e
  Map-image licenses are separate; no images are included here.
- **pitAlex / PIT Fireteam**: https://github.com/pitAlex/SPT-PitFireTeam.
  Reflection-based roster access; no implementation or equipment assets copied.
- **UnityToolkit / Arys and contributors** and **WTT CommonLib / WTT contributors**:
  transitive upstream dependencies installed separately; see DEPENDENCIES.md.

No Call of Duty asset, scene, extracted animation or audio is included. COD footage
was a historical visual reference, not a claimed port. No Kenney particle texture or
fal-generated asset remains in this candidate. The mod is not endorsed by BSG, SPT
or any named dependency author. Credits do not imply permission to repack their work.
