using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace UH60FastRopeInsertion
{
    // Loading an additive reference must not import its world lighting into the running raid.
    // This guard only spans the reference load/extraction; it is NOT a permanent weather override.
    internal sealed class SourceSceneEnvironment : IDisposable
    {
        private readonly Scene _scene = SceneManager.GetActiveScene();
        private readonly SourceShaderState _shaders=new SourceShaderState();
        private readonly Material _sky = RenderSettings.skybox;
        private readonly Light _sun = RenderSettings.sun;
        private readonly AmbientMode _ambientMode = RenderSettings.ambientMode;
        private readonly Color _ambient = RenderSettings.ambientLight;
        private readonly Color _skyColour = RenderSettings.ambientSkyColor;
        private readonly Color _equator = RenderSettings.ambientEquatorColor;
        private readonly Color _ground = RenderSettings.ambientGroundColor;
        private readonly float _ambientIntensity = RenderSettings.ambientIntensity;
        private readonly float _reflectionIntensity = RenderSettings.reflectionIntensity;
        private readonly SphericalHarmonicsL2 _ambientProbe = RenderSettings.ambientProbe;
        private readonly bool _fog = RenderSettings.fog;
        private readonly FogMode _fogMode = RenderSettings.fogMode;
        private readonly Color _fogColour = RenderSettings.fogColor;
        private readonly float _fogDensity = RenderSettings.fogDensity;
        private readonly float _fogStart = RenderSettings.fogStartDistance;
        private readonly float _fogEnd = RenderSettings.fogEndDistance;
        private bool _disposed;

        internal SourceSceneEnvironment() { Plugin.Log.LogInfo("Before reference load: " + Describe()); }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Plugin.Log.LogInfo("After reference extraction, before environment restore: " + Describe());
            if (_scene.IsValid() && _scene.isLoaded) SceneManager.SetActiveScene(_scene);
            RenderSettings.skybox = _sky; RenderSettings.sun = _sun;
            RenderSettings.ambientMode = _ambientMode; RenderSettings.ambientLight = _ambient;
            RenderSettings.ambientSkyColor = _skyColour; RenderSettings.ambientEquatorColor = _equator;
            RenderSettings.ambientGroundColor = _ground; RenderSettings.ambientIntensity = _ambientIntensity;
            RenderSettings.reflectionIntensity = _reflectionIntensity;
            // Colours/intensity alone do not restore the irradiance used to shade the skins.
            // This is a one-time source-load guard, NOT a per-frame weather/exposure override.
            RenderSettings.ambientProbe = _ambientProbe;
            RenderSettings.fog = _fog; RenderSettings.fogMode = _fogMode; RenderSettings.fogColor = _fogColour;
            RenderSettings.fogDensity = _fogDensity; RenderSettings.fogStartDistance = _fogStart;
            RenderSettings.fogEndDistance = _fogEnd;
            _shaders.Restore();
            Plugin.Log.LogInfo("Reference environment restored once: " + Describe());
        }

        internal static string Describe()
        {
            return "scene=" + SceneManager.GetActiveScene().name + "; ambient=" + RenderSettings.ambientMode
                + "/" + RenderSettings.ambientIntensity.ToString("F3") + "; fog=" + RenderSettings.fog
                + "/" + RenderSettings.fogMode + "/" + RenderSettings.fogDensity.ToString("F5")
                + "; sky=" + (RenderSettings.skybox != null ? RenderSettings.skybox.name : "none");
        }
    }
}
