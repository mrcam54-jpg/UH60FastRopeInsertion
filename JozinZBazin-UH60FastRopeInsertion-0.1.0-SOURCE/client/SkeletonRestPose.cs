using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace UH60FastRopeInsertion
{
    // Skin inverse binds are in the CLOTHING IMPORTER'S coordinates, not the pawn's
    // coordinates (USEC/BEAR tops are ~79 degrees away from actor-up). Never infer
    // the pawn rest pose from renderer.localToWorldMatrix * mesh.bindposes.Inverse.
    // Read the installed game's original skeleton instead; only value types are cached.
    internal static class SkeletonRestPose
    {
        internal struct Bone
        {
            internal Vector3 Position, Scale;
            internal Quaternion Rotation;
        }

        private const string AssetPath = "assets/content/characters/character/skeleton.fbx";
        private static Dictionary<string, Bone> _bones;

        internal static IReadOnlyDictionary<string, Bone> Read()
        {
            if (_bones != null) return _bones;
            AssetBundle bundle = null;
            bool owned = false;
            try
            {
                // Reuse the game's bundle. Loading another copy of an already loaded
                // Unity bundle is illegal, and a borrowed bundle must NEVER be unloaded.
                foreach (var loaded in AssetBundle.GetAllLoadedAssetBundles())
                    if (loaded != null && loaded.GetAllAssetNames().Any(n =>
                        string.Equals(n, AssetPath, StringComparison.OrdinalIgnoreCase)))
                    { bundle = loaded; break; }
                if (bundle == null)
                {
                    string path = Path.Combine(Application.dataPath,
                        "StreamingAssets/Windows/assets/content/characters/character/skeleton.bundle");
                    if (!File.Exists(path)) throw new FileNotFoundException("Installed EFT rest skeleton is missing.", path);
                    bundle = AssetBundle.LoadFromFile(path);
                    owned = true;
                }
                if (bundle == null) throw new InvalidOperationException("Cannot read installed EFT rest skeleton; refusing an animated-pose bind fallback.");
                // Skeleton.fbx is a multi-object FBX: Skeleton, Root_Joint and Avatar
                // share a container path. A singular typed LoadAsset is not a reliable
                // hierarchy selector. Preserve the name Unity actually reports, then
                // inspect the GameObject subassets without instantiating/activating them.
                var names = bundle.GetAllAssetNames();
                var paths = names.Where(n => string.Equals(n, AssetPath, StringComparison.OrdinalIgnoreCase))
                    .Distinct(StringComparer.Ordinal).ToArray();
                if (paths.Length == 0) throw new InvalidOperationException("EFT rest skeleton bundle has no FBX entry: "
                    + bundle.name + "; available=" + string.Join(", ", names.Take(8)));
                var diagnostics = new List<string>();
                var seen = new HashSet<Transform>();
                var valid = new List<Dictionary<string, Bone>>();
                Plugin.Log.LogInfo("EFT rest skeleton acquisition: bundle=" + bundle.name
                    + "; borrowed=" + !owned + "; exactPaths=" + string.Join(", ", paths));
                foreach (string path in paths)
                    Inspect(bundle.LoadAssetWithSubAssets<GameObject>(path), "subassets:" + path, seen, valid, diagnostics);
                if (valid.Count == 0)
                {
                    // Some Unity bundles expose an FBX object only through the bundle
                    // object inventory. This is the SAME installed skeleton.bundle, not
                    // a substitute rig or an animated/clothing-pose fallback.
                    Inspect(bundle.LoadAllAssets<GameObject>(), "bundle inventory", seen, valid, diagnostics);
                }
                string detail = string.Join("; ", diagnostics);
                Plugin.Log.LogInfo("EFT rest skeleton candidates: " + detail);
                if (valid.Count == 0) throw new InvalidOperationException("EFT rest skeleton acquisition failed: "
                    + detail + "; refusing animated/clothing bind fallback.");
                var result = valid[0];
                if (valid.Skip(1).Any(other => !SameRest(result, other)))
                    throw new InvalidOperationException("EFT rest skeleton has conflicting complete hierarchies: " + detail);
                _bones = result;
                Plugin.Log.LogInfo("EFT canonical rest skeleton: " + result.Count
                    + " transforms; borrowedBundle=" + !owned + "; no clothing inverse-bind inference.");
                return _bones;
            }
            finally
            {
                // Own load contains only the tiny skeleton prefab/avatar. Cache no Unity
                // objects, and do not destroy shared assets or unload a borrowed bundle.
                if (owned && bundle != null) bundle.Unload(false);
            }
        }

        private static void Inspect(GameObject[] objects, string source, HashSet<Transform> seen,
            List<Dictionary<string, Bone>> valid, List<string> diagnostics)
        {
            diagnostics.Add(source + " objects=" + (objects == null ? "null" : objects.Length.ToString()));
            if (objects == null) return;
            foreach (var obj in objects)
            {
                if (obj == null) { diagnostics.Add("null GameObject"); continue; }
                var transforms = obj.GetComponentsInChildren<Transform>(true);
                var joints = transforms.Where(t => t.name == "Root_Joint").ToArray();
                diagnostics.Add(obj.name + " transforms=" + transforms.Length + " Root_Joint=" + joints.Length);
                foreach (var joint in joints)
                {
                    if (!seen.Add(joint)) continue;
                    var result = new Dictionary<string, Bone>(StringComparer.Ordinal);
                    try { Capture(joint, "Root_Joint", result); }
                    catch (ArgumentException) { diagnostics.Add("duplicate bone path; rejected"); continue; }
                    string[] required = { "Base HumanPelvis", "Base HumanHead", "Base HumanLPalm", "Base HumanRPalm" };
                    if (result.Count < 65 || required.Any(name => !result.Keys.Any(p =>
                        p.EndsWith("/" + name, StringComparison.Ordinal))))
                    { diagnostics.Add("incomplete Root_Joint bones=" + result.Count + "; rejected"); continue; }
                    diagnostics.Add("complete Root_Joint bones=" + result.Count);
                    valid.Add(result);
                }
            }
        }

        private static bool SameRest(Dictionary<string, Bone> a, Dictionary<string, Bone> b)
        {
            return a.Count == b.Count && a.All(pair => b.TryGetValue(pair.Key, out Bone bone)
                && pair.Value.Position.Equals(bone.Position) && pair.Value.Rotation.Equals(bone.Rotation)
                && pair.Value.Scale.Equals(bone.Scale));
        }

        private static void Capture(Transform t, string path, Dictionary<string, Bone> result)
        {
            result.Add(path, new Bone { Position = t.localPosition, Rotation = t.localRotation, Scale = t.localScale });
            foreach (Transform child in t) Capture(child, path + "/" + child.name, result);
        }

        internal static int Apply(Transform joint, Transform presentationRoot)
        {
            if (joint == null) throw new InvalidOperationException("Target visual clone has no Root_Joint.");
            if (presentationRoot == null || !joint.IsChildOf(presentationRoot))
                throw new InvalidOperationException("Rest reset requires a private presentation hierarchy.");
            var data = Read();
            for (Transform parent = joint.parent; parent != null && parent != presentationRoot; parent = parent.parent)
            {
                parent.localPosition = Vector3.zero;
                parent.localRotation = Quaternion.identity;
                parent.localScale = Vector3.one;
            }
            return ApplyBranch(joint, "Root_Joint", data);
        }

        private static int ApplyBranch(Transform t, string path, IReadOnlyDictionary<string, Bone> data)
        {
            int count = 0;
            if (data.TryGetValue(path, out Bone bone))
            {
                t.localPosition = bone.Position;
                t.localRotation = bone.Rotation;
                t.localScale = bone.Scale;
                count = 1;
            }
            foreach (Transform child in t) count += ApplyBranch(child, path + "/" + child.name, data);
            return count;
        }
    }
}
