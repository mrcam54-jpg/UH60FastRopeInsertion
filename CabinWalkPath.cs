using UnityEngine;

namespace UH60FastRopeInsertion
{
    // Foot contacts share the root trajectory. A supporting boot stays at its
    // previous step on the cabin floor, instead of sliding with a sine wave.
    internal static class CabinWalkPath
    {
        internal static Vector3 Centre(Vector3 from,Vector3 to,float p)
            => Vector3.Lerp(from,to,SupportedRopePose.Smooth(p));
        internal static Vector3 Foot(Vector3 from,Vector3 to,Quaternion facing,float p,bool left)
        {
            int steps=Mathf.Clamp(Mathf.CeilToInt(Vector3.Distance(from,to)/.32f),2,8);
            float clock=Mathf.Clamp01(p)*steps;
            int current=Mathf.Min(steps-1,Mathf.FloorToInt(clock));
            int own=current;
            if((own%2==0)!=left)own--;
            Vector3 up=facing*Vector3.up,right=facing*Vector3.right;
            float side=left?-.17f:.17f;
            if(own<0)return from+right*side+up*SupportedRopePose.BootCentreHeight;
            float previous=own<2?0f:Mathf.Min(1,(own-.5f)/steps);
            float next=own>=steps-2?1f:Mathf.Min(1,(own+1.5f)/steps);
            float swing=SupportedRopePose.Smooth(Mathf.Clamp01(clock-own));
            Vector3 a=Centre(from,to,previous),b=Centre(from,to,next);
            return Vector3.Lerp(a,b,swing)+right*side+up*(SupportedRopePose.BootCentreHeight+Mathf.Sin(swing*Mathf.PI)*.055f);
        }
    }
}
