using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace UH60FastRopeInsertion
{
    // Reads a separately installed dependency. Never distributes or copies its bundle.
    internal sealed class InstalledFlightAsset : IDisposable
    {
        internal AssetBundle Bundle { get; private set; }
        private readonly bool _ownsBundle;

        private InstalledFlightAsset(AssetBundle bundle, bool ownsBundle)
        { Bundle = bundle; _ownsBundle = ownsBundle; }

        internal static InstalledFlightAsset Open(string pluginsRoot, string prefabName)
        {
            string path = Path.Combine(pluginsRoot, "Tylevo.TacticalServicesControl", "assets",
                "content", "vehicles", "uh60_blackhawk.bundle");
            if (!File.Exists(path))
                throw new FileNotFoundException("Install the complete Tactical Services Control dependency; UH60 does not redistribute its assets.", path);
            // TSC may already own this bundle. Reuse it rather than attempting a second
            // Unity load with the same internal identity; do not unload a borrowed handle.
            AssetBundle existing = AssetBundle.GetAllLoadedAssetBundles()
                .FirstOrDefault(bundle => bundle != null && bundle.Contains(prefabName));
            if (existing != null) return new InstalledFlightAsset(existing, false);
            AssetBundle loaded = AssetBundle.LoadFromFile(path);
            if (loaded == null)
                throw new InvalidOperationException("Unity could not read the installed Tactical Services UH60 bundle.");
            if (!loaded.Contains(prefabName))
            {
                loaded.Unload(false);
                throw new InvalidOperationException("The installed Tactical Services bundle has no supported UH60 prefab.");
            }
            return new InstalledFlightAsset(loaded, true);
        }

        public void Dispose()
        {
            if (Bundle == null) return;
            if (_ownsBundle) Bundle.Unload(false);
            Bundle = null;
        }
    }
}
