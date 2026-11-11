using Comfort.Common;
using EFT.Settings;

namespace UH60FastRopeInsertion
{
    internal static class GameLocale
    {
        // The actual EFT language dropdown's settings object. Read-only; no disk access.
        internal static string Read() => Singleton<SettingsManager>.Instance?.Game?.Settings?.Language?.Value;
    }
}
