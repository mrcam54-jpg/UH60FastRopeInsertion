using System;
using UnityEngine;

namespace UH60FastRopeInsertion
{
    // Fixed-step damped span + independently dense slack path. Surface resolution must not be
    // tied to the 40 physics particles: that left six vertices for several turns and drew a star.
    internal sealed class SteelRope : IDisposable
    {
        private const int PhysicsCount = 40;
        internal const int RenderCount = 320;
        private const float Radius = 0.024f;
        private readonly GameObject _root;
        private readonly Material _material;
        private readonly CableSurface _surface;
        private readonly Vector3[] _points = new Vector3[PhysicsCount];
        private readonly Vector3[] _previous = new Vector3[PhysicsCount];
        private readonly Vector3[] _path = new Vector3[RenderCount];
        private readonly int _side;
        private bool _initialised;
        private Vector3 _top, _ground;
        private float _visible01, _retract01, _deployedLength, _spanLength, _vertical;
        private float _accumulator;
        internal Transform Transform => _root.transform;

        internal SteelRope(string name, int side, Material sourceMaterial = null, Mesh sourceMesh = null)
        {
            _side = side < 0 ? -1 : 1;
            _root = new GameObject(name); UnityEngine.Object.DontDestroyOnLoad(_root);
            _material = sourceMaterial != null ? new Material(sourceMaterial)
                : new Material(Shader.Find("Standard") ?? Shader.Find("Legacy Shaders/Diffuse"));
            if (sourceMaterial == null)
            {
                _material.color = new Color(0.075f, 0.085f, 0.09f, 1f);
                if (_material.HasProperty("_Metallic")) _material.SetFloat("_Metallic", 0.88f);
                if (_material.HasProperty("_Glossiness")) _material.SetFloat("_Glossiness", 0.43f);
            }
            _surface = sourceMesh != null
                ? (CableSurface)new OriginalCableVisual(name + "_OriginalSurface", _root.transform, RenderCount, sourceMesh, _material)
                : new CableTubeVisual(name + "_ExplicitTacticalServicesSurface", _root.transform, RenderCount, _material);
        }

        internal void SetDeployment(float visible01, float retract01)
        {
            _visible01 = Mathf.Clamp01(visible01); _retract01 = Mathf.Clamp01(retract01);
            _root.SetActive(_visible01 > 0.002f && _retract01 < 0.999f);
        }
        internal void SetSurfaceVisible(bool visible) { _surface.Renderer.forceRenderingOff = !visible; }
        internal void CopyPath(Vector3[] destination) { Array.Copy(_path,destination,RenderCount); }
        internal void OverridePresentationPath(Vector3[] path)
        {
            Array.Copy(path,_path,RenderCount); _root.SetActive(true);
            _surface.Renderer.forceRenderingOff=false; _surface.SetPoints(_path);
        }

