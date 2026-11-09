using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;

namespace UH60FastRopeInsertion
{
    // Borrow only the aircraft geometry from the installed, intact cutscene. Its Timeline, actors,
    // cameras, lights and effects never drive this scene. Tactical Services retains the flight rig.
    internal sealed class IcebreakerAirframe : IDisposable
    {
        private const string SceneName = "Icebreaker_cutscene_01";
        private Scene _sourceScene;
        private bool _ownsScene;
        private GameObject _shell;
        private AssetBundle _ownedBundle;
        private readonly List<Material> _materials = new List<Material>();
        private Transform _mainRotor, _tailRotor, _leftDoor, _rightDoor;
        private Quaternion _mainRest, _tailRest;
        private Vector3 _leftRest, _rightRest;
        private readonly AircraftDoorMotion _doors=new AircraftDoorMotion();
        private float _angle;
        private SourceSceneEnvironment _sourceEnvironment;
        private Mesh _connectorMesh,_leftConnectorMesh;
        private Vector3 _leftFixedMount,_rightFixedMount;
        private readonly List<GameObject> _connectors=new List<GameObject>();
        internal string Error { get; private set; }
        internal bool Ready { get { return _shell != null && _mainRotor != null && _tailRotor != null; } }
        internal Material RopeMaterial { get; private set; }
        internal Mesh OriginalRopeMesh { get; private set; }
        internal NativeCabinMotion SeatedMotion { get; private set; }
        internal NativeCabinMotion GuardMotion { get; private set; }
        internal Vector3 LeftRopeAnchor { get; private set; }
        internal Vector3 RightRopeAnchor { get; private set; }

