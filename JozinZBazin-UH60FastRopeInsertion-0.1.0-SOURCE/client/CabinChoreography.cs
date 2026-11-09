using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;

namespace UH60FastRopeInsertion
{
    // Our authored Blender controls, not a generated replacement for a missing native clip.
    internal struct CabinCue
    {
        internal Vector3 Coil;
        internal float Yaw,Lean,Shift,Left,Right;
    }
    internal static class CabinChoreography
    {
        internal const float Release=.64f;
        private static readonly CabinCue[] Frames=Read();
        private static CabinCue[] Read()
        {
            using(var stream=typeof(CabinChoreography).Assembly.GetManifestResourceStream("UH60.CabinCues"))
            {
                if(stream==null)throw new InvalidOperationException("Authored cabin cue resource missing.");
                using(var reader=new StreamReader(stream))
                {
                    reader.ReadLine();var frames=new List<CabinCue>();string line;
                    while((line=reader.ReadLine())!=null)
                    {
                        var cells=line.Split(',');if(cells.Length!=9)throw new InvalidDataException("Invalid cabin cue row.");
                        var v=new float[9];for(int i=0;i<9;i++)
                        {v[i]=float.Parse(cells[i],CultureInfo.InvariantCulture);if(float.IsNaN(v[i])||float.IsInfinity(v[i]))throw new InvalidDataException("Nonfinite cabin cue.");}
                        frames.Add(new CabinCue{Coil=new Vector3(v[1],v[2],v[3]),Yaw=v[4],Lean=v[5],Shift=v[6],Left=v[7],Right=v[8]});
                    }
                    if(frames.Count!=241)throw new InvalidDataException("Expected 241 authored cabin cue frames.");
                    return frames.ToArray();
                }
            }
        }
        internal static CabinCue Evaluate(float phase)
        {
            float f=Mathf.Clamp01(phase)*(Frames.Length-1);int i=Mathf.Min((int)f,Frames.Length-2);float t=f-i;
            var a=Frames[i];var b=Frames[i+1];
            return new CabinCue{Coil=Vector3.Lerp(a.Coil,b.Coil,t),Yaw=Mathf.Lerp(a.Yaw,b.Yaw,t),Lean=Mathf.Lerp(a.Lean,b.Lean,t),
                Shift=Mathf.Lerp(a.Shift,b.Shift,t),Left=Mathf.Lerp(a.Left,b.Left,t),Right=Mathf.Lerp(a.Right,b.Right,t)};
        }
    }
}
