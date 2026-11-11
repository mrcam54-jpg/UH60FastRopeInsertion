using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace UH60FastRopeInsertion
{
    // A real round cable surface for the cabin coil and paid-out lead, including proper UVs and
    // tangent vectors for Icebreaker's normal-mapped material. No billboard strip near the hands.
    internal sealed class CableTubeVisual : CableSurface
    {
        private const int Sides = 8;
        private const int Stride = Sides + 1;
        private readonly GameObject _root;
        private readonly Mesh _mesh;
        private readonly Vector3[] _vertices, _normals;
        private readonly Vector4[] _tangents;
        private readonly Vector2[] _uv;
        private readonly int _count;
        internal override Renderer Renderer => _root.GetComponent<MeshRenderer>();
        internal CableTubeVisual(string name, Transform parent, int count, Material material)
        {
            _count = count;
            _root = new GameObject(name); _root.transform.SetParent(parent, false);
            _mesh = new Mesh { name = name }; _mesh.MarkDynamic();
            _root.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var renderer = _root.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.On; renderer.receiveShadows = true;
            _vertices = new Vector3[count * Stride]; _normals = new Vector3[count * Stride];
            _tangents = new Vector4[count * Stride]; _uv = new Vector2[count * Stride];
            int[] triangles = new int[(count - 1) * Sides * 6];
            int cursor = 0;
            for (int ring = 0; ring < count - 1; ring++)
                for (int side = 0; side < Sides; side++)
                {
                    int a = ring * Stride + side, b = a + 1;
                    int c = a + Stride, d = b + Stride;
                    triangles[cursor++] = a; triangles[cursor++] = b; triangles[cursor++] = c;
                    triangles[cursor++] = b; triangles[cursor++] = d; triangles[cursor++] = c;
                }
            _mesh.vertices = _vertices; _mesh.triangles = triangles;
        }

        internal override void SetPoints(Vector3[] points)
        {
            float arc = 0f;
            for (int i = 0; i < _count; i++)
            {
                Vector3 axis = points[Mathf.Min(i + 1, _count - 1)] - points[Mathf.Max(i - 1, 0)];
                if (axis.sqrMagnitude < 0.000001f) axis = Vector3.down;
                axis.Normalize();
                Vector3 n = Vector3.Cross(axis, Mathf.Abs(axis.y) > 0.92f ? Vector3.right : Vector3.up).normalized;
                Vector3 b = Vector3.Cross(axis, n).normalized;
                if (i > 0) arc += Vector3.Distance(points[i], points[i - 1]);
                for (int side = 0; side <= Sides; side++)
                {
                    float angle = side * Mathf.PI * 2f / Sides;
                    Vector3 radial = n * Mathf.Cos(angle) + b * Mathf.Sin(angle);
                    Vector3 tangent = -n * Mathf.Sin(angle) + b * Mathf.Cos(angle);
                    int index = i * Stride + side;
                    _vertices[index] = points[i] + radial * 0.024f;
                    _normals[index] = radial; _tangents[index] = new Vector4(tangent.x, tangent.y, tangent.z, 1f);
                    _uv[index] = new Vector2(side / (float)Sides, arc * 0.5f);
                }
            }
            _mesh.vertices = _vertices; _mesh.normals = _normals; _mesh.tangents = _tangents; _mesh.uv = _uv;
            _mesh.RecalculateBounds();
        }

        public override void Dispose() { if (_root != null) UnityEngine.Object.Destroy(_root); if (_mesh != null) UnityEngine.Object.Destroy(_mesh); }
    }
}
