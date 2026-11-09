using UnityEngine;

namespace UH60FastRopeInsertion
{
    internal sealed partial class Uh60Visual
    {
        // Read the EXISTING Tactical Services animation, never replace or reshape it. Sweep the
        // rotor envelope along its sampled centreline before any actor is parked or moved.
        internal bool ValidateChosenApproach(Vector3 landing,Quaternion rotation,out string reason)
        {
            reason="authored approach corridor clear";
            bool events=_animator.fireEvents;
            try
            {
                _animator.fireEvents=false;
                _animator.enabled=true;
                _root.transform.SetPositionAndRotation(landing,rotation);
                _animator.Rebind();
                Vector3 previous=Vector3.zero;
                for(int i=0;i<=96;i++)
                {
                    _animator.Play("Movement.Flying",0,i==96?.99999f:i/96f);
                    _animator.Update(0f);
                    Vector3 position=_motion.position;
                    if(i>0 && !LandingGeometry.ClearCapsule(previous,position,8.6f,out reason))
                    { reason="Tactical Services approach blocked (sample "+i+"): "+reason;return false; }
                    previous=position;
                }
                return true;
            }
            finally
            {
                _animator.fireEvents=events;
                PreviewTacticalHover(landing,rotation);
            }
        }
    }
}
