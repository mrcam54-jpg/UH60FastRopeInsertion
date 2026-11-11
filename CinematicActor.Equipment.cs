using System;
using System.Collections.Generic;
using System.Linq;
using EFT.InventoryLogic;
using UnityEngine;

namespace UH60FastRopeInsertion
{
    internal sealed partial class CinematicActor
    {
        private readonly HashSet<Renderer> _sourceHeadRenderers = new HashSet<Renderer>();
        private readonly HashSet<Renderer> _sourceBackpackRenderers = new HashSet<Renderer>();
        private readonly HashSet<Renderer> _headRenderers = new HashSet<Renderer>();
        private readonly HashSet<Transform> _movingHeadTransforms = new HashSet<Transform>();

        private void ClassifyEquipmentRenderers()
        {
            var body = SourcePlayer.PlayerBody;
            if (body == null) return;
            foreach (var slot in body.SlotViews.GetValuesEnumerator())
            {
                bool head = slot.EquipmentSlot == EquipmentSlot.Headwear || slot.EquipmentSlot == EquipmentSlot.FaceCover
                    || slot.EquipmentSlot == EquipmentSlot.Eyewear || slot.EquipmentSlot == EquipmentSlot.Earpiece;
                bool backpack = slot.EquipmentSlot == EquipmentSlot.Backpack;
                if (!head && !backpack) continue;
                foreach (Renderer renderer in slot._renderers ?? new Renderer[0])
                    if (renderer != null) (head ? _sourceHeadRenderers : _sourceBackpackRenderers).Add(renderer);
                if (slot.Model == null) continue;
                foreach (var renderer in slot.Model.GetComponentsInChildren<Renderer>(true))
                    (head ? _sourceHeadRenderers : _sourceBackpackRenderers).Add(renderer);
                if (head)
                    foreach (var transform in slot.Model.GetComponentsInChildren<Transform>(true))
                        if (!transform.name.StartsWith("Base Human", StringComparison.Ordinal) && transform.name != "Root_Joint")
                            _movingHeadTransforms.Add(transform);
            }
            foreach (var pair in body.BodySkins)
                if (pair.Key.ToString() == "Head" && pair.Value != null)
                    foreach (var renderer in pair.Value.GetComponentsInChildren<Renderer>(true)) _sourceHeadRenderers.Add(renderer);
        }

        private void RegisterEquipmentRenderer(Renderer source, Renderer clone)
        {
            bool head = _sourceHeadRenderers.Contains(source);
            // Slot membership includes nested/modded glasses and NVG; bone ancestry also
            // catches head renderers not exposed by an item's top-level renderer list.
            var skin = source as SkinnedMeshRenderer;
            if (!head && skin != null && skin.bones.Any(b => b != null))
                head = skin.bones.Where(b => b != null).All(b => HasHeadAncestor(b));
            if (!head) head = HasHeadAncestor(source.transform);
            if (head) _headRenderers.Add(clone);
        }
        private static bool HasHeadAncestor(Transform node)
        {
            for (var t = node; t != null; t = t.parent)
                if (t.name == "Base HumanHead") return true;
            return false;
        }

        private void RestoreEquipmentBinds()
        {
            int restored = 0;
            var changed = new HashSet<Transform>();
            foreach (var source in _sourceBackpackRenderers.OfType<SkinnedMeshRenderer>())
            {
                if (source.sharedMesh == null) continue;
                var bones = source.bones;
                var binds = source.sharedMesh.bindposes;
                if (bones.Length != binds.Length) continue;
                // Relative inverse binds cancel each clothing importer's coordinate frame.
                // Do NOT infer canonical world rest from renderer.localToWorldMatrix.
                int anchor = Array.FindIndex(bones, b => b != null && b.name.StartsWith("Base HumanSpine", StringComparison.Ordinal));
                if (anchor < 0) anchor = Array.FindIndex(bones, b => b != null && b.name.StartsWith("Base Human", StringComparison.Ordinal));
                if (anchor < 0 || MapTransform(bones[anchor]) == null) continue;
                var frame = MapTransform(bones[anchor]);
                foreach (int i in Enumerable.Range(0, bones.Length).OrderBy(i => bones[i] == null ? 0 : GetDepth(bones[i])))
                {
                    var bone = bones[i];
                    if (bone == null || bone.name == "Root_Joint" || bone.name.StartsWith("Base Human", StringComparison.Ordinal)) continue;
                    var clone = MapTransform(bone);
                    if (clone == null || clone.parent == null || !changed.Add(clone)) continue;
                    Matrix4x4 local = clone.parent.worldToLocalMatrix * frame.localToWorldMatrix * binds[anchor] * binds[i].inverse;
                    Vector3 position = local.GetColumn(3);
                    Quaternion rotation = local.rotation;
                    if (float.IsNaN(position.x) || float.IsNaN(rotation.w) || position.sqrMagnitude > 4f) continue;
                    clone.localPosition = position;
                    clone.localRotation = rotation;
                    restored++;
                }
            }
            Plugin.Log.LogInfo("Actor " + Index + " equipment: head renderers=" + _headRenderers.Count
                + "; backpack renderers=" + _sourceBackpackRenderers.Count + "; secondary relative binds restored=" + restored);
        }

        internal void SyncHeadEquipment()
        {
            // ToggleGoggles moves the real item's pivots. Only gear-local transforms are
            // copied; anatomical pose, source visibility, materials and inventory untouched.
            foreach (var source in _movingHeadTransforms)
            {
                var clone = source != null ? MapTransform(source) : null;
                if (clone == null) continue;
                clone.localPosition = source.localPosition;
                clone.localRotation = source.localRotation;
                clone.localScale = source.localScale;
            }
        }
    }
}