        internal IEnumerator Prepare()
        {
            _sourceScene = SceneManager.GetSceneByName(SceneName);
            if (!_sourceScene.IsValid() || !_sourceScene.isLoaded)
            {
                _sourceEnvironment = new SourceSceneEnvironment();
                if (!Application.CanStreamedLevelBeLoaded(SceneName))
                {
                    string path = Path.Combine(BepInEx.Paths.PluginPath, "ManimalIcebreaker", "streamingassets", "Windows", "assets", "content", "locations", "icebreaker", "icebreaker_scenes.bundle");
                    if (!File.Exists(path)) { Error = "Installed Icebreaker scenes bundle is missing: " + path; yield break; }
                    var request = AssetBundle.LoadFromFileAsync(path);
                    yield return request;
                    _ownedBundle = request.assetBundle;
                    if (_ownedBundle == null || !Application.CanStreamedLevelBeLoaded(SceneName))
                    { Error = "Icebreaker cutscene bundle could not register its source scene."; yield break; }
                }
                var load = SceneManager.LoadSceneAsync(SceneName, LoadSceneMode.Additive);
                if (load == null) { Error = "Icebreaker airframe scene load could not start."; yield break; }
                while (!load.isDone) yield return null;
                _sourceScene = SceneManager.GetSceneByName(SceneName);
                _ownsScene = true;
            }
            if (!_sourceScene.IsValid() || !_sourceScene.isLoaded) { Error = "Icebreaker source scene failed to load."; yield break; }
            GameObject cutsceneRoot = _sourceScene.GetRootGameObjects().FirstOrDefault(x => x.name == "CutsceneRoot");
            if (cutsceneRoot == null) { Error = "No CutsceneRoot in Icebreaker source scene."; yield break; }
            if (_ownsScene)
            {
                // Suppress source rendering/cameras immediately; do not play or sample the Timeline.
                foreach (var root in _sourceScene.GetRootGameObjects()) root.SetActive(false);
                foreach (var director in cutsceneRoot.GetComponentsInChildren<PlayableDirector>(true)) director.enabled = false;
            }
            var source = cutsceneRoot.transform.Find("Helicopter_01/Helicopter_Sikorsky_HH60_Assembled");
            if (source == null) { Error = "Icebreaker aircraft hierarchy not found."; yield break; }
            SeatedMotion=NativeCabinMotion.Read(cutsceneRoot.transform,"Actor_Jumper00","Cutscene_Icebtreaker_Helicopter_Jumper00");
            GuardMotion=NativeCabinMotion.Read(cutsceneRoot.transform,"Actor_RopeHelper","Cutscene_Icebtreaker_Helicopter_RopeHelper");
            _shell = UnityEngine.Object.Instantiate(source.gameObject);
            _shell.name = "UH60_IcebreakerAircraftOnly";
            UnityEngine.Object.DontDestroyOnLoad(_shell);
            _shell.SetActive(false);
            _shell.transform.SetParent(null, false);
            _shell.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, 180f, 0f));
            _shell.transform.localScale = Vector3.one;
            foreach (var behaviour in _shell.GetComponentsInChildren<MonoBehaviour>(true)) UnityEngine.Object.DestroyImmediate(behaviour);
            foreach (var animator in _shell.GetComponentsInChildren<Animator>(true)) UnityEngine.Object.DestroyImmediate(animator);
            foreach (var collider in _shell.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            foreach (var rigidbody in _shell.GetComponentsInChildren<Rigidbody>(true))
            { rigidbody.isKinematic = true; rigidbody.detectCollisions = false; }
            foreach (var sourceAudio in _shell.GetComponentsInChildren<AudioSource>(true)) { sourceAudio.Stop(); sourceAudio.enabled = false; }
            foreach (var camera in _shell.GetComponentsInChildren<Camera>(true)) camera.enabled = false;
            foreach (var listener in _shell.GetComponentsInChildren<AudioListener>(true)) listener.enabled = false;
            foreach (var particle in _shell.GetComponentsInChildren<ParticleSystem>(true)) particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var remap = new Dictionary<Material, Material>();
            foreach (var renderer in _shell.GetComponentsInChildren<Renderer>(true))
            {
                renderer.gameObject.layer=CabinLighting.Layer;
                var originals = renderer.sharedMaterials;
                for (int i = 0; i < originals.Length; i++)
                {
                    if (originals[i] == null) continue;
                    Material copy;
                    if (!remap.TryGetValue(originals[i], out copy))
                    {
                        copy = new Material(originals[i]); remap.Add(originals[i], copy); _materials.Add(copy);
                    }
                    originals[i] = copy;
                }
                renderer.sharedMaterials = originals;
                renderer.forceRenderingOff = false;
                bool unmannedGun = renderer.name.IndexOf("_gun_", StringComparison.OrdinalIgnoreCase) >= 0
                    || renderer.name.IndexOf("bullets", StringComparison.OrdinalIgnoreCase) >= 0;
                renderer.enabled = !(renderer is ParticleSystemRenderer) && !unmannedGun && !renderer.name.Contains("LOD1") && !renderer.name.Contains("LOD2");
            }
            // The installed Icebreaker shader resolver knows EFT shader aliases. Scope it to our own
            // cloned materials; never enable CutsceneHold or alter map materials/culling globally.
            var rebinder = AccessTools.Method(AccessTools.TypeByName("Manimal.Icebreaker.RenderEnvProbe"), "RebindShadersUnder");
            if (rebinder == null) { Error = "Icebreaker scoped material rebinder is unavailable."; yield break; }
            rebinder.Invoke(null, new object[] { _shell.transform });
            CabinLighting.Install(cutsceneRoot.transform,source,_shell.transform);
            _mainRotor = Find("helicopter_top_rotor");
            _tailRotor = Find("helicopter_tail_rotor");
            _leftDoor = Find("helicopter_door_left");
            _rightDoor = Find("helicopter_door_right");
            if (!Ready || _leftDoor == null || _rightDoor == null)
            { Error = "Icebreaker airframe doors or rotor pivots are missing."; yield break; }
            // If an Icebreaker raid already had the source scene loaded, do not inherit its sampled
            // door pose. These audited source pivots are OPEN, not the closed-door pose.
            var modelRoot = Find("helicopter_root");
            modelRoot.localPosition = Vector3.zero; modelRoot.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            _leftDoor.localPosition = new Vector3(1.4060862f, 0.4935427f, 1.6394076f);
            _rightDoor.localPosition = new Vector3(-1.4060899f, 0.4935427f, 1.6394076f);
            _leftDoor.localRotation=_rightDoor.localRotation=Quaternion.identity;
            _mainRest = _mainRotor.localRotation; _tailRest = _tailRotor.localRotation;
            _leftRest = _leftDoor.localPosition; _rightRest = _rightDoor.localPosition;
            var mountLeft = Find("helicopter_rope_dangle_left");
            var mountRight = Find("helicopter_rope_dangle_right");
            var mountRenderer = mountLeft != null ? mountLeft.GetComponentInChildren<MeshRenderer>(true) : null;
            if (mountRenderer == null || mountRight == null)
            { Error = "Original Icebreaker rope attachment meshes are missing."; yield break; }
            RopeMaterial = mountRenderer.sharedMaterial;
            var straight = cutsceneRoot.transform.Find("HelipadCenter/HelipadProps/rope_straight_L");
            var sourceRope = straight != null ? straight.GetComponentInChildren<SkinnedMeshRenderer>(true) : null;
            if (sourceRope == null || sourceRope.sharedMesh == null)
            { Error = "Original Icebreaker rope_straight_L surface is missing; not substituting a generated cable."; yield break; }
            // Source imported meshes may disallow CPU reads; BakeMesh yields a readable private
            // copy while retaining the original surface/UVs, without activating its scene animation.
            OriginalRopeMesh = new Mesh { name = "UH60_OriginalIcebreakerRopeTemplate" };
            sourceRope.BakeMesh(OriginalRopeMesh);
            if (OriginalRopeMesh.vertexCount < 1000)
            { Error = "Original rope bake returned incomplete geometry."; yield break; }
            // Attachment meshes are already part of the cloned aircraft. Their lower boundary is
            // the continuation socket, not the dangle object's modelling pivot near the rotor.
            LeftRopeAnchor = FindAttachmentEnd(mountRenderer);
            var rightRenderer = mountRight.GetComponentInChildren<MeshRenderer>(true);
            RightRopeAnchor = FindAttachmentEnd(rightRenderer);
            var sourceSpiral=cutsceneRoot.transform.Find("Helicopter_01/rope_spiral_new_export")?.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if(sourceSpiral==null)throw new InvalidOperationException("Original packed cable/carabiner mesh missing.");
            var spiralBake=new Mesh();
            try
            {
                sourceSpiral.BakeMesh(spiralBake);
                _connectorMesh=NativeRopeConnector.Extract(spiralBake,out Vector3 cableSocket,out Vector3 authoredEye);
                _leftConnectorMesh=NativeRopeConnector.OppositeDoor(_connectorMesh);
                Vector3 leftEye=NativeRopeConnector.EyeForMount(authoredEye,mountRenderer.GetComponent<MeshFilter>().sharedMesh.bounds,true);
                Vector3 rightEye=NativeRopeConnector.EyeForMount(authoredEye,rightRenderer.GetComponent<MeshFilter>().sharedMesh.bounds,false);
                _leftFixedMount=PointInFlight(mountRenderer.transform.TransformPoint(leftEye));
                _rightFixedMount=PointInFlight(rightRenderer.transform.TransformPoint(rightEye));
                LeftRopeAnchor=_leftFixedMount+NativeRopeConnector.OppositeSocket(cableSocket);
                RightRopeAnchor=_rightFixedMount+Quaternion.Euler(0,180,0)*cableSocket;
                Plugin.Log.LogInfo("Native connector contact fitted in source modelling frame (not staging/AABB offsets): L="
                    +_leftFixedMount+" R="+_rightFixedMount+"; cable sockets="+LeftRopeAnchor+" / "+RightRopeAnchor);
            }
            finally{UnityEngine.Object.Destroy(spiralBake);}
            Plugin.Log.LogInfo("Prepared Icebreaker aircraft geometry only: " + _shell.GetComponentsInChildren<Renderer>(true).Length
                + " renderers, " + _materials.Count + " private materials; no cutscene actors or Timeline cloned.");
            Plugin.Log.LogInfo("Original rope material=" + (RopeMaterial != null ? RopeMaterial.name : "missing")
                + "; mount endpoints=" + LeftRopeAnchor + " / " + RightRopeAnchor);
            if (_ownsScene)
            {
                // Keep only the aircraft's referenced meshes/materials/textures resident after cloning.
                var unload = SceneManager.UnloadSceneAsync(_sourceScene);
                if (unload != null) yield return unload;
                _ownsScene = false;
            }
            _sourceEnvironment?.Dispose(); _sourceEnvironment = null;
        }

