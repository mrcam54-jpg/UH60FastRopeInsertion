using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace UH60Performance
{
    [BepInPlugin(Guid,"JozinZBazin-UH60Performance","0.1.2")]
    [BepInDependency("com.jozinzbazin.uh60fastropeinsertion")]
    [DefaultExecutionOrder(10000)]
    public sealed class Plugin:BaseUnityPlugin
    {
        public const string Guid="com.jozinzbazin.uh60performance";
        internal const string SupportedHash=ReleasePin.Sha256;
        internal static Plugin Instance;
        private Harmony _harmony;
        private Type _controller;
        private PropertyInfo _controllerInstance;
        private FieldInfo _active,_camera,_sky,_sequence,_state,_aircraft,_root;
        private FieldInfo _todSky,_todCamera,_todTransform,_scaleEnabled,_positionEnabled,_scaleFactor,_positionOffset;
        private PropertyInfo _skyInitialized,_skyComponents,_domeTransform;
        private ConfigEntry<bool> _fastCable,_deduplicateSky,_diagnostics,_hideAircraft;
        private readonly ConditionalWeakTable<object,CableRuntime> _cables=new ConditionalWeakTable<object,CableRuntime>();
        private ConditionalWeakTable<object,CableRuntime>.CreateValueCallback _createCable;
        private readonly Dictionary<string,FieldInfo> _cableFields=new Dictionary<string,FieldInfo>();
        private readonly SkyPoseGate _skyGate=new SkyPoseGate();
        private readonly PerformanceTelemetry _telemetry=new PerformanceTelemetry();
        private readonly AircraftProbe _probe=new AircraftProbe();
        private int _previousAircraft;
        private object _previousState;
        internal long CableTicks,WorldTicks,SkyTicks;
        internal int CableCalls,WorldCalls,SkyCalls,SkySkipped;

        private sealed class CableRuntime
        {
            internal CableDeformation Deformation;
            internal Mesh Mesh;
            internal bool Failed;
        }
        private void Awake()
        {
            Instance=this;
            _createCable=CreateCable;
            _fastCable=Config.Bind("01. Optimization","Reuse exact cable frames",true,"Reuse identical cross-section calculations. Original mesh, every vertex, physics and update rate are preserved.");
            _deduplicateSky=Config.Bind("01. Optimization","Skip duplicate sky callbacks",true,"Only during UH60 eye-camera presentation: skip same-frame callbacks with identical camera pose and FOV. Camera/culling/exposure updates remain intact.");
            _diagnostics=Config.Bind("02. Diagnostics","Log performance samples",true,"Log 5-second CPU/engine/GPU samples, including 10 seconds after aircraft cleanup. No in-raid overlay. GPU timing may be unavailable in this game build.");
            _hideAircraft=Config.Bind("02. Diagnostics","Hide aircraft for comparison",false,"DIAGNOSTIC ONLY: hide owned aircraft renderers without removing it or changing its flight. Compare 5-second log windows; switch back off afterwards. Actors, ropes and map stay visible.");
            _hideAircraft.Value=false; // The temporary comparison must never survive a client restart.
            try
            {
                string file=Path.Combine(Paths.PluginPath,"UH60FastRopeInsertion","UH60FastRopeInsertion.dll");
                using(var sha=SHA256.Create())using(var stream=File.OpenRead(file))
                    if(BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","")!=SupportedHash)
                        throw new InvalidOperationException("Unrecognised UH60 binary; refusing internal hooks");
                var owner=BepInEx.Bootstrap.Chainloader.PluginInfos["com.jozinzbazin.uh60fastropeinsertion"].Instance;
                var assembly=owner.GetType().Assembly;
                _controller=assembly.GetType("UH60FastRopeInsertion.InsertionController",true);
                _controllerInstance=AccessTools.Property(_controller,"Instance");
                _active=Required(_controller,"_cameraActive");_camera=Required(_controller,"_camera");_sky=Required(_controller,"_skyCamera");
                _sequence=Required(_controller,"_sequence");_state=Required(_controller,"_state");_aircraft=Required(_controller,"_aircraft");
                var aircraftType=assembly.GetType("UH60FastRopeInsertion.Uh60Visual",true);_root=Required(aircraftType,"_root");
                var cable=assembly.GetType("UH60FastRopeInsertion.OriginalCableVisual",true);
                foreach(var field in new[]{"_source","_sourceNormals","_sourceTangents","_along","_frameNormal","_vertices","_normals","_tangents","_mesh"})
                    _cableFields.Add(field,Required(cable,field));
                _harmony=new Harmony(Guid);
                _harmony.Patch(AccessTools.Method(cable,"SetPoints"),prefix:Hook(nameof(CablePrefix)),postfix:Hook(nameof(CablePostfix)));
                _harmony.Patch(AccessTools.Method(cable,"Dispose"),prefix:Hook(nameof(CableDispose)));
                _harmony.Patch(AccessTools.Method(_controller,"ApplyCameraPoseForWorldSystems"),prefix:Hook(nameof(WorldPrefix)),postfix:Hook(nameof(WorldPostfix)));
                Type skyType=AccessTools.TypeByName("TOD_Camera")??throw new TypeLoadException("TOD_Camera");
                _todSky=Required(skyType,"sky");_todCamera=Required(skyType,"cameraComponent");_todTransform=Required(skyType,"cameraTransform");
                _scaleEnabled=Required(skyType,"DomeScaleToFarClip");_positionEnabled=Required(skyType,"DomePosToCamera");
                _scaleFactor=Required(skyType,"DomeScaleFactor");_positionOffset=Required(skyType,"DomePosOffset");
                _skyInitialized=AccessTools.Property(_todSky.FieldType,"Initialized")??throw new MissingMemberException("TOD_Sky.Initialized");
                _skyComponents=AccessTools.Property(_todSky.FieldType,"Components")??throw new MissingMemberException("TOD_Sky.Components");
                _domeTransform=AccessTools.Property(_skyComponents.PropertyType,"DomeTransform")??throw new MissingMemberException("TOD_Components.DomeTransform");
                _harmony.Patch(AccessTools.Method(skyType,"OnPreCull"),prefix:Hook(nameof(SkyPrefix)),postfix:Hook(nameof(SkyPostfix)));
                Logger.LogInfo("Public 0.1.2 performance module installed for the exact paired core; no flight/pose changes.");
            }
            catch(Exception error)
            {
                _harmony?.UnpatchSelf();_harmony=null;
                Logger.LogError("Performance module disabled safely; base UH60 unchanged: "+error.GetBaseException().Message);
            }
        }
        private static FieldInfo Required(Type type,string name)=>AccessTools.Field(type,name)??throw new MissingFieldException(type.FullName,name);
        private static HarmonyMethod Hook(string name)=>new HarmonyMethod(typeof(Plugin),name);
        private object Controller=>_controllerInstance?.GetValue(null,null);
        private T Read<T>(object owner,string field)=>(T)_cableFields[field].GetValue(owner);
        private CableRuntime CreateCable(object owner)
        {
            var data=new CableRuntime{Mesh=Read<Mesh>(owner,"_mesh")};
            data.Deformation=new CableDeformation(Read<Vector3[]>(owner,"_source"),Read<Vector3[]>(owner,"_sourceNormals"),
                Read<Vector4[]>(owner,"_sourceTangents"),Read<float[]>(owner,"_along"),Read<Vector3[]>(owner,"_frameNormal").Length,
                Read<Vector3[]>(owner,"_vertices"),Read<Vector3[]>(owner,"_normals"),Read<Vector4[]>(owner,"_tangents"));
            Logger.LogInfo("Cable exact-frame cache: "+data.Deformation.Vertices.Length+" vertices / "+data.Deformation.BindingCount+" longitudinal frames; topology/material/physics unchanged.");
            return data;
        }
        private static bool CablePrefix(object __instance,Vector3[] points,out long __state)
        {
            var p=Instance;__state=p._diagnostics.Value?Stopwatch.GetTimestamp():0;
            if(!p._fastCable.Value)return true;
            CableRuntime data=null;
            try
            {
                data=p._cables.GetValue(__instance,p._createCable);
                if(data.Failed)return true;
                data.Deformation.Deform(points);
                // Keep the original mesh setters and native bounds calculation: only repeated
                // managed interpolation is removed. No new mesh layout/index/UV assumptions.
                data.Mesh.vertices=data.Deformation.Vertices;data.Mesh.normals=data.Deformation.Normals;
                data.Mesh.tangents=data.Deformation.Tangents;data.Mesh.RecalculateBounds();
                return false;
            }
            catch(Exception error)
            {
                if(data!=null)data.Failed=true;else p._fastCable.Value=false;
                p.Logger.LogWarning("Cable optimization failed; original method resumes: "+error.GetBaseException().Message);
                return true;
            }
        }
        private static void CablePostfix(long __state){if(__state!=0){Instance.CableTicks+=Stopwatch.GetTimestamp()-__state;Instance.CableCalls++;}}
        private static void CableDispose(object __instance){Instance._cables.Remove(__instance);}
        private static void WorldPrefix(out long __state){__state=Instance._diagnostics.Value?Stopwatch.GetTimestamp():0;}
        private static void WorldPostfix(long __state){if(__state!=0){Instance.WorldTicks+=Stopwatch.GetTimestamp()-__state;Instance.WorldCalls++;}}
        private static bool SkyPrefix(Component __instance,out long __state)
        {
            __state=0;var p=Instance;
            try
            {
                object controller=p.Controller;
                if(controller==null||!(bool)p._active.GetValue(controller)||!ReferenceEquals(p._sky.GetValue(controller),__instance))return true;
                var camera=p._camera.GetValue(controller) as Camera;
                if(camera==null)return true;
                if(p._deduplicateSky.Value&&!p._skyGate.Refresh(Time.frameCount,camera.GetInstanceID(),camera.transform.position,camera.transform.rotation,camera.fieldOfView,
                    camera.farClipPlane,p.NativeSkyMatches(__instance,camera)))
                {p.SkySkipped++;return false;}
                __state=p._diagnostics.Value?Stopwatch.GetTimestamp():0;return true;
            }
            catch(Exception error)
            {
                p._deduplicateSky.Value=false;
                p.Logger.LogWarning("Sky deduplication disabled; native callback resumes: "+error.GetBaseException().Message);
                return true;
            }
        }
        private bool NativeSkyMatches(Component owner,Camera camera)
        {
            // Another camera or TOD update may have moved the shared dome since our last call.
            // In that case NEVER skip the native refresh, even for an identical eye-camera pose.
            object sky=_todSky.GetValue(owner);
            if(sky==null||!(bool)_skyInitialized.GetValue(sky,null)||!ReferenceEquals(_todCamera.GetValue(owner),camera))return false;
            object components=_skyComponents.GetValue(sky,null);
            var dome=components!=null?_domeTransform.GetValue(components,null) as Transform:null;
            var eye=_todTransform.GetValue(owner) as Transform;
            if(dome==null||eye==null)return false;
            Vector3 scale=Vector3.one*((float)_scaleFactor.GetValue(owner)*camera.farClipPlane);
            Vector3 position=eye.position+eye.rotation*(Vector3)_positionOffset.GetValue(owner);
            return SkyPoseGate.Matches((bool)_scaleEnabled.GetValue(owner),(bool)_positionEnabled.GetValue(owner),dome.localScale,scale,dome.position,position);
        }
        private static void SkyPostfix(long __state){if(__state!=0){Instance.SkyTicks+=Stopwatch.GetTimestamp()-__state;Instance.SkyCalls++;}}
        private void LateUpdate()
        {
            if(_harmony==null)return;
            try
            {
                object controller=Controller;
                bool scene=controller!=null&&_sequence.GetValue(controller)!=null;
                var aircraft=controller!=null?_aircraft.GetValue(controller):null;
                var root=aircraft!=null?_root.GetValue(aircraft) as GameObject:null;
                int id=root!=null?root.GetInstanceID():0;
                object state=controller!=null?_state.GetValue(controller):null;
                if(id!=_previousAircraft||!Equals(state,_previousState))
                {_probe.Bind(root,Logger);_previousAircraft=id;_previousState=state;_skyGate.Clear();}
                _probe.SetHidden(_hideAircraft.Value);
                _telemetry.Tick(scene,state,_hideAircraft.Value,_diagnostics.Value,this,Logger);
            }
            catch(Exception error)
            {
                Logger.LogWarning("Performance diagnostics stopped safely: "+error.GetBaseException().Message);
                _probe.Restore();_harmony.UnpatchSelf();_harmony=null;
            }
        }
        internal void ClearCounters(){CableTicks=WorldTicks=SkyTicks=0;CableCalls=WorldCalls=SkyCalls=SkySkipped=0;}
        private void OnDestroy(){_probe.Restore();_harmony?.UnpatchSelf();if(Instance==this)Instance=null;}
    }
}
