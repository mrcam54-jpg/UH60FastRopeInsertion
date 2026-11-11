using UnityEngine;

namespace UH60FastRopeInsertion
{
    internal sealed class SupportedCabinPose
    {
        private readonly Transform _root,_spine,_lt,_lc,_lf,_rt,_rc,_rf,_lu,_ll,_lp,_ru,_rl,_rp;
        private readonly Quaternion _leftBoot,_rightBoot;
        internal SupportedCabinPose(Transform root,Transform spine,Transform lt,Transform lc,Transform lf,
            Transform rt,Transform rc,Transform rf,Transform lu,Transform ll,Transform lp,Transform ru,Transform rl,Transform rp)
        {
            _root=root;_spine=spine;_lt=lt;_lc=lc;_lf=lf;_rt=rt;_rc=rc;_rf=rf;_lu=lu;_ll=ll;_lp=lp;_ru=ru;_rl=rl;_rp=rp;
            _leftBoot=Quaternion.Inverse(root.rotation)*lf.rotation;_rightBoot=Quaternion.Inverse(root.rotation)*rf.rotation;
        }
        internal void Apply(Vector3 floor,Quaternion rotation,Vector3 leftFoot,Vector3 rightFoot)
        {
            _root.SetPositionAndRotation(floor-rotation*Vector3.up*.19f,rotation);
            _spine.rotation=Quaternion.AngleAxis(22f,_root.right)*_spine.rotation;
            PoseMath.SolveTwoBone(_lt,_lc,_lf,leftFoot,_root.forward-_root.right*.12f,1);
            PoseMath.SolveTwoBone(_rt,_rc,_rf,rightFoot,_root.forward+_root.right*.12f,1);
            _lf.rotation=rotation*_leftBoot;_rf.rotation=rotation*_rightBoot;
            PoseMath.SolveTwoBone(_lu,_ll,_lp,floor+_root.up*1.04f-_root.right*.16f+_root.forward*.33f,
                -_root.right*.22f+_root.forward*.12f-_root.up*.8f,1);
            PoseMath.SolveTwoBone(_ru,_rl,_rp,floor+_root.up*.84f+_root.right*.22f+_root.forward*.24f,
                _root.right*.22f+_root.forward*.12f-_root.up*.8f,1);
        }
    }
}
