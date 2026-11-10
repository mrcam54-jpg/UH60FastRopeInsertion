using System.Collections.Generic;
using UnityEngine;

namespace UH60FastRopeInsertion
{
    // Original authored point data imported from the user's RED DOTS on seven calibrated PNGs.
    // Y is ONLY a nearby native spawn-height hint. Physics resolves the actual surface in-raid.
    // No SVGs, foreign map data, screenshots or dependency binaries are shipped with these points.
    internal static class AnnotatedLandingSites
    {
        internal static List<LandingSite> For(MapSafeSector basis)
        {
            var sites=new List<LandingSite>();
            switch(basis.LocationId.ToLowerInvariant())
            {
                case "rezervbase":
                    Add(sites,basis,164.818f,-6.497f,-204.316f,"Barracks clearing");
                    Add(sites,basis,-219.152f,-5.246f,-21.052f,"Perimeter clearing");
                    Add(sites,basis,193.489f,-6.023f,-19.433f,"Perimeter clearing");break;
                case "bigmap":
                    Add(sites,basis,-280.184f,.229f,-210.911f,"Outer clearing");
                    Add(sites,basis,-189.431f,1.064f,-69.258f,"Warehouse yard");
                    Add(sites,basis,414.653f,.965f,-21.159f,"Roadside landing area");
                    Add(sites,basis,6.500f,-.884f,54.477f,"Roadside landing area");break;
                case "woods":
                    Add(sites,basis,-90.452f,11.943f,-693.955f,"Village outskirts");
                    Add(sites,basis,-478.365f,15.766f,-449.149f,"Village outskirts");
                    Add(sites,basis,368.658f,-17.472f,212.567f,"Forest clearing");
                    Add(sites,basis,-144.833f,-1.314f,383.297f,"Lakeside approach");break;
                case "shoreline":
                    Add(sites,basis,-573.623f,-29.541f,-266.518f,"Farm outskirts");
                    Add(sites,basis,51.639f,-31.184f,-177.065f,"Forest clearing");
                    Add(sites,basis,150.576f,-53.371f,321.866f,"Coastal road");
                    Add(sites,basis,-608.561f,-59.590f,484.310f,"Waterfront landing area");break;
                case "lighthouse":
                    Add(sites,basis,71.786f,10.769f,-755.098f,"Industrial perimeter");
                    Add(sites,basis,149.757f,5.175f,178.169f,"Coastal clearing");
                    Add(sites,basis,-5.288f,6.884f,462.262f,"Coastal road");break;
                case "interchange":
                    Add(sites,basis,257.279f,21.412f,273.935f,"Mall perimeter road");break;
                case "tarkovstreets":
                    Add(sites,basis,-49.746f,-.179f,402.166f,"Courtyard landing area");break;
            }
            return sites;
        }
        private static void Add(List<LandingSite> sites,MapSafeSector basis,float x,float yHint,float z,string name)
        {
            int index=sites.Count;
            sites.Add(new LandingSite(basis.LocationId+".marked"+(index+1).ToString("00"),
                "LZ-"+(char)('A'+index),name,new MapSafeSector(basis.LocationId,new Vector3(x,yHint,z),
                1.25f,basis.HoverHeight,basis.ApproachBearing,basis.DepartureBearing,name),true));
        }
    }
}
