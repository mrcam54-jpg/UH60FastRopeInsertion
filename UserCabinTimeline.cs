using UnityEngine;
namespace UH60FastRopeInsertion
{
    internal static class UserCabinTimeline
    {
        internal const float DeploymentSeconds=8f;
        internal static float Smooth(float p){p=Mathf.Clamp01(p);return p*p*p*(p*(p*6-15)+10);}
        private static float Range(float t,float a,float b)=>Smooth(Mathf.InverseLerp(a,b,t));
        internal static float ReleaseSeconds(int side)=>side<0?2.8f:3.25f;
        internal static float PackedPhase(float seconds,int side)
        {
            float release=ReleaseSeconds(side),start=side<0?1.3f:2.55f;
            return seconds<=release?.74f*Range(seconds,start,release):Mathf.Lerp(.74f,1,Mathf.Clamp01((seconds-release)/2.4f));
        }
        internal static Vector3 CoilCentre(Transform frame,Vector3 start,Vector3 ground,float seconds,int side)
        {
            float release=ReleaseSeconds(side);Vector3 outward=frame.right*(side<0?-1:1);
            Vector3 end=start+outward*.55f;
            if(seconds<=release)return Vector3.Lerp(start,end,Range(seconds,side<0?1.3f:2.55f,release));
            float fall=Mathf.Clamp01((seconds-release)/2.4f);
            return Vector3.Lerp(end,ground+Vector3.up*.024f,fall*fall);
        }
    }
}
