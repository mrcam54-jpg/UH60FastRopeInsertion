using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.ExceptionServices;
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
        private readonly object _mover;
        private readonly MethodInfo _moverTeleport;
        private readonly MethodInfo _followerGrace;
        private BotLandingCache _landingCache;
        private float _handoffElapsed;
        private int _handoffObserved;

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
            _mover = member.Bot?.GetType().GetProperty("Mover",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(member.Bot, null);
            _moverTeleport = _mover?.GetType().GetMethod("Teleport", new[] { typeof(Vector3) });
            _followerGrace = member.Follower?.GetType().GetMethod("BeginTeleportGrace", new[] { typeof(Vector3) });
            if(_mover!=null)_landingCache=BotLandingCache.Bind(_mover);
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
                TransferPosition(position);
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

        private void TransferPosition(Vector3 position)
        {
            // BotMover owns the native navigation anchor and relocation permission. Moving
            // only Player lets the next mover tick snap the bot to its previous linked path.
            if (_moverTeleport != null)
            {
                if(!BotLandingGeometry.TryAnchor(position,out Vector3 navAnchor))
                    throw new InvalidOperationException("No same-floor local bot NavMesh at touchdown: "+position);
                try { _moverTeleport.Invoke(_mover, new object[] { position }); }
                catch (TargetInvocationException ex) when (ex.InnerException != null)
                { ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
                // Stop again AFTER teleport so the path's stop point is the new location.
                // Replace ALL relink/history anchors while AI still belongs to the scene.
                TryStopBot(Member.Bot);
                // The mover can switch between default/near-door path controllers while
                // waiting. Bind the controller that is CURRENT at the actual handoff.
                _landingCache=BotLandingCache.Bind(_mover);
                _landingCache.Rebase(position,navAnchor,Time.time);
                Plugin.Log.LogInfo("Native bot landing cache rebased: "+Player.Profile?.Nickname+"; "+_landingCache.Describe());
            }
            else
            {
                if (Member.Bot != null) throw new InvalidOperationException("Bot has no supported native mover teleport; refusing a root-only handoff.");
                Player.Teleport(position, false);
            }
            // Optional PIT API, not a hard DLL dependency. Retarget its short existing grace
            // window to this landing rather than its old spawn. No persistent follow orders.
            if (_followerGrace != null)
                try { _followerGrace.Invoke(Member.Follower, new object[] { position }); }
                catch (Exception ex) { Plugin.Log.LogWarning("PIT landing grace unavailable: " + ex.Message); }
        }

        internal void ObserveHandoff(float deltaTime)
        {
            if (!_released || Player == null || _handoffObserved >= 5) return;
            _handoffElapsed += deltaTime;
            float threshold = _handoffObserved == 0 ? 0f : _handoffObserved == 1 ? .6f : _handoffObserved == 2 ? 2f : _handoffObserved == 3 ? 5f : 10f;
            if (_handoffElapsed < threshold) return;
            _handoffObserved++;
            Plugin.Log.LogInfo("Handoff observation: " + Player.Profile?.Nickname + " t=" + _handoffElapsed.ToString("F2")
                + " at " + Player.Position + "; landingDeltaSquared=" + (Player.Position-LandingPosition).sqrMagnitude.ToString("F3")
                + "; alive=" + Player.HealthController?.IsAlive + "; frozen=" + IsFrozen(Player)
                + "; "+(_landingCache?.Describe()??"main player"));
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
                        TransferPosition(_originalPosition);
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
