using System;
using System.Diagnostics;
using UnityEngine;

namespace UH60FastRopeInsertion
{
    internal enum PresentationStage { Cabin, Ropes, Bundle, Descent }
    // Cheap scoped CPU measurements, not an FPS optimizer or a GPU profiler.
    internal sealed class PresentationMetrics
    {
        private long _sceneTicks,_worldTicks,_peak;
        private int _frames,_worldCalls;
        private float _elapsed,_next;
        private readonly long[] _stages=new long[4];
        internal void Stage(PresentationStage stage,long ticks){_stages[(int)stage]+=ticks;}
        internal void Scene(long ticks){_sceneTicks+=ticks;_peak=Math.Max(_peak,ticks);_frames++;_elapsed+=Time.unscaledDeltaTime;}
        internal void World(long ticks){_worldTicks+=ticks;_worldCalls++;}
        internal void Flush(string state)
        {
            if(_next<=0){_next=Time.unscaledTime+5;return;}
            if(Time.unscaledTime<_next||_frames==0)return;
            double ms=1000.0/Stopwatch.Frequency;
            Plugin.Log.LogInfo("Scene CPU ["+state+"]: late avg="+(_sceneTicks*ms/_frames).ToString("F2")
                +"ms peak="+(_peak*ms).ToString("F2")+"ms; observer total/frame="+(_worldTicks*ms/_frames).ToString("F2")
                +"ms calls/frame="+(_worldCalls/(float)_frames).ToString("F1")+"; observed FPS="+(_frames/Math.Max(.001f,_elapsed)).ToString("F1")
                +"; managedMB="+(GC.GetTotalMemory(false)/1048576.0).ToString("F1")+" GC0="+GC.CollectionCount(0)
                +"; stages/frame ms cabin="+(_stages[0]*ms/_frames).ToString("F2")
                +" ropes="+(_stages[1]*ms/_frames).ToString("F2")+" packed="+(_stages[2]*ms/_frames).ToString("F2")
                +" descent="+(_stages[3]*ms/_frames).ToString("F2")
                +" (CPU scopes overlap; GPU/other mods not profiled)");
            Array.Clear(_stages,0,_stages.Length);
            _sceneTicks=_worldTicks=_peak=0;_frames=_worldCalls=0;_elapsed=0;_next=Time.unscaledTime+5;
        }
    }
}
