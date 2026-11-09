using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Bootstrap;
using EFT;
using EFT.Game.Spawning;
using EFT.UI;
using HarmonyLib;
using JsonType;
using TMPro;
using UnityEngine;

namespace UH60FastRopeInsertion
{
    // External mod types NEVER occur in our class layouts. Missing Infil/Maps cannot poison
    // Unity's global JSON/type scan. Exact pinned contracts are checked before any optional hook.
    internal static class InfilIntegration
    {
        internal static bool Ready { get; private set; }
        private static Type _spotType;
        private static MethodInfo Method(Type type,string name) => AccessTools.Method(type,name);
        internal static void Install(Harmony harmony)
        {
            if(!Chainloader.PluginInfos.ContainsKey("net.liquidwarp.infil"))return;
            var adapter=new Harmony(Plugin.PluginGuid+".infil");
            try
            {
                if(Chainloader.PluginInfos["net.liquidwarp.infil"].Metadata.Version!=new System.Version(2,1,0)
                    || !Chainloader.PluginInfos.TryGetValue("com.mpstark.dynamicmaps",out var maps)
                    || maps.Metadata.Version!=new System.Version(1,2,1))
                    throw new InvalidOperationException("Adapter requires audited Infil2.1.0 / DynamicMaps1.2.1.");
                var spots=AccessTools.TypeByName("Liquidwarp.Infil.Spawn.SpawnSpots");
                var store=AccessTools.TypeByName("Liquidwarp.Infil.Spawn.SpawnChoiceStore");
                var picker=AccessTools.TypeByName("Liquidwarp.Infil.UI.SpawnPickerWindow");
                _spotType=AccessTools.TypeByName("Liquidwarp.Infil.Spawn.SpawnSpot");
                foreach(var name in new[]{"Label","Position","Where","PointIds","PointPositions"})
                    if(AccessTools.Field(_spotType,name)==null)throw new InvalidOperationException("Infil SpawnSpot contract missing: "+name);
                var targets=new[]{Method(spots,"For"),Method(store,"Set"),Method(store,"Clear"),Method(picker,"BuildPanel"),Method(picker,"Select")};
                if(targets.Any(m=>m==null))throw new InvalidOperationException("Unsupported Infil API; required methods missing.");
                adapter.Patch(targets[0],prefix:new HarmonyMethod(typeof(InfilIntegration),nameof(OfferSites)));
                adapter.Patch(targets[1],postfix:new HarmonyMethod(typeof(InfilIntegration),nameof(Selected)));
                adapter.Patch(targets[2],postfix:new HarmonyMethod(typeof(InfilIntegration),nameof(Cleared)));
                adapter.Patch(targets[3],postfix:new HarmonyMethod(typeof(InfilIntegration),nameof(Describe)));
                adapter.Patch(targets[4],postfix:new HarmonyMethod(typeof(InfilIntegration),nameof(Describe)));
                Ready=true;
                Plugin.Log.LogInfo("Infil 2.1.0 + Dynamic Maps 1.2.1 adapter ready; selected LZs remain runtime-validated.");
            }
            catch(Exception e) { adapter.UnpatchSelf();Ready=false;Plugin.Log.LogError("Infil adapter disabled: "+e); }
        }

        private static bool Heli(string map,ESideType side) => Plugin.SelectedDeployment.Value==DeploymentMode.Helicopter
            && PreRaidDeployment.GetAvailability(map,side!=ESideType.Pmc,false)==InsertionAvailability.Available;

        private static bool OfferSites(LocationSettings.Location location,EPlayerSide side,ref object __result)
        {
            if(!Ready || location==null || !Heli(location.Id,side==EPlayerSide.Savage?ESideType.Savage:ESideType.Pmc))return true;
            var sites=LandingSites.For(location.Id);
            var points=(location.SpawnPointParams??Array.Empty<SpawnPointParams>())
                .Where(p=>p.Position!=null && p.Categories.ContainPlayerCategory()).ToArray();
            var matching=points.Where(p=>p.Sides.Contain(side)).ToArray();
            // An extended BD profile side is not a stock spawn-side bit. Use genuine PMC
            // initial supports for presentation, without changing the player's faction or AI.
            if(matching.Length==0)matching=points.Where(p=>p.Sides.Contain(EPlayerSide.Usec)||p.Sides.Contain(EPlayerSide.Bear)).ToArray();
            var list=(IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(_spotType));
            foreach(var site in sites)
            {
                // Infil still chooses an ordinary initial support point (before our presentation),
                // not a second post-touchdown teleport. Retain REAL spawn ids, never invent them.
                var nearest=matching.OrderBy(p=>Vector3.SqrMagnitude((Vector3)p.Position-site.Sector.Seed)).Take(1).ToArray();
                if(nearest.Length==0)continue;
                object spot=Activator.CreateInstance(_spotType);
                Put(spot,"Label",site.Label);Put(spot,"Position",site.Sector.Seed);Put(spot,"Where",site.Name);
                Put(spot,"PointIds",new HashSet<string>(nearest.Select(p=>p.Id)));
                Put(spot,"PointPositions",new List<Vector3>{site.Sector.Seed});list.Add(spot);
            }
            __result=list;
            return false;
        }

        private static object Get(object obj,string field) => obj==null?null:AccessTools.Field(obj.GetType(),field)?.GetValue(obj);
        private static void Put(object obj,string field,object value) => AccessTools.Field(obj.GetType(),field).SetValue(obj,value);

        private static void Selected(string locationId,ESideType side,object spot)
        {
            LandingRequest.Select(Heli(locationId,side)?LandingSites.Find(locationId,Get(spot,"Label") as string):null);
        }
        private static void Cleared() { LandingRequest.ClearPending(); }

        private static void Describe(object __instance)
        {
            var location=Get(__instance,"_location") as LocationSettings.Location;
            var side=Get(__instance,"_side");
            if(location==null || !(side is ESideType chosen) || !Heli(location.Id,chosen))return;
            var text=Get(__instance,"_infoText") as TextMeshProUGUI;
            if(text==null)return;
            Transform panel=text.transform.parent;
            var title=panel.Find("Title")?.GetComponent<TextMeshProUGUI>();
            if(title!=null)title.text="ВЫБОР ЗОНЫ ВЫСАДКИ UH-60\n<size=60%>"+location.LocalizedName+"</size>";
            var help=panel.Find("Help")?.GetComponent<TextMeshProUGUI>();
            if(help!=null)help.text=LandingBriefingText.Help;
            var selected=Get(Get(__instance,"_selected"),"Spot");
            var site=LandingSites.Find(location.Id,Get(selected,"Label") as string)??LandingSites.Default(location.Id);
            text.text=LandingBriefingText.Description(site,selected!=null);
            var confirm=panel.Find("CONFIRM")?.GetComponent<DefaultUIButton>();
            if(confirm!=null){confirm.SetRawText(LandingBriefingText.Confirm,22);confirm._minWidth=260f;confirm.UpdateMinWidth();}
            panel.Find("BACK")?.GetComponent<DefaultUIButton>()?.SetRawText("НАЗАД",22);
        }
    }
}
