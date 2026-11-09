using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using UnityEngine;

namespace UH60FastRopeInsertion
{
    internal sealed partial class Uh60Visual : IDisposable
    {
        private const string PrefabAssetName =
            "assets/content/firesupport/uh-60 black hawk/sikorsky uh-60 black hawk.prefab";

        private static readonly int FlySpeedMultiplier = Animator.StringToHash("FlySpeedMultiplier");
        private static readonly int RotorSpeedMultiplier = Animator.StringToHash("RotorSpeedMultiplier");
        private static readonly int OpenDoor = Animator.StringToHash("OpenDoor");
        private static readonly int FlyAway = Animator.StringToHash("FlyAway");

        private AssetBundle _bundle;
        private InstalledFlightAsset _flightAsset;
        private GameObject _root;
        private Animator _animator;
        private TacticalFlightEventRelay _flightEvents;
        private Transform _motion;
        private Transform _body;
        private Transform _leftDoor;
        private Transform _rightDoor;
        private Transform _mainRotor;
        private Transform _tailRotor;
        private Vector3 _leftDoorClosed;
        private Vector3 _rightDoorClosed;
        private Quaternion _mainRotorBase;
        private Quaternion _tailRotorBase;
        private float _rotorAngle;
        private bool _authoredFlightActive;
        private bool _audioStarted;
        private Vector3 _placementPosition;
        private IcebreakerAirframe _airframe;
        internal float PackedRopeLength { get; set; } = 17.5f;
        private readonly List<AudioSource> _audioSources = new List<AudioSource>();
        private readonly Dictionary<AudioSource, float> _baseVolumes = new Dictionary<AudioSource, float>();

        // Tactical Services places the prefab root at the requested landing point, while
        // its controller animates b_vhc_main through the complete approach/hover/departure.
        // Every passenger, camera and rope anchor must follow that animated frame.
        internal Transform RootTransform
        {
            get { return _motion != null ? _motion : (_root != null ? _root.transform : null); }
        }

        internal Transform BodyTransform
        {
            get { return _body != null ? _body : RootTransform; }
        }

        internal bool Ready
        {
            get
            {
                return _root != null && _animator != null && _motion != null && _body != null
                    && _leftDoor != null && _rightDoor != null;
            }
        }

        internal bool HasReachedTacticalHover
        {
            get
            {
                if (!Ready || !_authoredFlightActive)
                {
                    return false;
                }
                if (_flightEvents != null && _flightEvents.Arrived)
                {
                    return true;
                }

                // Geometry is a second guard in case Unity suppresses the imported
                // AnimationEvent. The authored clip finishes at local (0, 13, 0).
                Vector3 local = _root.transform.InverseTransformPoint(_motion.position);
                return new Vector2(local.x, local.z).sqrMagnitude < 0.64f
                    && Mathf.Abs(local.y - 13f) < 0.85f;
            }
        }

        internal static bool TryCreate(out Uh60Visual result, out string error)
        {
            result = new Uh60Visual();
            if (result.Load(out error))
            {
                return true;
            }
            result.Dispose();
            result = null;
            return false;
        }

