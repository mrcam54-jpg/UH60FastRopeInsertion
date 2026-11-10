using UnityEngine;
using UnityEngine.AI;

namespace UH60FastRopeInsertion
{
    internal static class BotLandingGeometry
    {
        internal static bool TryAnchor(Vector3 position,out Vector3 anchor)
        {
            anchor=position;
            if(!NavMesh.SamplePosition(position,out NavMeshHit nav,.65f,NavMesh.AllAreas))return false;
            Vector3 delta=nav.position-position;
            if(Mathf.Abs(delta.y)>.35f || delta.x*delta.x+delta.z*delta.z>.36f)return false;
            anchor=nav.position;return true;
        }
    }
}
