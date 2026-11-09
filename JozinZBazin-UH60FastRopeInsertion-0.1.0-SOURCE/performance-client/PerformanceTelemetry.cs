using System;
using System.Diagnostics;
using BepInEx.Logging;
using UnityEngine;

namespace UH60Performance
{
    internal sealed class PerformanceTelemetry
    {
        private readonly FrameTiming[] _timing=new FrameTiming[1];
        private bool _featureChecked,_engineAvailable,_wasScene,_seenScene,_started,_hidden;
        private float _postUntil;
        private string _phase;
        private object _state;
        private int _frames,_engineSamples,_gpuSamples;
        private long _start;
        private ulong _lastTiming;
        private double _cpu,_main,_render,_wait,_gpu;
        private readonly Func<long> _clock;
        internal PerformanceTelemetry(Func<long> clock=null){_clock=clock??Stopwatch.GetTimestamp;}

        internal void Tick(bool scene,object state,bool hidden,bool enabled,Plugin plugin,ManualLogSource log)
        {
            float now=Time.realtimeSinceStartup;
            if(scene){_postUntil=now+10f;_seenScene=true;}
            bool observe=enabled&&(scene||(_seenScene&&now<_postUntil));
            if(!observe){if(_started)Flush(plugin,log);_started=false;_wasScene=scene;plugin.ClearCounters();return;}
            bool changed=!Equals(state,_state)||_wasScene!=scene||_hidden!=hidden;
            if(changed&&_started)Flush(plugin,log);
            if(!_started||changed)
            {
                _phase=scene?Convert.ToString(state):"AfterCleanup";_state=state;_hidden=hidden;
                _start=_clock();_started=true;
            }
            _wasScene=scene;_frames++;
            SampleEngine(log);
            if((_clock()-_start)/(double)Stopwatch.Frequency>=5)Flush(plugin,log);
        }
        private void SampleEngine(ManualLogSource log)
        {
            if(!_featureChecked)
            {
                _featureChecked=true;
                try{_engineAvailable=FrameTimingManager.IsFeatureEnabled();}
                catch(Exception){_engineAvailable=false;}
                log.LogInfo("Native FrameTimingManager available="+_engineAvailable+". No player/graphics settings changed; missing GPU samples will be marked unavailable.");
            }
            if(!_engineAvailable)return;
            try
            {
                FrameTimingManager.CaptureFrameTimings();
                if(FrameTimingManager.GetLatestTimings(1,_timing)==0)return;
                var frame=_timing[0];
                if(frame.cpuFrameTime<=0||(frame.frameStartTimestamp!=0&&frame.frameStartTimestamp==_lastTiming))return;
                _lastTiming=frame.frameStartTimestamp;
                _cpu+=frame.cpuFrameTime;_main+=frame.cpuMainThreadFrameTime;
                _render+=frame.cpuRenderThreadFrameTime;_wait+=frame.cpuMainThreadPresentWaitTime;_engineSamples++;
                if(frame.gpuFrameTime>0){_gpu+=frame.gpuFrameTime;_gpuSamples++;}
            }
            catch(Exception error){_engineAvailable=false;log.LogWarning("Native frame timings unavailable: "+error.GetBaseException().Message);}
        }
        private void Flush(Plugin p,ManualLogSource log)
        {
            if(_frames>0)
            {
                double seconds=(_clock()-_start)/(double)Stopwatch.Frequency;
                double ms=1000.0/Stopwatch.Frequency/_frames;
                log.LogInfo("Perf ["+_phase+"] aircraftHidden="+_hidden+" frames="+_frames+" wallSeconds="+seconds.ToString("F2")
                    +" wallFPS="+(_frames/Math.Max(.001,seconds)).ToString("F1")
                    +" cable="+(p.CableTicks*ms).ToString("F2")+"ms/frame uploads="+(p.CableCalls/(double)_frames).ToString("F2")
                    +" observer="+(p.WorldTicks*ms).ToString("F2")+"ms/frame calls="+(p.WorldCalls/(double)_frames).ToString("F2")
                    +" sky="+(p.SkyTicks*ms).ToString("F2")+"ms/frame executed="+(p.SkyCalls/(double)_frames).ToString("F2")
                    +" duplicateSkipped="+(p.SkySkipped/(double)_frames).ToString("F2")
                    +"; engine="+(_engineSamples>0?"cpu="+(_cpu/_engineSamples).ToString("F2")+" main="+(_main/_engineSamples).ToString("F2")
                        +" render="+(_render/_engineSamples).ToString("F2")+" presentWait="+(_wait/_engineSamples).ToString("F2")+"ms":"unavailable")
                    +" gpu="+(_gpuSamples>0?(_gpu/_gpuSamples).ToString("F2")+"ms":"unavailable")
                    +" managedMB="+(GC.GetTotalMemory(false)/1048576.0).ToString("F1")
                    +" (CPU scopes overlap; engine timings delayed; no GPU claim when unavailable)");
            }
            _frames=_engineSamples=_gpuSamples=0;_cpu=_main=_render=_wait=_gpu=0;
            p.ClearCounters();_start=_clock();
        }
    }
}
