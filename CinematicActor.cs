using System;
using System.Collections.Generic;
using System.Linq;
using EFT;
using UnityEngine;
using UnityEngine.Rendering;

namespace UH60FastRopeInsertion
{
    internal sealed partial class CinematicActor : IDisposable
    {
        private struct RestTransform
        {
            internal Transform Transform;
            internal Vector3 LocalPosition;
            internal Quaternion LocalRotation;
            internal Vector3 LocalScale;
        }

        private readonly GameObject _root;
        private readonly Dictionary<Transform, Transform> _sourceToClone = new Dictionary<Transform, Transform>();
        private readonly List<RestTransform> _rest = new List<RestTransform>();
        private readonly List<Renderer> _renderers = new List<Renderer>();
        private readonly Dictionary<Transform,Quaternion> _bindWorld = new Dictionary<Transform,Quaternion>();
        private RopeGripSolver _leftGrip,_rightGrip;
        private SupportedRopePose _ropePose;
        private SupportedCabinPose _cabinPose;
        private UserCabinPerformance _userPerformance;
        private Transform _head;
        private Transform _pelvis;
        private Quaternion _headViewOffset;
        private float _pelvisHeight = 0.93f;
        private RestTransform[] _blendFrom;
        private Transform _spine;
        private Transform _leftUpperArm;
        private Transform _leftForearm;
        private Transform _leftPalm;
        private Transform _rightUpperArm;
        private Transform _rightForearm;
        private Transform _rightPalm;
        private Transform _leftThigh;
        private Transform _leftCalf;
        private Transform _leftFoot;
        private Transform _rightThigh;
        private Transform _rightCalf;
        private Transform _rightFoot;
        private Quaternion _headLocalNeutral, _neckLocalNeutral, _leftFootModelRotation, _rightFootModelRotation;
        private Transform _neck;
        private Vector3 _cabinResponseWorld;
        private Vector3 _bodyLag, _bodyLagVelocity;
        private Quaternion _cabinFrame = Quaternion.identity;
        private Vector3 _releaseLeftLocal, _releaseRightLocal;
        private bool _hasReleaseHands;
        private readonly Dictionary<Transform, Quaternion> _palmFrames = new Dictionary<Transform, Quaternion>();
        private NativeCabinMotion _cabinMotion;
        private float _lastCabinSourceTime;
        private bool _nativeSeated;
        internal void SetNativeSource(NativeCabinMotion cabin)
        {
            _cabinMotion = cabin;
            if (cabin == null) return;
            // Test the REAL target clone before hiding any live pawn. A moving source-hand
            // probe alone passed in test9 even while every target torso was lying sideways.
            var up = new List<float>();
            Root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            foreach (float time in new[] { 0f, 6f, 13f })
            {
                ResetPose(); cabin.Sample(time);
                int matched = ApplyNativeRetarget(cabin);
                float vertical = _head != null && _pelvis != null ? (_head.position - _pelvis.position).y : 0f;
                if (matched < 25 || float.IsNaN(vertical) || vertical < .35f)
                    throw new InvalidOperationException("Actual EFT target pose preflight failed for actor " + Index
                        + ": clip=" + cabin.ClipName + "; t=" + time + "; mapped=" + matched + "; headAbovePelvis=" + vertical);
                up.Add(vertical);
            }
            ResetPose();
            Plugin.Log.LogInfo("Target pose preflight actor " + Index + ": headAbovePelvis="
                + string.Join("/", up.Select(v => v.ToString("F3"))) + "; canonical EFT rest; before live-pawn hiding.");
        }

        internal readonly Player SourcePlayer;
        internal readonly int Index;
        internal int LaneSide;
        internal int LaneSlot;
        internal bool Landed;
        internal float DescentDelay;
        internal Vector3 LandingPosition;
        internal Quaternion LandingRotation;

        internal Transform Root
        {
            get { return _root.transform; }
        }

        internal bool HasHead
        {
            get { return _head != null; }
        }

        private CabinClipData _validatedCabinClip;
        internal void ValidateCabinClip(CabinClipData clip)
        {
            if(clip==_validatedCabinClip)return;
            if(clip.Roles.TryGetValue(Index,out var bones))
            {
                int matched=0;
                foreach(var item in _rest)
                    if(bones.TryGetValue(item.Transform.name,out var track))
                    {
                        matched++;
                        if(item.Transform.name=="Root_Joint"||item.Transform.name=="Base HumanPelvis")continue;
                        foreach(var frame in track)
                            if(Vector3.Distance(frame.Position,item.LocalPosition)>.015f)
                                throw new InvalidOperationException("Editor clip changes bone length/frame: "+item.Transform.name);
                    }
                if(matched<25)throw new InvalidOperationException("Cabin clip target mapping incomplete for actor "+Index);
            }
            _validatedCabinClip=clip;
        }
        internal Dictionary<string,CabinBonePose> CaptureCabinPose(Transform aircraft)
        {
            var result=new Dictionary<string,CabinBonePose>(StringComparer.Ordinal);
            result.Add("$actor",new CabinBonePose(aircraft.InverseTransformPoint(Root.position),Quaternion.Inverse(aircraft.rotation)*Root.rotation));
            foreach(var item in _rest)
            {
                var t=item.Transform;
                if(t.name=="Root_Joint")result.Add(t.name,new CabinBonePose(Root.InverseTransformPoint(t.position),Quaternion.Inverse(Root.rotation)*t.rotation));
                else if(t.name.StartsWith("Base Human",StringComparison.Ordinal))result.Add(t.name,new CabinBonePose(t.localPosition,t.localRotation));
            }
            return result;
        }
        internal void ApplyCabinClip(CabinClipData clip,Transform aircraft,float seconds,float weight)
        {
            if(weight<=0||!clip.Roles.TryGetValue(Index,out var bones))return;
            ValidateCabinClip(clip);
            var pose=clip.Sample(bones["$actor"],seconds);
            Root.SetPositionAndRotation(Vector3.Lerp(Root.position,aircraft.TransformPoint(pose.Position),weight),
                Quaternion.Slerp(Root.rotation,aircraft.rotation*pose.Rotation,weight));
            foreach(var item in _rest)
            {
                var t=item.Transform;
                if(!bones.TryGetValue(t.name,out var track))continue;
                pose=clip.Sample(track,seconds);
                if(t.name=="Root_Joint")
                    t.SetPositionAndRotation(Vector3.Lerp(t.position,Root.TransformPoint(pose.Position),weight),
                        Quaternion.Slerp(t.rotation,Root.rotation*pose.Rotation,weight));
                else
                {
                    t.localPosition=Vector3.Lerp(t.localPosition,pose.Position,weight);
                    t.localRotation=Quaternion.Slerp(t.localRotation,pose.Rotation,weight);
                }
            }
        }

        private CinematicActor(Player source, int index)
        {
            SourcePlayer = source;
            Index = index;
            _root = new GameObject("UH60_CinematicActor_" + index + "_" + SafeName(source));
            UnityEngine.Object.DontDestroyOnLoad(_root);
        }

        internal static bool TryCreate(Player source, int index, out CinematicActor actor, out string error)
        {
            actor = null;
            error = string.Empty;
            if (source == null)
            {
                error = "Source player is null.";
                return false;
            }

            CinematicActor candidate = new CinematicActor(source, index);
            try
            {
                candidate.BuildVisualClone();
                actor = candidate;
                Plugin.Log.LogInfo("Created visual-only actor " + index + " for " + SafeName(source)
                    + ": " + candidate._sourceToClone.Count + " transforms, " + candidate._renderers.Count
                    + " renderers, head=" + (candidate._head != null ? candidate._head.name : "missing") + ".");
                return true;
            }
            catch (Exception ex)
            {
                error = ex.ToString();
                candidate.Dispose();
                return false;
            }
        }

