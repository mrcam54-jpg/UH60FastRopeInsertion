using UnityEngine;
namespace UH60FastRopeInsertion
{
    internal static class CinematicObservation
    {
        // Visibility samples are ground-baked. This affects ONLY the bake sample, not the camera,
        // Streamer, world geometry, pawn, frustum matrix or flight. No projection across map edges.
        internal static bool TryProject(Vector3 eye,Bounds bounds,out Vector3 sample)
        {
            var c=bounds.center;var e=bounds.extents;sample=eye;
            if(e.x<.1f||e.y<.1f||e.z<.1f)return false;
            sample=new Vector3(Mathf.Clamp(eye.x,c.x-e.x+.05f,c.x+e.x-.05f),
                Mathf.Clamp(eye.y,c.y-e.y+.05f,c.y+e.y-.05f),Mathf.Clamp(eye.z,c.z-e.z+.05f,c.z+e.z-.05f));
            Vector3 d=sample-eye;
            return d.x*d.x+d.z*d.z<16f && Mathf.Abs(d.y)<=180f;
        }
        internal static bool SuppressFeature(bool presenting,bool depth,bool known,float now,float resume)
            =>presenting&&known&&(depth||now<resume);
    }
}
