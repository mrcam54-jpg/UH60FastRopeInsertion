using System;
using System.Collections.Generic;
using EFT;
using EFT.InputSystem;
using HarmonyLib;
using UnityEngine;

namespace UH60FastRopeInsertion
{
    internal static class SceneInputGuard
    {
        internal static bool Owned { get; private set; }
        internal static bool UiOpen => Cursor.visible || Cursor.lockState != CursorLockMode.Locked;
        internal static void Begin() { Owned = true; }
        internal static void End() { Owned = false; }

        internal static void Filter(List<ECommand> commands, float[] axes)
        {
            if (!Owned || PhotoModeBridge.OwnsCamera) return;
            commands?.RemoveAll(command => !PresentationPolicy.AllowsCommand(command.ToString()));
            if (axes != null) Array.Clear(axes, 0, axes.Length);
        }
    }

    [HarmonyPatch(typeof(InputManager), nameof(InputManager.DispatchInput),
        new[] { typeof(List<ECommand>), typeof(float[]) })]
    internal static class SceneDispatchInputPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Prefix(List<ECommand> commandsList, float[] axesList)
            => SceneInputGuard.Filter(commandsList, axesList);
    }
}
