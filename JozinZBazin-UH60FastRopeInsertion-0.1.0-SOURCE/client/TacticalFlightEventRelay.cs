using UnityEngine;

namespace UH60FastRopeInsertion
{
    // The imported Tactical Services clips contain these AnimationEvent names. The
    // original network/service behaviour is deliberately absent; this local relay
    // exposes only the two visual milestones required by the insertion sequence.
    internal sealed class TacticalFlightEventRelay : MonoBehaviour
    {
        internal bool Arrived { get; private set; }
        internal bool Left { get; private set; }

        internal void ResetState()
        {
            Arrived = false;
            Left = false;
        }

        private void OnHelicopterArrive()
        {
            Arrived = true;
            Plugin.Log.LogInfo("Tactical Services animation event: helicopter arrived at hover.");
        }

        private void OnHelicopterLeft()
        {
            Left = true;
            Plugin.Log.LogInfo("Tactical Services animation event: helicopter left the scene.");
        }
    }
}
