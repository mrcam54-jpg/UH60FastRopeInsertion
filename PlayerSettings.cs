using BepInEx.Configuration;
using UnityEngine;

namespace UH60FastRopeInsertion
{
    public sealed partial class Plugin
    {
        internal static BepInEx.Logging.ManualLogSource Log;
        internal static readonly bool Enabled = true, AutomaticRaidStart = true;
        internal static readonly float SquadWaitSeconds = 4f;
        internal static readonly string AircraftModel = "Icebreaker";
        internal static ConfigEntry<DeploymentMode> SelectedDeployment;
        internal static ConfigEntry<float> FlightSpeedMultiplier, DescentDuration;
        internal static ConfigEntry<float> CameraYawLimit, CameraPitchUpLimit, CameraPitchDownLimit;
        internal static ConfigEntry<float> CameraSensitivity, CabinFovBonus;
        internal static ConfigEntry<float> HelicopterVolume, RopeVolume, InspectionSpeed;
        internal static ConfigEntry<KeyboardShortcut> InspectionKey;

        // ConfigurationManager reads this standard tag by reflection. This is remembered
        // pre-raid selection, not a second deployment selector in F12.
        private sealed class HiddenSetting { public bool? Browsable = false; }
        private void BindPlayerSettings()
        {
            SelectedDeployment = Config.Bind("01. General", "Deployment mode", DeploymentMode.Helicopter,
                new ConfigDescription("Remembered pre-raid deployment choice.", null, new HiddenSetting()));
            FlightSpeedMultiplier = Config.Bind("02. Timing", "Flight speed multiplier", 1f,
                new ConfigDescription("Scales only the authored approach/departure time.", new AcceptableValueRange<float>(.6f, 1.6f)));
            DescentDuration = Config.Bind("02. Timing", "Fast-rope descent seconds", 5.2f,
                new ConfigDescription("Time for one actor to move from the door to touchdown.", new AcceptableValueRange<float>(3.4f, 9f)));
            CameraYawLimit = Config.Bind("03. Camera", "Cabin look yaw", 70f,
                new ConfigDescription("Maximum free-look angle to either side.", new AcceptableValueRange<float>(15f, 85f)));
            CameraPitchUpLimit = Config.Bind("03. Camera", "Cabin look up", 38f,
                new ConfigDescription("Maximum upward look angle.", new AcceptableValueRange<float>(10f, 70f)));
            CameraPitchDownLimit = Config.Bind("03. Camera", "Cabin look down", 48f,
                new ConfigDescription("Maximum downward look angle.", new AcceptableValueRange<float>(10f, 75f)));
            CameraSensitivity = Config.Bind("03. Camera", "Cabin look sensitivity", 1.7f,
                new ConfigDescription("Mouse sensitivity while the cinematic camera is active.", new AcceptableValueRange<float>(.3f, 4f)));
            CabinFovBonus = Config.Bind("03. Camera", "Cabin FOV bonus", 8f,
                new ConfigDescription("Extra vertical view angle only during cinematic camera ownership. Original raid FOV is restored at handoff.", new AcceptableValueRange<float>(0f, 18f)));
            HelicopterVolume = Config.Bind("04. Presentation", "Helicopter volume", 100f,
                new ConfigDescription("Volume of this helicopter only, 0-100 percent. Applies immediately; departure distance fading is retained.", new AcceptableValueRange<float>(0f, 100f)));
            RopeVolume = Config.Bind("04. Presentation", "Rope descent volume", 55f,
                new ConfigDescription("Volume of the quiet normalized rope sound, 0-100 percent. Follows each actual descent and stops at ground contact.", new AcceptableValueRange<float>(0f, 100f)));
            // Player-facing camera controls, not an authoring/test category.
            InspectionKey = Config.Bind("05. Free camera", "Toggle inspection camera", new KeyboardShortcut(KeyCode.F8),
                "Toggle aircraft-relative inspection: mouse look, WASD/QE move, Shift faster. F8 returns to the actor's eyes.");
            InspectionSpeed = Config.Bind("05. Free camera", "Inspection move speed", 2f,
                new ConfigDescription("Inspection camera speed in metres per second.", new AcceptableValueRange<float>(.3f, 10f)));
        }
    }

    internal static class PlayerCabinClip
    {
        internal static CabinClipData Read()
        {
            using (var stream = typeof(PlayerCabinClip).Assembly.GetManifestResourceStream("UH60.CabinFlight"))
            {
                if (stream == null) throw new System.IO.InvalidDataException("Built-in cabin animation missing.");
                using (var reader = new System.IO.StreamReader(stream)) return CabinClipData.Parse(reader.ReadToEnd());
            }
        }
    }
}