        private void BuildVisualClone()
        {
            Transform sourceRoot = SourcePlayer.Transform.Original;
            Renderer[] allRenderers = SourcePlayer.gameObject.GetComponentsInChildren<Renderer>(true);
            ClassifyEquipmentRenderers();
            List<Renderer> selected = allRenderers.Where(IsUsefulVisualRenderer).ToList();
            if (selected.Count == 0)
            {
                throw new InvalidOperationException("The source player has no usable body/equipment renderers.");
            }

            HashSet<Transform> required = new HashSet<Transform>();
            required.Add(sourceRoot);
            foreach (Renderer renderer in selected)
            {
                AddAncestors(renderer.transform, sourceRoot, required);
                SkinnedMeshRenderer skinned = renderer as SkinnedMeshRenderer;
                if (skinned == null)
                {
                    continue;
                }
                if (skinned.rootBone != null) AddAncestors(skinned.rootBone, sourceRoot, required);
                foreach (Transform bone in skinned.bones)
                {
                    if (bone != null) AddAncestors(bone, sourceRoot, required);
                }
            }

            _sourceToClone[sourceRoot] = _root.transform;
            foreach (Transform sourceTransform in required.OrderBy(GetDepth))
            {
                if (sourceTransform == sourceRoot)
                {
                    continue;
                }
                Transform parentClone;
                if (!_sourceToClone.TryGetValue(sourceTransform.parent, out parentClone))
                {
                    continue;
                }
                GameObject cloneObject = new GameObject(sourceTransform.name);
                Transform clone = cloneObject.transform;
                clone.SetParent(parentClone, false);
                clone.localPosition = sourceTransform.localPosition;
                clone.localRotation = sourceTransform.localRotation;
                clone.localScale = sourceTransform.localScale;
                _sourceToClone[sourceTransform] = clone;
            }

            foreach (Renderer sourceRenderer in selected)
            {
                Transform cloneTransform;
                if (!_sourceToClone.TryGetValue(sourceRenderer.transform, out cloneTransform))
                {
                    continue;
                }
                cloneTransform.gameObject.layer=CabinLighting.Layer;

                SkinnedMeshRenderer sourceSkinned = sourceRenderer as SkinnedMeshRenderer;
                if (sourceSkinned != null)
                {
                    SkinnedMeshRenderer cloneSkinned = cloneTransform.gameObject.AddComponent<SkinnedMeshRenderer>();
                    cloneSkinned.sharedMesh = sourceSkinned.sharedMesh;
                    cloneSkinned.sharedMaterials = sourceSkinned.sharedMaterials;
                    cloneSkinned.bones = sourceSkinned.bones.Select(MapTransform).ToArray();
                    cloneSkinned.rootBone = MapTransform(sourceSkinned.rootBone);
                    cloneSkinned.localBounds = sourceSkinned.localBounds;
                    cloneSkinned.quality = sourceSkinned.quality;
                    cloneSkinned.updateWhenOffscreen = true;
                    cloneSkinned.skinnedMotionVectors = false;
                    // Local-player third-person skins may be ShadowsOnly in EFT. The presentation
                    // double must render its body and hands; only head gear is hidden for eye POV.
                    cloneSkinned.shadowCastingMode = ShadowCastingMode.On;
                    cloneSkinned.receiveShadows = sourceSkinned.receiveShadows;
                    cloneSkinned.lightProbeUsage=sourceSkinned.lightProbeUsage;
                    cloneSkinned.reflectionProbeUsage=sourceSkinned.reflectionProbeUsage;
                    cloneSkinned.probeAnchor=MapTransform(sourceSkinned.probeAnchor);
                    cloneSkinned.enabled = true;
                    if (cloneSkinned.sharedMesh != null)
                    {
                        for (int shape = 0; shape < cloneSkinned.sharedMesh.blendShapeCount; shape++)
                        {
                            cloneSkinned.SetBlendShapeWeight(shape, sourceSkinned.GetBlendShapeWeight(shape));
                        }
                    }
                    CopyPropertyBlock(sourceRenderer, cloneSkinned);
                    _renderers.Add(cloneSkinned);
                    RegisterEquipmentRenderer(sourceRenderer, cloneSkinned);
                    continue;
                }

                MeshRenderer sourceMeshRenderer = sourceRenderer as MeshRenderer;
                MeshFilter sourceFilter = sourceRenderer.GetComponent<MeshFilter>();
                if (sourceMeshRenderer != null && sourceFilter != null && sourceFilter.sharedMesh != null)
                {
                    MeshFilter cloneFilter = cloneTransform.gameObject.AddComponent<MeshFilter>();
                    cloneFilter.sharedMesh = sourceFilter.sharedMesh;
                    MeshRenderer cloneRenderer = cloneTransform.gameObject.AddComponent<MeshRenderer>();
                    cloneRenderer.sharedMaterials = sourceMeshRenderer.sharedMaterials;
                    cloneRenderer.shadowCastingMode = ShadowCastingMode.On;
                    cloneRenderer.receiveShadows = sourceMeshRenderer.receiveShadows;
                    cloneRenderer.lightProbeUsage=sourceMeshRenderer.lightProbeUsage;
                    cloneRenderer.reflectionProbeUsage=sourceMeshRenderer.reflectionProbeUsage;
                    cloneRenderer.probeAnchor=MapTransform(sourceMeshRenderer.probeAnchor);
                    cloneRenderer.enabled = sourceMeshRenderer.enabled || _sourceHeadRenderers.Contains(sourceRenderer)
                        || _sourceBackpackRenderers.Contains(sourceRenderer);
                    CopyPropertyBlock(sourceRenderer, cloneRenderer);
                    _renderers.Add(cloneRenderer);
                    RegisterEquipmentRenderer(sourceRenderer, cloneRenderer);
                }
            }

            // The pose captured from a live pawn may be crouched, aiming or prone. Its
            // Root_Joint/container translation and all anatomical locals must not become
            // the cinematic rest pose. Use the same original skeleton Skin.ApplySkin binds to.
            Transform joint = _sourceToClone.Values.FirstOrDefault(t => t.name == "Root_Joint");
            if (joint == null) throw new InvalidOperationException("Actor " + Index + " lacks Root_Joint.");
            int restBones = SkeletonRestPose.Apply(joint, Root);
            if (restBones < 50) throw new InvalidOperationException("Target EFT rest mapping incomplete for actor " + Index + ": " + restBones);
            RestoreEquipmentBinds();

            foreach (Transform clone in _sourceToClone.Values)
            {
                if (clone == _root.transform)
                {
                    continue;
                }
                _rest.Add(new RestTransform
                {
                    Transform = clone,
                    LocalPosition = clone.localPosition,
                    LocalRotation = clone.localRotation,
                    LocalScale = clone.localScale,
                });
            }

            ResolveBones();
            _blendFrom = new RestTransform[_rest.Count];
            if (_pelvis != null) _pelvisHeight = Vector3.Dot(_pelvis.position - Root.position, Root.up);
            if (_pelvisHeight < 0.15f || _pelvisHeight > 1.65f)
                throw new InvalidOperationException("Source actor has a displaced/corrupt body (pelvis height=" + _pelvisHeight
                    + "); refusing to clone or hide this pawn.");
            _rest.Sort((a,b)=>GetDepth(a.Transform).CompareTo(GetDepth(b.Transform)));
            _headLocalNeutral=_head!=null?_head.localRotation:Quaternion.identity;
            _neckLocalNeutral=_neck!=null?_neck.localRotation:Quaternion.identity;
            CachePalmFrame(_leftPalm); CachePalmFrame(_rightPalm);
            _leftGrip=new RopeGripSolver(Root,_leftUpperArm,_leftForearm,_leftPalm,true);
            _rightGrip=new RopeGripSolver(Root,_rightUpperArm,_rightForearm,_rightPalm,false);
            _ropePose=new SupportedRopePose(Root,_pelvis,_spine,_leftThigh,_leftCalf,_leftFoot,
                _rightThigh,_rightCalf,_rightFoot,_leftGrip,_rightGrip);
            _cabinPose=new SupportedCabinPose(Root,_spine,_leftThigh,_leftCalf,_leftFoot,_rightThigh,_rightCalf,_rightFoot,
                _leftUpperArm,_leftForearm,_leftPalm,_rightUpperArm,_rightForearm,_rightPalm);
            foreach(var item in _rest)_bindWorld[item.Transform]=Quaternion.Inverse(Root.rotation)*item.Transform.rotation;
            _userPerformance=new UserCabinPerformance(Root,_rest.Select(r=>r.Transform),Index,_cabinPose,_ropePose,_leftGrip,_rightGrip);
            // Frame comes from the actual EFT rest skeleton, independent of the pawn's stance
            // and of the unrelated import axes used by each piece of clothing.
            _leftFootModelRotation = _leftFoot != null ? Quaternion.Inverse(Root.rotation) * _leftFoot.rotation : Quaternion.identity;
            _rightFootModelRotation = _rightFoot != null ? Quaternion.Inverse(Root.rotation) * _rightFoot.rotation : Quaternion.identity;
            SetHeadLook(0f, 0f);
            if (_head != null) _headViewOffset = Quaternion.Inverse(_head.rotation) * Root.rotation;
            Plugin.Log.LogInfo("Actor " + Index + " rig: canonicalRest=" + restBones + "; pelvisHeight=" + _pelvisHeight.ToString("F3")
                + ", arms=" + (_leftPalm != null && _rightPalm != null) + ", legs="
                + (_leftFoot != null && _rightFoot != null));
            _root.SetActive(false);
        }

