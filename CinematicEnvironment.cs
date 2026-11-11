using System;
using System.Linq;
using System.Collections.Generic;
using EFT;
using EFT.EnvironmentEffect;
using UnityEngine;

namespace UH60FastRopeInsertion
{
    // Exposure/environment queries normally follow the real pawn. That pawn is parked while
    // the eye camera is flying: a bunker spawn must not supply exposure for the outside view.
    internal sealed class CinematicEnvironment : IDisposable
    {
        private readonly Player _player;
        private readonly Camera _camera;
        private readonly EnvironmentManager _manager;
        private float _next;
        private readonly List<Light> _sunMaskOwned=new List<Light>();
        internal CinematicEnvironment(Player player, Camera camera)
        {
            _player=player; _camera=camera; _manager=EnvironmentManagerBase.Instance as EnvironmentManager;
            // Restore the map's authored reflection path even if an earlier test already left the
            // wrong global keyword active. Use the native sky's own cubemap decision, not a grade.
            if(TODSkyProvider.Instance is EFT.Weather.TODSkySimple simple&&simple.isActiveAndEnabled)
                simple.ApplyGlobalReflectionProbe();
            Plugin.Log.LogInfo("Cinematic native sky="+(TODSkyProvider.Instance!=null?TODSkyProvider.Instance.GetType().Name:"none")
                +"; simple reflections="+Shader.IsKeywordEnabled("USE_SIMPLE_REFLECTION_PROBE")
                +"; simple cubemap="+(Shader.GetGlobalTexture("_MySimpleGlobalReflectionProbe")!=null));
            // Our private layer must still receive the RAID sun. An unused layer is
            // not necessarily included in EFT's directional-light mask. Never add a
            // replacement ambient/sun or change colour/intensity/exposure globally.
            int bit=1<<CabinLighting.Layer;
            foreach(var light in UnityEngine.Object.FindObjectsOfType<Light>())
                if(light.type==LightType.Directional&&(light.cullingMask&bit)==0)
                {_sunMaskOwned.Add(light);light.cullingMask|=bit;}
            Plugin.Log.LogInfo("Cinematic lighting: raid directional coverage extended to private layer on "+_sunMaskOwned.Count+" lights; no world grade override.");
            Plugin.Log.LogInfo("Eye camera effects retained: " + string.Join(", ", camera.GetComponents<Behaviour>()
                .Select(b=>b.GetType().Name+"="+b.enabled)));
            if (_manager!=null) Plugin.Log.LogInfo("Exposure before cinematic observer: environment="+_manager.Environment
                +"; offset="+_manager.PrismExposureOffset+"; player="+player.Position+"; camera="+camera.transform.position);
        }
        internal void Observe()
        {
            if (_manager==null || _player==null || _camera==null || Time.unscaledTime<_next) return;
            _next=Time.unscaledTime+.2f;
            _manager.SetTriggerForPlayer(_player,_manager.TryFindTriggerByPos(_camera.transform.position));
        }
        internal bool OverrideTrigger(IPlayer player, out IndoorTrigger trigger)
        {
            trigger=null;
            if (_manager==null || _camera==null || !ReferenceEquals(player,_player)) return false;
            trigger=_manager.TryFindTriggerByPos(_camera.transform.position);
            return true;
        }
        public void Dispose()
        {
            foreach(var light in _sunMaskOwned)if(light!=null)light.cullingMask&=~(1<<CabinLighting.Layer);
            _sunMaskOwned.Clear();
            if (_manager==null || _player==null) return;
            _manager.UpdateEnvironmentForPlayer(_player);
            Plugin.Log.LogInfo("Exposure observer restored to live player: environment="+_manager.Environment
                +"; offset="+_manager.PrismExposureOffset+"; player="+_player.Position);
        }
    }
}
