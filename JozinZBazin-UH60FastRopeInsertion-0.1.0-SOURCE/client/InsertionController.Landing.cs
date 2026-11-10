using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UH60FastRopeInsertion
{
    internal sealed partial class InsertionController
    {
        private bool _landingPlanned;
        private Quaternion _plannedRouteRotation;

        private IEnumerator PlanLanding(bool manual)
        {
            _landingPlanned=false;
            IReadOnlyList<LandingSite> candidates=!manual && LandingRequest.RandomFallback
                ?LandingRequest.RandomSites:new[]{_landingSite};
            foreach(var candidate in candidates)
            {
                if(!IsRaidValid())yield break;
                if(candidate==null)continue;
                _landingSite=candidate;
                _sector=candidate.Sector;
                SetState(SceneState.ValidatingSector,"checking LZ "+candidate.Id);
                Vector3 target=_sector.Seed;
                if(candidate.IsMarked && !LandingGeometry.TryMarkedSurface(target,out target,out _validation))
                {
                    Plugin.Log.LogWarning("LZ rejected before flight: "+candidate.Id+"; surface: "+_validation);
                    yield return null;continue;
                }
                float[] bearings=candidate.IsMarked?LandingPlacement.Bearings(_sector.ApproachBearing)
                    :new[]{_sector.ApproachBearing};
                foreach(float bearing in bearings)
                {
                    // Bounded work per frame, before actor ownership/camera takeover. No busy wait.
                    yield return null;
                    if(!IsRaidValid())yield break;
                    Quaternion rotation=Quaternion.LookRotation(YawDirection(bearing),Vector3.up);
                    _aircraft.PreviewTacticalHover(target,rotation);
                    _hoverLeftOffset=_aircraft.GetDoorAnchor(-1)-target;
                    _hoverRightOffset=_aircraft.GetDoorAnchor(1)-target;
                    _previewLeftFacing=_aircraft.GetRopeFacing(-1);
                    _previewRightFacing=_aircraft.GetRopeFacing(1);
                    bool valid;
                    if(candidate.IsMarked)
                    {
                        _landing=LandingPlacement.RootForTouchdown(target,_hoverLeftOffset,_previewLeftFacing,DescentBodyOffset);
                        valid=HasSquadNavArea(target) && HasRotorClearance(_landing,_sector.HoverHeight);
                        _validation=valid?"marked X/Z and squad/rotor clear":"marked point lacks squad or rotor clearance";
                        if(valid)valid=ValidateDoorLanes(_landing+_hoverLeftOffset,_landing+_hoverRightOffset,
                            _previewLeftFacing,_previewRightFacing,out _validation,target.y);
                    }
                    else valid=TryFindSafePoint(_sector,out _landing,out _validation);
                    if(valid && (candidate.IsMarked || LandingRequest.Current!=null))
                        valid=_aircraft.ValidateChosenApproach(_landing,rotation,out _validation);
                    if(!valid)
                    {
                        Plugin.Log.LogWarning("LZ rejected before flight: "+candidate.Id+"; bearing="+bearing+"; "+_validation);
                        continue;
                    }
                    _plannedRouteRotation=rotation;
                    _landingPlanned=true;
                    LandingRequest.AcceptRandomSite(candidate);
                    InsertionApi.BeginAttempt(candidate.Id,manual);
                    Plugin.Log.LogInfo("LZ accepted: "+candidate.Id+"; randomFallback="+LandingRequest.RandomFallback
                        +"; annotatedTarget="+target+"; aircraftPlacement="+_landing+"; leftGround="+_leftGround
                        +"; bearing="+bearing+"; "+_validation);
                    yield break;
                }
                // Only a random NO-INTEGRATION fallback can try another marked site.
                // Explicit Infil selections have exactly one candidate and are never silently moved.
            }
        }
    }
}