        internal void SetVisible(bool visible)
        {
            if (_root != null)
            {
                _root.SetActive(visible);
            }
        }

        internal void SetMainActorFirstPersonPresentation(bool enabled)
        {
            foreach (Renderer renderer in _renderers)
            {
                if (renderer == null)
                {
                    continue;
                }
                bool nearHead = _headRenderers.Contains(renderer);
                if (nearHead)
                {
                    renderer.shadowCastingMode = enabled ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;
                }
            }
        }

        internal void ApplySeatedPose(Vector3 position, Quaternion rotation, float idlePhase)
        {
            ResetPose();
            Root.SetPositionAndRotation(position, rotation);
            PlacePelvis(position);
            Vector3 up = Root.up;
            Vector3 forward = Root.forward;
            Vector3 right = Root.right;
            float breath = Mathf.Sin(idlePhase * Mathf.PI * 2f) * 0.008f;
            if (_spine != null) _spine.rotation = Quaternion.AngleAxis(breath * 18f, Root.right) * _spine.rotation;

            if (_spine != null)
            {
                _spine.rotation = Quaternion.AngleAxis(-7f, Root.right) * _spine.rotation;
            }
            SolveLeg(_leftThigh, _leftCalf, _leftFoot,
                position - up * 0.43f + forward * 0.42f - right * 0.18f, 1f);
            SolveLeg(_rightThigh, _rightCalf, _rightFoot,
                position - up * 0.43f + forward * 0.42f + right * 0.18f, 1f);
            SolveArm(_leftUpperArm, _leftForearm, _leftPalm,
                position + up * 0.12f + forward * 0.31f - right * 0.17f, 1f);
            SolveArm(_rightUpperArm, _rightForearm, _rightPalm,
                position + up * 0.14f + forward * 0.29f + right * 0.16f, 1f);
        }

        internal void ApplyDoorSillPose(Vector3 position, Quaternion rotation,
            float idlePhase, float throwProgress, Vector3? bundleRightGrip = null, Vector3? bundleLeftGrip = null,
            Vector3? rightBrace = null)
        {
            ResetPose();
            throwProgress = Mathf.Clamp01(throwProgress);
            Root.SetPositionAndRotation(position, rotation);
            PlacePelvis(position);
            Vector3 up = Root.up;
            Vector3 forward = Root.forward;
            Vector3 right = Root.right;
            float breath = Mathf.Sin(idlePhase * Mathf.PI * 2f) * 0.009f;
            float reaction = Mathf.Sin(throwProgress * Mathf.PI) * 6.5f;
            Root.rotation *= Quaternion.Euler(-reaction * 0.35f, 0f,
                Mathf.Sin(idlePhase * 1.7f) * 0.45f);

            if (_spine != null)
            {
                _spine.rotation = Quaternion.AngleAxis(-8f - reaction * 0.45f, Root.right)
                    * _spine.rotation;
            }
            ApplyCabinBodyMotion(idlePhase, true);

            // Feet hang outside the open door instead of being solved onto the cabin floor.
            SolveLeg(_leftThigh, _leftCalf, _leftFoot,
                position - up * 0.43f + forward * 0.55f - right * 0.17f, 0.94f);
            SolveLeg(_rightThigh, _rightCalf, _rightFoot,
                position - up * 0.48f + forward * 0.52f + right * 0.17f, 0.94f);

            float lift = SmootherStep01(Mathf.InverseLerp(0.28f, 0.53f, throwProgress));
            float release = SmootherStep01(Mathf.InverseLerp(0.53f, 0.77f, throwProgress));
            Vector3 leftRest = position + up * 0.12f + forward * 0.25f - right * 0.18f;
            Vector3 rightRest = position + up * 0.10f + forward * 0.27f + right * 0.18f;
            Vector3 gathered = position + up * 0.28f + forward * 0.20f;
            Vector3 thrown = position + up * 0.40f + forward * 0.67f;
            Vector3 leftTarget = Vector3.Lerp(leftRest, gathered - right * 0.09f, lift);
            Vector3 rightTarget = Vector3.Lerp(rightRest, gathered + right * 0.09f, lift);
            leftTarget = Vector3.Lerp(leftTarget, thrown - right * 0.11f, release);
            rightTarget = Vector3.Lerp(rightTarget, thrown + right * 0.11f, release);
            if (rightBrace.HasValue)
            {
                float reach = SmootherStep01(Mathf.InverseLerp(0f, 0.06f, throwProgress));
                rightTarget = Vector3.Lerp(rightBrace.Value, rightTarget, reach);
            }
            float tugWeight = SmootherStep01(Mathf.InverseLerp(0f, 0.08f, throwProgress))
                * (1f - SmootherStep01(Mathf.InverseLerp(0.23f, 0.34f, throwProgress)));
            if (tugWeight > 0f)
            {
                float check = Mathf.InverseLerp(0.01f, 0.28f, throwProgress);
                float tug = Mathf.Sin(check * Mathf.PI * 2f) * 0.035f;
                leftTarget = Vector3.Lerp(leftTarget, position + up * (0.48f - tug) + forward * 0.24f - right * 0.025f, tugWeight);
                rightTarget = Vector3.Lerp(rightTarget, position + up * (0.34f - tug) + forward * 0.25f + right * 0.025f, tugWeight);
            }
            if (bundleRightGrip.HasValue && bundleLeftGrip.HasValue && throwProgress >= 0.25f && throwProgress <= 0.74f)
            {
                leftTarget = Vector3.Lerp(leftTarget, bundleLeftGrip.Value - forward * 0.025f,
                    SmootherStep01(Mathf.InverseLerp(0.34f, 0.48f, throwProgress)));
                rightTarget = Vector3.Lerp(rightTarget, bundleRightGrip.Value - forward * 0.025f,
                    SmootherStep01(Mathf.InverseLerp(0.25f, 0.38f, throwProgress)));
            }
            if (throwProgress < 0.01f) _hasReleaseHands = false;
            if (throwProgress > 0.50f && throwProgress <= 0.74f)
            {
                _releaseLeftLocal = Root.InverseTransformPoint(leftTarget);
                _releaseRightLocal = Root.InverseTransformPoint(rightTarget);
                _hasReleaseHands = true;
            }
            if (throwProgress > 0.74f)
            {
                float settle = SmootherStep01((throwProgress - 0.74f) / 0.26f);
                leftTarget = Vector3.Lerp(_hasReleaseHands ? Root.TransformPoint(_releaseLeftLocal) : leftTarget,
                    position + up * 0.45f + forward * 0.32f - right * 0.02f, settle);
                rightTarget = Vector3.Lerp(_hasReleaseHands ? Root.TransformPoint(_releaseRightLocal) : rightTarget,
                    position + up * 0.27f + forward * 0.33f + right * 0.02f, settle);
            }
            SolveArm(_leftUpperArm, _leftForearm, _leftPalm, leftTarget, 1f);
            SolveArm(_rightUpperArm, _rightForearm, _rightPalm, rightTarget, 1f);
            if (rightBrace.HasValue && throwProgress < 0.025f)
                OrientPalm(_rightPalm, up, -forward);
            if (tugWeight > 0f)
            {
                CloseGrip(_leftPalm, leftTarget + forward * 0.045f, tugWeight);
                CloseGrip(_rightPalm, rightTarget + forward * 0.045f, tugWeight);
            }
            float glance = Mathf.Sin(Mathf.InverseLerp(0.20f, 0.76f, throwProgress) * Mathf.PI);
            SetHeadLook(throwProgress > 0f ? glance * 27f : Mathf.Sin(idlePhase * 0.8f) * 6f,
                throwProgress > 0f ? 20f + glance * 12f : 5f);
        }

        internal bool TryGetGripContacts(out Vector3 left, out Vector3 right)
        {
            left = _leftPalm != null ? _leftPalm.position + Root.forward * 0.045f : Vector3.zero;
            right = _rightPalm != null ? _rightPalm.position + Root.forward * 0.045f : Vector3.zero;
            return _leftPalm != null && _rightPalm != null;
        }

