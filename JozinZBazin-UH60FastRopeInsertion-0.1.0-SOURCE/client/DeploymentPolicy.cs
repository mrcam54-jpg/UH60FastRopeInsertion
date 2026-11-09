using System;

namespace UH60FastRopeInsertion
{
    // Data-only policy, shared by the native pre-raid selector and the actual raid-start gate.
    public enum DeploymentMode { Helicopter, Standard }

    internal enum InsertionAvailability
    {
        Available, Disabled, AutomaticDisabled, Scav, Transition, NoLocation, Enclosed, Unsupported
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
                case InsertionAvailability.AutomaticDisabled: return "Вертолётная высадка сейчас недоступна.";
                case InsertionAvailability.Scav: return "Для Дикого — наземный вход.";
                case InsertionAvailability.Transition: return "Продолжение пути из соседнего района.";
                case InsertionAvailability.NoLocation: return "Выберите район прибытия.";
                case InsertionAvailability.Enclosed:
                case InsertionAvailability.Unsupported: return "В этом районе доступен наземный вход.";
                default: return requested == DeploymentMode.Helicopter
                    ? "UH-60. Высадка отряда по тросам."
                    : "Начало рейда с наземной позиции.";
            }
        }
    }
}
