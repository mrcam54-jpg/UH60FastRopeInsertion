using System;
using System.Collections.Generic;
using UnityEngine;

namespace UH60FastRopeInsertion
{
    internal sealed class MapSafeSector
    {
        internal readonly string LocationId;
        internal readonly Vector3 Seed;
        internal readonly float SearchRadius;
        internal readonly float HoverHeight;
        internal readonly float ApproachBearing;
        internal readonly float DepartureBearing;
        internal readonly string Description;

        internal MapSafeSector(string locationId, Vector3 seed, float searchRadius, float hoverHeight,
            float approachBearing, float departureBearing, string description)
        {
            LocationId = locationId;
            Seed = seed;
            SearchRadius = searchRadius;
            HoverHeight = hoverHeight;
            ApproachBearing = approachBearing;
            DepartureBearing = departureBearing;
            Description = description;
        }
    }

    internal static class MapSafeSectors
    {
        private static readonly Dictionary<string, MapSafeSector> Sectors =
            new Dictionary<string, MapSafeSector>(StringComparer.OrdinalIgnoreCase)
            {
                // Primary test sector requested by the user: the map-edge pair of guard barracks.
                // Runtime validation is intentionally bounded so it cannot drift to the crane yard.
                ["rezervbase"] = Sector("rezervbase", 164.5f, -6.82f, -234.9f, 22f, 15.5f, 224f, 48f,
                    "Reserve — map-edge corner by the two guard barracks"),

                ["bigmap"] = Sector("bigmap", -278f, 1f, -221f, 34f, 15.5f, 245f, 58f,
                    "Customs — western perimeter clearing"),
                ["interchange"] = Sector("interchange", 299f, 21f, -421f, 38f, 16f, 146f, 326f,
                    "Interchange — outer south-east approach"),
                ["lighthouse"] = Sector("lighthouse", -385f, 24f, -512f, 36f, 16f, 214f, 34f,
                    "Lighthouse — southern coastal perimeter"),
                ["sandbox"] = Sector("sandbox", 102f, 28f, 315f, 32f, 15f, 12f, 190f,
                    "Ground Zero — northern perimeter"),
                ["sandbox_high"] = Sector("sandbox_high", 102f, 28f, 315f, 32f, 15f, 12f, 190f,
                    "Ground Zero high level — northern perimeter"),
                ["shoreline"] = Sector("shoreline", -918f, -58f, 255f, 42f, 16f, 272f, 88f,
                    "Shoreline — western perimeter"),
                ["tarkovstreets"] = Sector("tarkovstreets", 274f, 5f, 517f, 30f, 17f, 34f, 214f,
                    "Streets — north-east perimeter"),
                ["woods"] = Sector("woods", 369f, 14f, -700f, 44f, 16f, 152f, 332f,
                    "Woods — south-east perimeter clearing"),
                ["icebreaker"] = Sector("icebreaker", -4f, 9f, 90f, 13f, 14f, 180f, 0f,
                    "Icebreaker — aft helipad"),

                // Labs has no open-sky flight volume. The record is kept so diagnostics say why
                // it is not silently treated as Factory; validation is expected to reject it.
                ["laboratory"] = Sector("laboratory", -118f, 0f, -253f, 12f, 12f, 90f, 270f,
                    "Laboratory — enclosed map; open-sky validation normally rejects insertion"),

                // Terminal is enabled in the 4.1.6 database but has no authored player spawns.
                // This seed is provisional until that location becomes playable in the local build.
                ["terminal"] = Sector("terminal", 0f, 0f, 0f, 20f, 15f, 180f, 0f,
                    "Terminal — provisional database-only sector"),
            };

        internal static bool IsFactory(string locationId)
        {
            return !string.IsNullOrEmpty(locationId)
                && locationId.StartsWith("factory4", StringComparison.OrdinalIgnoreCase);
        }

        internal static bool TryGet(string locationId, out MapSafeSector sector)
        {
            sector = null;
            return !string.IsNullOrEmpty(locationId) && Sectors.TryGetValue(locationId.Trim(), out sector);
        }

        internal static IEnumerable<MapSafeSector> All
        {
            get { return Sectors.Values; }
        }

        private static MapSafeSector Sector(string id, float x, float y, float z, float radius,
            float hoverHeight, float approach, float depart, string description)
        {
            return new MapSafeSector(id, new Vector3(x, y, z), radius, hoverHeight, approach, depart, description);
        }
    }
}

