using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace UH60FastRopeInsertion
{
    internal sealed partial class InsertionController
    {
        private CabinClipData _editorClip;
        private bool _editorLoadAttempted;
        private bool _userChoreography;
        private void ScheduleUserActors()
        {
            if(!_userChoreography)return;
            var preparation=new float[5];
            foreach(var actor in _actors)preparation[actor.Double.Index]=actor.Double.UserPreparationSeconds(_aircraft.RootTransform,_aircraft.GetDoorStandPoint(actor.Side));
            var starts=InsertionTiming.UserStarts(Plugin.DescentDuration.Value,preparation);
            foreach(var actor in _actors)
            {
                actor.StartTime=starts[actor.Double.Index];actor.Double.DescentDelay=actor.StartTime;
                Plugin.Log.LogInfo("User cabin role "+(actor.Double.Index+1)+": preparation="+preparation[actor.Double.Index].ToString("F2")
                    +"s; slide at="+actor.StartTime.ToString("F2")+"s after payout; lane="+actor.Side);
            }
        }
        private bool TryUserPreparation(ActorSlot actor,float local)
        {
            // Explicit gate: this adapter never owns a sliding/landing actor.
            if(!_userChoreography||local>=0)return false;
            float preparation=actor.Double.UserPreparationSeconds(_aircraft.RootTransform,_aircraft.GetDoorStandPoint(actor.Side));
            Vector3 coil=UserCabinPerformance.CoilCentre(_aircraft.RootTransform,_aircraft.GetCabinRopeBundlePoint(actor.Side),
                actor.Side<0?_leftGround:_rightGround,UserCabinPerformance.ReleaseSeconds(actor.Side),actor.Side);
            if(local< -preparation)
                actor.Double.ApplyUserCabin(_editorClip,_aircraft.RootTransform,_sceneTime-_flightSceneStart,
                    UserCabinPerformance.DeploymentSeconds,coil,_aircraft.CabinFloor,null);
            else
            {
                SteelRope rope=actor.Side<0?_leftRope:_rightRope;Quaternion facing=_aircraft.GetRopeFacing(actor.Side);
                Vector3 axis=rope.EvaluateDescent(0)-Vector3.up*.45f;
                Vector3 outside=axis-Vector3.up*SupportedRopePose.ContactHeight-facing*Vector3.forward*DescentBodyOffset;
                actor.Double.PrepareUserExit(_editorClip,_aircraft.RootTransform,_sceneTime-_flightSceneStart,coil,_aircraft.CabinFloor,
                    _aircraft.GetDoorStandPoint(actor.Side),facing,outside,axis,local+preparation);
            }
            return true;
        }
        private bool PoseUserCabin(float phase,float progress)
        {
            if(!_userChoreography)return false;
            if(_editorClip==null)ReloadCabinClip();
            if(_editorClip==null)throw new InvalidOperationException("User cabin choreography selected, but exported poses did not load. Built-in animation is invalid.");
            float seconds=Math.Max(0,phase-_flightSceneStart);
            float deployment=_state==SceneState.DeployingRopes?progress*UserCabinPerformance.DeploymentSeconds:-1;
            foreach(var actor in _actors)
            {
                actor.Double.SetCabinResponse(_cabinResponse,_aircraft.RootTransform.rotation);
                Vector3 coil=UserCabinPerformance.CoilCentre(_aircraft.RootTransform,_aircraft.GetCabinRopeBundlePoint(actor.Side),
                    actor.Side<0?_leftGround:_rightGround,Mathf.Clamp(deployment,0,UserCabinPerformance.ReleaseSeconds(actor.Side)),actor.Side);
                Vector3? partner=actor.Double.Index==1?_actors[0].Double.UserShoulder:
                    actor.Double.Index==3&&_actors.Count>1?(Vector3?)_actors[1].Double.UserHead:null;
                actor.Double.ApplyUserCabin(_editorClip,_aircraft.RootTransform,seconds,deployment,coil,_aircraft.CabinFloor,partner);
            }
            return true;
        }
        private void ReloadCabinClip()
        {
            _editorLoadAttempted=true;
            try
            {
                var replacement=PlayerCabinClip.Read();
                foreach(var actor in _actors)actor.Double.ValidateCabinClip(replacement);
                _editorClip=replacement;
                Plugin.Log.LogInfo("Built-in cabin animation loaded: "+replacement.FrameCount+" frames; "+replacement.Roles.Count+" roles. Descent is excluded.");
            }
            catch(Exception ex)
            {Plugin.Log.LogWarning("Built-in cabin animation failed: "+ex.Message);}
        }
        private void ApplyCabinAuthoring()
        {
            if(true)
            {
                if(!_editorLoadAttempted)ReloadCabinClip();
                if(_editorClip!=null)
                {
                    // Finish overlay before native acquisition. NO calls from UpdateActorsDuringDescent.
                    try
                    {
                        // Validate every new actor before mutating ANY pose; invalid data never fails the scene.
                        foreach(var actor in _actors)actor.Double.ValidateCabinClip(_editorClip);
                        float weight=_state==SceneState.DeployingRopes?1-SmootherStep(Math.Min(1,_throwProgress/.10f)):1;
                        foreach(var actor in _actors)
                            actor.Double.ApplyCabinClip(_editorClip,_aircraft.RootTransform,_sceneTime-_flightSceneStart,weight);
                    }
                    catch(Exception ex)
                    {
                        _editorClip=null;
                        Plugin.Log.LogWarning("Cabin overlay disabled; native poses retained: "+ex.Message);
                    }
                }
            }
            
        }
    }
}
