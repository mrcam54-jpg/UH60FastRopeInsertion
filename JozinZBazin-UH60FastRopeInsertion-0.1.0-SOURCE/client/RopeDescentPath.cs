using UnityEngine;

namespace UH60FastRopeInsertion
{
    internal static class RopeDescentPath
    {
        // The contact belongs at the HAND'S height on the rendered rope, not at the
        // actor's normalized progress along a cable that already reaches the ground coil.
        internal static Vector3 AtHeight(Vector3[] points, float height)
        {
            if (height >= points[0].y) return points[0];
            for (int i=0; i<points.Length-1; i++)
            {
                Vector3 a=points[i], b=points[i+1];
                if (a.y >= height && b.y <= height && a.y-b.y > .00001f)
                    return Vector3.Lerp(a,b,(a.y-height)/(a.y-b.y));
            }
            return points[points.Length-1];
        }
    }
}