        internal void Tick(Vector3 top, Vector3 ground, Vector3 wind, float deltaTime, bool updateSurface = true)
        {
            _top = top; _ground = ground;
            float shown = Mathf.Clamp01(_visible01 * (1f - _retract01));
            if (shown <= 0.002f) return;
            _vertical = Mathf.Max(0.5f, top.y - ground.y - Radius);
            _deployedLength = Mathf.Max(0.18f, (_vertical + 2.8f) * shown);
            _spanLength = Mathf.Min(_vertical, _deployedLength);
            Vector3 end = top + Vector3.down * _spanLength;
            if (!_initialised)
            {
                for (int i = 0; i < PhysicsCount; i++)
                    _previous[i] = _points[i] = Vector3.Lerp(top, end, i / (float)(PhysicsCount - 1));
                _initialised = true;
            }
            // Transport the first support smoothly, but do not add a frame-rate-dependent jiggle.
            _accumulator = Mathf.Min(_accumulator + Mathf.Max(0f, deltaTime), 4f / 60f);
            const float dt = 1f / 60f;
            while (_accumulator >= dt)
            {
                _accumulator -= dt;
                _points[0] = top; _points[PhysicsCount - 1] = end;
                for (int i = 1; i < PhysicsCount - 1; i++)
                {
                    Vector3 current = _points[i];
                    Vector3 velocity = (current - _previous[i]) * 0.84f;
                    _previous[i] = current;
                    Vector3 target = Vector3.Lerp(top, end, i / (float)(PhysicsCount - 1));
                    Vector3 force = wind * 0.12f + (target - current) * 38f;
                    _points[i] = current + velocity + force * (dt * dt);
                }
                float segment = _spanLength / (PhysicsCount - 1);
                for (int iteration = 0; iteration < 7; iteration++)
                {
                    _points[0] = top; _points[PhysicsCount - 1] = end;
                    for (int i = 0; i < PhysicsCount - 1; i++)
                    {
                        Vector3 delta = _points[i + 1] - _points[i];
                        Vector3 correction = delta * ((delta.magnitude - segment) / Mathf.Max(0.0001f, delta.magnitude));
                        if (i == 0) _points[i + 1] -= correction;
                        else if (i == PhysicsCount - 2) _points[i] += correction;
                        else { _points[i] += correction * 0.5f; _points[i + 1] -= correction * 0.5f; }
                    }
                }
                for (int i = 1; i < PhysicsCount - 1; i++)
                {
                    Vector3 guide = Vector3.Lerp(top, end, i / (float)(PhysicsCount - 1));
                    // Heavy, tensioned steel cable: finite restrained lag, not a rigid line or cloth.
                    _points[i] = guide + Vector3.ClampMagnitude(_points[i] - guide, 0.075f);
                    _points[i].y = Mathf.Max(_points[i].y, ground.y + Radius);
                }
            }
            _points[0] = top; _points[PhysicsCount - 1] = end;
            for (int i = 0; i < RenderCount; i++)
            {
                float arc = i / (float)(RenderCount - 1) * _deployedLength;
                _path[i] = SampleArc(arc);
            }
            // Smooth only the contact bend (not the suspended cable or hands). A small fillet and
            // ground clamp avoid a sharp 90-degree kink or below-ground Catmull overshoot.
            int joint = Mathf.Clamp(Mathf.RoundToInt(_vertical / _deployedLength * (RenderCount - 1)), 2, RenderCount - 3);
            if (_deployedLength > _vertical + 0.2f)
                for (int pass = 0; pass < 3; pass++)
                    for (int i = Mathf.Max(1, joint - 3); i <= Mathf.Min(RenderCount - 2, joint + 3); i++)
                    {
                        _path[i] = (_path[i - 1] + _path[i] * 2f + _path[i + 1]) * 0.25f;
                        _path[i].y = Mathf.Max(_path[i].y, ground.y + Radius);
                    }
            // During cabin/payout the SAME surface receives the packed presentation path later
            // this frame. Keep every physics/path step, skip only its hidden intermediate upload.
            if (updateSurface) _surface.SetPoints(_path);
        }

        private Vector3 SampleArc(float arc)
        {
            if (arc <= _spanLength || _deployedLength <= _vertical)
            {
                float index = Mathf.Clamp01(arc / Mathf.Max(0.001f, _spanLength)) * (PhysicsCount - 1);
                int a = Mathf.Min((int)index, PhysicsCount - 2); float t = index - a;
                Vector3 p0 = _points[Mathf.Max(a - 1, 0)], p1 = _points[a];
                Vector3 p2 = _points[a + 1], p3 = _points[Mathf.Min(a + 2, PhysicsCount - 1)];
                return 0.5f * ((2f * p1) + (-p0 + p2) * t
                    + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t * t
                    + (-p0 + 3f * p1 - 3f * p2 + p3) * t * t * t);
            }
            float extra = arc - _vertical;
            float angle = extra * 4.4f;
            float radius = 0.18f + extra * 0.12f;
            float direction = _side < 0 ? 1f : -1f;
            // Start at the contact point, then lay two smooth, loosely growing turns. Each turn
            // receives ~25-35 surface samples rather than the old two/three aliased physics knots.
            return _ground + Vector3.up * Radius
                + Vector3.right * (radius * (1f - Mathf.Cos(angle)) * direction)
                + Vector3.forward * (radius * Mathf.Sin(angle) * 0.76f);
        }

        internal Vector3 EvaluateDescent(float progress)
        {
            return EvaluateCable(Mathf.Clamp01(progress) * Mathf.Max(0.5f, _top.y - _ground.y));
        }
        internal Vector3 EvaluateCable(float arcMetres)
        {
            float sample = Mathf.Clamp01(arcMetres / Mathf.Max(0.001f, _deployedLength)) * (RenderCount - 1);
            int a = Mathf.Min((int)sample, RenderCount - 2);
            return Vector3.Lerp(_path[a], _path[a + 1], sample - a);
        }
        public void Dispose()
        {
            _surface.Dispose();
            if (_root != null) UnityEngine.Object.Destroy(_root);
            if (_material != null) UnityEngine.Object.Destroy(_material);
        }
    }
}

