using UnityEngine;
namespace UH60Performance
{
    internal sealed class SkyPoseGate
    {
        private bool _seen;
        private int _frame,_camera;
        private Vector3 _position;
        private Quaternion _rotation;
        private float _fov,_farClip;
        internal bool Refresh(int frame,int camera,Vector3 position,Quaternion rotation,float fov,float farClip=0,bool nativeStateMatches=true)
        {
            // Exact component comparisons: even tiny real changes must run the native callback.
            bool duplicate=_seen&&_frame==frame&&_camera==camera&&_position.x==position.x&&_position.y==position.y&&_position.z==position.z
                &&_rotation.x==rotation.x&&_rotation.y==rotation.y&&_rotation.z==rotation.z&&_rotation.w==rotation.w&&_fov==fov&&_farClip==farClip;
            _seen=true;_frame=frame;_camera=camera;_position=position;_rotation=rotation;_fov=fov;_farClip=farClip;
            return !duplicate||!nativeStateMatches;
        }
        internal static bool Matches(bool scaleEnabled,bool positionEnabled,Vector3 scale,Vector3 expectedScale,Vector3 position,Vector3 expectedPosition)
            =>(!scaleEnabled||Same(scale,expectedScale))&&(!positionEnabled||Same(position,expectedPosition));
        private static bool Same(Vector3 a,Vector3 b)=>a.x==b.x&&a.y==b.y&&a.z==b.z;
        internal void Clear(){_seen=false;}
    }
}
