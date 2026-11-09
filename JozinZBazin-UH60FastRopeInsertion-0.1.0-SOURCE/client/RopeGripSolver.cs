using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace UH60FastRopeInsertion
{
    // Authored visible grip, independent of the cutscene's off-camera terminal pose.
    // Anatomical frames, roll distributed through the forearm, actual phalanges around cable.
    internal sealed class RopeGripSolver
    {
        private sealed class Finger
        {
            internal Transform[] Joints;
            internal Vector3 TipAxis;
            internal Quaternion[] Bind;
        }
        private readonly Transform _root,_upper,_lower,_palm;
        private readonly bool _left;
        private readonly Quaternion _palmFrame,_palmBind;
        private readonly Transform[] _knuckles,_owned,_twists,_digits;
        private readonly Quaternion[] _from,_digitBind;
        private readonly Quaternion[] _armBind;
        private readonly Finger[] _fingers;
        private readonly Transform[] _thumb;
        internal float LastContactError { get; private set; }
        internal float LastWristBend { get; private set; }
        // Bone centres need room for glove thickness AND straight phalanx chords.
        // Putting them only 8 mm outside a 24 mm cable drove finger surfaces through it.
        private const float ContactRadius=.042f;
        private const float PalmOffset=.043f;

        internal RopeGripSolver(Transform root,Transform upper,Transform lower,Transform palm,bool left)
        {
            _root=root;_upper=upper;_lower=lower;_palm=palm;_left=left;
            string prefix="Base Human"+(left?"L":"R")+"Digit";
            var nodes=palm.GetComponentsInChildren<Transform>(true);
            Transform Find(int finger,int joint)
            {
                var t=nodes.FirstOrDefault(n=>n.name==prefix+finger+joint);
                if(t==null)throw new InvalidOperationException("Anatomical grip joint missing: "+prefix+finger+joint);
                return t;
            }
            _knuckles=Enumerable.Range(2,4).Select(i=>Find(i,1)).ToArray();
            _fingers=Enumerable.Range(2,4).Select(i=>
            {
                var chain=new[]{Find(i,1),Find(i,2),Find(i,3)};
                return new Finger{Joints=chain,Bind=chain.Select(t=>t.localRotation).ToArray(),TipAxis=Quaternion.Inverse(chain[2].rotation)
                    *(chain[2].position-chain[1].position).normalized};
            }).ToArray();
            _thumb=new[]{Find(1,1),Find(1,2),Find(1,3)};
            _digits=nodes.Where(t=>t.name.StartsWith(prefix,StringComparison.Ordinal)).ToArray();
            _digitBind=_digits.Select(t=>t.localRotation).ToArray();
            Vector3 longitudinal=KnuckleCentre()-palm.position;
            Vector3 normal=Vector3.Cross(longitudinal,_knuckles[3].position-_knuckles[0].position);
            if(Vector3.Dot(normal,root.forward)<0f)normal=-normal;
            if(normal.sqrMagnitude<.0000001f)throw new InvalidOperationException("Degenerate anatomical palm frame: "+palm.name);
            _palmFrame=Quaternion.Inverse(palm.rotation)*Quaternion.LookRotation(longitudinal,normal);
            _palmBind=palm.localRotation;
            _owned=upper.GetComponentsInChildren<Transform>(true);_from=new Quaternion[_owned.Length];
            _armBind=_owned.Select(t=>t.localRotation).ToArray();
            var twist=new List<Transform>();
            for(var t=palm.parent;t!=null && t!=upper;t=t.parent)
            {
                twist.Add(t);if(t==lower)break;
            }
            twist.Reverse();_twists=twist.ToArray();
        }
        private Vector3 KnuckleCentre()
        {
            Vector3 result=Vector3.zero;foreach(var t in _knuckles)result+=t.position;return result*.25f;
        }
        internal void Fit(Vector3 contact,Vector3 cableAxis,float weight)
        {
            if(weight<=.001f)return;
            weight=Mathf.Clamp01(weight);cableAxis=cableAxis.normalized;
            if(cableAxis.sqrMagnitude<.5f)throw new ArgumentException("Grip requires a cable tangent.");
            for(int i=0;i<_owned.Length;i++)_from[i]=_owned[i].localRotation;
            for(int i=0;i<_digits.Length;i++)_digits[i].localRotation=_digitBind[i];
            Vector3 longitudinal=Vector3.ProjectOnPlane(_root.forward,cableAxis).normalized;
            if(longitudinal.sqrMagnitude<.1f)longitudinal=Vector3.ProjectOnPlane(_root.up,cableAxis).normalized;
            Vector3 normal=PalmNormal(cableAxis,longitudinal);
            _palm.rotation=Quaternion.LookRotation(longitudinal,normal)*Quaternion.Inverse(_palmFrame);
            Vector3 pole=(_left?-_root.right:_root.right)*.65f+_root.forward*.20f-_root.up*.55f;
            for(int i=0;i<12;i++)
            {
                Vector3 offset=KnuckleCentre()-_palm.position+normal*PalmOffset;
                // Under-relax the positional solve. At a low pickup the forearm is almost
                // parallel to the cable tangent; a full correction flipped the palm normal
                // on alternate iterations, leaving a 13 cm error despite reachable contact.
                Vector3 wrist=Vector3.Lerp(_palm.position,contact-offset,.60f);
                CinematicActor.SolveTwoBone(_upper,_lower,_palm,wrist,pole,1f);
                longitudinal=(_palm.position-_lower.position).normalized;
                normal=PalmNormal(cableAxis,longitudinal);
                _palm.rotation=Quaternion.LookRotation(longitudinal,normal)*Quaternion.Inverse(_palmFrame);
            }
            Quaternion desired=_palm.rotation;
            _palm.localRotation=_palmBind;
            Vector3 current=_palm.rotation*_palmFrame*Vector3.up;
            float roll=Vector3.SignedAngle(Vector3.ProjectOnPlane(current,longitudinal),
                Vector3.ProjectOnPlane(normal,longitudinal),longitudinal);
            // Forearm1/2/3 share pronation. Do not dump it into the wrist joint.
            for(int i=0;i<_twists.Length;i++)
                _twists[i].rotation=Quaternion.AngleAxis(roll/Mathf.Max(1,_twists.Length),longitudinal)*_twists[i].rotation;
            _palm.rotation=desired;
            foreach(var finger in _fingers)
                for(int j=0;j<3;j++)
                {
                    Transform joint=finger.Joints[j];Vector3 start=joint.position;
                    Vector3 end=j<2?finger.Joints[j+1].position:start+joint.rotation*finger.TipAxis*.017f;
                    Vector3 centre=contact+cableAxis*Vector3.Dot(start-contact,cableAxis);
                    Vector3 radial=start-centre;float distance=radial.magnitude;
                    float length=(end-start).magnitude;
                    if(distance<.00001f || length<.00001f)continue;
                    float angle=Mathf.Acos(Mathf.Clamp((distance*distance+ContactRadius*ContactRadius-length*length)
                        /(2f*distance*ContactRadius),-1f,1f))*Mathf.Rad2Deg;
                    float sense=Vector3.Dot(Vector3.Cross(cableAxis,radial),longitudinal)>0f?1f:-1f;
                    Vector3 target=centre+Quaternion.AngleAxis(sense*angle,cableAxis)*radial.normalized*ContactRadius;
                    if((target-start).sqrMagnitude>.0000001f)
                        joint.rotation=Quaternion.FromToRotation(end-start,target-start)*joint.rotation;
                    // A short little-finger tip previously curled all the way back
                    // into its own proximal segment. Bound anatomical flexion instead
                    // of treating each phalanx as an unconstrained ball joint.
                    float flex=Quaternion.Angle(finger.Bind[j],joint.localRotation);
                    float limit=j==2?65f:105f;
                    if(flex>limit)joint.localRotation=Quaternion.Slerp(finger.Bind[j],joint.localRotation,limit/flex);
                }
            Vector3 thumbTarget=contact+normal*.038f-longitudinal*.016f+cableAxis*.025f;
            CinematicActor.SolveTwoBone(_thumb[0],_thumb[1],_thumb[2],thumbTarget,
                cableAxis*(_left?1f:-1f),1f);
            LastContactError=(KnuckleCentre()+normal*PalmOffset-contact).magnitude;
            LastWristBend=Vector3.Angle(longitudinal,_palm.rotation*_palmFrame*Vector3.forward);
            // Blend acquisition/release once, not six times through the iterative fitter.
            if(weight<.9999f)for(int i=0;i<_owned.Length;i++)
                _owned[i].localRotation=Quaternion.Slerp(_from[i],_owned[i].localRotation,weight);
        }
        private Vector3 PalmNormal(Vector3 axis,Vector3 longitudinal)
        {
            Vector3 normal=Vector3.Cross(axis,longitudinal);
            if(normal.sqrMagnitude<.0001f)normal=Vector3.ProjectOnPlane(_root.right,longitudinal);
            return normal.normalized*(_left?1f:-1f);
        }
        internal void Release(Vector3 wrist,float progress)
        {
            if(progress<=0)return;
            for(int i=0;i<_owned.Length;i++){_from[i]=_owned[i].localRotation;_owned[i].localRotation=_armBind[i];}
            PoseMath.SolveTwoBone(_upper,_lower,_palm,wrist,
                (_left?-_root.right:_root.right)*.65f+_root.forward*.2f-_root.up*.55f,1);
            for(int i=0;i<_owned.Length;i++)_owned[i].localRotation=Quaternion.Slerp(_from[i],_owned[i].localRotation,progress);
        }
    }
}
