using System;

namespace UH60FastRopeInsertion
{
    // Data-only policy, shared by the native pre-raid selector and the actual raid-start gate.
    public enum DeploymentMode { Helicopter, Standard }

    internal enum InsertionAvailability
    {
        Available, Disabled, AutomaticDisabled, Scav, Transition, NoLocation, Enclosed, Unsupported, HighRise
    }

    internal static class DeploymentPolicy
    {
        internal static InsertionAvailability Availability(bool enabled, bool automatic, bool scav,
            bool transition, string location, bool hasSector)
        {
            if (!enabled) return InsertionAvailability.Disabled;
            if (!automatic) return InsertionAvailability.AutomaticDisabled;
            if (scav) return InsertionAvailability.Scav;
            if (transition) return InsertionAvailability.Transition;
            if (string.IsNullOrWhiteSpace(location)) return InsertionAvailability.NoLocation;
            string id = location.Trim();
            if (id.Equals("sandbox", StringComparison.OrdinalIgnoreCase)
                || id.Equals("sandbox_high", StringComparison.OrdinalIgnoreCase)) return InsertionAvailability.HighRise;
            if (id.StartsWith("factory4", StringComparison.OrdinalIgnoreCase)
                || id.Equals("laboratory", StringComparison.OrdinalIgnoreCase)) return InsertionAvailability.Enclosed;
            if (!hasSector || id.Equals("terminal", StringComparison.OrdinalIgnoreCase)) return InsertionAvailability.Unsupported;
            return InsertionAvailability.Available;
        }

        internal static bool ShouldInsert(DeploymentMode requested, InsertionAvailability availability)
        {
            return requested == DeploymentMode.Helicopter && availability == InsertionAvailability.Available;
        }

        internal static string Description(InsertionAvailability availability, DeploymentMode requested)
        {
            switch (availability)
            {
                case InsertionAvailability.Disabled:
                case InsertionAvailability.AutomaticDisabled: return UiText.Get("Helicopter insertion is currently unavailable.");
                case InsertionAvailability.Scav: return UiText.Get("Scavs enter on foot.");
                case InsertionAvailability.Transition: return UiText.Get("Continuing from the neighbouring area.");
                case InsertionAvailability.NoLocation: return UiText.Get("Select an arrival area.");
                case InsertionAvailability.HighRise: return UiText.Get("Ground Zero: helicopter insertion is unavailable among high-rise buildings.");
                case InsertionAvailability.Enclosed:
                case InsertionAvailability.Unsupported: return UiText.Get("Only ground entry is available in this area.");
                default: return requested == DeploymentMode.Helicopter
                    ? UiText.Get("UH-60. Squad fast-rope insertion.")
                    : UiText.Get("Start the raid from a ground position.");
            }
        }
    }
}
