using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using EFT;
using EFT.EnvironmentEffect;
using Koenigz.PerfectCulling.EFT;
using HarmonyLib;
using UnityEngine;

namespace UH60FastRopeInsertion
{
    [BepInDependency("com.manimal.icebreaker", "1.1.3")]
    [BepInDependency("com.tylevo.tacticalservicescontrol", "1.3.13")]
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency("net.liquidwarp.infil", BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.jozinzbazin.uh60fastropeinsertion";
        public const string PluginName = "JozinZBazin-UH60FastRopeInsertion";
        // BepInEx 5 parses this as System.Version; prerelease suffixes cause the plug-in to be skipped.
        public const string PluginVersion = "0.1.0";
        public const string BuildLabel = "publication-candidate-1";

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> AutomaticRaidStart;
        internal static ConfigEntry<DeploymentMode> SelectedDeployment;
        internal static ConfigEntry<bool> DebugOverlay;
        internal static ConfigEntry<bool> VerboseLogging;
        internal static ConfigEntry<KeyboardShortcut> QuickTestKey;
        internal static ConfigEntry<float> SquadWaitSeconds;
        internal static ConfigEntry<float> FlightSpeedMultiplier;
        internal static ConfigEntry<float> DescentDuration;
        internal static ConfigEntry<float> CameraYawLimit;
        internal static ConfigEntry<float> CameraPitchUpLimit;
        internal static ConfigEntry<float> CameraPitchDownLimit;
        internal static ConfigEntry<float> CameraSensitivity;
        internal static ConfigEntry<float> CabinFovBonus;
        internal static ConfigEntry<string> AircraftModel;
        internal static ConfigEntry<KeyboardShortcut> InspectionKey;
        internal static ConfigEntry<float> InspectionSpeed;
        internal static ConfigEntry<bool> CabinClipEnabled;
        internal static ConfigEntry<bool> UserCabinActions;
        internal static ConfigEntry<string> CabinClipPath,CabinCapturePath;
        internal static ConfigEntry<KeyboardShortcut> CabinReloadKey,CabinCaptureKey;

        private void Awake()
        {
            Log = Logger;
            UserCabinActions=Config.Bind("06. Cabin editor","Animate user poses",true,
                "Add authored flight reactions, team gestures, coil push/kick and continuous exits to the edited cabin poses. Requires Use edited flight animation. Frozen per scene; repeat after changing.");
            Enabled = Config.Bind("01. General", "Enabled", true,
                "Enable the standalone UH-60 insertion scene.");
            AutomaticRaidStart = Config.Bind("01. General", "Automatic raid-start insertion", true,
                "Master switch for automatic insertion. The native pre-raid selector chooses Helicopter or Standard; the manual test hotkey is independent.");
            SelectedDeployment = Config.Bind("01. General", "Deployment mode", DeploymentMode.Helicopter,
                "Remembered pre-raid choice: Helicopter = UH-60 insertion, Standard = ordinary spawn. On the location screen. With audited Infil2.1.0 and DynamicMaps1.2.1, Next opens the landing-zone map.");
            DebugOverlay = Config.Bind("01. General", "Debug overlay", false,
                "Show the current scene state, map, landing point and actor lanes.");
            VerboseLogging = Config.Bind("01. General", "Verbose logging", false,
                "Write additional camera, streaming and rope diagnostics.");
            QuickTestKey = Config.Bind("01. General", "Quick scene test hotkey",
                new KeyboardShortcut(KeyCode.F9, KeyCode.LeftControl),
                "Restart the presentation at the authored point in the current supported raid. Saves are not changed.");
            SquadWaitSeconds = Config.Bind("02. Timing", "PIT squad wait seconds", 4.0f,
                new ConfigDescription("Wait for PIT friends to finish spawning before capturing the roster.",
                    new AcceptableValueRange<float>(0f, 15f)));
            FlightSpeedMultiplier = Config.Bind("02. Timing", "Flight speed multiplier", 1.0f,
                new ConfigDescription("Scales only the authored approach/departure time.",
                    new AcceptableValueRange<float>(0.6f, 1.6f)));
            DescentDuration = Config.Bind("02. Timing", "Fast-rope descent seconds", 5.2f,
                new ConfigDescription("Time for one actor to move from the door to touchdown.",
                    new AcceptableValueRange<float>(3.4f, 9f)));
            CameraYawLimit = Config.Bind("03. Camera", "Cabin look yaw", 70f,
                new ConfigDescription("Maximum free-look angle to either side.",
                    new AcceptableValueRange<float>(15f, 85f)));
            CameraPitchUpLimit = Config.Bind("03. Camera", "Cabin look up", 38f,
                new ConfigDescription("Maximum upward look angle.",
                    new AcceptableValueRange<float>(10f, 70f)));
            CameraPitchDownLimit = Config.Bind("03. Camera", "Cabin look down", 48f,
                new ConfigDescription("Maximum downward look angle.",
                    new AcceptableValueRange<float>(10f, 75f)));
            CameraSensitivity = Config.Bind("03. Camera", "Cabin look sensitivity", 1.7f,
                new ConfigDescription("Mouse sensitivity while the cinematic camera is active.",
                    new AcceptableValueRange<float>(0.3f, 4f)));
            CabinFovBonus=Config.Bind("03. Camera","Cabin FOV bonus",8f,
                new ConfigDescription("Extra vertical view angle only during cinematic camera ownership. Original raid FOV is restored at handoff.",
                    new AcceptableValueRange<float>(0f,18f)));
            AircraftModel = Config.Bind("04. Presentation", "Aircraft model", "Icebreaker",
                new ConfigDescription("Icebreaker reads the installed airframe, native cabin clips, cable and carabiner locally; flight still uses Tactical Services. No silent fallback if unavailable.",
                    new AcceptableValueList<string>("Icebreaker", "TacticalServices")));
            InspectionKey = Config.Bind("05. Test camera", "Toggle inspection camera", new KeyboardShortcut(KeyCode.F8),
                "Toggle aircraft-relative inspection: mouse look, WASD/QE move, Shift faster. F8 returns to the actor's eyes.");
            InspectionSpeed = Config.Bind("05. Test camera", "Inspection move speed", 2f,
                new ConfigDescription("Inspection camera speed in metres per second.", new AcceptableValueRange<float>(0.3f, 10f)));
            CabinClipEnabled=Config.Bind("06. Cabin editor", "Use edited flight animation",true,
                "Opt-in: play cabin-flight.csv only in the cabin; accepted rope descent is not edited. Disabled preserves test11 poses.");
            CabinClipPath=Config.Bind("06. Cabin editor", "Animation file",System.IO.Path.Combine(BepInEx.Paths.PluginPath,"UH60FastRopeInsertion","assets","cabin-flight.csv"),
                "Local data-only Blender export. Reload explicitly after exporting. No network or script execution.");
            CabinCapturePath=Config.Bind("06. Cabin editor", "Pose capture file",System.IO.Path.Combine(BepInEx.Paths.PluginPath,"UH60FastRopeInsertion","captures","capture.csv"),
                "Capture the currently displayed cabin poses for importing into Blender; no profile or save changes.");
            CabinReloadKey=Config.Bind("06. Cabin editor", "Reload animation",new KeyboardShortcut(KeyCode.F6,KeyCode.LeftControl),
                "Reload a validated cabin clip without restarting EFT. Invalid data retains the previous clip.");
            CabinCaptureKey=Config.Bind("06. Cabin editor", "Capture current poses",new KeyboardShortcut(KeyCode.F7,KeyCode.LeftControl),
                "During flight/hover before deployment: capture actual displayed poses to the local CSV.");

            var harmony=new Harmony(PluginGuid);
            harmony.PatchAll(typeof(Plugin).Assembly);
            InfilIntegration.Install(harmony);
            gameObject.AddComponent<InsertionController>();
            Logger.LogInfo(PluginName + " " + PluginVersion + " (" + BuildLabel + ") loaded (standalone scene; Black Division Alliance is not referenced)." );
        }
    }

