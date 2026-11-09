using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace UH60FastRopeInsertion
{
    // The user's exported five poses are the source, not an overlay fading into old native poses.
    // Deterministic action curves + contact IK; no Animator, live-pawn mutation or per-frame arrays.
    internal sealed class UserCabinPerformance
    {
        internal const float DeploymentSeconds=8f,MainPreparation=5.2f;
        private readonly Transform _root,_pelvis,_spine,_head,_lu,_ll,_lp,_ru,_rl,_rp,_lt,_lc,_lf,_rt,_rc,_rf;
        private readonly Transform[] _ordered;
        private readonly Vector3[] _bindPos,_savedPos;
        private readonly Quaternion[] _bindRot,_savedRot;
        private readonly int _role;
        private readonly SupportedCabinPose _stance;
        private readonly SupportedRopePose _rope;
        private readonly RopeGripSolver _leftGrip,_rightGrip;
        private Vector3 _savedRoot;private Quaternion _savedRootRot;
        private Vector3 _baseFloorLocal;
        internal float LastSupportError {get;private set;}
        internal float LastActionError {get;private set;}
        internal UserCabinPerformance(Transform root,IEnumerable<Transform> bones,int role,
            SupportedCabinPose stance,SupportedRopePose rope,RopeGripSolver leftGrip,RopeGripSolver rightGrip)
        {
            _root=root;_role=role;_stance=stance;_rope=rope;_leftGrip=leftGrip;_rightGrip=rightGrip;
            _ordered=bones.Where(t=>t.name=="Root_Joint"||t.name.StartsWith("Base Human",StringComparison.Ordinal)).ToArray();
            var byName=_ordered.ToDictionary(t=>t.name);
            Transform B(string n)=>byName["Base Human"+n];
            _pelvis=B("Pelvis");_spine=B("Spine2");_head=B("Head");
            _lu=B("LUpperarm");_ll=B("LForearm1");_lp=B("LPalm");_ru=B("RUpperarm");_rl=B("RForearm1");_rp=B("RPalm");
            _lt=B("LThigh1");_lc=B("LCalf");_lf=B("LFoot");_rt=B("RThigh1");_rc=B("RCalf");_rf=B("RFoot");
            _bindPos=_ordered.Select(t=>t.localPosition).ToArray();_bindRot=_ordered.Select(t=>t.localRotation).ToArray();
            _savedPos=new Vector3[_ordered.Length];_savedRot=new Quaternion[_ordered.Length];
        }
        internal static float Smooth(float p){p=Mathf.Clamp01(p);return p*p*p*(p*(p*6-15)+10);}
        private static float Range(float t,float a,float b)=>Smooth(Mathf.InverseLerp(a,b,t));
        private static float Envelope(float t,float start,float arrive,float leave,float end)=>Range(t,start,arrive)*(1-Range(t,leave,end));
        internal static float ReleaseSeconds(int side)=>UserCabinTimeline.ReleaseSeconds(side);
        internal static float WalkSeconds(Vector3 from,Vector3 to)=>Mathf.Clamp(Vector3.Distance(from,to)*4f,2.2f,8f);
        internal float PreparationSeconds(Transform aircraft,Vector3 stand)=>_role==0?MainPreparation:
            InsertionTiming.Rise+WalkSeconds(aircraft.TransformPoint(_baseFloorLocal),stand)+InsertionTiming.Exit;
        internal static Vector3 CoilCentre(Transform frame,Vector3 start,Vector3 ground,float seconds,int side)
            =>UserCabinTimeline.CoilCentre(frame,start,ground,seconds,side);
        private void Base(CabinClipData clip,Transform aircraft,float seconds)
        {
            if(!clip.Roles.TryGetValue(_role,out var tracks))throw new InvalidOperationException("User cabin pose missing role "+(_role+1));
            var r=clip.Sample(tracks["$actor"],seconds);_root.SetPositionAndRotation(aircraft.TransformPoint(r.Position),aircraft.rotation*r.Rotation);
            foreach(var t in _ordered)
            {
                if(!tracks.TryGetValue(t.name,out var track))continue;
                var p=clip.Sample(track,seconds);
                if(t.name=="Root_Joint")t.SetPositionAndRotation(_root.TransformPoint(p.Position),_root.rotation*p.Rotation);
                else{t.localPosition=p.Position;t.localRotation=p.Rotation;}
            }
        }
        internal void SourcePose(CabinClipData clip,Transform aircraft,float seconds)=>Base(clip,aircraft,seconds);
        private void Save()
        {
            _savedRoot=_root.position;_savedRootRot=_root.rotation;
            for(int i=0;i<_ordered.Length;i++){_savedPos[i]=_ordered[i].localPosition;_savedRot[i]=_ordered[i].localRotation;}
        }
        private void Bind(){for(int i=0;i<_ordered.Length;i++){_ordered[i].localPosition=_bindPos[i];_ordered[i].localRotation=_bindRot[i];}}
        private void SlideStart(Vector3 outside,Quaternion facing,Vector3 axis)
        {
            _rope.Apply(outside,facing,axis,0,1);
            // Match CinematicActor.ApplyRopePose(0), including its neutral head pitch.
            _head.rotation=Quaternion.AngleAxis(4f,_root.right)*_head.rotation;
        }
        private void Blend(float weight)
        {
            _root.SetPositionAndRotation(Vector3.Lerp(_savedRoot,_root.position,weight),Quaternion.Slerp(_savedRootRot,_root.rotation,weight));
            for(int i=0;i<_ordered.Length;i++){var t=_ordered[i];t.localPosition=Vector3.Lerp(_savedPos[i],t.localPosition,weight);t.localRotation=Quaternion.Slerp(_savedRot[i],t.localRotation,weight);}
        }
        private Quaternion Facing(Transform aircraft)
        {
            Vector3 right=Vector3.ProjectOnPlane(_rt.position-_lt.position,aircraft.up).normalized;
            return Quaternion.LookRotation(Vector3.Cross(right,aircraft.up).normalized,aircraft.up);
        }
        private static float Fit(Transform a,Transform b,Transform c,Vector3 goal,Quaternion rotation)
        {
            Vector3 direction=(goal-a.position).normalized;
            Vector3 pole=Vector3.ProjectOnPlane(b.position-a.position,direction);
            if(pole.sqrMagnitude<.00001f)pole=Vector3.ProjectOnPlane(-a.forward-a.right*.2f,direction);
            PoseMath.SolveTwoBone(a,b,c,goal,pole,1);c.rotation=rotation;
            return Vector3.Distance(c.position,goal);
        }
        private void Look(Vector3 up,Vector3 forward,float yaw,float pitch)
        {
            _head.rotation=Quaternion.AngleAxis(yaw,up)*Quaternion.AngleAxis(pitch,Vector3.Cross(up,forward))*_head.rotation;
        }
        private void Lean(Transform aircraft,Vector3 toward,float degrees)
        {
            Vector3 d=Vector3.ProjectOnPlane(toward,aircraft.up);
            if(d.sqrMagnitude>.0001f)_spine.rotation=Quaternion.AngleAxis(degrees,Vector3.Cross(aircraft.up,d.normalized))*_spine.rotation;
        }
        private void OpenGestureHand(bool left,float weight)
        {
            string prefix=left?"Base HumanLDigit":"Base HumanRDigit";
            // Open only the signalling/patting hand, relative to the rig's real neutral fingers.
            // No changes to supporting fingers, flight holds or the accepted rope grip.
            for(int i=0;i<_ordered.Length;i++)if(_ordered[i].name.StartsWith(prefix,StringComparison.Ordinal))
                _ordered[i].localRotation=Quaternion.Slerp(_ordered[i].localRotation,_bindRot[i],.85f*weight);
        }
        internal Vector3 Floor(Transform aircraft,float height)
        {
            Vector3 p=aircraft.InverseTransformPoint(_pelvis.position);p.y=height;return aircraft.TransformPoint(p);
        }
        private void Rise(Vector3 floor,Quaternion facing,float p)
        {
            Vector3 l=_lf.position,r=_rf.position;Quaternion lr=_lf.rotation,rr=_rf.rotation;Save();Bind();
            Vector3 nl=floor-facing*Vector3.right*.17f+facing*Vector3.up*SupportedRopePose.BootCentreHeight;
            Vector3 nr=floor+facing*Vector3.right*.17f+facing*Vector3.up*SupportedRopePose.BootCentreHeight;
            Crouch(floor,facing,nl,nr);Blend(Smooth(p));
            float a=Range(p,.06f,.55f),b=Range(p,.35f,.96f);
            Fit(_lt,_lc,_lf,Vector3.Lerp(l,nl,a),Quaternion.Slerp(lr,_lf.rotation,a));
            Fit(_rt,_rc,_rf,Vector3.Lerp(r,nr,b),Quaternion.Slerp(rr,_rf.rotation,b));
        }
        private void Crouch(Vector3 floor,Quaternion facing,Vector3 left,Vector3 right)
        {
            _stance.Apply(floor,facing,left,right);_root.position-=facing*Vector3.up*.08f;
            Fit(_lt,_lc,_lf,left,_lf.rotation);Fit(_rt,_rc,_rf,right,_rf.rotation);
        }
        private static Vector3 WalkFoot(Vector3 from,Vector3 to,Quaternion facing,float p,bool left)
        {
            int steps=Mathf.Clamp(Mathf.CeilToInt(Vector3.Distance(from,to)/.26f),2,12);
            float clock=Smooth(p)*steps;int own=Mathf.Min(steps-1,Mathf.FloorToInt(clock));
            if((own%2==0)!=left)own--;
            Vector3 side=facing*Vector3.right*(left?-.17f:.17f),up=facing*Vector3.up;
            if(own<0)return from+side+up*SupportedRopePose.BootCentreHeight;
            float previous=own<2?0:(own-.5f)/steps,next=own>=steps-2?1:(own+1.5f)/steps;
            float swing=Smooth(Mathf.Clamp01(clock-own));
            return Vector3.Lerp(from,to,Mathf.Lerp(previous,next,swing))+side+up*(SupportedRopePose.BootCentreHeight+Mathf.Sin(swing*Mathf.PI)*.045f);
        }
        internal void Apply(CabinClipData clip,Transform aircraft,float seconds,float deploySeconds,Vector3 response,
            Vector3 coil,float cabinFloor,Vector3? partner)
        {
            Base(clip,aircraft,seconds);LastSupportError=LastActionError=0;
            _baseFloorLocal=aircraft.InverseTransformPoint(Floor(aircraft,cabinFloor));
            var facing=Facing(aircraft);Vector3 forward=facing*Vector3.forward;
            float s=deploySeconds,fade=Smooth(Mathf.Clamp01(seconds/.8f));
            if(_role==2 && s>=0)Rise(Floor(aircraft,cabinFloor),facing,Range(s,.75f,2.15f));
            Vector3 lp=_lp.position,rp=_rp.position,lf=_lf.position,rf=_rf.position;
            Quaternion lpr=_lp.rotation,rpr=_rp.rotation,lfr=_lf.rotation,rfr=_rf.rotation;
            float phase=seconds*Mathf.PI/4f,offset=_role*.73f;
            float oscillation=(Mathf.Sin(phase+offset)-Mathf.Sin(offset))*fade;
            _pelvis.position+=aircraft.right*(Mathf.Clamp(response.x,-.035f,.035f)*(_role==0?.35f:.65f));
            _spine.rotation=Quaternion.AngleAxis(Mathf.Clamp(-response.x*75,-4,4)+oscillation*.65f,aircraft.forward)*_spine.rotation;
            _spine.rotation=Quaternion.AngleAxis(Mathf.Clamp(response.z*65,-4,4),aircraft.right)*_spine.rotation;
            float yaw=oscillation*8,pitch=(Mathf.Sin(phase*2+offset)-Mathf.Sin(offset))*2*fade;
            if(_role==1 && s<0)
            {
                float over=Envelope(seconds,12,13.8f,14.8f,17);
                _spine.rotation=Quaternion.AngleAxis(over*10,aircraft.up)*_spine.rotation;yaw+=over*55;
            }
            bool supportLeft=_role!=0&&_role!=3;float support=1;
            Vector3? action=null;bool actionLeft=!supportLeft;float actionWeight=0;
            float handTurn=0,openHand=0;
            if(s>=0)
            {
                if(_role==0)
                {
                    actionWeight=Envelope(s,1.05f,1.8f,2.8f,3.8f);actionLeft=true;
                    float roll=Range(s,1.3f,2.8f);
                    Vector3 goal=coil+aircraft.right*(.12f+.18f*roll)+aircraft.forward*.14f+aircraft.up*.095f;
                    action=Vector3.Lerp(lp,goal,actionWeight);
                    float turn=Mathf.Clamp(Vector3.SignedAngle(forward,Vector3.ProjectOnPlane(goal-_pelvis.position,aircraft.up),aircraft.up),-8,8);
                    _spine.rotation=Quaternion.AngleAxis(turn*actionWeight,aircraft.up)*_spine.rotation;
                    Lean(aircraft,goal-_pelvis.position,14*actionWeight);_pelvis.position-=aircraft.up*(.065f*actionWeight);
                    yaw+=actionWeight*12;pitch+=actionWeight*12;
                }
                else if(_role==1 && partner.HasValue)
                {
                    actionWeight=Envelope(s,.18f,.70f,1.04f,1.95f);actionLeft=false;
                    float body=Envelope(s,.04f,.55f,.98f,2.08f);
                    Vector3 toward=Vector3.ProjectOnPlane(partner.Value-_pelvis.position,aircraft.up).normalized;
                    _pelvis.position+=(-aircraft.right*.28f-aircraft.forward*.10f-aircraft.up*.08f)*body;
                    Lean(aircraft,toward,27*body);
                    float tap=Envelope(s,.65f,.82f,.87f,1.04f);
                    action=Vector3.Lerp(rp,partner.Value+aircraft.up*(.11f-.075f*tap),actionWeight);pitch+=actionWeight*10;
                    handTurn=-9*actionWeight;openHand=actionWeight;
                }
                else if(_role==2)
                {
                    support=0;
                    float lift=Range(s,2.15f,2.6f),recover=Range(s,3.0f,3.9f),kick=lift*(1-recover);
                    _pelvis.position+=(-aircraft.forward*.12f+aircraft.right*.035f)*kick;
                    Vector3 packed=coil-aircraft.right*(.55f*Range(s,2.55f,3.25f));
                    Vector3 target=packed-aircraft.right*.10f+aircraft.forward*.12f+aircraft.up*.065f;
                    target+=aircraft.right*(Range(s,2.65f,3.0f)*.18f);
                    rf=Vector3.Lerp(rf,target,kick)+aircraft.up*(Mathf.Sin(lift*Mathf.PI)*.07f*(1-recover));
                    Lean(aircraft,target-_pelvis.position,8*kick);pitch+=kick*12;
                }
                else if(_role==3 && partner.HasValue)
                {
                    actionLeft=Vector3.Distance(lp,partner.Value)<Vector3.Distance(rp,partner.Value);supportLeft=!actionLeft;
                    actionWeight=Envelope(s,.35f,.9f,2.6f,3.65f);
                    Vector3 direction=(partner.Value-_head.position).normalized;
                    yaw+=Mathf.Clamp(Vector3.SignedAngle(forward,Vector3.ProjectOnPlane(direction,aircraft.up),aircraft.up),-45,45)*actionWeight;
                    var shoulder=actionLeft?_lu:_ru;
                    float beats=Envelope(s,.95f,1.2f,1.4f,1.65f)+Envelope(s,1.7f,1.95f,2.15f,2.4f);
                    float follow=Envelope(s,.25f,.8f,2.65f,3.8f);
                    Lean(aircraft,-aircraft.right,3*follow);
                    _spine.rotation=Quaternion.AngleAxis(-4*follow,aircraft.up)*_spine.rotation;
                    Vector3 exit=-aircraft.right;
                    Vector3 goal=shoulder.position+forward*(.20f-.045f*beats)+aircraft.up*(.01f+.025f*beats)+exit*(.18f+.18f*beats);
                    action=Vector3.Lerp(actionLeft?lp:rp,goal,actionWeight);
                    handTurn=14*beats*actionWeight;openHand=actionWeight;
                }
            }
            Look(aircraft.up,forward,yaw,pitch);
            if(_role==0)
            {
                lf+=aircraft.forward*(oscillation*.022f-response.z*.7f)-aircraft.right*(response.x*.8f);
                rf+=aircraft.forward*(oscillation*.030f-response.z*.8f)-aircraft.right*(response.x*.9f);
            }
            LastSupportError=Mathf.Max(Fit(_lt,_lc,_lf,lf,lfr),Fit(_rt,_rc,_rf,rf,rfr));
            if(support>.001f)
            {
                float error=supportLeft?Fit(_lu,_ll,_lp,lp,lpr):Fit(_ru,_rl,_rp,rp,rpr);
                LastSupportError=Mathf.Max(LastSupportError,error);
            }
            if(action.HasValue)
            {
                LastActionError=actionLeft?Fit(_lu,_ll,_lp,action.Value,lpr):Fit(_ru,_rl,_rp,action.Value,rpr);
                if(openHand>0)
                {
                    var palm=actionLeft?_lp:_rp;
                    palm.rotation=Quaternion.AngleAxis(handTurn,forward)*palm.rotation;
                    OpenGestureHand(actionLeft,openHand);
                }
            }
        }
        internal void Prepare(CabinClipData clip,Transform aircraft,float seconds,Vector3 response,Vector3 coil,float cabinFloor,
            Vector3 stand,Quaternion ropeFacing,Vector3 outside,Vector3 axis,float elapsed)
        {
            Apply(clip,aircraft,seconds,DeploymentSeconds,response,coil,cabinFloor,null);
            if(elapsed>=PreparationSeconds(aircraft,stand))
            {Bind();SlideStart(outside,ropeFacing,axis);return;}
            Vector3 from=aircraft.TransformPoint(_baseFloorLocal);Quaternion initial=Facing(aircraft);
            if(_role==0)
            {
                // From the actual sitter to the sill below his boots, never back into the cabin.
                Vector3 sill=stand+aircraft.right*.06f;
                Rise(sill,ropeFacing,Mathf.Clamp01(elapsed/1.45f));
                if(elapsed<=1.45f)return;
                Vector3 l=_lf.position,r=_rf.position;Quaternion lr=_lf.rotation,rr=_rf.rotation;Save();Bind();
                SlideStart(outside,ropeFacing,axis);
                float transfer=Range(elapsed,4.05f,MainPreparation);Blend(transfer);
                Fit(_lt,_lc,_lf,Vector3.Lerp(l,_lf.position,transfer),Quaternion.Slerp(lr,_lf.rotation,transfer));
                Fit(_rt,_rc,_rf,Vector3.Lerp(r,_rf.position,transfer),Quaternion.Slerp(rr,_rf.rotation,transfer));
                float acquire=Range(elapsed,1.45f,2.5f);
                float tug=.028f*(Envelope(elapsed,2.55f,2.8f,2.9f,3.12f)+Envelope(elapsed,3.22f,3.47f,3.57f,3.8f));
                _leftGrip.Fit(axis+Vector3.up*(.23f-tug),Vector3.up,acquire);
                _rightGrip.Fit(axis+Vector3.up*(.04f-tug),Vector3.up,acquire);
                return;
            }
            if(elapsed<InsertionTiming.Rise){Rise(from,initial,Mathf.Clamp01(elapsed/InsertionTiming.Rise));return;}
            Vector3 direction=Vector3.ProjectOnPlane(stand-from,aircraft.up);
            Quaternion walk=direction.sqrMagnitude>.001f?Quaternion.LookRotation(direction.normalized,aircraft.up):initial;
            float walkSeconds=WalkSeconds(from,stand);
            float walkProgress=Mathf.Clamp01((elapsed-InsertionTiming.Rise)/walkSeconds);
            Quaternion moving=Quaternion.Slerp(initial,walk,Range(elapsed,InsertionTiming.Rise,InsertionTiming.Rise+.6f));
            Bind();Crouch(CabinWalkPath.Centre(from,stand,walkProgress),moving,
                WalkFoot(from,stand,moving,walkProgress,true),WalkFoot(from,stand,moving,walkProgress,false));
            if(elapsed<InsertionTiming.Rise+walkSeconds)return;
            float p=Mathf.Clamp01((elapsed-InsertionTiming.Rise-walkSeconds)/InsertionTiming.Exit);
            Quaternion atDoor=Quaternion.Slerp(walk,ropeFacing,Range(p,0,.28f));
            Bind();Crouch(stand,atDoor,stand-atDoor*Vector3.right*.17f+aircraft.up*SupportedRopePose.BootCentreHeight,
                stand+atDoor*Vector3.right*.17f+aircraft.up*SupportedRopePose.BootCentreHeight);
            Vector3 sl=_lf.position,sr=_rf.position;Quaternion slr=_lf.rotation,srr=_rf.rotation;Save();Bind();
            SlideStart(outside,ropeFacing,axis);float body=Range(p,.65f,1);Blend(body);
            Fit(_lt,_lc,_lf,Vector3.Lerp(sl,_lf.position,body),Quaternion.Slerp(slr,_lf.rotation,body));
            Fit(_rt,_rc,_rf,Vector3.Lerp(sr,_rf.position,body),Quaternion.Slerp(srr,_rf.rotation,body));
            _leftGrip.Fit(axis+Vector3.up*.23f,Vector3.up,Range(p,.1f,.65f));
            _rightGrip.Fit(axis+Vector3.up*.04f,Vector3.up,Range(p,.1f,.65f));
        }
    }
}
