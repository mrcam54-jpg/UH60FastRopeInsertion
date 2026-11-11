using System;
using System.Collections.Generic;
using Koenigz.PerfectCulling;
using Koenigz.PerfectCulling.EFT;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UH60FastRopeInsertion
{
    // Move observers, not authored terrain state: a disabled Terrain can be the
    // authoring heightmap beneath shipped mesh terrain, not missing world detail.
    internal sealed class CinematicVisibility : IDisposable
    {
        private readonly Camera _camera;
        private readonly PerfectCullingCamera _observer;
        private readonly bool _ownsObserver;
        private readonly Vector3 _originalObservation;
        private readonly SmoothieCompatibility _smoothie;
        private readonly CinematicDetailBubble _detail;
        private float _nextSample;
        private Vector3 _sample;
        private bool _projected;
        private readonly Dictionary<GameObjectsToggleByDistance, Transform> _origins = new Dictionary<GameObjectsToggleByDistance, Transform>();

        internal CinematicVisibility(Camera camera)
        {
            _camera=camera;
            _observer=camera.GetComponent<PerfectCullingCamera>();
            if(_observer==null){_observer=camera.gameObject.AddComponent<PerfectCullingCamera>();_ownsObserver=true;}
            _originalObservation=_observer.ObservePosition;
            foreach(var toggle in Resources.FindObjectsOfTypeAll<GameObjectsToggleByDistance>())Track(toggle);
            SceneManager.sceneLoaded+=OnSceneLoaded;
            _smoothie=new SmoothieCompatibility();
            _detail=new CinematicDetailBubble();
            Plugin.Log.LogInfo("Cinematic visibility: native camera observation; no Terrain/LOD0/stream-distance overrides; distance observers="+_origins.Count);
        }
        private void Track(GameObjectsToggleByDistance toggle)
        {
            if(toggle==null||!toggle.gameObject.scene.IsValid()||_origins.ContainsKey(toggle))return;
            _origins.Add(toggle,toggle.pointForCalculation);toggle.pointForCalculation=_camera.transform;
        }
        private void OnSceneLoaded(Scene scene,LoadSceneMode mode)
        {
            if(!scene.IsValid()||scene.name=="Icebreaker_cutscene_01")return;
            foreach(var root in scene.GetRootGameObjects())
                foreach(var toggle in root.GetComponentsInChildren<GameObjectsToggleByDistance>(true))Track(toggle);
            _detail.InvalidateIndex();
        }
        internal void Observe(Vector3 position)
        {
            if(Time.unscaledTime>=_nextSample)
            {
                _nextSample=Time.unscaledTime+.2f;_sample=position;float best=float.PositiveInfinity;
                foreach(var volume in PerfectCullingCrossSceneVolume.AllRuntimeCrossGroupVolumes)
                {
                    if(volume==null)continue;
                    if(volume.runtimeBounds.Contains(position)){_sample=position;best=0;break;}
                    if(!CinematicObservation.TryProject(position,volume.runtimeBounds,out var candidate))continue;
                    float distance=(candidate-position).sqrMagnitude;
                    if(distance<best){best=distance;_sample=candidate;}
                }
                bool projected=(_sample-position).sqrMagnitude>.01f;
                if(projected!=_projected)Plugin.Log.LogInfo("Airborne native culling sample projected="+projected
                    +"; eye="+position+"; bake sample="+_sample+". Eye/flight/stream distances unchanged.");
                _projected=projected;
            }
            if(_observer!=null)_observer.ObservePosition=_sample;
            _detail.Observe(position);
        }
        public void Dispose()
        {
            SceneManager.sceneLoaded-=OnSceneLoaded;
            try{_detail.Dispose();}
            finally
            {
                foreach(var origin in _origins)if(origin.Key!=null)origin.Key.pointForCalculation=origin.Value;
                _origins.Clear();
                if(_observer!=null){_observer.ObservePosition=_originalObservation;_observer.SetDirty();}
                if(_ownsObserver&&_observer!=null)UnityEngine.Object.Destroy(_observer);
                _smoothie.Dispose();
            }
        }
    }
}
