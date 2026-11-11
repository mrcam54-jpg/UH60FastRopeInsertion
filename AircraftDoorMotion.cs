using UnityEngine;

namespace UH60FastRopeInsertion
{
    // The source Icebreaker pivot is ALREADY OPEN, not the closed-door reference.
    // Z-up model coordinates: local Y is the longitudinal rail, local X is outboard.
    internal sealed class AircraftDoorMotion
    {
        internal const float Duration=2.6f,Travel=1.64f,SeatDepth=.065f;
        private float _from=1,_target=1,_elapsed=Duration;
        internal float OpenAmount {get;private set;}=1;
        internal void Snap(float open){OpenAmount=_from=_target=Mathf.Clamp01(open);_elapsed=Duration;}
        internal void SetTarget(float open)
        {
            open=Mathf.Clamp01(open);
            if(open==_target)return;
            _from=OpenAmount;_target=open;_elapsed=0;
        }
        internal void Tick(float delta)
        {
            if(float.IsNaN(delta)||float.IsInfinity(delta)||delta<0)return;
            _elapsed=Mathf.Min(Duration,_elapsed+delta);
            OpenAmount=Mathf.Lerp(_from,_target,_elapsed/Duration);
        }
        private static float Smooth(float p){p=Mathf.Clamp01(p);return p*p*p*(p*(p*6-15)+10);}
        internal static Vector3 Position(Vector3 authoredOpen,int side,float open)
        {
            // Opening: unlock outboard first, then slide aft. Closing traverses exactly the
            // reverse rail: slow at both ends, then the final inward latch/seal seating.
            float unlock=Smooth(open/.18f),slide=Smooth((open-.14f)/.86f);
            return authoredOpen+new Vector3(-side*SeatDepth*(1-unlock),Travel*(1-slide),0);
        }
    }
}
