using System;
using System.Reflection;
using BepInEx;
using HarmonyLib;

namespace UH60FastRopeInsertion
{
    // Optional F12 display adapter. No dependency on ConfigurationManager and no config key/value writes.
    internal static class SettingsLocalization
    {
        private static PropertyInfo _owner;
        internal static void Install()
        {
            var type=AccessTools.TypeByName("ConfigurationManager.SettingEntryBase");
            if(type==null)return;
            var harmony=new Harmony(Plugin.PluginGuid+".localized-settings");
            try
            {
                _owner=AccessTools.Property(type,"PluginInfo");
                if(_owner==null || _owner.PropertyType!=typeof(BepInPlugin))throw new MissingMemberException("SettingEntryBase.PluginInfo");
                foreach(string name in new[]{"DispName","Description","Category"})
                {
                    var getter=AccessTools.PropertyGetter(type,name);
                    if(getter==null || getter.ReturnType!=typeof(string))throw new MissingMemberException(name);
                    harmony.Patch(getter,postfix:new HarmonyMethod(typeof(SettingsLocalization),nameof(Translate)));
                }
            }
            catch(Exception error)
            {
                harmony.UnpatchSelf();
                Plugin.Log.LogWarning("Optional F12 translation unavailable; English settings retained: "+error.Message);
            }
        }
        internal static bool Owns(string guid) => guid==Plugin.PluginGuid
            || guid==Plugin.PluginGuid.Replace("uh60fastropeinsertion","uh60performance");
        private static void Translate(object __instance,ref string __result)
        {
            var owner=_owner.GetValue(__instance,null) as BepInPlugin;
            if(owner!=null && Owns(owner.GUID))__result=UiText.Get(__result);
        }
    }
}
