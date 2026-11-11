using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace UH60FastRopeInsertion
{
    // Native Generic clips run on a transform-only copy of THEIR OWN hierarchy. Transfer only
    // explicitly mapped anatomical bones; never play the source Timeline, root trajectory or camera.
    internal sealed class NativeCabinMotion : IDisposable
    {
        private readonly GameObject _root;
        private readonly AnimationClip _clip;
        private PlayableGraph _graph;
        private AnimationClipPlayable _playable;
        private bool _disposed;
        private readonly Dictionary<string, Transform> _bones = new Dictionary<string, Transform>();
        private readonly Dictionary<string, Quaternion> _bindWorld = new Dictionary<string, Quaternion>();
        private readonly Transform _pelvis, _head, _leftHip, _rightHip;
        private float _lastSample=float.NaN;
        internal Quaternion HeadingCorrection { get; }
        internal Quaternion BindHeadingCorrection { get; }
        internal string ClipName => _clip.name;
        internal Quaternion CurrentHeadingCorrection
        {
            get
            {
                Vector3 forward=Vector3.ProjectOnPlane(Vector3.Cross(_rightHip.position-_leftHip.position,Vector3.up),Vector3.up);
                return forward.sqrMagnitude>.0001f?Quaternion.Inverse(Quaternion.LookRotation(forward.normalized,Vector3.up)):HeadingCorrection;
            }
        }
        internal float CurrentBodyPitch
        {
            get
            {
                Vector3 forward=Vector3.ProjectOnPlane(Vector3.Cross(_rightHip.position-_leftHip.position,Vector3.up),Vector3.up).normalized;
                Vector3 body=_head.position-_pelvis.position;
                return Mathf.Atan2(Vector3.Dot(body,forward),Vector3.Dot(body,Vector3.up))*Mathf.Rad2Deg;
            }
        }

        internal NativeCabinMotion(Animator sourceAnimator, AnimationClip clip)
        {
            if (sourceAnimator==null || clip==null) throw new InvalidOperationException("Original cabin hierarchy/clip missing; no procedural replacement is being selected.");
            if (clip.legacy || clip.humanMotion) throw new InvalidOperationException("Expected original non-legacy Generic cabin clip: "+clip.name);
            Transform source=sourceAnimator.transform;
            _clip=clip; _root=new GameObject("UH60_NativeMotionSampler_"+source.name);
            UnityEngine.Object.DontDestroyOnLoad(_root);
            try
            {
            CloneChildren(source,_root.transform);
            foreach(var t in _root.GetComponentsInChildren<Transform>(true))
                if(t.name.StartsWith("Base Human",StringComparison.OrdinalIgnoreCase) && !_bones.ContainsKey(t.name)) _bones.Add(t.name,t);
            // Use source skin bind matrices, not an arbitrary currently sampled local rotation.
            // Target rigs do not share every forearm twist/neck parent or modelling basis.
            var bindPoints=new Dictionary<string,Vector3>();
            foreach(var skin in source.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if(skin.sharedMesh==null)continue;
                var binds=skin.sharedMesh.bindposes;var bones=skin.bones;
                for(int i=0;i<Math.Min(bones.Length,binds.Length);i++)
                    if(bones[i]!=null && _bones.ContainsKey(bones[i].name))
                    {
                        Matrix4x4 boneBind=source.worldToLocalMatrix*skin.transform.localToWorldMatrix*binds[i].inverse;
                        _bindWorld[bones[i].name]=boneBind.rotation;
                        bindPoints[bones[i].name]=boneBind.GetColumn(3);
                    }
            }
            if(!bindPoints.TryGetValue("Base HumanLThigh1",out Vector3 bindLeft)
                ||!bindPoints.TryGetValue("Base HumanRThigh1",out Vector3 bindRight))
                throw new InvalidOperationException("Native bind hip frame unavailable: "+clip.name);
            Vector3 bindForward=Vector3.ProjectOnPlane(Vector3.Cross(bindRight-bindLeft,Vector3.up),Vector3.up);
            if(bindForward.sqrMagnitude<.0001f)throw new InvalidOperationException("Native bind hip frame degenerate: "+clip.name);
            BindHeadingCorrection=Quaternion.Inverse(Quaternion.LookRotation(bindForward.normalized,Vector3.up));
            foreach(var pair in _bones)
                if(!_bindWorld.ContainsKey(pair.Key))_bindWorld[pair.Key]=pair.Value.rotation;
            _pelvis=Require("Base HumanPelvis"); _head=Require("Base HumanHead");
            Require("Base HumanLPalm"); Require("Base HumanRPalm");
            _leftHip=Require("Base HumanLThigh1"); _rightHip=Require("Base HumanRThigh1");
            // Compiled Generic clips must be bound to an Animator output. SampleAnimation on
            // bare transforms left these native runtime hand bindings unchanged in test8.
            // The source controller/Timeline/behaviours are deliberately NOT copied.
            var animator=_root.AddComponent<Animator>();
            animator.avatar=sourceAnimator.avatar;
            animator.applyRootMotion=false;
            animator.fireEvents=false;
            animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            _graph=PlayableGraph.Create("UH60_Cabin_"+clip.name);
            _graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            _playable=AnimationClipPlayable.Create(_graph,clip);
            _playable.SetApplyFootIK(false);
            _playable.SetApplyPlayableIK(false);
            _playable.SetSpeed(0d);
            var output=AnimationPlayableOutput.Create(_graph,"NativeCabinPose",animator);
            output.SetSourcePlayable(_playable);
            output.SetWeight(1f);
            _graph.Play();
            Plugin.Log.LogInfo("Native cabin graph bound: "+clip.name+"; source="+source.name
                +"; transforms="+_root.GetComponentsInChildren<Transform>(true).Length
                +"; avatar="+(animator.avatar!=null?animator.avatar.name:"none (Generic paths)")
                +"; legacy="+clip.legacy+"; humanMotion="+clip.humanMotion);
            Sample(0f);
            Quaternion leftAtStart=Require("Base HumanLPalm").localRotation,rightAtStart=Require("Base HumanRPalm").localRotation;
            Sample(19f);
            float animated=Quaternion.Angle(leftAtStart,Require("Base HumanLPalm").localRotation)
                +Quaternion.Angle(rightAtStart,Require("Base HumanRPalm").localRotation);
            if(float.IsNaN(animated)||float.IsInfinity(animated)||animated<1f)
                throw new InvalidOperationException("Native PlayableGraph did not animate its original hand bindings: "+clip.name
                    +"; deltaDeg="+animated.ToString("F3")+"; source="+source.name+"; bones="+_bones.Count);
            Sample(0f);
            Vector3 across=_rightHip.position-_leftHip.position;
            Vector3 forward=Vector3.ProjectOnPlane(Vector3.Cross(across,Vector3.up),Vector3.up).normalized;
            if (forward.sqrMagnitude<.1f || _head.position.y-_pelvis.position.y<.15f)
                throw new InvalidOperationException("Native Generic clip did not produce an upright anatomical pose: "+clip.name);
            HeadingCorrection=Quaternion.Inverse(Quaternion.LookRotation(forward,Vector3.up));
            Plugin.Log.LogInfo("Native cabin motion ready: "+clip.name+"; anatomical bones="+_bones.Count
                +"; hand probe deltaDeg="+animated.ToString("F3")
                +"; native head/pelvis vertical="+(_head.position.y-_pelvis.position.y).ToString("F3"));
            }
            catch { Dispose();throw; }
        }
        private static void CloneChildren(Transform source, Transform target)
        {
            foreach(Transform child in source)
            {
                var copy=new GameObject(child.name).transform;copy.SetParent(target,false);
                copy.localPosition=child.localPosition;copy.localRotation=child.localRotation;copy.localScale=child.localScale;
                CloneChildren(child,copy);
            }
        }
        private Transform Require(string name)
        {
            if(!_bones.TryGetValue(name,out Transform t)) throw new InvalidOperationException("Native anatomical bone missing: "+name);
            return t;
        }
        internal void Sample(float seconds)
        {
            if(_disposed)throw new ObjectDisposedException(nameof(NativeCabinMotion));
            if(float.IsNaN(seconds)||float.IsInfinity(seconds))throw new ArgumentOutOfRangeException(nameof(seconds));
            seconds=Mathf.Clamp(seconds,0f,_clip.length);
            if(seconds==_lastSample)return;
            _playable.SetTime(seconds);
            _graph.Evaluate(0f);
            _lastSample=seconds;
        }
        internal bool TryRotation(string name,out Quaternion local)
        {
            if(_bones.TryGetValue(name,out Transform bone)) { local=bone.localRotation;return true; }
            local=Quaternion.identity;return false;
        }
        internal bool TryWorldPose(string name,out Vector3 position,out Quaternion rotation,out Quaternion bind)
        {
            if(_bones.TryGetValue(name,out Transform bone) && _bindWorld.TryGetValue(name,out bind))
            { position=bone.position;rotation=bone.rotation;return true; }
            position=Vector3.zero;rotation=bind=Quaternion.identity;return false;
        }
        internal bool TryGripFrame(bool left,out Quaternion rotation,out Vector3 offset)
        {
            Transform palm=Require(left?"Base HumanLPalm":"Base HumanRPalm");
            var children=palm.GetComponentsInChildren<Transform>(true);
            Transform index=children.FirstOrDefault(t=>t.name.IndexOf("Digit22",StringComparison.OrdinalIgnoreCase)>=0
                ||t.name.IndexOf("index2",StringComparison.OrdinalIgnoreCase)>=0);
            Transform thumb=children.FirstOrDefault(t=>t.name.IndexOf("Digit12",StringComparison.OrdinalIgnoreCase)>=0
                ||t.name.IndexOf("thumb2",StringComparison.OrdinalIgnoreCase)>=0);
            rotation=palm.rotation;offset=Vector3.zero;
            if(index==null||thumb==null)return false;
            offset=(index.position+thumb.position)*.5f-palm.position;
            return true;
        }
        public void Dispose()
        {
            if(_disposed)return;
            _disposed=true;
            try { if(_graph.IsValid())_graph.Destroy(); }
            finally { if(_root!=null)UnityEngine.Object.Destroy(_root); }
        }

        internal static NativeCabinMotion Read(Transform cutsceneRoot,string actor,string clipName)
        {
            var container=cutsceneRoot.Find("Helicopter_01/Crew/"+actor);
            var animator=container!=null ? container.GetComponentsInChildren<Animator>(true)
                .OrderByDescending(a=>a.name.IndexOf("ClothShader",StringComparison.OrdinalIgnoreCase)>=0).FirstOrDefault():null;
            var clip=Resources.FindObjectsOfTypeAll<AnimationClip>().FirstOrDefault(c=>c.name==clipName);
            return new NativeCabinMotion(animator,clip);
        }
    }
}
