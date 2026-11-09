using EFT;
using UnityEngine;
using UnityEngine.AI;

namespace UH60FastRopeInsertion
{
    internal static class LandingGeometry
    {
        private static readonly RaycastHit[] Hits = new RaycastHit[128];
        private static readonly Collider[] Overlaps = new Collider[64];

        private static bool IsWorld(Collider collider)
        {
            // Live people, including the hidden PIT squad, are not roofs or terrain. Never disable
            // their controllers to make a query pass; omit only character-owned query hits instead.
            return collider != null && !(collider is CharacterController)
                && collider.GetComponentInParent<Player>() == null;
        }

        private static string Describe(Collider collider)
        {
            return collider == null ? "none" : collider.name + " [layer="
                + LayerMask.LayerToName(collider.gameObject.layer) + ", scene=" + collider.gameObject.scene.name + "]";
        }

        private static bool FirstWorldRay(Vector3 origin, float distance, out RaycastHit nearest, out string reason)
        {
            nearest = default;
            int count = Physics.RaycastNonAlloc(origin, Vector3.down, Hits, distance,
                Physics.AllLayers, QueryTriggerInteraction.Ignore);
            if (count == Hits.Length) { reason = "ray hit buffer exhausted; rejecting conservatively"; return false; }
            float closest = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
                if (Hits[i].distance < closest && IsWorld(Hits[i].collider))
                { nearest = Hits[i]; closest = nearest.distance; }
            reason = float.IsPositiveInfinity(closest) ? "no solid world surface below ray" : Describe(nearest.collider);
            return !float.IsPositiveInfinity(closest);
        }

        internal static bool ClearCapsule(Vector3 a, Vector3 b, float radius, out string reason)
        {
            int count = Physics.OverlapCapsuleNonAlloc(a, b, radius, Overlaps,
                Physics.AllLayers, QueryTriggerInteraction.Ignore);
            if (count == Overlaps.Length) { reason = "capsule hit buffer exhausted"; return false; }
            for (int i = 0; i < count; i++)
                if (IsWorld(Overlaps[i])) { reason = "capsule obstructed by " + Describe(Overlaps[i]); return false; }
            reason = "clear"; return true;
        }

        // A nav point under a roof is not a landing surface. Resolve the first solid surface from
        // the sky, then require the navmesh and a standing capsule to agree with that surface.
        internal static bool TrySurface(Vector3 near, out Vector3 surface)
        { return TrySurface(near, out surface, out _); }

        private static bool TrySurface(Vector3 near, out Vector3 surface, out string reason)
        {
            surface = near;
            RaycastHit hit;
            if (!FirstWorldRay(near + Vector3.up * 65f, 80f, out hit, out reason)) return false;
            if (hit.normal.y < 0.82f) { reason = "surface too steep: " + reason; return false; }
            NavMeshHit nav;
            if (!NavMesh.SamplePosition(hit.point, out nav, 0.65f, NavMesh.AllAreas))
            { reason = "no NavMesh at " + hit.point + " on " + reason; return false; }
            if (Mathf.Abs(nav.position.y - hit.point.y) > 0.35f
                || new Vector2(nav.position.x - hit.point.x, nav.position.z - hit.point.z).sqrMagnitude > 0.36f)
            { reason = "surface/NavMesh mismatch: " + hit.point + " / " + nav.position + " on " + reason; return false; }
            surface = hit.point + Vector3.up * 0.035f;
            return ClearCapsule(surface + Vector3.up * 0.48f, surface + Vector3.up * 1.48f, 0.34f, out reason);
        }

        internal static bool IsOpenGround(Vector3 navPosition)
        {
            Vector3 surface;
            return TrySurface(navPosition, out surface) && Mathf.Abs(surface.y - navPosition.y) < 0.40f;
        }

        internal static bool InGroundBand(Vector3 candidate,Vector3 authoredGround,float tolerance)
        { return Mathf.Abs(candidate.y-authoredGround.y)<=tolerance; }

        internal static bool TryRopeLanding(Vector3 top, Vector3 bodyOffset, out Vector3 surface, out string reason)
        {
            surface = top;
            RaycastHit hit;
            if (!FirstWorldRay(top - Vector3.up * 0.15f, 55f, out hit, out reason)) return false;
            if (hit.normal.y < 0.82f) { reason = "lane first hit too steep: " + reason; return false; }
            if (!TrySurface(hit.point, out surface, out reason)) return false;
            if (Mathf.Abs(surface.y - hit.point.y) > 0.4f)
            { reason = "lane surface differs from open-sky surface"; return false; }
            float height = top.y - surface.y;
            if (height < 4f) { reason = "lane height below 4 m: " + height; return false; }
            Vector3 bodySurface;
            if (!TrySurface(surface + bodyOffset, out bodySurface, out reason)) return false;
            if (Mathf.Abs(bodySurface.y - surface.y) > 0.20f)
            { reason = "body touchdown and rope touchdown are on different levels"; return false; }
            // Clearance includes the inboard body, not just a thin ray at the cable centre.
            return ClearCapsule(bodySurface + Vector3.up * 0.48f,
                top + bodyOffset - Vector3.up * 0.45f, 0.43f, out reason);
        }
    }
}
