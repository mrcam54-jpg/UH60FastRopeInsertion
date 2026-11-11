using UnityEngine;

namespace UH60FastRopeInsertion
{
    internal static class PoseMath
    {
        internal static void SolveTwoBone(Transform root,Transform middle,Transform end,Vector3 target,Vector3 pole,float weight)
        {
            if(root==null||middle==null||end==null||weight<=.001f)return;
            Vector3 origin=root.position;float a=Vector3.Distance(origin,middle.position),b=Vector3.Distance(middle.position,end.position);
            if(a<.0001f||b<.0001f)return;
            Vector3 delta=target-origin;if(delta.sqrMagnitude<.00001f)return;
            float distance=Mathf.Clamp(delta.magnitude,Mathf.Abs(a-b)+.0001f,a+b-.0001f);
            Vector3 direction=delta.normalized,bend=Vector3.ProjectOnPlane(pole,direction).normalized;
            if(bend.sqrMagnitude<.01f)bend=Vector3.ProjectOnPlane(Vector3.forward,direction).normalized;
            if(bend.sqrMagnitude<.01f)bend=Vector3.ProjectOnPlane(Vector3.right,direction).normalized;
            float along=(a*a+distance*distance-b*b)/(2*distance);
            Vector3 elbow=origin+direction*along+bend*Mathf.Sqrt(Mathf.Max(0,a*a-along*along));
            root.rotation=Quaternion.Slerp(root.rotation,Quaternion.FromToRotation(middle.position-origin,elbow-origin)*root.rotation,weight);
            Vector3 reachable=origin+direction*distance;
            middle.rotation=Quaternion.Slerp(middle.rotation,Quaternion.FromToRotation(end.position-middle.position,reachable-middle.position)*middle.rotation,weight);
        }
    }
}
