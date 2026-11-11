using UnityEngine;

namespace UH60FastRopeInsertion
{
    internal static class LandingPlacement
    {
        // Align the PLAYER'S touchdown to the annotated point, not the aircraft's prefab origin.
        // Other actors use the existing distinct two-rope lanes; never stack both ropes together.
        internal static Vector3 RootForTouchdown(Vector3 touchdown,Vector3 leftHoverOffset,
            Quaternion leftFacing,float bodyOffset)
        {
            Vector3 offset=leftHoverOffset-leftFacing*Vector3.forward*bodyOffset;
            return touchdown-new Vector3(offset.x,0f,offset.z);
        }
        internal static float[] Bearings(float authored)
        {
            return new[]{authored,(authored+90f)%360f,(authored+180f)%360f,(authored+270f)%360f,
                (authored+45f)%360f,(authored+135f)%360f,(authored+225f)%360f,(authored+315f)%360f};
        }
    }
}
