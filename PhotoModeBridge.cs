using System;
using System.Reflection;
using BepInEx.Bootstrap;
using UnityEngine;

namespace UH60FastRopeInsertion
{
    // Own adapter: inspect documented-by-source state only, never alter DuckCam.
    internal static class PhotoModeBridge
    {
        private static FieldInfo _cameraOwned, _worldFrozen, _fixedStep;
        internal static void Initialize()
        {
            if (!Chainloader.PluginInfos.TryGetValue("com.hj.duckcam", out var plugin)) return;
            try
            {
                Assembly assembly = plugin.Instance.GetType().Assembly;
                var flags = assembly.GetType("DuckCam.Pause.FreezeFlags", true);
                _cameraOwned = flags.GetField("CameraOwned", BindingFlags.Public | BindingFlags.Static);
                _worldFrozen = flags.GetField("WorldTickFrozen", BindingFlags.Public | BindingFlags.Static);
                _fixedStep = assembly.GetType("DuckCam.Core.PhotoTime", true)
                    .GetField("FixedStep", BindingFlags.Public | BindingFlags.Static);
                if (_cameraOwned?.FieldType != typeof(bool) || _worldFrozen?.FieldType != typeof(bool)
                    || _fixedStep?.FieldType != typeof(float)) throw new MissingFieldException("DuckCam photo state changed");
                Plugin.Log.LogInfo("Optional DuckCam camera/freeze adapter bound; no foreign state writes.");
            }
            catch (Exception error)
            {
                _cameraOwned = _worldFrozen = _fixedStep = null;
                Plugin.Log.LogWarning("DuckCam adapter unavailable; native scaled time retained: " + error.Message);
            }
        }
        internal static bool OwnsCamera => ReadBool(_cameraOwned);
        internal static bool Frozen => ReadBool(_worldFrozen);
        private static bool ReadBool(FieldInfo field) => field != null && (bool)field.GetValue(null);
        internal static float FixedStep => _fixedStep == null ? 0f : (float)_fixedStep.GetValue(null);
    }

    internal static class SceneClock
    {
        internal static float Elapsed { get; private set; }
        internal static float Delta => PresentationPolicy.Delta(Time.deltaTime, Time.timeScale,
            PhotoModeBridge.FixedStep, PhotoModeBridge.Frozen);
        internal static bool Paused => Delta <= 0f;
        internal static void Reset() { Elapsed = 0f; }
        internal static void Advance() { Elapsed += Delta; }
    }
}