        private bool Load(out string error)
        {
            error = string.Empty;
            try
            {
                _flightAsset = InstalledFlightAsset.Open(BepInEx.Paths.PluginPath, PrefabAssetName);
                _bundle = _flightAsset.Bundle;
                if (_bundle == null)
                {
                    error = "Unity could not load the UH-60 asset bundle.";
                    return false;
                }

                GameObject prefab = _bundle.LoadAsset<GameObject>(PrefabAssetName);
                if (prefab == null)
                {
                    prefab = _bundle.LoadAllAssets<GameObject>().FirstOrDefault();
                }
                if (prefab == null)
                {
                    error = "The UH-60 prefab is absent from the asset bundle.";
                    return false;
                }

                // Keep the clone inactive until the Tactical Services runtime scripts have
                // been removed. This preserves the visual Animator without dispatching a
                // second support request or subscribing the original service component.
                bool prefabWasActive = prefab.activeSelf;
                prefab.SetActive(false);
                _root = UnityEngine.Object.Instantiate(prefab);
                prefab.SetActive(prefabWasActive);
                _root.name = "UH60FastRopeInsertion_Aircraft";
                UnityEngine.Object.DontDestroyOnLoad(_root);
                _root.SetActive(false);

                foreach (MonoBehaviour behaviour in _root.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (behaviour != null)
                    {
                        UnityEngine.Object.DestroyImmediate(behaviour);
                    }
                }

                _animator = _root.GetComponentInChildren<Animator>(true);
                _motion = FindDeep(_root.transform, "b_vhc_main");
                _body = FindDeep(_root.transform, "b_vhc_body") ?? _motion;
                _leftDoor = FindDeep(_root.transform, "b_vhc_door_l");
                _rightDoor = FindDeep(_root.transform, "b_vhc_door_r");
                _mainRotor = FindDeep(_root.transform, "b_vhc_rotor");
                _tailRotor = FindDeep(_root.transform, "b_vhc_rotor_tail");
                if (_animator == null || _motion == null || _leftDoor == null || _rightDoor == null
                    || _mainRotor == null || _tailRotor == null)
                {
                    error = "UH-60 hierarchy is incomplete (Animator, motion frame, doors or rotors are missing).";
                    return false;
                }

                _animator.enabled = true;
                _animator.updateMode = AnimatorUpdateMode.UnscaledTime;
                _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                _flightEvents = _animator.gameObject.AddComponent<TacticalFlightEventRelay>();

                foreach (Collider collider in _root.GetComponentsInChildren<Collider>(true))
                {
                    collider.enabled = false;
                }
                foreach (Rigidbody rigidbody in _root.GetComponentsInChildren<Rigidbody>(true))
                {
                    rigidbody.isKinematic = true;
                    rigidbody.detectCollisions = false;
                }

                _leftDoorClosed = _leftDoor.localPosition;
                _rightDoorClosed = _rightDoor.localPosition;
                _mainRotorBase = _mainRotor.localRotation;
                _tailRotorBase = _tailRotor.localRotation;

                foreach (AudioSource source in _root.GetComponentsInChildren<AudioSource>(true))
                {
                    if (source == null || source.clip == null)
                    {
                        continue;
                    }
                    source.playOnAwake = false;
                    source.loop = source.clip.name.IndexOf("door", StringComparison.OrdinalIgnoreCase) < 0;
                    source.spatialBlend = 1f;
                    source.dopplerLevel = 0.45f;
                    source.rolloffMode = AudioRolloffMode.Logarithmic;
                    source.minDistance = source.clip.name.IndexOf("distant", StringComparison.OrdinalIgnoreCase) >= 0 ? 42f : 8f;
                    source.maxDistance = 650f;
                    _audioSources.Add(source);
                    _baseVolumes[source] = Mathf.Max(0.05f, source.volume);
                }

                Plugin.Log.LogInfo("Loaded Tactical Services UH-60 visual/controller with "
                    + _root.GetComponentsInChildren<Renderer>(true).Length + " renderers, "
                    + _animator.layerCount + " Animator layers and " + _audioSources.Count + " audio sources.");
                return true;
            }
            catch (Exception ex)
            {
                error = ex.ToString();
                return false;
            }
        }

        internal void PreviewTacticalHover(Vector3 landingPoint, Quaternion routeRotation)
        {
            if (!Ready) throw new InvalidOperationException("Cannot validate hover without the actual aircraft.");
            _root.transform.SetPositionAndRotation(landingPoint, routeRotation);
            _root.SetActive(true);
            _animator.enabled = true;
            _animator.Rebind();
            _animator.Play("Movement.Flying", 0, 0.99999f);
            _animator.Update(0f);
            _animator.enabled = false;
            Vector3 local = _root.transform.InverseTransformPoint(_motion.position);
            if (Mathf.Abs(local.y - 13f) > 1f || new Vector2(local.x, local.z).sqrMagnitude > 4f)
                throw new InvalidOperationException("Authored hover preview was not at (0,13,0): " + local
                    + "; refusing door validation against the wrong animation frame.");
            // No audio/dispatch. BeginTacticalFlight rebinds and resets the event relay afterwards.
        }

