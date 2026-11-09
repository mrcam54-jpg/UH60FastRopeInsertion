using System;
using UnityEngine;

namespace UH60FastRopeInsertion
{
    // One continuous centreline drives the SAME SteelRope surface from packed to paid-out.
    // No independent coil renderer, disappearing prop, alpha swap or second lead mesh.
    internal sealed class CabinRopeBundle : IDisposable
    {
        private readonly int _side;
        private readonly Vector3[] _path = new Vector3[SteelRope.RenderCount];
        private readonly Vector3[] _settled = new Vector3[SteelRope.RenderCount];
        private Vector3 _release;
        private bool _released;
        internal CabinRopeBundle(Material unusedMaterial=null, Mesh unusedMesh=null, int side=-1) { _side=side<0?-1:1; }

        internal static Vector3 EvaluateCentre(Uh60Visual aircraft,float p,Vector3 ground,int side=-1)
        {
            var f=aircraft.RootTransform;Vector3 start=aircraft.GetCabinRopeBundlePoint(side);
            Vector3 outward=f.right*(side<0?-1:1);
            if(side<0)
            {
                CabinCue cue=CabinChoreography.Evaluate(Mathf.Min(p,CabinChoreography.Release));
                Vector3 released=start+f.right*cue.Coil.x+f.up*cue.Coil.y+f.forward*cue.Coil.z;
                if(p<=CabinChoreography.Release)return released;
                float fallPhase=Mathf.Clamp01((p-CabinChoreography.Release)/(1-CabinChoreography.Release));
                return Vector3.Lerp(released,ground+Vector3.up*.024f,fallPhase*fallPhase);
            }
            Vector3 lift=start+f.up*.55f+outward*.10f;
            Vector3 release=start+f.up*.68f+outward*.86f;
            if(side>0) { lift=start+f.up*.08f+outward*.15f;release=start+f.up*.02f+outward*.76f; }
            if(p<.25f)return start;
            if(p<.53f)return Vector3.Lerp(start,lift,Smooth((p-.25f)/.28f));
            if(p<CabinChoreography.Release)return Vector3.Lerp(lift,release,Smooth((p-.53f)/(CabinChoreography.Release-.53f)));
            float fall=Mathf.Clamp01((p-CabinChoreography.Release)/(1-CabinChoreography.Release));
            return Vector3.Lerp(release,ground+Vector3.up*.024f,fall*fall);
        }
        internal static Vector3 EvaluateGrip(Uh60Visual aircraft,float p,Vector3 ground,float t)
        {
            Vector3 centre=EvaluateCentre(aircraft,Mathf.Min(p,CabinChoreography.Release),ground);
            // Match actual points on the coil, not empty space at its origin.
            return centre+CoilAxis(aircraft.RootTransform.rotation,Mathf.Min(p,CabinChoreography.Release))*.26f
                +aircraft.RootTransform.forward*(t<.01f?-.203f:.203f)+aircraft.RootTransform.up*.035f;
        }
        internal static Vector3 CoilAxis(Quaternion frame,float p)
        {
            float lift=Smooth(Mathf.InverseLerp(.18f,.38f,p));
            float lay=1-Smooth(Mathf.InverseLerp(CabinChoreography.Release,1f,p));
            float angle=65f*lift*lay*Mathf.PI/180f;
            return frame*(Vector3.right*Mathf.Cos(angle)-Vector3.up*Mathf.Sin(angle));
        }
        internal void Tick(Uh60Visual aircraft,float p,SteelRope rope=null,Vector3? hit=null,CinematicActor handler=null,bool authored=false)
        {
            if(rope==null||aircraft==null)return;
            p=Mathf.Clamp01(p);var frame=aircraft.RootTransform;
            Vector3 ground=hit??(aircraft.GetDoorAnchor(_side)-Vector3.up*13f);
            Vector3 anchor=aircraft.GetDoorAnchor(_side);
            Vector3 centre=EvaluateCentre(aircraft,p,ground,_side);
            if(authored)
            {
                float seconds=p*UserCabinTimeline.DeploymentSeconds;
                centre=UserCabinTimeline.CoilCentre(frame,aircraft.GetCabinRopeBundlePoint(_side),ground,seconds,_side);
                p=UserCabinTimeline.PackedPhase(seconds,_side);
            }
            if(p<.01f)_released=false;
            if(p>=CabinChoreography.Release&&!_released) { _release=EvaluateCentre(aircraft,CabinChoreography.Release,ground,_side);_released=true; }
            if(!authored && p>=CabinChoreography.Release&&_released)
            {
                float fall=Mathf.Clamp01((p-CabinChoreography.Release)/(1-CabinChoreography.Release));
                centre=Vector3.Lerp(_release,ground+Vector3.up*.024f,fall*fall);
            }
            rope.CopyPath(_settled);
            float leadLength=Mathf.Max(.2f,Vector3.Distance(anchor,centre));
            float remaining=Mathf.Max(2.8f,aircraft.PackedRopeLength-leadLength);
            float total=leadLength+remaining;
            float turns=remaining/(Mathf.PI*2f*.265f);
            Vector3 a=!authored&&_side<0?CoilAxis(frame.rotation,p):frame.right,b=frame.forward;
            float grounded=Smooth(Mathf.InverseLerp(.84f,1f,p));
            a=Vector3.Lerp(a,Vector3.right,grounded).normalized;b=Vector3.Lerp(b,Vector3.forward,grounded).normalized;
            float join=Smooth(Mathf.InverseLerp(.91f,1f,p));
            for(int i=0;i<_path.Length;i++)
            {
                float arc=i/(float)(_path.Length-1)*total;
                if(arc<leadLength)
                {
                    float t=arc/leadLength;
                    Vector3 first=anchor-Vector3.up*Mathf.Min(.42f,leadLength*.32f);
                    Vector3 second=Vector3.Lerp(anchor,centre,.76f)-Vector3.up*.12f;
                    _path[i]=(1-t)*(1-t)*(1-t)*anchor+3*(1-t)*(1-t)*t*first
                        +3*(1-t)*t*t*second+t*t*t*centre;
                    if(p<(authored?.74f:.53f))
                    {
                        Vector3 local=frame.InverseTransformPoint(_path[i]);
                        if(Mathf.Abs(local.x)<1.3f)local.y=Mathf.Max(local.y,aircraft.CabinFloor+.024f);
                        _path[i]=frame.TransformPoint(local);
                    }
                }
                else
                {
                    float t=(arc-leadLength)/remaining,angle=t*turns*Mathf.PI*2f;
                    float radius=Mathf.Lerp(.21f,.32f,t);
                    _path[i]=centre+a*(radius*(1-Mathf.Cos(angle))*(_side<0?1:-1))
                        +b*(radius*Mathf.Sin(angle)*.78f);
                    if(p>.84f)_path[i].y=Mathf.Max(_path[i].y,ground.y+.024f);
                }
                _path[i]=Vector3.Lerp(_path[i],_settled[i],join);
            }
            rope.OverridePresentationPath(_path);
        }
        private static float Smooth(float p) { p=Mathf.Clamp01(p);return p*p*p*(p*(p*6-15)+10); }
        public void Dispose() { }
    }
}
