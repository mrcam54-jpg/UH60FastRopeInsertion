using System;

namespace UH60FastRopeInsertion
{
    // Pure rules shared by runtime and bounded offline contracts.
    internal static class PresentationPolicy
    {
        internal static float Delta(float scaled, float scale, float fixedStep, bool frozen)
        {
            float dt = frozen ? 0f : fixedStep > 0f ? fixedStep * scale : scaled;
            return float.IsNaN(dt) || float.IsInfinity(dt) || dt < 0f ? 0f : dt;
        }
        internal static bool AllowsCommand(string command) => command == "Escape"
            || command == "ToggleInventory" || command == "ToggleGoggles"
            || command == "DisplayTimer" || command == "DisplayTimerAndExits"
            || command == "ToggleTalk" || command == "StopTalk";

        internal static float SlideEnvelope(float elapsed, float duration)
        {
            if (duration <= 0f || elapsed < 0f || elapsed >= duration) return 0f;
            float attack = Math.Min(1f, elapsed / .085f);
            float release = Math.Min(1f, (duration - elapsed) / .16f);
            return attack * release;
        }
    }
}