        internal void BeginTacticalFlight(Vector3 landingPoint, Quaternion routeRotation, float speedMultiplier)
        {
            if (!Ready)
            {
                return;
            }

            _placementPosition = landingPoint;
            _root.transform.SetPositionAndRotation(landingPoint, routeRotation);
            _root.SetActive(true);
            _animator.enabled = true;
            _animator.updateMode = AnimatorUpdateMode.UnscaledTime;
            _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            _animator.Rebind();
            _animator.Update(0f);
            _flightEvents.ResetState();

            SetFloatIfPresent(FlySpeedMultiplier, Mathf.Clamp(speedMultiplier, 0.6f, 1.6f));
            SetFloatIfPresent(RotorSpeedMultiplier, 1f);
            if (HasParameter(OpenDoor, AnimatorControllerParameterType.Bool))
            {
                _animator.SetBool(OpenDoor, true);
            }
            if (HasParameter(FlyAway, AnimatorControllerParameterType.Trigger))
            {
                _animator.ResetTrigger(FlyAway);
            }

            _animator.Play("Movement.Flying", 0, 0f);
            _animator.Update(0f);
            _authoredFlightActive = true;
            SetDoorOpen(true);
            StartAircraftAudio();

            Plugin.Log.LogInfo("Tactical Services flight started at landing root " + landingPoint
                + "; animated visual starts at " + _motion.position + "; speed="
                + Mathf.Clamp(speedMultiplier, 0.6f, 1.6f).ToString("F2") + ".");
        }

        internal void BeginTacticalDeparture()
        {
            if (!Ready || !_authoredFlightActive)
            {
                return;
            }
            SetDoorOpen(false);
            if (HasParameter(FlyAway, AnimatorControllerParameterType.Trigger))
            {
                _animator.SetTrigger(FlyAway);
            }
            Plugin.Log.LogInfo("Tactical Services FlyAway trigger sent; current visual position " + _motion.position + ".");
        }

        internal bool HasClearedLanding(float distance)
        {
            return RootTransform != null
                && Vector3.Distance(RootTransform.position, _placementPosition) >= Mathf.Max(1f, distance);
        }

        internal void SetPose(Vector3 position, Quaternion rotation)
        {
            if (_root != null && !_authoredFlightActive)
            {
                _root.transform.SetPositionAndRotation(position, rotation);
            }
        }

        internal void TickRotors(float deltaTime, float speed01)
        {
            if (!Ready)
            {
                return;
            }
            if (_airframe != null) { _airframe.Tick(deltaTime); return; }
            _rotorAngle = Mathf.Repeat(_rotorAngle + Mathf.Max(0f, deltaTime) * Mathf.Lerp(720f, 1260f, speed01), 360f);
            _mainRotor.localRotation = _mainRotorBase * Quaternion.AngleAxis(_rotorAngle, Vector3.forward);
            _tailRotor.localRotation = _tailRotorBase * Quaternion.AngleAxis(_rotorAngle * 1.91f, Vector3.right);
            foreach (var renderer in _mainRotor.GetComponentsInChildren<Renderer>(true))
                if (renderer.name.Contains("Blades_LOD0")) renderer.enabled = true;
            foreach (var renderer in _tailRotor.GetComponentsInChildren<Renderer>(true))
                if (renderer.name.Contains("Blades_LOD0")) renderer.enabled = true;
        }

        internal bool UsesIcebreaker { get { return _airframe != null; } }
        internal float CabinFloor { get { return UsesIcebreaker ? .07906f : -0.42f; } }

        internal void AttachAirframe(IcebreakerAirframe airframe)
        {
            _airframe = airframe;
            if (airframe == null) return;
            foreach (var renderer in _root.GetComponentsInChildren<Renderer>(true)) renderer.forceRenderingOff = true;
            airframe.Attach(RootTransform);
        }

        internal void SetDoorProgress(float progress)
        {
            // The flight team already occupies the open Icebreaker doorways. Do not briefly
            // close those doors in the first half of the old hover "opening" callback.
            if(_airframe!=null){_airframe.SetDoors(1f);return;}
            if (_authoredFlightActive)
            {
                SetDoorOpen(progress >= 0.5f);
                return;
            }
            SetDoorTransforms(progress);
        }

        internal void SetDoorOpen(bool open)
        {
            if (_airframe != null) _airframe.SetDoors(open ? 1f : 0f);
            if (_leftDoor == null || _rightDoor == null)
            {
                return;
            }
            if (_animator != null && HasParameter(OpenDoor, AnimatorControllerParameterType.Bool))
            {
                _animator.SetBool(OpenDoor, open);
            }

            // Tactical Services' authored clip drives the left leaf. Its right leaf has no
            // translation curve, so mirror only that leaf to keep both fast-rope lanes open.
            Vector3 right = _rightDoorClosed;
            right.x += (open ? 1f : 0f) * 0.055f;
            right.y = Mathf.Lerp(_rightDoorClosed.y, -0.112f, open ? 1f : 0f);
            _rightDoor.localPosition = right;
        }