        internal void ApplyNativeCabinPose(NativeCabinMotion motion, Vector3 anchor, Quaternion facing,
            float sourceTime, bool seated, float preparation, Vector3? leftContact=null, Vector3? rightContact=null,
            Vector3? attachment=null)
        {
            _cabinMotion=motion;_lastCabinSourceTime=sourceTime;_nativeSeated=seated;
            ResetPose(); motion.Sample(sourceTime);
            Root.SetPositionAndRotation(anchor,facing);
            int matched=ApplyNativeRetarget(motion);
            if(matched<25) throw new InvalidOperationException("Native cabin mapping incomplete for actor "+Index+": "+matched);
            if(seated) PlacePelvis(anchor);
            else if(_leftFoot!=null && _rightFoot!=null)
            {
                float lowest=Mathf.Min(Vector3.Dot(_leftFoot.position-anchor,Root.up),Vector3.Dot(_rightFoot.position-anchor,Root.up));
                Root.position+=Root.up*(SupportedRopePose.BootCentreHeight-lowest);
            }
            Vector3 lf=_leftFoot!=null?_leftFoot.position:anchor, rf=_rightFoot!=null?_rightFoot.position:anchor;
            Quaternion lr=_leftFoot!=null?_leftFoot.rotation:Quaternion.identity, rr=_rightFoot!=null?_rightFoot.rotation:Quaternion.identity;
            ApplyCabinBodyMotion(sourceTime,seated);
            if(!seated)
            {
                SolveLeg(_leftThigh,_leftCalf,_leftFoot,lf,1f);SolveLeg(_rightThigh,_rightCalf,_rightFoot,rf,1f);
                if(_leftFoot!=null)_leftFoot.rotation=lr;if(_rightFoot!=null)_rightFoot.rotation=rr;
                if(Index==1)ApplySupportedCabinHands(anchor,preparation,sourceTime);
            }
            if(preparation>0f && seated && leftContact.HasValue && rightContact.HasValue)
            {
                CabinCue cue=CabinChoreography.Evaluate(preparation);
                if(_pelvis!=null)_pelvis.position+=Root.right*cue.Shift;
                float forwardLean=25f*SmootherStep01(Mathf.InverseLerp(.38f,.64f,preparation))
                    *(1-SmootherStep01(Mathf.InverseLerp(.72f,.86f,preparation)))
                    +12f*SmootherStep01(Mathf.InverseLerp(.10f,.26f,preparation))
                    *(1-SmootherStep01(Mathf.InverseLerp(.30f,.44f,preparation)));
                // Shift weight over the sill before the toss rather than stretching the
                // wrist/upper arm to an unreachable bundle while the hips stay immobile.
                if (_pelvis != null) _pelvis.position += Root.forward * (.10f
                    * SmootherStep01(Mathf.InverseLerp(.35f,.64f,preparation))
                    * (1-SmootherStep01(Mathf.InverseLerp(.72f,.86f,preparation))));
                if(_spine!=null)
                {
                    _spine.rotation=Quaternion.AngleAxis(forwardLean,Root.right)
                        *Quaternion.AngleAxis(-cue.Lean,Root.forward)*_spine.rotation;
                    // The native hold is already turned. Adding a mirrored yaw to that
                    // hidden baseline turned the chest AWAY from the left-hand bundle.
                    // Aim the actual anatomical shoulder frame, not the imported bone axes.
                    Vector3 across=Root.InverseTransformDirection(_rightUpperArm.position-_leftUpperArm.position);
                    float currentYaw=Mathf.Atan2(-across.z,across.x)*Mathf.Rad2Deg;
                    float turnWeight=SmootherStep01(Mathf.InverseLerp(0f,.18f,preparation))
                        *(1-SmootherStep01(Mathf.InverseLerp(.72f,.86f,preparation)));
                    _spine.rotation=Quaternion.AngleAxis(Mathf.DeltaAngle(currentYaw,cue.Yaw)*turnWeight,Root.up)*_spine.rotation;
                }
                // Coil pickup is beside/in front of the body after the torso turns. Hands
                // follow the authored support arc until release, never chase the falling coil.
                if(Mathf.Max(cue.Left,cue.Right)>.001f)
                {
                    Vector3 coilAxis=CabinRopeBundle.CoilAxis(_cabinFrame,Mathf.Min(preparation,CabinChoreography.Release));
                    _leftGrip.Fit(leftContact.Value,coilAxis,cue.Left);
                    _rightGrip.Fit(rightContact.Value,coilAxis,cue.Right);
                }
                LookTowards((leftContact.Value+rightContact.Value)*.5f,Mathf.Max(cue.Left,cue.Right)*.35f);
            }
        }

        private int ApplyNativeRetarget(NativeCabinMotion motion)
        {
            int matched=0;Quaternion heading=motion.HeadingCorrection;
            // Parent-first WORLD bind deltas avoid reapplying imported local bases and missing
            // twist parents. A matching name alone was not sufficient in the old transfer.
            foreach(var item in _rest)
                if(motion.TryWorldPose(item.Transform.name,out Vector3 unused,out Quaternion q,out Quaternion bind)
                    && _bindWorld.TryGetValue(item.Transform,out Quaternion targetBind))
                {
                    // Animated scene placement and neutral-model heading are different frames.
                    // Conjugating with the placement heading on BOTH sides left the torso turned
                    // into the cabin and forced a throw behind its own shoulder.
                    Quaternion delta=heading*q*Quaternion.Inverse(bind)*Quaternion.Inverse(motion.BindHeadingCorrection);
                    item.Transform.rotation=Root.rotation*delta*targetBind;matched++;
                }
            return matched;
        }

        internal void ApplyDoorGuardPose(Vector3 position, Quaternion rotation,
            float idlePhase, float reaction)
        {
            ResetPose();
            reaction = Mathf.Clamp01(reaction);
            Root.SetPositionAndRotation(position, rotation);
            Vector3 up = Root.up;
            Vector3 forward = Root.forward;
            Vector3 right = Root.right;
            float sway = Mathf.Sin(idlePhase * 1.43f + 0.7f) * 0.65f;
            float bob = Mathf.Sin(idlePhase * 0.91f) * 0.008f;
            Root.position -= up * 0.28f; // Cabin guard braces with bent knees beneath the roof.
            Root.rotation *= Quaternion.Euler(0f, reaction * 5.5f, sway);
            if (_spine != null)
            {
                _spine.rotation = Quaternion.AngleAxis(-3f - reaction * 4f, Root.right)
                    * _spine.rotation;
            }
            ApplyCabinBodyMotion(idlePhase, false);

            Vector3 braceHand = position + up * 1.37f - right * 0.22f + forward * 0.04f;
            Vector3 watchHand = position + up * 0.92f + forward * (0.18f + reaction * 0.34f)
                + right * 0.14f;
            SolveArm(_leftUpperArm, _leftForearm, _leftPalm, braceHand, 1f);
            SolveArm(_rightUpperArm, _rightForearm, _rightPalm, watchHand, 1f);
            CloseGrip(_leftPalm, braceHand + up * 0.035f, 0.75f);
            OrientPalm(_leftPalm, right, -up);
            SetHeadLook(Mathf.Sin(idlePhase * 0.65f + Index) * 18f + reaction * 12f, 7f + reaction * 10f);
            SolveLeg(_leftThigh, _leftCalf, _leftFoot, position - right * 0.19f + up * 0.13f, 1f);
            SolveLeg(_rightThigh, _rightCalf, _rightFoot, position + right * 0.19f + up * 0.13f, 1f);
            LevelBoots();
        }

