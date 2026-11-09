using System;
using System.Collections.Generic;
using System.Reflection;
using EFT;
using UnityEngine;

namespace UH60FastRopeInsertion
{
    internal sealed class LiveActorState : IDisposable
    {
        private readonly List<Renderer> _hidden = new List<Renderer>();
        private static readonly HashSet<Player> FrozenPlayers = new HashSet<Player>();
        private readonly Dictionary<Renderer, bool> _renderEnabled = new Dictionary<Renderer, bool>();
        private bool _released;
        private readonly Vector3 _originalPosition;
        private readonly Quaternion _originalRotation;

        internal readonly SquadMember Member;
        internal Vector3 LandingPosition;
        internal Quaternion LandingRotation;

        internal Player Player
        {
            get { return Member.Player; }
        }

        internal bool IsMain
        {
            get { return Member.Follower == null; }
        }

        internal LiveActorState(SquadMember member)
        {
            Member = member;
            _originalPosition = member.Player != null ? member.Player.Position : Vector3.zero;
            _originalRotation = member.Player != null ? member.Player.Transform.rotation : Quaternion.identity;
        }

        internal void FreezeAndHide()
        {
            if (Player == null || _released) return;
            FrozenPlayers.Add(Player);
            TryStopBot(Member.Bot);
            // HideWeapon is NOT a rendering toggle in EFT: it asynchronously switches to empty
            // hands and sets buffer-zone state. Freezing during that swap stranded every gun in
            // test8.1. Preserve the exact live hands/inventory state; hide their renderers only.
            Renderer[] renderers = Player.gameObject.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null) continue;
                _renderEnabled[renderer] = renderer.enabled;
                if (renderer.forceRenderingOff) continue;
                renderer.forceRenderingOff = true;
                _hidden.Add(renderer);
            }
            // Keep ALL supporting colliders intact. EFT bots use a custom collider/controller,
            // not necessarily Unity CharacterController. Disabling it lets their body fall away.
            // BotOwner.enabled does not stop its externally called UpdateManual either.
            Plugin.Log.LogInfo("Live actor parked with movement/AI ownership: " + Player.Profile?.Nickname
                + " at " + _originalPosition + "; colliders kept; body=" + BodyHeight().ToString("F3"));
        }

        internal void HoldOffstage(Vector3 position)
        {
            if (Player == null || _released) return;
            // Each real pawn stays at its own original valid spawn, never stacked on the LZ.
            if ((Player.Position - _originalPosition).sqrMagnitude > 0.0025f) Player.Teleport(_originalPosition, false);
        }

        internal void Release(Vector3 position, Quaternion rotation)
        {
            if (Player == null || _released) return;
            try
            {
                TryStopBot(Member.Bot);
                Player.Teleport(position, false);
                if (Player.MovementContext != null)
                {
                    Player.MovementContext.Rotation = new Vector2(rotation.eulerAngles.y, 0f);
                    Player.MovementContext.CachedRotation = new Vector2(rotation.eulerAngles.y, 0f);
                    Player.MovementContext.ResetFlying();
                }
            }
            finally { Restore(); _released = true; }
            Plugin.Log.LogInfo("Live actor handed off: " + Player.Profile?.Nickname + " at " + Player.Position
                + "; body=" + BodyHeight().ToString("F3") + "; alive=" + Player.HealthController?.IsAlive
                + "; movement/AI resumed, hidden renderers restored.");
        }

        internal static bool IsFrozen(Player player) { return player != null && FrozenPlayers.Contains(player); }

        internal float BodyHeight()
        {
            var pelvis = Player?.PlayerBones?.Pelvis;
            return pelvis != null ? Vector3.Dot(pelvis.position - Player.Position, Player.Transform.up) : float.NaN;
        }

        private void Restore()
        {
            FrozenPlayers.Remove(Player);
            foreach (Renderer renderer in _hidden)
            {
                if (renderer != null) renderer.forceRenderingOff = false;
            }
            _hidden.Clear();
            foreach (var entry in _renderEnabled) if (entry.Key != null) entry.Key.enabled = entry.Value;
            _renderEnabled.Clear();
        }

        private static void TryStopBot(object bot)
        {
            if (bot == null) return;
            try
            {
                object mover = bot.GetType().GetProperty("Mover",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(bot, null);
                mover?.GetType().GetMethod("Stop", Type.EmptyTypes)?.Invoke(mover, null);
            }
            catch (Exception)
            {
                // Optional PIT integration: a changed mover surface must not break cleanup.
            }
        }

        public void Dispose()
        {
            if (!_released)
            {
                try
                {
                    if (Player != null)
                    {
                        Player.Teleport(_originalPosition, false);
                        if (Player.MovementContext != null)
                        {
                            Player.MovementContext.Rotation = new Vector2(_originalRotation.eulerAngles.y, 0f);
                            Player.MovementContext.ResetFlying();
                        }
                    }
                }
                finally { Restore(); _released = true; }
            }
        }
    }
}