        private void SetDoorTransforms(float progress)
        {
            if (_leftDoor == null || _rightDoor == null)
            {
                return;
            }
            progress = Mathf.Clamp01(progress);
            float unlock = SmoothStep01(Mathf.Clamp01(progress / 0.22f));
            float slide = SmoothStep01(Mathf.Clamp01((progress - 0.16f) / 0.84f));
            const float outboard = 0.055f;
            const float openLocalY = -0.112f;
            _leftDoor.localPosition = new Vector3(
                _leftDoorClosed.x - outboard * unlock,
                Mathf.Lerp(_leftDoorClosed.y, openLocalY, slide),
                _leftDoorClosed.z);
            _rightDoor.localPosition = new Vector3(
                _rightDoorClosed.x + outboard * unlock,
                Mathf.Lerp(_rightDoorClosed.y, openLocalY, slide),
                _rightDoorClosed.z);
        }

        internal Vector3 GetDoorAnchor(int side)
        {
            if (UsesIcebreaker)
                return RootTransform.TransformPoint(side < 0 ? _airframe.LeftRopeAnchor : _airframe.RightRopeAnchor);
            Transform door = side < 0 ? _leftDoor : _rightDoor;
            if (door == null)
            {
                return RootTransform != null ? RootTransform.position : Vector3.zero;
            }
            Vector3 outward = side < 0 ? -RootTransform.right : RootTransform.right;
            Vector3 closed = side < 0 ? _leftDoorClosed : _rightDoorClosed;
            Vector3 doorway = door.parent != null ? door.parent.TransformPoint(closed) : door.position;
            return doorway + outward * 0.18f + RootTransform.up * 1.20f + RootTransform.forward * 0.04f;
        }

        internal Vector3 GetCabinSeat(int side, int slot)
        {
            float x = side < 0 ? -0.75f : 0.75f;
            float z = UsesIcebreaker ? 1.6f + Mathf.Clamp(slot, 0, 2) * 0.70f : 1.04f - Mathf.Clamp(slot, 0, 2) * 0.78f;
            return RootTransform.TransformPoint(new Vector3(x, CabinFloor + 0.46f, z));
        }

        internal Quaternion GetCabinSeatRotation(int side)
        {
            Vector3 inward = side < 0 ? RootTransform.right : -RootTransform.right;
            return Quaternion.LookRotation(inward, RootTransform.up);
        }

        internal Vector3 GetDoorSillSeat(int side)
        {
            Vector3 stand = GetDoorStandPoint(side);
            Vector3 outward = side < 0 ? -RootTransform.right : RootTransform.right;
            return stand + outward * (UsesIcebreaker ? -0.11f : 0.22f) + RootTransform.up * 0.14f
                + (side<0?RootTransform.forward*.16f:Vector3.zero);
        }

        internal Quaternion GetDoorSillRotation(int side)
        {
            Vector3 outward = side < 0 ? -RootTransform.right : RootTransform.right;
            return Quaternion.LookRotation(outward, RootTransform.up);
        }

        internal Vector3 GetDoorGuardPoint(int side)
        {
            Vector3 stand = GetDoorStandPoint(side);
            Vector3 inward = side < 0 ? RootTransform.right : -RootTransform.right;
            return stand + inward * 0.70f + RootTransform.forward * (side<0?.10f:.40f);
        }

        internal Vector3 GetWaitingFloor(int index)
        {
            // The two central seats face the windows, not the aisle. Avoid inventing unsupported
            // seating positions: the remaining three passengers crouch-brace on the real floor.
            Vector3 local = index == 2 ? new Vector3(0.98f, CabinFloor, 0.68f)
                : index == 3 ? new Vector3(-0.65f, CabinFloor, 2.45f)
                : new Vector3(0.60f, CabinFloor, 2.30f);
            return RootTransform.TransformPoint(local);
        }

        internal Quaternion GetWaitingRotation(int index)
        {
            Vector3 forward = index == 2 ? RootTransform.right + RootTransform.forward * 0.14f
                : index == 3 ? -RootTransform.right * 0.65f - RootTransform.forward * 0.76f
                : RootTransform.right * 0.35f - RootTransform.forward;
            return Quaternion.LookRotation(forward, RootTransform.up);
        }

        internal Vector3 GetSillRightBrace(int side)
        {
            Vector3 sill = GetDoorSillSeat(side);
            // Nearest jamb surface measured on the real source mesh (private BVH audit), on the
            // operator's anatomical right. The old guessed point was in the open doorway.
            return RootTransform.TransformPoint(new Vector3(side < 0 ? -1.2322f : 1.2322f,
                UsesIcebreaker?.5708f:CabinFloor + 0.4708f, RootTransform.InverseTransformPoint(sill).z + 0.6240f));
        }

