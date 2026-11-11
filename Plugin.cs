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
    [BepInDependency("com.hj.duckcam", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("com.manimal.icebreaker", "1.1.3")]
    [BepInDependency("com.tylevo.tacticalservicescontrol", "1.3.13")]
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency("net.liquidwarp.infil", BepInDependency.DependencyFlags.SoftDependency)]
    public sealed partial class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.jozinzbazin.uh60fastropeinsertion";
        public const string PluginName = "JozinZBazin-UH60FastRopeInsertion";
        // BepInEx 5 parses this as System.Version; prerelease suffixes cause the plug-in to be skipped.
        public const string PluginVersion = "0.1.3";
        public const string BuildLabel = "publication-0.1.3";

        private void Awake()
        {
            Log = Logger;
            UiText.LanguageSource = GameLocale.Read;
            BindPlayerSettings();

            var harmony=new Harmony(PluginGuid);
            harmony.PatchAll(typeof(Plugin).Assembly);
            InfilIntegration.Install(harmony);
            // F12 uses the original English setting labels/tooltips; game UI localization is independent.
            PhotoModeBridge.Initialize();
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
            LandingRequest.BeginRaid(__instance,__instance.LocationId,!InfilIntegration.Ready);
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