        internal void ApplyWaitingPose(Vector3 floor, Quaternion rotation, float phase, float preparation)
        {
            if(_cabinMotion!=null)
            {
                // One knee and a planted front boot, not an unsupported shared half-squat.
                float nativeTime=4.5f+Index*.85f+Mathf.Sin(phase*.18f+Index)*.35f;
                ApplyNativeCabinPose(_cabinMotion,floor,rotation,nativeTime,false,0f);
                if(Index==3&&_spine!=null)_spine.rotation=Quaternion.AngleAxis(-7f,Root.right)*_spine.rotation;
                float nativeCue=Mathf.Sin(Mathf.Clamp01(preparation)*Mathf.PI);
                LookTowards(floor+Root.forward*3f+Root.up*1.1f+Root.right*(Index==4?-1f:1f),.25f+nativeCue*.2f);
                ApplySupportedCabinHands(floor,preparation,phase);
                return;
            }
            ApplyDoorGuardPose(floor, rotation, phase * 0.21f + Index * 0.77f, 0f);
            Vector3 up = Root.up, right = Root.right, forward = Root.forward;
            // Different support stances, not three mirrored duplicates of the doorway guard.
            float width = Index == 2 ? 0.20f : Index == 3 ? 0.24f : 0.17f;
            float stagger = Index == 3 ? 0.16f : -0.10f;
            SolveLeg(_leftThigh, _leftCalf, _leftFoot, floor - right * width + forward * stagger + up * 0.13f, 1f);
            SolveLeg(_rightThigh, _rightCalf, _rightFoot, floor + right * width - forward * stagger * 0.5f + up * 0.13f, 1f);
            LevelBoots();
            Vector3 brace = floor + up * (Index == 4 ? 1.34f : 1.41f) - right * 0.24f + forward * (Index == 3 ? 0.24f : 0.08f);
            SolveArm(_leftUpperArm, _leftForearm, _leftPalm, brace, 1f);
            OrientPalm(_leftPalm, Index == 3 ? forward : right, -up);
            // Weight stays on the cabin floor. One hand holds the overhead rail, the other gives
            // a brief palm-forward ready/come-up signal, staggered by role rather than random ticks.
            float cue=Mathf.Clamp01((preparation-Index*.045f)/.8f);
            float gesture = Mathf.Sin(cue*Mathf.PI);
            if(_spine!=null)_spine.rotation=Quaternion.AngleAxis(-7f*gesture,Root.right)
                *Quaternion.AngleAxis((Index%2==0?-1:1)*16f*gesture,Root.up)*_spine.rotation;
            Vector3 rest = floor + up * 0.94f + right * 0.15f + forward * 0.20f;
            float beckon = Mathf.Sin(cue*Mathf.PI*5f)*.14f;
            Vector3 signal = floor + up * 1.28f + right * 0.25f + forward * (0.46f + beckon);
            SolveArm(_rightUpperArm, _rightForearm, _rightPalm, Vector3.Lerp(rest, signal, gesture), 1f);
            OrientPalm(_rightPalm, Vector3.Lerp(-up, up, gesture).normalized, -forward);
            SetHeadLook(Mathf.Sin(phase * 0.17f + Index * 1.3f) * 22f + gesture * -16f, 5f + gesture * 6f);
        }

        private void ApplySupportedCabinHands(Vector3 floor,float preparation,float phase)
        {
            // Native helper animation is the moving lower-body base. Adapt its free
            // hands to this cabin instead of leaving them braced against absent props.
            float chest=Mathf.Clamp(Vector3.Dot(_spine.position-floor,Root.up),.65f,1.12f);
            Vector3 brace=floor+Root.up*(chest+.16f)-Root.right*.18f+Root.forward*.30f;
            Vector3 rest=floor+Root.up*(chest-.16f)+Root.right*.14f+Root.forward*.28f;
            float cue=Mathf.Clamp01((preparation-Index*.045f)/.8f),gesture=Mathf.Sin(cue*Mathf.PI);
            float beckon=Mathf.Sin(cue*Mathf.PI*4)*.08f*gesture;
            Vector3 signal=rest+Root.up*(.18f*gesture)+Root.forward*(.13f*gesture+beckon);
            SolveArm(_leftUpperArm,_leftForearm,_leftPalm,brace,1);
            SolveArm(_rightUpperArm,_rightForearm,_rightPalm,signal,1);
            // Keep palms in the forearm's natural bind frame during gestures. The
            // old arbitrary world LookRotation caused the visibly folded wrists.
            LookTowards(floor+Root.forward*2+Root.up*1.1f+Root.right*Mathf.Sin(phase*.18f+Index),.2f+gesture*.2f);
        }

        internal void LookTowards(Vector3 target,float weight)
        {
            if(_head==null || weight<=0f)return;
            Vector3 d=Root.InverseTransformDirection(target-_head.position);
            float yaw=Mathf.Clamp(Mathf.Atan2(d.x,d.z)*Mathf.Rad2Deg,-38f,38f);
            float pitch=Mathf.Clamp(-Mathf.Atan2(d.y,new Vector2(d.x,d.z).magnitude)*Mathf.Rad2Deg,-16f,18f);
            Quaternion view=Root.rotation*Quaternion.Euler(pitch,yaw,0f);
            _head.rotation=Quaternion.Slerp(_head.rotation,view*Quaternion.Inverse(_headViewOffset),weight);
        }

        internal void ApplyBraceReleasePose(Vector3 floor, Quaternion rotation, float progress, int role)
        {
            if(_cabinMotion!=null) ApplyNativeCabinPose(_cabinMotion,floor,rotation,_lastCabinSourceTime,_nativeSeated,1f);
            else if (role == 1) ApplyDoorGuardPose(floor, rotation, 0f, 0f);
            else ApplyWaitingPose(floor, rotation, 0f, 0f);
            SaveBlendPose(out Vector3 position, out Quaternion startRotation);
            Vector3 leftFoot=_leftFoot.position,rightFoot=_rightFoot.position;
            ApplyWalkPose(floor, rotation, 0f);
            BlendPose(position, startRotation, SmootherStep01(progress));
            // The front boot bears weight while the rear knee rises. Retain actual
            // floor contact; interpolating local leg rotations alone lifts both feet.
            float feet=SmootherStep01(Mathf.InverseLerp(.22f,.88f,progress));
            SolveLeg(_leftThigh,_leftCalf,_leftFoot,Vector3.Lerp(leftFoot,floor-Root.right*.17f+Root.up*SupportedRopePose.BootCentreHeight,feet),1);
            SolveLeg(_rightThigh,_rightCalf,_rightFoot,Vector3.Lerp(rightFoot,floor+Root.right*.17f+Root.up*SupportedRopePose.BootCentreHeight,feet),1);
            if(progress>.88f)LevelBoots();
        }

        internal void ApplyRightDoorKick(Vector3 floor,Quaternion rotation,Vector3 coil,float phase,float progress)
        {
            ApplyWaitingPose(floor,rotation,phase,progress);
            SaveBlendPose(out Vector3 heldPosition,out Quaternion heldRotation);
            ApplyWalkPose(floor,rotation,0f);
            // Rise off the rear knee BEFORE lifting the other foot. Kicking from the held
            // kneeling pose left the body with neither a knee nor a boot carrying its weight.
            float stand=SmootherStep01(Mathf.InverseLerp(.18f,.39f,progress));
            BlendPose(heldPosition,heldRotation,stand);
            float raise=SmootherStep01(Mathf.InverseLerp(.42f,.55f,progress));
            float recover=SmootherStep01(Mathf.InverseLerp(.72f,.90f,progress));
            Vector3 rest=floor+Root.right*.18f+Root.up*.13f;
            Vector3 contact=coil+Root.up*.09f;
            Vector3 target=Vector3.Lerp(rest,contact,raise);
            target+=Root.up*(Mathf.Sin(raise*Mathf.PI)*.13f);
            target+=Root.forward*(SmootherStep01(Mathf.InverseLerp(.57f,.70f,progress))*.22f);
            target=Vector3.Lerp(target,rest,recover);
            // Put the mass over the planted left boot while the right foot moves.
            float transfer=raise*(1-recover);
            if(_pelvis!=null)_pelvis.position-=Root.right*(.09f*transfer);
            if(_spine!=null)_spine.rotation=Quaternion.AngleAxis(-5f*raise*(1-recover),Root.right)*_spine.rotation;
            SolveLeg(_rightThigh,_rightCalf,_rightFoot,target,stand);
            if(stand>.99f)LevelBoots();
        }

        internal void SetCabinResponse(Vector3 localAccelerationResponse, Quaternion frame)
        {
            _cabinFrame = frame;
            // Distinct damped responses by body mass/stance; the same aircraft motion does not
            // move all five chests in lockstep. Contacts are solved after the compliant torso.
            float remaining = Mathf.Min(SceneClock.Delta, 0.08f);
            float stiffness = 32f + Index * 3.5f, damping = 9f + Index * 0.8f;
            while (remaining > 0f)
            {
                float dt = Mathf.Min(remaining, 1f / 120f); remaining -= dt;
                _bodyLagVelocity += ((localAccelerationResponse - _bodyLag) * stiffness - _bodyLagVelocity * damping) * dt;
                _bodyLag += _bodyLagVelocity * dt;
            }
            _cabinResponseWorld = frame * _bodyLag;
        }

        private void ApplyCabinBodyMotion(float phase, bool seated)
        {
            if (_pelvis == null) return;
            float t = SceneClock.Elapsed + Index * 0.61f;
            float bumpPhase = Mathf.Repeat(t, 5.9f);
            float bump = GestureEnvelope(bumpPhase, 5.9f, 0.1f, 0.62f);
            Vector3 right = _cabinFrame * Vector3.right, forward = _cabinFrame * Vector3.forward;
            Vector3 offset = _cabinResponseWorld + right * (Mathf.Sin(t * 1.13f) * 0.005f)
                + forward * (Mathf.Sin(t * 0.83f + 0.7f) * 0.004f);
            _pelvis.position += offset * (seated ? 0.45f : 1f);
            if (_spine != null)
                _spine.rotation = Quaternion.AngleAxis(Mathf.Clamp(-_bodyLag.x * 82f, -7f, 7f)
                    + Mathf.Sin(t * 1.13f) * 0.45f + bump * 0.45f, forward)
                    * Quaternion.AngleAxis(Mathf.Clamp(_bodyLag.z * 72f, -6f, 6f)
                    + Mathf.Sin(t * 0.83f) * 0.35f, right) * _spine.rotation;
        }

