using System;
using System.Collections.Generic;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.Rendering;

namespace UH60Performance
{
    // Explicit user-controlled A/B rendering test. Never disables the flight root, Animator,
    // colliders, camera, map, characters, light/shadow settings or cable simulation.
    internal sealed class AircraftProbe
    {
        private readonly Dictionary<Renderer,bool> _saved=new Dictionary<Renderer,bool>();
        private Renderer[] _renderers=new Renderer[0];
        private bool _hidden;
        internal void Bind(GameObject root,ManualLogSource log)
        {
            Restore();_renderers=root!=null?root.GetComponentsInChildren<Renderer>(true):new Renderer[0];
            if(root==null)return;
            int drawing=0,skinned=0,shadow=0,vertices=0,particles=0,activeParticles=0,cameras=0,lights=0,cloth=0;
            var materials=new HashSet<Material>();
            foreach(var renderer in _renderers)
            {
                if(renderer.enabled&&!renderer.forceRenderingOff&&renderer.gameObject.activeInHierarchy)drawing++;
                if(renderer.shadowCastingMode!=ShadowCastingMode.Off)shadow++;
                Mesh mesh=null;
                if(renderer is SkinnedMeshRenderer skin){skinned++;mesh=skin.sharedMesh;}
                else {var filter=renderer.GetComponent<MeshFilter>();if(filter!=null)mesh=filter.sharedMesh;}
                if(mesh!=null)vertices+=mesh.vertexCount;
                foreach(var material in renderer.sharedMaterials)if(material!=null)materials.Add(material);
            }
            foreach(var p in root.GetComponentsInChildren<ParticleSystem>(true)){particles++;if(p.gameObject.activeInHierarchy&&p.isPlaying)activeParticles++;}
            foreach(var c in root.GetComponentsInChildren<Camera>(true))if(c.enabled&&c.gameObject.activeInHierarchy)cameras++;
            foreach(var l in root.GetComponentsInChildren<Light>(true))if(l.enabled&&l.gameObject.activeInHierarchy)lights++;
            foreach(var c in root.GetComponentsInChildren<Component>(true))if(c!=null&&c.GetType().Name=="Cloth")cloth++;
            log.LogInfo("Aircraft inventory (owned hierarchy only): renderers="+_renderers.Length+" drawing="+drawing+" skinned="+skinned
                +" verticesIncludingHiddenLODs="+vertices+" shadowCapable="+shadow+" materials="+materials.Count
                +" particleSystems="+particles+" playing="+activeParticles+" activeCameras="+cameras+" activeLights="+lights+" cloth="+cloth);
        }
        internal void SetHidden(bool hidden)
        {
            if(_hidden==hidden)return;
            if(!hidden){Restore();return;}
            foreach(var renderer in _renderers)if(renderer!=null){_saved.Add(renderer,renderer.forceRenderingOff);renderer.forceRenderingOff=true;}
            _hidden=true;
        }
        internal void Restore()
        {
            foreach(var pair in _saved)if(pair.Key!=null)pair.Key.forceRenderingOff=pair.Value;
            _saved.Clear();_hidden=false;
        }
    }
}