        private Vector3 FindAttachmentEnd(MeshRenderer renderer)
        {
            var mesh = renderer != null ? renderer.GetComponent<MeshFilter>()?.sharedMesh : null;
            if (mesh == null) throw new InvalidOperationException("Original attachment mesh is absent.");
            // Bounds transform is valid even when the source mesh disallows CPU vertex reads.
            Bounds bounds = mesh.bounds;
            Vector3 lowest = Vector3.zero;
            float y = float.PositiveInfinity;
            for (int i = 0; i < 8; i++)
            {
                var point = bounds.center + Vector3.Scale(bounds.extents, new Vector3((i & 1) == 0 ? -1 : 1,
                    (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                point = _shell.transform.InverseTransformPoint(renderer.transform.TransformPoint(point));
                // Convert from the assembled object's Y-rotated local space into the flight frame.
                point = Quaternion.Euler(0f, 180f, 0f) * point + new Vector3(0f, -0.90f, -1.55f);
                if (point.y < y) { y = point.y; lowest = point; }
            }
            var centre = Quaternion.Euler(0f, 180f, 0f)
                * _shell.transform.InverseTransformPoint(renderer.transform.TransformPoint(bounds.center))
                + new Vector3(0f, -0.90f, -1.55f);
            return new Vector3(centre.x, lowest.y, centre.z);
        }

        private Vector3 PointInFlight(Vector3 world)
        {
            return Quaternion.Euler(0,180,0)*_shell.transform.InverseTransformPoint(world)+new Vector3(0,-.90f,-1.55f);
        }

        internal void Attach(Transform flightFrame)
        {
            _shell.transform.SetParent(flightFrame, false);
            _shell.transform.localPosition = new Vector3(0f, -0.90f, -1.55f);
            // One -90 X conversion is already on helicopter_root. Do not copy the second conversion
            // on the cutscene staging object: doing so was the historic vertical/upside-down defect.
            _shell.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            _shell.SetActive(true);
            for(int side=0;side<2;side++)
            {
                var obj=new GameObject("UH60_NativeOvalCarabiner");obj.transform.SetParent(flightFrame,false);
                obj.transform.localPosition=side==0?_leftFixedMount:_rightFixedMount;
                obj.transform.localRotation=side==0?Quaternion.identity:Quaternion.Euler(0,180,0);
                obj.layer=CabinLighting.Layer;obj.AddComponent<MeshFilter>().sharedMesh=side==0?_leftConnectorMesh:_connectorMesh;
                var renderer=obj.AddComponent<MeshRenderer>();renderer.sharedMaterial=RopeMaterial;_connectors.Add(obj);
            }
            _doors.Snap(1f);ApplyDoors();
        }

        internal Transform Frame { get { return _shell != null ? _shell.transform.parent : null; } }

        internal void Tick(float dt)
        {
            if (!Ready) return;
            _doors.Tick(dt);ApplyDoors();
            _angle = Mathf.Repeat(_angle + dt * 1620f, 360f);
            _mainRotor.localRotation = _mainRest * Quaternion.AngleAxis(_angle, Vector3.forward);
            _tailRotor.localRotation = _tailRest * Quaternion.AngleAxis(_angle * 4.1f, Vector3.right);
        }

        internal void SetDoors(float open)
        {
            if (_leftDoor == null || _rightDoor == null) return;
            _doors.SetTarget(open);
        }
        private void ApplyDoors()
        {
            if(_leftDoor==null||_rightDoor==null)return;
            _leftDoor.localPosition=AircraftDoorMotion.Position(_leftRest,1,_doors.OpenAmount);
            _rightDoor.localPosition=AircraftDoorMotion.Position(_rightRest,-1,_doors.OpenAmount);
        }

        private Transform Find(string name) { return _shell.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name); }

        public void Dispose()
        {
            SeatedMotion?.Dispose(); GuardMotion?.Dispose();
            _sourceEnvironment?.Dispose(); _sourceEnvironment = null;
            if (_shell != null) UnityEngine.Object.Destroy(_shell);
            foreach(var obj in _connectors)if(obj!=null)UnityEngine.Object.Destroy(obj);_connectors.Clear();
            if(_connectorMesh!=null)UnityEngine.Object.Destroy(_connectorMesh);
            if(_leftConnectorMesh!=null)UnityEngine.Object.Destroy(_leftConnectorMesh);
            foreach (var material in _materials) if (material != null) UnityEngine.Object.Destroy(material);
            if (OriginalRopeMesh != null) UnityEngine.Object.Destroy(OriginalRopeMesh);
            _materials.Clear();
            if (_ownsScene && _sourceScene.IsValid() && _sourceScene.isLoaded) SceneManager.UnloadSceneAsync(_sourceScene);
            if (_ownedBundle != null) _ownedBundle.Unload(false);
            _ownedBundle = null;
            _ownsScene = false;
        }
    }
}
