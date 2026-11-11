using UnityEngine;

namespace UH60FastRopeInsertion
{
    internal sealed partial class CinematicActor
    {
        internal Vector3 UserShoulder => _leftUpperArm.position+_cabinFrame*Vector3.up*.025f;
        internal Vector3 UserHead => _head.position;
        internal float UserPreparationSeconds(Transform aircraft,Vector3 stand)=>_userPerformance.PreparationSeconds(aircraft,stand);
        internal void ApplyUserCabin(CabinClipData clip,Transform aircraft,float seconds,float deployment,
            Vector3 coil,float cabinFloor,Vector3? partner)
        {
            ValidateCabinClip(clip);
            _userPerformance.Apply(clip,aircraft,seconds,deployment,_bodyLag,coil,cabinFloor,partner);
        }
        internal void PrepareUserExit(CabinClipData clip,Transform aircraft,float seconds,Vector3 coil,float cabinFloor,
            Vector3 stand,Quaternion facing,Vector3 outside,Vector3 axis,float elapsed)
        {
            _userPerformance.Prepare(clip,aircraft,seconds,_bodyLag,coil,cabinFloor,stand,facing,outside,axis,elapsed);
        }
    }
}