        private static float GestureEnvelope(float time, float period, float start, float end)
        {
            float cycle = Mathf.Repeat(time, period);
            if (cycle <= start || cycle >= end) return 0f;
            float p = (cycle - start) / (end - start);
            return Mathf.Sin(Mathf.PI * p) * Mathf.Sin(Mathf.PI * p);
        }

        private void SetHeadLook(float yaw, float pitch)
        {
            if (_neck != null) _neck.localRotation = _neckLocalNeutral;
            if (_head == null) return;
            _head.localRotation = _headLocalNeutral;
            // Bind pose is parent-local. Multiplying a renderer-space bind by Root.rotation
            // made everybody stare into the roof. Apply world deltas only AFTER local neutral.
            _head.rotation = Quaternion.AngleAxis(Mathf.Clamp(yaw, -32f, 32f), Root.up)
                * Quaternion.AngleAxis(Mathf.Clamp(pitch, -12f, 30f), Root.right) * _head.rotation;
        }

        private Quaternion LocalBindRotation(Transform bone)
        {
            if (bone == null) return Quaternion.identity;
            foreach (var renderer in _renderers)
            {
                var skin = renderer as SkinnedMeshRenderer;
                if (skin == null || skin.sharedMesh == null) continue;
                var binds = skin.sharedMesh.bindposes;
                var bones = skin.bones;
                int child = Array.IndexOf(bones, bone), parent = Array.IndexOf(bones, bone.parent);
                if (child >= 0 && parent >= 0 && child < binds.Length && parent < binds.Length)
                    return (binds[parent] * binds[child].inverse).rotation;
            }
            return bone.localRotation;
        }

        private void LevelBoots()
        {
            if (_leftFoot != null) _leftFoot.rotation = Root.rotation * _leftFootModelRotation;
            if (_rightFoot != null) _rightFoot.rotation = Root.rotation * _rightFootModelRotation;
        }

        private void CachePalmFrame(Transform palm)
        {
            if (palm == null) return;
            Vector3 fingers = Vector3.zero, first = Vector3.zero, last = Vector3.zero; int count = 0;
            foreach (Transform child in palm)
            {
                if (child.childCount == 0 || child.name.IndexOf("thumb", StringComparison.OrdinalIgnoreCase) >= 0
                    ||child.name.IndexOf("Digit1",StringComparison.OrdinalIgnoreCase)>=0) continue;
                Vector3 d = child.position - palm.position;
                if (count == 0) first = d;
                last = d; fingers += d; count++;
            }
            Vector3 normal = Vector3.Cross(fingers, last - first);
            if (fingers.sqrMagnitude < 0.000001f || normal.sqrMagnitude < 0.000001f)
                throw new InvalidOperationException("Cannot calibrate anatomical palm frame for " + palm.name);
            if (Vector3.Dot(normal, Root.forward) < 0f) normal = -normal;
            _palmFrames[palm] = Quaternion.Inverse(palm.rotation)
                * Quaternion.LookRotation(fingers.normalized, normal.normalized);
        }

        private void OrientPalm(Transform palm, Vector3 fingersDirection, Vector3 normal)
        {
            if (palm == null || fingersDirection.sqrMagnitude < 0.0001f || normal.sqrMagnitude < 0.0001f) return;
            Quaternion frame;
            if (!_palmFrames.TryGetValue(palm, out frame)) return;
            palm.rotation = Quaternion.LookRotation(fingersDirection.normalized, normal.normalized) * Quaternion.Inverse(frame);
        }

        internal void ApplyStandingPose(Vector3 position, Quaternion rotation, float stepPhase, float stepWeight)
        {
            ResetPose();
            Root.SetPositionAndRotation(position, rotation);
            float bob = Mathf.Sin(stepPhase * Mathf.PI * 2f) * 0.018f * stepWeight;
            float sway = Mathf.Sin(stepPhase * Mathf.PI) * 0.9f * stepWeight;
            Root.position += Root.up * bob;
            Root.rotation *= Quaternion.Euler(0f, 0f, sway);
            SolveLeg(_leftThigh, _leftCalf, _leftFoot, position - Root.right * 0.17f + Root.up * 0.13f, 1f);
            SolveLeg(_rightThigh, _rightCalf, _rightFoot, position + Root.right * 0.17f + Root.up * 0.13f, 1f);
            LevelBoots();
            SolveArm(_leftUpperArm, _leftForearm, _leftPalm, position + Root.up * 0.94f - Root.right * 0.25f + Root.forward * 0.10f, 1f);
            SolveArm(_rightUpperArm, _rightForearm, _rightPalm, position + Root.up * 0.92f + Root.right * 0.25f + Root.forward * 0.13f, 1f);
        }

        internal void ApplyWalkPose(Vector3 floor, Quaternion rotation, float phase)
        {
            ApplyCabinStep(floor,rotation,floor-rotation*Vector3.right*.17f+rotation*Vector3.up*SupportedRopePose.BootCentreHeight,
                floor+rotation*Vector3.right*.17f+rotation*Vector3.up*SupportedRopePose.BootCentreHeight,0);
        }

        internal void ApplyCabinWalk(Vector3 from,Vector3 to,Quaternion rotation,float progress)
        {
            ApplyCabinStep(CabinWalkPath.Centre(from,to,progress),rotation,
                CabinWalkPath.Foot(from,to,rotation,progress,true),CabinWalkPath.Foot(from,to,rotation,progress,false),progress);
        }

        private void ApplyCabinStep(Vector3 floor,Quaternion rotation,Vector3 leftFoot,Vector3 rightFoot,float progress)
        {
            ResetPose();_cabinPose.Apply(floor,rotation,leftFoot,rightFoot);
            SetHeadLook(0,5);
        }

        internal void ApplyRisePose(Vector3 seat, Quaternion rotation, Vector3 floor, float progress, bool guard)
        {
            if (guard) ApplyDoorGuardPose(floor, rotation, 0f, 0f);
            else ApplySeatedPose(seat, rotation, 0f);
            SaveBlendPose(out Vector3 rootPosition, out Quaternion rootRotation);
            ApplyWalkPose(floor, rotation, 0f);
            BlendPose(rootPosition, rootRotation, SmootherStep01(progress));
        }

        internal void ApplyRopePose(Vector3 rootPosition, Quaternion rootRotation, Vector3 ropeAxis,
            float descentProgress, float gripWeight)
        {
            ResetPose(); Root.SetPositionAndRotation(rootPosition,rootRotation);
            _ropePose.Apply(rootPosition,rootRotation,ropeAxis,descentProgress,gripWeight);
            SetHeadLook(0f,Mathf.Lerp(4f,0f,descentProgress));
        }

        private void FitRopeHands(Vector3 upper,Vector3 lower,float weight)
        {
            _leftGrip.Fit(upper,Vector3.up,weight);
            _rightGrip.Fit(lower,Vector3.up,weight);
        }

        internal void ApplyTouchdownPose(Vector3 position, Quaternion rotation, Vector3 axis, float progress)
        {
            ResetPose();_ropePose.Land(position,rotation,axis,progress);SetHeadLook(0f,0f);
        }

        internal void BlendToGameplayPose(float weight)
        {
            // Same exact skin hierarchy: finish on the live animator's CURRENT pose before
            // swapping renderers. Never change a real pawn's animator, hands or inventory.
            GameplayPoseBlend.Apply(_sourceToClone,Root,weight);
        }