        internal Quaternion GetDoorGuardRotation(int side)
        {
            Vector3 outward = side < 0 ? -RootTransform.right : RootTransform.right;
            Vector3 look = (outward + RootTransform.forward * 0.24f).normalized;
            return Quaternion.LookRotation(look, RootTransform.up);
        }

        internal Vector3 GetCabinRopeBundlePoint(int side)
        {
            Vector3 sill = GetDoorSillSeat(side);
            Vector3 inward = side < 0 ? RootTransform.right : -RootTransform.right;
            // User-requested layout: operator slightly to his right; fixed hook/coil
            // on his LEFT and ahead of the hips. Keep pickup outside the torso volume.
            // Original interior floor triangles are at flight-local Y=.07906,
            // not at the former .10 staging plane. Whole coil gets 3 mm clearance.
            return sill + inward * .28f - RootTransform.up * (UsesIcebreaker?.11306f:.134f)
                + RootTransform.forward * (side<0?-.50f:-.34f);
        }

        internal Vector3 GetDoorStandPoint(int side)
        {
            if (UsesIcebreaker)
            {
                Vector3 mount = side < 0 ? _airframe.LeftRopeAnchor : _airframe.RightRopeAnchor;
                return RootTransform.TransformPoint(new Vector3(side < 0 ? -1.35f : 1.35f, CabinFloor, mount.z));
            }
            Vector3 anchor = GetDoorAnchor(side);
            Vector3 inward = side < 0 ? RootTransform.right : -RootTransform.right;
            return anchor + inward * 0.58f - RootTransform.up * 1.62f;
        }

        internal Quaternion GetRopeFacing(int side)
        {
            Vector3 outward = side < 0 ? -RootTransform.right : RootTransform.right;
            return Quaternion.LookRotation(outward, RootTransform.up);
        }

        internal void PlayDoorSound()
        {
            AudioSource source = _audioSources.FirstOrDefault(item =>
                item != null && item.clip != null && item.clip.name.IndexOf("door", StringComparison.OrdinalIgnoreCase) >= 0);
            if (source != null)
            {
                source.loop = false;
                source.Play();
            }
        }

        internal void SetAudioVolume(float volume01)
        {
            foreach (AudioSource source in _audioSources)
            {
                if (source == null || source.clip == null)
                {
                    continue;
                }
                float baseVolume;
                if (!_baseVolumes.TryGetValue(source, out baseVolume))
                {
                    baseVolume = 1f;
                }
                source.volume = Mathf.Clamp01(baseVolume * volume01);
            }
        }

        private void StartAircraftAudio()
        {
            if (_audioStarted)
            {
                return;
            }
            _audioStarted = true;
            foreach (AudioSource source in _audioSources)
            {
                if (source != null && source.clip != null
                    && source.clip.name.IndexOf("door", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    source.loop = true;
                    source.Play();
                }
            }
        }

        private void SetFloatIfPresent(int hash, float value)
        {
            if (HasParameter(hash, AnimatorControllerParameterType.Float))
            {
                _animator.SetFloat(hash, value);
            }
        }

        private bool HasParameter(int hash, AnimatorControllerParameterType type)
        {
            if (_animator == null)
            {
                return false;
            }
            foreach (AnimatorControllerParameter parameter in _animator.parameters)
            {
                if (parameter.nameHash == hash && parameter.type == type)
                {
                    return true;
                }
            }
            return false;
        }

        public void Dispose()
        {
            foreach (AudioSource source in _audioSources)
            {
                if (source != null)
                {
                    source.Stop();
                }
            }
            _audioSources.Clear();
            _baseVolumes.Clear();
            if (_root != null)
            {
                UnityEngine.Object.Destroy(_root);
                _root = null;
            }
            if (_bundle != null)
            {
                _flightAsset?.Dispose();
                _flightAsset = null;
                _bundle = null;
            }
        }

        private static Transform FindDeep(Transform root, string name)
        {
            if (root == null)
            {
                return null;
            }
            if (string.Equals(root.name, name, StringComparison.Ordinal))
            {
                return root;
            }
            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindDeep(root.GetChild(i), name);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }

        private static float SmoothStep01(float value)
        {
            value = Mathf.Clamp01(value);
            return value * value * (3f - 2f * value);
        }
    }
}
