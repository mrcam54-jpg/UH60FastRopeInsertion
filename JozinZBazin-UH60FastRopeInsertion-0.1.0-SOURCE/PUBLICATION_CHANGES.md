# Difference from the private local build

Baseline: test15.4 main + test15.5 performance sidecar. The original source-file
hashes are in source-baseline.json. This is an isolated exported source snapshot,
not an installation over the private BDA-pinned build.

Publication-only differences:

1. Names: JozinZBazin-UH60FastRopeInsertion / JozinZBazin-UH60Performance.
2. GUIDs: com.jozinzbazin.uh60fastropeinsertion / com.jozinzbazin.uh60performance.
3. Three-part version 0.1.0 in plugin/project/release metadata. Build label marks candidate.
4. Hard loader dependencies for Icebreaker 1.1.3 and TSC 1.3.13 (loader minimums,
   not certification of future versions); exact optional Infil/Maps checks retained.
5. Read TSC flight bundle from its own installed directory, reuse an already loaded
   bundle and unload only a handle this mod loaded. No copied bundle in our package.
6. The accepted fitted cabin CSV is included unchanged and enabled by default;
   animation/capture paths resolve under the installed mod. Status overlay defaults OFF.
7. Performance sidecar is pinned at build time to the paired PUBLIC core SHA256.
8. Project build references supplied by SptRoot/SPT_ROOT, no personal paths or
   automatic deployment. Deterministic non-debug builds, MIT license and attribution.

NO edits to flight paths, poses, gesture curves, rope/descent solver, doors,
streaming/environment policies, ground handoff or campaign code.

Source exactness is necessary but not sufficient: public identity/loading/defaults
still require a clean-install game test. Private BDA's exact old core-hash adapter
does not accept this public build. No BDA compatibility claim is made.
