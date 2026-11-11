using UnityEngine;

namespace UH60FastRopeInsertion
{
    // One shared supported pose for exit/slide/landing. Tested through this actual
    // class, not a second Python implementation. All positions are bone centres.
    internal sealed class SupportedRopePose
    {
        internal const float BodyOffset=.34f;
        internal const float ContactHeight=1.32f;
        internal const float BootCentreHeight=.067f;
        private readonly Transform _root,_pelvis,_spine,_lt,_lc,_lf,_rt,_rc,_rf;
        private readonly Quaternion _leftBoot,_rightBoot;
        private readonly RopeGripSolver _left,_right;
        internal SupportedRopePose(Transform root,Transform pelvis,Transform spine,
            Transform lt,Transform lc,Transform lf,Transform rt,Transform rc,Transform rf,RopeGripSolver left,RopeGripSolver right)
        {
            _root=root;_pelvis=pelvis;_spine=spine;_lt=lt;_lc=lc;_lf=lf;_rt=rt;_rc=rc;_rf=rf;_left=left;_right=right;
            _leftBoot=Quaternion.Inverse(root.rotation)*lf.rotation;_rightBoot=Quaternion.Inverse(root.rotation)*rf.rotation;
        }
        internal static float Smooth(float p){p=Mathf.Clamp01(p);return p*p*p*(p*(p*6-15)+10);}
        internal void Apply(Vector3 position,Quaternion rotation,Vector3 axis,float progress,float grip)
        {
            _root.SetPositionAndRotation(position,rotation);
            float land=Smooth(Mathf.InverseLerp(.82f,1f,progress));
            _pelvis.position-=Vector3.up*(.12f*(1-land));
            _spine.rotation=Quaternion.AngleAxis(-5f*(1-land),_root.right)*_spine.rotation;
            // Grip at face height, not the old waist-level hands-on-hips pose.
            _left.Fit(axis+Vector3.up*.23f,Vector3.up,grip);
            _right.Fit(axis+Vector3.up*.04f,Vector3.up,grip);
            Vector3 l=axis-Vector3.up*.99f-_root.right*.065f+_root.forward*.12f;
            Vector3 r=axis-Vector3.up*1.15f+_root.right*.065f-_root.forward*.08f;
            l=Vector3.Lerp(l,position-_root.right*.18f+Vector3.up*BootCentreHeight,land);
            r=Vector3.Lerp(r,position+_root.right*.18f+Vector3.up*BootCentreHeight,land);
            PoseMath.SolveTwoBone(_lt,_lc,_lf,l,_root.forward-_root.right*.12f,1);
            PoseMath.SolveTwoBone(_rt,_rc,_rf,r,_root.forward+_root.right*.12f,1);
            _lf.rotation=rotation*_leftBoot;_rf.rotation=rotation*_rightBoot;
        }
        internal void Land(Vector3 position,Quaternion rotation,Vector3 axis,float progress)
        {
            Apply(position,rotation,axis,1,1);
            // Retain the SAME grip until release, then blend the entire arm/digits
            // once. Don't blend body to a standing pose and run fractional IK again.
            _left.Release(position+_root.up*.98f-_root.right*.24f+_root.forward*.12f,
                Smooth(Mathf.InverseLerp(.18f,.78f,progress)));
            _right.Release(position+_root.up*.96f+_root.right*.24f+_root.forward*.12f,
                Smooth(Mathf.InverseLerp(.35f,.94f,progress)));
        }
    }
}