    [HarmonyPatch(typeof(GameWorld), nameof(GameWorld.OnGameStarted))]
    internal static class StartAfterRaidReadyPatch
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(GameWorld __instance)
        {
            // Consume the confirmed selection BEFORE Infil's raid-start postfix clears its store.
            LandingRequest.BeginRaid(__instance,__instance.LocationId);
        }
        private static void Postfix(GameWorld __instance)
        {
            if (InsertionController.Instance != null)
            {
                InsertionController.Instance.OnRaidStarted(__instance);
            }
        }
    }

    [HarmonyPatch(typeof(Streamer), nameof(Streamer.Update))]
    internal static class StreamFromCinematicCameraPatch
    {
        private static void Prefix()
        {
            if (InsertionController.Instance != null)
            {
                InsertionController.Instance.ApplyCameraPoseForWorldSystems();
            }
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.MovementUpdate))]
    internal static class FrozenMovementPatch
    {
        private static bool Prefix(Player __instance) { return !LiveActorState.IsFrozen(__instance); }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.ComplexLateUpdate))]
    internal static class FrozenLateMovementPatch
    {
        private static bool Prefix(Player __instance) { return !LiveActorState.IsFrozen(__instance); }
    }

    [HarmonyPatch(typeof(BotOwner), nameof(BotOwner.UpdateManual))]
    internal static class FrozenBotManualPatch
    {
        private static bool Prefix(BotOwner __instance) { return !LiveActorState.IsFrozen(__instance.GetPlayer); }
    }

    [HarmonyPatch(typeof(BotOwner), nameof(BotOwner.FixedUpdate))]
    internal static class FrozenBotPhysicsPatch
    {
        private static bool Prefix(BotOwner __instance) { return !LiveActorState.IsFrozen(__instance.GetPlayer); }
    }

    [HarmonyPatch(typeof(CullingManager), nameof(CullingManager.ScheduleCullingJobs))]
    internal static class CullFromCinematicCameraPatch
    {
        private static void Prefix() { InsertionController.Instance?.ApplyCameraPoseForWorldSystems(); }
    }

    [HarmonyPatch(typeof(EFT.EnvironmentEffect.EnvironmentManager), nameof(EFT.EnvironmentEffect.EnvironmentManager.SetTriggerForPlayer))]
    internal static class CinematicEnvironmentTriggerPatch
    {
        private static void Prefix(IPlayer player, ref IndoorTrigger trigger)
        {
            if (InsertionController.Instance!=null && InsertionController.Instance.TryOverrideEnvironment(player,out IndoorTrigger actual)) trigger=actual;
        }
    }

    [HarmonyPatch(typeof(Koenigz.PerfectCulling.PerfectCullingBakeGroup),nameof(Koenigz.PerfectCulling.PerfectCullingBakeGroup.Toggle))]
    internal static class CinematicBakeDetailPatch
    {
        private static void Prefix(Koenigz.PerfectCulling.PerfectCullingBakeGroup __instance,ref bool rendererEnabled)
        { CinematicDetailBubble.Active?.OverrideBake(__instance,ref rendererEnabled); }
    }

    [HarmonyPatch(typeof(ScreenDistanceSwitcher),nameof(ScreenDistanceSwitcher.Show))]
    internal static class CinematicBakedProxyPatch
    {
        private static void Prefix(ScreenDistanceSwitcher __instance,ref bool flag)
        { CinematicDetailBubble.Active?.OverrideProxy(__instance,ref flag); }
    }
}

