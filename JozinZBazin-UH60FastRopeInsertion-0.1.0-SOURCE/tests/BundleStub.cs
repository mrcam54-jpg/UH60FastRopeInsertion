using System;
using System.Collections.Generic;
using System.Linq;

namespace UnityEngine
{
    // Contract-only fake. Does NOT load Unity, a game bundle or the user's game.
    internal sealed class AssetBundle
    {
        internal static readonly List<AssetBundle> Loaded = new List<AssetBundle>();
        internal static AssetBundle Next;
        internal static Exception LoadError;
        internal static int Loads;
        internal static string LastPath;
        internal readonly HashSet<string> Assets = new HashSet<string>();
        internal int Unloads;
        public bool Contains(string asset) => Assets.Contains(asset);
        public static IEnumerable<AssetBundle> GetAllLoadedAssetBundles() => Loaded.ToArray();
        public static AssetBundle LoadFromFile(string path)
        {
            Loads++; LastPath = path;
            if (LoadError != null) throw LoadError;
            if (Next != null) Loaded.Add(Next);
            return Next;
        }
        public void Unload(bool all)
        {
            if (all) throw new Exception("Do not destroy shared assets.");
            Unloads++; Loaded.Remove(this);
        }
        internal static void Reset()
        { Loaded.Clear(); Next = null; LoadError = null; Loads = 0; LastPath = null; }
    }
}
