using System;
using System.Linq;
using UnityEngine;

namespace UH60FastRopeInsertion
{
    internal static class CabinLighting
    {
        private static int _layer=-1;
        internal static int Layer
        {
            get
            {
                if(_layer>=0)return _layer;
                // Audited installed TagManager: unnamed 3/6/7 are unused. Do not rename a layer,
                // change Physics settings or borrow a named layer used by map geometry.
                foreach(int candidate in new[]{7,6,3})
                    if(string.IsNullOrEmpty(LayerMask.LayerToName(candidate))) return _layer=candidate;
                throw new InvalidOperationException("No audited free presentation layer for cabin-only lights.");
            }
        }
        internal static void Install(Transform sceneRoot,Transform sourceAircraft,Transform target)
        {
            var rig=sceneRoot.Find("Helicopter_01/VFX_Icebreaker_Cutscene_Helicopter_01/Light");
            var originals=rig!=null?rig.GetComponentsInChildren<Light>(true)
                .Where(l=>l.name.StartsWith("G_spot_Cabin_",StringComparison.Ordinal)).ToArray():new Light[0];
            if(originals.Length!=2) throw new InvalidOperationException("Expected both original red cabin practicals; found "+originals.Length);
            int index=0;
            foreach(var original in originals)
            {
                var obj=new GameObject("UH60_OriginalCabinPractical_"+original.name);obj.transform.SetParent(target,false);
                // Source VFX is staged separately from the re-based animated airframe. Its
                // unsampled local Z puts both lights over the cockpit, outside the cabin.
                // Fit the same practicals to measured cabin ceiling coordinates in flight space.
                Vector3 flightPoint=new Vector3(index==0?-.42f:.42f,1.52f,index==0?.85f:2.05f);
                obj.transform.localPosition=Quaternion.Euler(0,180,0)*(flightPoint-new Vector3(0,-.90f,-1.55f));
                obj.transform.localRotation=Quaternion.Inverse(sourceAircraft.rotation)*original.transform.rotation;
                var light=obj.AddComponent<Light>();light.type=original.type;light.color=original.color;
                light.intensity=original.intensity;light.range=original.range;light.spotAngle=original.spotAngle;
                light.shadows=LightShadows.None;light.cullingMask=1<<Layer;light.enabled=true;
                light.renderMode=LightRenderMode.ForcePixel;
                Plugin.Log.LogInfo("Cabin practical fitted: "+original.name+"; flightLocal="+flightPoint
                    +"; originalStageLocal="+sourceAircraft.InverseTransformPoint(original.transform.position));
                index++;
            }
            Plugin.Log.LogInfo("Two native red cabin lights: fitted cabin ceiling, original colour/intensity/range; presentation-only layer="+Layer+"; no global grade.");
        }
    }
}