        internal void ApplyExitPose(Vector3 seat, Quaternion seatRotation, Vector3 supportFloor, Vector3 outside,
            Quaternion ropeRotation, Vector3 ropeAxis, float progress, int cabinRole)
        {
            progress = Mathf.Clamp01(progress);
            if(cabinRole>=0 && _cabinMotion!=null) ApplyNativeCabinPose(_cabinMotion,seat,seatRotation,_lastCabinSourceTime,_nativeSeated,1f);
            else if (cabinRole == 0) ApplyDoorSillPose(seat, seatRotation, 0f, 1f);
            else if (cabinRole == 1) ApplyDoorGuardPose(seat, seatRotation, 0f, 0f);
            else if (cabinRole < 0) ApplyWalkPose(seat, seatRotation, 0f);
            else ApplySeatedPose(seat, seatRotation, 0f);
            if(cabinRole>=0)
            {
                SaveBlendPose(out Vector3 seatedPosition,out Quaternion seatedRotation);
                Vector3 lf=_leftFoot.position,rf=_rightFoot.position;
                ApplyWalkPose(supportFloor,ropeRotation,0);
                float rise=SmootherStep01(Mathf.InverseLerp(0,.43f,progress));
                BlendPose(seatedPosition,seatedRotation,rise);
                SolveLeg(_leftThigh,_leftCalf,_leftFoot,Vector3.Lerp(lf,supportFloor-Root.right*.17f+Root.up*SupportedRopePose.BootCentreHeight,rise),1);
                SolveLeg(_rightThigh,_rightCalf,_rightFoot,Vector3.Lerp(rf,supportFloor+Root.right*.17f+Root.up*SupportedRopePose.BootCentreHeight,rise),1);
            }
            SaveBlendPose(out Vector3 startPosition, out Quaternion startRotation);
            Vector3 supportLeft=_leftFoot.position,supportRight=_rightFoot.position;
            // Hands acquire the rope during the first half while feet still support the body.
            // Only the second half transfers the body's weight outside the door.
            ApplyRopePose(outside, ropeRotation, ropeAxis, 0f, 1f);
            float bodyProgress = SmootherStep01(Mathf.InverseLerp(.65f, 1f, progress));
            Vector3 slideLeft=_leftFoot.position,slideRight=_rightFoot.position;
            BlendPose(startPosition, startRotation, bodyProgress);
            SolveLeg(_leftThigh,_leftCalf,_leftFoot,Vector3.Lerp(supportLeft,slideLeft,bodyProgress),1);
            SolveLeg(_rightThigh,_rightCalf,_rightFoot,Vector3.Lerp(supportRight,slideRight,bodyProgress),1);
            // Re-solve the grip in the final blended body frame. Solving in the fully-standing
            // frame and then translating the body would move the hands away from the cable.
            float acquire = SmootherStep01(Mathf.InverseLerp(cabinRole>=0?.35f:.10f,.65f,progress));
            FitRopeHands(ropeAxis+Vector3.up*.23f,ropeAxis+Vector3.up*.04f,acquire);
        }

        private void SaveBlendPose(out Vector3 position, out Quaternion rotation)
        {
            position = Root.position; rotation = Root.rotation;
            for (int i = 0; i < _rest.Count; i++)
            {
                var t = _rest[i].Transform;
                _blendFrom[i] = new RestTransform { Transform = t, LocalPosition = t.localPosition, LocalRotation = t.localRotation };
            }
        }

        private void BlendPose(Vector3 startPosition, Quaternion startRotation, float progress, float limbProgress = -1f)
        {
            Root.SetPositionAndRotation(Vector3.Lerp(startPosition, Root.position, progress), Quaternion.Slerp(startRotation, Root.rotation, progress));
            if (limbProgress < 0f) limbProgress = progress;
            for (int i = 0; i < _rest.Count; i++)
            {
                var t = _blendFrom[i].Transform;
                t.localPosition = Vector3.Lerp(_blendFrom[i].LocalPosition, t.localPosition, limbProgress);
                t.localRotation = Quaternion.Slerp(_blendFrom[i].LocalRotation, t.localRotation, limbProgress);
            }
        }

        internal bool TryGetEyePose(out Vector3 position, out Quaternion rotation)
        {
            if (_head != null)
            {
                // EFT biped head bones use modelling axes that are not guaranteed to be
                // camera-forward. Position follows the real head bone, while orientation
                // follows the actor frame, preventing the vertical/upside-down camera defect.
                Vector3 forward = Root.forward;
                Vector3 up = Root.up;
                if (forward.sqrMagnitude > 0.01f && up.sqrMagnitude > 0.01f)
                {
                    position = _head.position + forward * 0.075f + up * 0.012f;
                    rotation = _head.rotation * _headViewOffset;
                    return true;
                }
            }
            position = Root.position + Root.up * 1.62f + Root.forward * 0.06f;
            rotation = Root.rotation;
            return false;
        }

        private void ResolveBones()
        {
            if (SourcePlayer.PlayerBones != null)
            {
                _leftPalm = MapTransform(SourcePlayer.PlayerBones.LeftPalm);
                _rightPalm = MapTransform(SourcePlayer.PlayerBones.RightPalm);
                ResolveSidedPair(SourcePlayer.PlayerBones.Upperarms, out _leftUpperArm, out _rightUpperArm);
                ResolveSidedPair(SourcePlayer.PlayerBones.Forearms, out _leftForearm, out _rightForearm);
                _leftThigh = MapTransform(SourcePlayer.PlayerBones.LeftThigh1 != null
                    ? SourcePlayer.PlayerBones.LeftThigh1.Original : null);
                _rightThigh = MapTransform(SourcePlayer.PlayerBones.RightThigh1 != null
                    ? SourcePlayer.PlayerBones.RightThigh1.Original : null);
            }

            _leftCalf = FindChildBone(_leftThigh, "calf", "lowerleg", "shin");
            _leftFoot = FindChildBone(_leftCalf, "foot", "ankle");
            _rightCalf = FindChildBone(_rightThigh, "calf", "lowerleg", "shin");
            _rightFoot = FindChildBone(_rightCalf, "foot", "ankle");
            _head = FindBestNamedBone("bip01 head", "humanhead", "base humanhead", "head");
            _neck = FindBestNamedBone("base humanneck", "humanneck", "neck");
            _pelvis = FindBestNamedBone("base humanpelvis", "humanpelvis", "pelvis");
            _spine = FindBestNamedBone("bip01 spine2", "spine2", "spine_2", "chest");

            if (_leftUpperArm == null || _rightUpperArm == null || _leftForearm == null || _rightForearm == null)
            {
                ResolveArmFallbacks();
            }
            // EFT uses Base HumanL*/HumanR* names. Resolve the real ancestors of each palm as the
            // source of truth; guessing array order can pair a right shoulder with the left hand.
            _leftForearm = FindAncestorBone(_leftPalm, "forearm1", "forearm", "lowerarm") ?? _leftForearm;
            _rightForearm = FindAncestorBone(_rightPalm, "forearm1", "forearm", "lowerarm") ?? _rightForearm;
            _leftUpperArm = FindAncestorBone(_leftForearm, "upperarm1", "upperarm", "upper_arm") ?? _leftUpperArm;
            _rightUpperArm = FindAncestorBone(_rightForearm, "upperarm1", "upperarm", "upper_arm") ?? _rightUpperArm;
            if (!HasLimbChain(_leftUpperArm, _leftForearm, _leftPalm)
                || !HasLimbChain(_rightUpperArm, _rightForearm, _rightPalm))
                throw new InvalidOperationException("Actor " + Index + " has no valid same-side shoulder/elbow/palm chains; refusing a broken grip.");
            if(_spine==null||!HasLimbChain(_leftThigh,_leftCalf,_leftFoot)||!HasLimbChain(_rightThigh,_rightCalf,_rightFoot))
                throw new InvalidOperationException("Actor "+Index+" lacks a complete torso/boot chain for supported cabin movement.");
        }

        private static Transform FindAncestorBone(Transform child, params string[] names)
        {
            foreach (string name in names)
                for (var current = child != null ? child.parent : null; current != null; current = current.parent)
                    if (current.name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0
                        && current.name.IndexOf("twist", StringComparison.OrdinalIgnoreCase) < 0) return current;
            return null;
        }

        private static bool HasLimbChain(Transform upper, Transform lower, Transform end)
        { return upper != null && lower != null && end != null && lower.IsChildOf(upper) && end.IsChildOf(lower); }

        private void ResolveArmFallbacks()
        {
            List<Transform> transforms = _sourceToClone.Values.ToList();
            if (_leftUpperArm == null) _leftUpperArm = FindBySide(transforms, true, "upperarm", "upper_arm", "arm_l");
            if (_rightUpperArm == null) _rightUpperArm = FindBySide(transforms, false, "upperarm", "upper_arm", "arm_r");
            if (_leftForearm == null) _leftForearm = FindBySide(transforms, true, "forearm", "lowerarm", "fore_arm");
            if (_rightForearm == null) _rightForearm = FindBySide(transforms, false, "forearm", "lowerarm", "fore_arm");
            if (_leftPalm == null) _leftPalm = FindBySide(transforms, true, "palm", "hand");
            if (_rightPalm == null) _rightPalm = FindBySide(transforms, false, "palm", "hand");
        }

        private void ResetPose()
        {
            for (int i = 0; i < _rest.Count; i++)
            {
                RestTransform item = _rest[i];
                if (item.Transform == null) continue;
                item.Transform.localPosition = item.LocalPosition;
                item.Transform.localRotation = item.LocalRotation;
                item.Transform.localScale = item.LocalScale;
            }
        }

