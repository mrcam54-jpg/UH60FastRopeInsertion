using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace UH60FastRopeInsertion
{
    internal abstract class CableSurface : IDisposable
    {
        internal abstract void SetPoints(Vector3[] points);
        internal abstract Renderer Renderer { get; }
        public abstract void Dispose();
    }

    // Deform the actual installed Icebreaker cable surface, preserving its cross-section,
    // topology, UV atlas and tangents. This is not a tube that merely shares its material.
    internal sealed class OriginalCableVisual : CableSurface
    {
        private readonly GameObject _root;
        private readonly Mesh _mesh;
        private readonly MeshRenderer _renderer;
        private readonly Vector3[] _source, _sourceNormals, _vertices, _normals;
        private readonly Vector4[] _sourceTangents, _tangents;
        private readonly float[] _along;
        private readonly Vector3[] _frameNormal, _frameTangent;
        internal override Renderer Renderer => _renderer;

        internal OriginalCableVisual(string name, Transform parent, int pathCount, Mesh source, Material material)
        {
            if (source == null || !source.isReadable) throw new InvalidOperationException("Original Icebreaker rope mesh must be a readable baked copy.");
            _source = source.vertices; _sourceNormals = source.normals; _sourceTangents = source.tangents;
            Bounds b = source.bounds;
            if (b.size.z < 3f || b.size.x > 0.3f || b.size.y > 0.3f)
                throw new InvalidOperationException("Original rope is not straight in its authored Z axis: " + b);
            _along = new float[_source.Length];
            for (int i = 0; i < _source.Length; i++) _along[i] = Mathf.Clamp01((b.max.z - _source[i].z) / b.size.z);
            _vertices = new Vector3[_source.Length]; _normals = new Vector3[_source.Length]; _tangents = new Vector4[_source.Length];
            _frameNormal = new Vector3[pathCount]; _frameTangent = new Vector3[pathCount];
            _root = new GameObject(name); _root.transform.SetParent(parent, false);
            _root.layer=CabinLighting.Layer;
            _mesh = UnityEngine.Object.Instantiate(source); _mesh.name = name + "_OriginalIcebreakerSurface";
            _mesh.MarkDynamic(); _mesh.ClearBlendShapes();
            _root.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _renderer = _root.AddComponent<MeshRenderer>(); _renderer.sharedMaterial = material;
            _renderer.shadowCastingMode = ShadowCastingMode.On; _renderer.receiveShadows = true;
            Plugin.Log.LogInfo(name + ": original Icebreaker surface, " + _source.Length + " vertices; source UV/topology retained.");
        }

        internal override void SetPoints(Vector3[] points)
        {
            if (points.Length != _frameNormal.Length) throw new ArgumentException("Original rope centreline sample count changed.");
            Vector3 previous = Vector3.right;
            for (int i = 0; i < points.Length; i++)
            {
                Vector3 tangent = points[Mathf.Min(i + 1, points.Length - 1)] - points[Mathf.Max(i - 1, 0)];
                tangent = tangent.sqrMagnitude > 0.0000001f ? tangent.normalized : Vector3.down;
                Vector3 normal = Vector3.ProjectOnPlane(previous, tangent);
                if (normal.sqrMagnitude < 0.0001f) normal = Vector3.Cross(tangent, Vector3.forward);
                if (normal.sqrMagnitude < 0.0001f) normal = Vector3.Cross(tangent, Vector3.up);
                previous = normal.normalized; _frameNormal[i] = previous; _frameTangent[i] = tangent;
            }
            for (int i = 0; i < _source.Length; i++)
            {
                float sample = _along[i] * (points.Length - 1);
                int k = Mathf.Min((int)sample, points.Length - 2); float blend = sample - k;
                Vector3 t = Vector3.Lerp(_frameTangent[k], _frameTangent[k + 1], blend).normalized;
                Vector3 n = Vector3.Lerp(_frameNormal[k], _frameNormal[k + 1], blend).normalized;
                // Source longitudinal +Z points towards the hook; sampled path points downwards.
                Vector3 side = -Vector3.Cross(t, n).normalized;
                Vector3 p = _source[i];
                _vertices[i] = Vector3.Lerp(points[k], points[k + 1], blend) + n * p.x + side * p.y;
                Vector3 sn = _sourceNormals.Length == _source.Length ? _sourceNormals[i] : new Vector3(p.x, p.y, 0f).normalized;
                _normals[i] = (n * sn.x + side * sn.y - t * sn.z).normalized;
                Vector4 st = _sourceTangents.Length == _source.Length ? _sourceTangents[i] : new Vector4(1, 0, 0, 1);
                Vector3 transformed = (n * st.x + side * st.y - t * st.z).normalized;
                _tangents[i] = new Vector4(transformed.x, transformed.y, transformed.z, st.w);
            }
            _mesh.vertices = _vertices; _mesh.normals = _normals; _mesh.tangents = _tangents;
            _mesh.RecalculateBounds();
        }

        public override void Dispose()
        {
            if (_root != null) UnityEngine.Object.Destroy(_root);
            if (_mesh != null) UnityEngine.Object.Destroy(_mesh);
        }
    }
}
