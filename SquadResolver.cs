using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using EFT;

namespace UH60FastRopeInsertion
{
    internal sealed class SquadMember
    {
        internal readonly Player Player;
        internal readonly object Follower;
        internal readonly object Bot;

        internal SquadMember(Player player, object follower, object bot)
        {
            Player = player;
            Follower = follower;
            Bot = bot;
        }
    }

    internal static class SquadResolver
    {
        internal static List<SquadMember> Resolve(Player mainPlayer, int maximum)
        {
            List<SquadMember> result = new List<SquadMember>();
            if (mainPlayer == null)
            {
                return result;
            }

            result.Add(new SquadMember(mainPlayer, null, null));
            HashSet<string> profiles = new HashSet<string>(StringComparer.Ordinal);
            if (!string.IsNullOrEmpty(mainPlayer.ProfileId)) profiles.Add(mainPlayer.ProfileId);

            try
            {
                Type bossPlayers = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(assembly => assembly.GetType("pitTeam.Modules.BossPlayers", false))
                    .FirstOrDefault(type => type != null);
                MethodInfo getFollowers = bossPlayers?.GetMethod("GetFollowersByBoss",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                    null, new[] { typeof(string) }, null);
                IEnumerable followers = getFollowers?.Invoke(null, new object[] { mainPlayer.ProfileId }) as IEnumerable;
                if (followers == null)
                {
                    return result;
                }

                foreach (object follower in followers)
                {
                    if (result.Count >= maximum) break;
                    if (follower == null) continue;
                    MethodInfo getBot = follower.GetType().GetMethod("GetBot",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    object bot = getBot?.Invoke(follower, null);
                    PropertyInfo getPlayer = bot?.GetType().GetProperty("GetPlayer",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    Player player = getPlayer?.GetValue(bot, null) as Player;
                    if (player == null || string.IsNullOrEmpty(player.ProfileId) || !profiles.Add(player.ProfileId))
                    {
                        continue;
                    }
                    result.Add(new SquadMember(player, follower, bot));
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("PIT roster reflection failed; continuing with the main player only: " + ex.Message);
            }
            return result;
        }
    }
}