        private void PlacePelvis(Vector3 seat)
        {
            Root.position += seat - (_pelvis != null ? _pelvis.position : Root.position + Root.up * _pelvisHeight);
        }

        private void SolveArm(Transform upper, Transform lower, Transform hand, Vector3 target, float weight)
        {
            bool left = upper == _leftUpperArm;
            Vector3 pole = (left ? -Root.right : Root.right) * 0.65f + Root.forward * 0.20f - Root.up * 0.55f;
            SolveTwoBone(upper, lower, hand, target, pole, Mathf.Clamp01(weight));
        }

        private void SolveLeg(Transform thigh, Transform calf, Transform foot, Vector3 target, float weight)
        {
            SolveTwoBone(thigh, calf, foot, target, Root.forward + Root.right * (thigh == _leftThigh ? -0.12f : 0.12f), Mathf.Clamp01(weight));
        }

        private static float SmootherStep01(float value)
        {
            value = Mathf.Clamp01(value);
            return value * value * value * (value * (value * 6f - 15f) + 10f);
        }

        internal static void SolveTwoBone(Transform root, Transform middle, Transform end,
            Vector3 target, Vector3 pole, float weight)
        {
            PoseMath.SolveTwoBone(root,middle,end,target,pole,weight);
        }

        private void CloseGrip(Transform palm, Vector3 rope, float weight)
        {
            if (palm == null || weight <= 0f) return;
            Quaternion previous = palm.rotation;
            OrientPalm(palm, Root.up, Root.forward);
            palm.rotation = Quaternion.Slerp(previous, palm.rotation, weight);
            foreach (Transform finger in palm)
            {
                if (finger.childCount == 0 || finger.name.IndexOf("twist", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                Transform middle = finger.GetChild(0);
                if (middle.childCount == 0) continue;
                Transform tip = middle.GetChild(0);
                Vector3 contact = new Vector3(rope.x, finger.position.y, rope.z) + Root.forward * 0.028f;
                SolveTwoBone(finger, middle, tip, contact, -Root.forward, weight);
            }
        }

        private static void RotateJointToward(Transform joint, Transform end, Vector3 target, float weight)
        {
            Vector3 current = end.position - joint.position;
            Vector3 desired = target - joint.position;
            if (current.sqrMagnitude < 0.00001f || desired.sqrMagnitude < 0.00001f)
            {
                return;
            }
            Quaternion delta = Quaternion.FromToRotation(current, desired);
            joint.rotation = Quaternion.Slerp(joint.rotation, delta * joint.rotation, weight);
        }

        private void ResolveSidedPair(Transform[] source, out Transform left, out Transform right)
        {
            left = null;
            right = null;
            if (source == null)
            {
                return;
            }
            foreach (Transform item in source)
            {
                Transform mapped = MapTransform(item);
                if (mapped == null) continue;
                if (IsLeftName(item.name)) left = mapped;
                else if (IsRightName(item.name)) right = mapped;
                else if (left == null) left = mapped;
                else if (right == null) right = mapped;
            }
        }

        private Transform MapTransform(Transform source)
        {
            if (source == null)
            {
                return null;
            }
            Transform mapped;
            return _sourceToClone.TryGetValue(source, out mapped) ? mapped : null;
        }

        private Transform FindBestNamedBone(params string[] priorities)
        {
            foreach (string priority in priorities)
            {
                Transform exact = _sourceToClone.Values.FirstOrDefault(item =>
                    item != null && string.Equals(item.name, priority, StringComparison.OrdinalIgnoreCase));
                if (exact != null) return exact;
            }
            foreach (string priority in priorities)
            {
                Transform contains = _sourceToClone.Values.FirstOrDefault(item =>
                {
                    if (item == null) return false;
                    string lower = item.name.ToLowerInvariant();
                    return lower.Contains(priority) && !lower.Contains("item") && !lower.Contains("helmet")
                        && !lower.Contains("cover");
                });
                if (contains != null) return contains;
            }
            return null;
        }

        private static Transform FindChildBone(Transform root, params string[] tokens)
        {
            if (root == null) return null;
            Transform[] children = root.GetComponentsInChildren<Transform>(true);
            foreach (string token in tokens)
            {
                Transform found = children.FirstOrDefault(item => item != root
                    && item.name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0);
                if (found != null) return found;
            }
            return root.childCount > 0 ? root.GetChild(0) : null;
        }

        private static Transform FindBySide(IEnumerable<Transform> transforms, bool left, params string[] tokens)
        {
            foreach (string token in tokens)
            {
                Transform result = transforms.FirstOrDefault(item =>
                {
                    if (item == null) return false;
                    string lower = item.name.ToLowerInvariant();
                    return lower.Contains(token) && (left ? IsLeftName(lower) : IsRightName(lower));
                });
                if (result != null) return result;
            }
            return null;
        }

        private bool IsUsefulVisualRenderer(Renderer renderer)
        {
            if (renderer == null || !renderer.gameObject.activeInHierarchy || renderer is ParticleSystemRenderer
                || renderer is TrailRenderer || renderer is LineRenderer)
            {
                return false;
            }
            string lower = renderer.name.ToLowerInvariant();
            if (!renderer.enabled && !(renderer is SkinnedMeshRenderer) && !_sourceHeadRenderers.Contains(renderer)
                && !_sourceBackpackRenderers.Contains(renderer)) return false;
            string hierarchy = BuildHierarchyPath(renderer.transform).ToLowerInvariant();
            if (hierarchy.Contains("handscontainer") || hierarchy.Contains("weapon_root")
                || hierarchy.Contains("weaponroot") || hierarchy.Contains("firstperson")
                || hierarchy.Contains("fireport") || hierarchy.Contains("camera"))
            {
                return false;
            }
            if (lower.Contains("lod1") || lower.Contains("lod2") || lower.Contains("lod3")
                || lower.Contains("_lod_1") || lower.Contains("_lod_2") || lower.Contains("shadowonly"))
            {
                return false;
            }
            if (lower.Contains("weapon") || lower.Contains("magazine") || lower.Contains("patron")
                || (lower.StartsWith("mod_") && !_sourceHeadRenderers.Contains(renderer)) || lower.Contains("muzzle"))
            {
                return false;
            }
            SkinnedMeshRenderer skinned = renderer as SkinnedMeshRenderer;
            if (skinned != null)
            {
                return skinned.sharedMesh != null;
            }
            MeshRenderer meshRenderer = renderer as MeshRenderer;
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            return meshRenderer != null && filter != null && filter.sharedMesh != null;
        }

        private static string BuildHierarchyPath(Transform transform)
        {
            System.Text.StringBuilder builder = new System.Text.StringBuilder(128);
            for (Transform current = transform; current != null; current = current.parent)
            {
                builder.Insert(0, "/" + current.name);
            }
            return builder.ToString();
        }

        private static void AddAncestors(Transform item, Transform stop, HashSet<Transform> target)
        {
            Transform current = item;
            while (current != null)
            {
                target.Add(current);
                if (current == stop) break;
                current = current.parent;
            }
        }

        private static int GetDepth(Transform transform)
        {
            int depth = 0;
            for (Transform current = transform; current != null; current = current.parent) depth++;
            return depth;
        }

        private static bool IsLeftName(string name)
        {
            string lower = name.ToLowerInvariant();
            return lower.Contains(" left") || lower.Contains("_l") || lower.Contains(" l ")
                || lower.Contains("bip01 l") || lower.Contains("humanl") || lower.StartsWith("l_") || lower.EndsWith(".l");
        }

        private static bool IsRightName(string name)
        {
            string lower = name.ToLowerInvariant();
            return lower.Contains(" right") || lower.Contains("_r") || lower.Contains(" r ")
                || lower.Contains("bip01 r") || lower.Contains("humanr") || lower.StartsWith("r_") || lower.EndsWith(".r");
        }

        private static void CopyPropertyBlock(Renderer source, Renderer destination)
        {
            MaterialPropertyBlock block = new MaterialPropertyBlock();
            source.GetPropertyBlock(block);
            destination.SetPropertyBlock(block);
        }

        private static string SafeName(Player player)
        {
            try
            {
                return player.Profile != null && !string.IsNullOrEmpty(player.Profile.Nickname)
                    ? player.Profile.Nickname : player.ProfileId;
            }
            catch
            {
                return "player";
            }
        }

        public void Dispose()
        {
            _renderers.Clear();
            _rest.Clear();
            _sourceToClone.Clear();
            if (_root != null)
            {
                UnityEngine.Object.Destroy(_root);
            }
        }
    }
}

