using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace UH60FastRopeInsertion
{
    // Optional, version-checked compatibility. Do not change/persist somebody else's F12 entries
    // or disable their bot/physics optimizations. Gate only the three independent visibility paths.
    internal sealed class SmoothieCompatibility : IDisposable
    {
        private static readonly HashSet<object> VisibilityFeatures = new HashSet<object>();
        private static bool _presenting;
        private static bool _patched;
        private static float _resumeAt;
        private static object _depthFeature;
        private MethodInfo _showDepth, _showLods;
        private bool _ownsGate;

        internal SmoothieCompatibility()
        {
            Type plugin = AccessTools.TypeByName("Smoothie.Plugin");
            if (plugin == null) return;
            try
            {
                MethodInfo on = AccessTools.Method(plugin, "On", new[] { typeof(ConfigEntry<bool>) });
                _showDepth = AccessTools.Method(AccessTools.TypeByName("Smoothie.DepthCulling"), "ShowAll", Type.EmptyTypes);
                _showLods = AccessTools.Method(AccessTools.TypeByName("Smoothie.LodCulling"), "ShowAll", Type.EmptyTypes);
                if (on == null || _showDepth == null || _showLods == null)
                    throw new MissingMethodException("installed Smoothie has no verified visibility control surface");
                foreach (string name in new[] { "HideCovered", "SkipOutOfView", "SkipTooFar" })
                {
                    object feature = AccessTools.Field(plugin, name)?.GetValue(null);
                    if (!(feature is ConfigEntry<bool>)) throw new MissingFieldException("Smoothie.Plugin." + name);
                    VisibilityFeatures.Add(feature);
                    if(name=="HideCovered")_depthFeature=feature;
                }
                if (!_patched)
                {
                    new Harmony(Plugin.PluginGuid + ".smoothie").Patch(on,
                        prefix: new HarmonyMethod(typeof(SmoothieCompatibility), nameof(FeaturePrefix)) { priority = Priority.First });
                    _patched = true;
                }
                _presenting = true; _ownsGate = true;
                _resumeAt=Time.unscaledTime+.6f;
                // Invalidate pending occlusion results and release native LOD overrides immediately.
                _showDepth.Invoke(null, null); _showLods.Invoke(null, null);
                Plugin.Log.LogInfo("Smoothie cinematic view: cached depth hiding suspended until eye-camera handoff; frustum/distant LOD optimizations resume after 0.6s. Settings/bot optimizations untouched.");
            }
            catch (Exception ex)
            {
                _presenting = false; _ownsGate = false;
                Plugin.Log.LogWarning("Smoothie visibility compatibility unavailable (not silently disabling the mod): " + ex);
            }
        }

        private static bool FeaturePrefix(object __0, ref bool __result)
        {
            if (!CinematicObservation.SuppressFeature(_presenting,ReferenceEquals(__0,_depthFeature),
                VisibilityFeatures.Contains(__0),Time.unscaledTime,_resumeAt))return true;
            __result = false; return false;
        }

        public void Dispose()
        {
            if (!_ownsGate) return;
            try { _showDepth?.Invoke(null, null); _showLods?.Invoke(null, null); }
            catch (Exception ex) { Plugin.Log.LogWarning("Smoothie visibility reset: " + ex.Message); }
            finally
            {
                _presenting = false; _ownsGate = false;
                Plugin.Log.LogInfo("Smoothie visibility compatibility released; original configured optimizations resume.");
            }
        }
    }
}
