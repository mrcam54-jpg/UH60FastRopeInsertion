using System;

namespace UH60FastRopeInsertion
{
    /// <summary>Immutable factual touchdown, usable by optional campaign adapters without a BDA dependency.</summary>
    public sealed class InsertionReceipt
    {
        public string RaidId { get; }
        public string MapId { get; }
        public string LandingSiteId { get; }
        public float X { get; }
        public float Y { get; }
        public float Z { get; }
        public bool ManualTest { get; }
        public long LandedAtTicks { get; }
        internal InsertionReceipt(string raid,string map,string site,float x,float y,float z,bool manual)
        { RaidId=raid;MapId=map;LandingSiteId=site;X=x;Y=y;Z=z;ManualTest=manual;LandedAtTicks=System.Diagnostics.Stopwatch.GetTimestamp(); }
    }

    /// <summary>Memory-only, local raid evidence. Not authoritative multiplayer/server anti-cheat.</summary>
    public static class InsertionApi
    {
        public const int ApiVersion=1;
        public static string CurrentRaidId { get; private set; }
        public static InsertionReceipt CurrentReceipt { get; private set; }
        private static string _map,_site;
        private static bool _manual,_attempt;
        internal static void BeginRaid(string map)
        {
            CurrentRaidId=Guid.NewGuid().ToString("N");
            _map=(map??"").Trim().ToLowerInvariant();
            CurrentReceipt=null;_site=null;_attempt=false;
        }
        internal static void BeginAttempt(string site,bool manual)
        { CurrentReceipt=null;_site=site;_manual=manual;_attempt=true; }
        internal static void Land(float x,float y,float z)
        {
            if(!_attempt || CurrentReceipt!=null || string.IsNullOrEmpty(CurrentRaidId) || string.IsNullOrEmpty(_site))return;
            if(float.IsNaN(x)||float.IsNaN(y)||float.IsNaN(z)||float.IsInfinity(x)||float.IsInfinity(y)||float.IsInfinity(z))return;
            CurrentReceipt=new InsertionReceipt(CurrentRaidId,_map,_site,x,y,z,_manual);
            _attempt=false;
        }
        internal static void CancelAttempt() { _attempt=false; }

        public static bool MatchesLanding(string raidId,string mapId,string siteId)
        {
            var receipt=CurrentReceipt;
            return receipt!=null && !receipt.ManualTest && !string.IsNullOrEmpty(raidId)
                && receipt.RaidId==CurrentRaidId && receipt.RaidId==raidId
                && string.Equals(receipt.MapId,mapId,StringComparison.OrdinalIgnoreCase)
                && string.Equals(receipt.LandingSiteId,siteId,StringComparison.OrdinalIgnoreCase);
        }

        // Objectives must be tracked in this SAME runtime raid. Separate cumulative quest counters
        // or last raid's persisted landing boolean are deliberately insufficient.
        public static bool CanCompleteSameRaid(string raidId,string mapId,string siteId,
            string objectiveRaidId,bool objectivesComplete,long objectivesCompletedAtTicks)
        { return objectivesComplete && objectiveRaidId==raidId && MatchesLanding(raidId,mapId,siteId)
            && objectivesCompletedAtTicks>=CurrentReceipt.LandedAtTicks; }
    }
}
