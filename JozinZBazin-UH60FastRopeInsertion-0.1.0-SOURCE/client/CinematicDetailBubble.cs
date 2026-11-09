using System;
using System.Collections.Generic;
using Koenigz.PerfectCulling;
using Koenigz.PerfectCulling.EFT;
using UnityEngine;

namespace UH60FastRopeInsertion
{
    // Airborne eyes can leave the ground-baked visibility grid. Force only nearby registered
    // world detail through the game's OWN toggles; don't enable random inactive map objects.
    internal sealed class CinematicDetailBubble : IDisposable
    {
        private sealed class Entry
        {
            internal PerfectCullingBakeGroup Group;
            internal PerfectCullingCrossSceneGroup Owner;
            internal Bounds Bounds;
            internal bool Requested;
        }
        internal static CinematicDetailBubble Active;
        private readonly List<Entry> _entries=new List<Entry>();
        private readonly HashSet<PerfectCullingBakeGroup> _indexed=new HashSet<PerfectCullingBakeGroup>();
        private readonly Dictionary<PerfectCullingBakeGroup,Entry> _forced=new Dictionary<PerfectCullingBakeGroup,Entry>();
        private readonly Dictionary<ScreenDistanceSwitcher,bool> _switchers=new Dictionary<ScreenDistanceSwitcher,bool>();
        private readonly List<ScreenDistanceSwitcher> _allSwitchers=new List<ScreenDistanceSwitcher>();
        private bool _applying;
        private bool _indexDirty;
        private float _nextObserve;
        private const float Radius=160f;
        internal void InvalidateIndex(){_indexDirty=true;}

        internal CinematicDetailBubble()
        {
            if(Active!=null)throw new InvalidOperationException("A cinematic detail owner already exists.");
            Index(); Active=this;
        }
        private void Index()
        {
            _allSwitchers.Clear();
            foreach(var sw in Resources.FindObjectsOfTypeAll<ScreenDistanceSwitcher>())
                if(sw!=null&&sw.gameObject.scene.IsValid())_allSwitchers.Add(sw);
            foreach(var cross in PerfectCullingCrossSceneGroup.AllCrossGroups)
            {
                if(cross==null||!cross.gameObject.scene.IsValid()||cross.bakeGroups==null)continue;
                foreach(var group in cross.bakeGroups)
                {
                    if(group==null||!_indexed.Add(group)||group.groupType==PerfectCullingBakeGroup.GroupType.Light
                        ||group.groupType==PerfectCullingBakeGroup.GroupType.CullingLightObject)continue;
                    bool found=false;Bounds bounds=default;
                    var data=group.RuntimeGroupData;
                    if(data!=null)foreach(var item in data)
                    {
                        var renderer=item.Renderer;
                        if(renderer==null||renderer is SkinnedMeshRenderer||renderer is ParticleSystemRenderer)continue;
                        if(!found){bounds=renderer.bounds;found=true;}else bounds.Encapsulate(renderer.bounds);
                    }
                    if(!found&&group.renderers!=null)foreach(var renderer in group.renderers)
                    {
                        if(renderer==null||renderer is SkinnedMeshRenderer||renderer is ParticleSystemRenderer)continue;
                        if(!found){bounds=renderer.bounds;found=true;}else bounds.Encapsulate(renderer.bounds);
                    }
                    if(found)_entries.Add(new Entry{Group=group,Owner=cross,Bounds=bounds,Requested=group.IsEnabled});
                }
            }
            _indexDirty=false;
        }
        internal void Observe(Vector3 eye)
        {
            if(Time.unscaledTime<_nextObserve)return;
            _nextObserve=Time.unscaledTime+.5f;
            // Invalidate on stream events, not by rescanning every few seconds.
            if(_indexDirty)Index();
            var nearby=new List<Entry>();
            foreach(var entry in _entries)
                if(entry.Owner!=null&&entry.Owner.gameObject.scene.IsValid()&&entry.Bounds.SqrDistance(eye)<=Radius*Radius)nearby.Add(entry);
            nearby.Sort((a,b)=>a.Bounds.SqrDistance(eye).CompareTo(b.Bounds.SqrDistance(eye)));
            var wanted=new HashSet<PerfectCullingBakeGroup>();
            foreach(var entry in nearby)if(wanted.Count<256)wanted.Add(entry.Group);
            _applying=true;
            try
            {
                var remove=new List<PerfectCullingBakeGroup>();
                foreach(var pair in _forced)if(!wanted.Contains(pair.Key))remove.Add(pair.Key);
                foreach(var group in remove){var state=_forced[group];_forced.Remove(group);RestoreGroup(group,state.Requested);}
                foreach(var entry in nearby)if(wanted.Contains(entry.Group))
                {
                    if(!_forced.ContainsKey(entry.Group)){entry.Requested=entry.Group.IsEnabled;_forced.Add(entry.Group,entry);}
                    if(!entry.Group.IsEnabled)entry.Group.Toggle(true);
                }
                var old=new List<ScreenDistanceSwitcher>();
                foreach(var pair in _switchers)
                    if(pair.Key==null||(pair.Key.RuntimeCenter-eye).sqrMagnitude>Radius*Radius)old.Add(pair.Key);
                foreach(var sw in old){bool requested=_switchers[sw];_switchers.Remove(sw);RestoreProxy(sw,requested);}
                foreach(var sw in _allSwitchers)
                {
                    if(sw==null||_switchers.Count>=64||(sw.RuntimeCenter-eye).sqrMagnitude>Radius*Radius)continue;
                    if(!_switchers.ContainsKey(sw))_switchers.Add(sw,sw.IsVisible);
                    // Show(true) displays the baked proxy and hides full-detail descendants.
                    // Show scans descendant meshes; don't repeat that when unchanged.
                    if(sw.IsVisible)sw.Show(false);
                }
            }
            finally{_applying=false;}
        }
        internal void OverrideBake(PerfectCullingBakeGroup group,ref bool visible)
        {
            if(!_forced.TryGetValue(group,out Entry entry))return;
            if(!_applying)entry.Requested=visible;
            visible=true;
        }
        internal void OverrideProxy(ScreenDistanceSwitcher sw,ref bool visible)
        {
            if(!_switchers.ContainsKey(sw))return;
            if(!_applying)_switchers[sw]=visible;
            visible=false;
        }
        public void Dispose()
        {
            if(Active==this)Active=null;
            // A streamed-out renderer may have been destroyed. One missing object must not
            // strand every other group or prevent the camera/Smoothie cleanup that follows.
            foreach(var pair in _forced)RestoreGroup(pair.Key,pair.Value.Requested);
            foreach(var pair in _switchers)RestoreProxy(pair.Key,pair.Value);
            _forced.Clear();_switchers.Clear();_entries.Clear();_indexed.Clear();_allSwitchers.Clear();
            foreach(var camera in PerfectCullingCamera.AllCameras)if(camera!=null)camera.SetDirty();
        }
        private static void RestoreGroup(PerfectCullingBakeGroup group,bool requested)
        {
            try { group.Toggle(requested); }
            catch(Exception ex) { Plugin.Log.LogWarning("Streamed detail restore skipped: "+ex.Message); }
        }
        private static void RestoreProxy(ScreenDistanceSwitcher sw,bool requested)
        {
            try { if(sw!=null)sw.Show(requested); }
            catch(Exception ex) { Plugin.Log.LogWarning("Streamed proxy restore skipped: "+ex.Message); }
        }
    }
}
