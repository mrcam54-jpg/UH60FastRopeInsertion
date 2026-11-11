using System.Collections.Generic;
using UnityEngine;

namespace UH60FastRopeInsertion
{
    internal static class GameplayPoseBlend
    {
        internal static void Apply(IEnumerable<KeyValuePair<Transform,Transform>> sourceToClone,Transform root,float weight)
        {
            weight=SupportedRopePose.Smooth(weight);
            if(weight<=0f)return;
            foreach(var pair in sourceToClone)
            {
                if(pair.Key==null || pair.Value==null || pair.Value==root)continue;
                pair.Value.localPosition=Vector3.Lerp(pair.Value.localPosition,pair.Key.localPosition,weight);
                pair.Value.localRotation=Quaternion.Slerp(pair.Value.localRotation,pair.Key.localRotation,weight);
                pair.Value.localScale=Vector3.Lerp(pair.Value.localScale,pair.Key.localScale,weight);
            }
        }
    }
}
