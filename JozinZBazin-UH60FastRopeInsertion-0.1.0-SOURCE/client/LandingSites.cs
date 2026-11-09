using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace UH60FastRopeInsertion
{
    internal sealed class LandingSite
    {
        internal readonly string Id, Label, Name;
        internal readonly MapSafeSector Sector;
        internal LandingSite(string id, string label, string name, MapSafeSector sector)
        { Id=id; Label=label; Name=name; Sector=sector; }
    }

    // Curated ground-level candidates, NOT a claim of verified live geometry. The normal
    // bounded ground/roof/NavMesh/rotor/actual-door validation remains mandatory in the raid.
    internal static class LandingSites
    {
        internal static string MapKey(string id) => (id ?? "").Trim().ToLowerInvariant();
        internal static List<LandingSite> For(string map)
        {
            string key=MapKey(map);
            if(!MapSafeSectors.TryGet(key,out var original) || key=="laboratory" || key=="terminal") return new List<LandingSite>();
            var sites=new List<LandingSite> { new LandingSite(key+".default","LZ-A",
                key=="rezervbase"?"Угловые казармы":"Подготовленный внешний сектор",original) };
            if(key=="rezervbase")
            {
                sites.Add(Extra(original,"east","LZ-B","Восточный периметр",192.44f,-6.830794f,-97.51999f,90f));
                sites.Add(Extra(original,"west","LZ-C","Западный периметр",-168.593018f,-6.298f,55.697998f,270f));
            }
            if(key=="bigmap") sites.Add(Extra(original,"east","LZ-B","Восточная окраина",648.75f,1.81f,50.160004f,90f));
            if(key=="woods") sites.Add(Extra(original,"west","LZ-B","Западная окраина",-621f,14.91f,-328.73f,270f));
            return sites;
        }

        private static LandingSite Extra(MapSafeSector basis,string id,string label,string name,float x,float y,float z,float bearing)
        {
            return new LandingSite(basis.LocationId+"."+id,label,name,new MapSafeSector(basis.LocationId,
                new Vector3(x,y,z),18f,basis.HoverHeight,bearing,(bearing+180f)%360f,name));
        }

        internal static LandingSite Find(string map,string label) => For(map).FirstOrDefault(s=>s.Label==label);
        internal static LandingSite Default(string map) => For(map).FirstOrDefault();
    }

    internal static class LandingRequest
    {
        private static LandingSite _pending, _raid;
        private static WeakReference _world;
        internal static LandingSite Current => _raid;
        internal static void Select(LandingSite site) { _pending=site; }
        internal static void ClearPending() { _pending=null; }
        internal static void BeginRaid(object world,string map)
        {
            if(world!=null && ReferenceEquals(world,_world?.Target)) return;
            _world=new WeakReference(world);
            _raid=_pending!=null && LandingSites.MapKey(_pending.Sector.LocationId)==LandingSites.MapKey(map)?_pending:null;
            _pending=null;
            InsertionApi.BeginRaid(map);
        }
        internal static LandingSite Resolve(string map) => _raid!=null
            && LandingSites.MapKey(_raid.Sector.LocationId)==LandingSites.MapKey(map)?_raid:LandingSites.Default(map);
    }
}
