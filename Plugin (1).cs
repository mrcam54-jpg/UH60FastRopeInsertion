using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace UH60Performance
{
    [BepInPlugin(Guid, "JozinZBazin-UH60Performance", "0.1.3")]
    [BepInDependency("com.jozinzbazin.uh60fastropeinsertion")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.jozinzbazin.uh60performance";
        internal const string SupportedHash = ReleasePin.Sha256;
        internal static Plugin Instance;
        private Harmony _harmony;
        private Type _controller;
        private PropertyInfo _controllerInstance;
        private FieldInfo _active, _camera, _sky;
        private FieldInfo _todSky, _todCamera, _todTransform, _scaleEnabled, _positionEnabled, _scaleFactor, _positionOffset;
        private PropertyInfo _skyInitialized, _skyComponents, _domeTransform;
        private bool _cableEnabled = true, _skyEnabled = true;
        private readonly ConditionalWeakTable<object, CableRuntime> _cables = new ConditionalWeakTable<object, CableRuntime>();
        private ConditionalWeakTable<object, CableRuntime>.CreateValueCallback _createCable;
        private readonly Dictionary<string, FieldInfo> _cableFields = new Dictionary<string, FieldInfo>();
        private readonly SkyPoseGate _skyGate = new SkyPoseGate();

        private sealed class CableRuntime { internal CableDeformation Deformation; internal Mesh Mesh; internal bool Failed; }
        private void Awake()
        {
            Instance = this;
            _createCable = CreateCable;
            try
            {
                string file = Path.Combine(Paths.PluginPath, "UH60FastRopeInsertion", "UH60FastRopeInsertion.dll");
                using (var sha = SHA256.Create()) using (var stream = File.OpenRead(file))
                    if (BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "") != SupportedHash)
                        throw new InvalidOperationException("Unrecognised UH60 binary; refusing internal hooks");
                var assembly = BepInEx.Bootstrap.Chainloader.PluginInfos["com.jozinzbazin.uh60fastropeinsertion"].Instance.GetType().Assembly;
                _controller = assembly.GetType("UH60FastRopeInsertion.InsertionController", true);
                _controllerInstance = AccessTools.Property(_controller, "Instance");
                _active = Required(_controller, "_cameraActive"); _camera = Required(_controller, "_camera"); _sky = Required(_controller, "_skyCamera");
                var cable = assembly.GetType("UH60FastRopeInsertion.OriginalCableVisual", true);
                foreach (var field in new[] { "_source", "_sourceNormals", "_sourceTangents", "_along", "_frameNormal", "_vertices", "_normals", "_tangents", "_mesh" })
                    _cableFields.Add(field, Required(cable, field));
                _harmony = new Harmony(Guid);
                _harmony.Patch(AccessTools.Method(cable, "SetPoints"), prefix: Hook(nameof(CablePrefix)));
                _harmony.Patch(AccessTools.Method(cable, "Dispose"), prefix: Hook(nameof(CableDispose)));
                Type skyType = AccessTools.TypeByName("TOD_Camera") ?? throw new TypeLoadException("TOD_Camera");
                _todSky = Required(skyType, "sky"); _todCamera = Required(skyType, "cameraComponent"); _todTransform = Required(skyType, "cameraTransform");
                _scaleEnabled = Required(skyType, "DomeScaleToFarClip"); _positionEnabled = Required(skyType, "DomePosToCamera");
                _scaleFactor = Required(skyType, "DomeScaleFactor"); _positionOffset = Required(skyType, "DomePosOffset");
                _skyInitialized = AccessTools.Property(_todSky.FieldType, "Initialized") ?? throw new MissingMemberException("TOD_Sky.Initialized");
                _skyComponents = AccessTools.Property(_todSky.FieldType, "Components") ?? throw new MissingMemberException("TOD_Sky.Components");
                _domeTransform = AccessTools.Property(_skyComponents.PropertyType, "DomeTransform") ?? throw new MissingMemberException("TOD_Components.DomeTransform");
                _harmony.Patch(AccessTools.Method(skyType, "OnPreCull"), prefix: Hook(nameof(SkyPrefix)));
                Logger.LogInfo("Public 0.1.3 optimizations enabled for the exact paired core.");
            }
            catch (Exception error)
            {
                _harmony?.UnpatchSelf(); _harmony = null;
                Logger.LogError("Performance module disabled safely; base UH60 unchanged: " + error.GetBaseException().Message);
            }
        }
        private static FieldInfo Required(Type type, string name) => AccessTools.Field(type, name) ?? throw new MissingFieldException(type.FullName, name);
        private static HarmonyMethod Hook(string name) => new HarmonyMethod(typeof(Plugin), name);
        private object Controller => _controllerInstance?.GetValue(null, null);
        private T Read<T>(object owner, string field) => (T)_cableFields[field].GetValue(owner);
        private CableRuntime CreateCable(object owner)
        {
            var data = new CableRuntime { Mesh = Read<Mesh>(owner, "_mesh") };
            data.Deformation = new CableDeformation(Read<Vector3[]>(owner, "_source"), Read<Vector3[]>(owner, "_sourceNormals"),
                Read<Vector4[]>(owner, "_sourceTangents"), Read<float[]>(owner, "_along"), Read<Vector3[]>(owner, "_frameNormal").Length,
                Read<Vector3[]>(owner, "_vertices"), Read<Vector3[]>(owner, "_normals"), Read<Vector4[]>(owner, "_tangents"));
            return data;
        }
        private static bool CablePrefix(object __instance, Vector3[] points)
        {
            var p = Instance;
            if (!p._cableEnabled) return true;
            CableRuntime data = null;
            try
            {
                data = p._cables.GetValue(__instance, p._createCable);
                if (data.Failed) return true;
                data.Deformation.Deform(points);
                data.Mesh.vertices = data.Deformation.Vertices; data.Mesh.normals = data.Deformation.Normals;
                data.Mesh.tangents = data.Deformation.Tangents; data.Mesh.RecalculateBounds();
                return false;
            }
            catch (Exception error)
            {
                if (data != null) data.Failed = true; else p._cableEnabled = false;
                p.Logger.LogWarning("Cable optimization failed; original method resumes: " + error.GetBaseException().Message);
                return true;
            }
        }
        private static void CableDispose(object __instance) { Instance._cables.Remove(__instance); }
        private static bool SkyPrefix(Component __instance)
        {
            var p = Instance;
            if (!p._skyEnabled) return true;
            try
            {
                object controller = p.Controller;
                if (controller == null || !(bool)p._active.GetValue(controller) || !ReferenceEquals(p._sky.GetValue(controller), __instance)) return true;
                var camera = p._camera.GetValue(controller) as Camera;
                if (camera == null) return true;
                return p._skyGate.Refresh(Time.frameCount, camera.GetInstanceID(), camera.transform.position, camera.transform.rotation, camera.fieldOfView,
                    camera.farClipPlane, p.NativeSkyMatches(__instance, camera));
            }
            catch (Exception error)
            {
                p._skyEnabled = false;
                p.Logger.LogWarning("Sky deduplication disabled; native callback resumes: " + error.GetBaseException().Message);
                return true;
            }
        }
        private bool NativeSkyMatches(Component owner, Camera camera)
        {
            object sky = _todSky.GetValue(owner);
            if (sky == null || !(bool)_skyInitialized.GetValue(sky, null) || !ReferenceEquals(_todCamera.GetValue(owner), camera)) return false;
            object components = _skyComponents.GetValue(sky, null);
            var dome = components != null ? _domeTransform.GetValue(components, null) as Transform : null;
            var eye = _todTransform.GetValue(owner) as Transform;
            if (dome == null || eye == null) return false;
            Vector3 scale = Vector3.one * ((float)_scaleFactor.GetValue(owner) * camera.farClipPlane);
            Vector3 position = eye.position + eye.rotation * (Vector3)_positionOffset.GetValue(owner);
            return SkyPoseGate.Matches((bool)_scaleEnabled.GetValue(owner), (bool)_positionEnabled.GetValue(owner), dome.localScale, scale, dome.position, position);
        }
        private void OnDestroy() { _harmony?.UnpatchSelf(); if (Instance == this) Instance = null; }
    }
}
