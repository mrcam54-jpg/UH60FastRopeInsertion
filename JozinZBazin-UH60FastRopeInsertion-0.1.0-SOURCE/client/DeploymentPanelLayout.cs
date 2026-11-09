using System;
using System.Collections.Generic;

namespace UH60FastRopeInsertion
{
    internal readonly struct UiBox
    {
        internal readonly float X,Y,Width,Height;
        internal float Right=>X+Width;
        internal float Top=>Y+Height;
        internal UiBox(float x,float y,float width,float height){X=x;Y=y;Width=width;Height=height;}
        internal bool Overlaps(UiBox other,float gap=0f)=>X<other.Right+gap && Right>other.X-gap
            && Y<other.Top+gap && Top>other.Y-gap;
    }

    // Coordinates are in the NATIVE screen RectTransform, not monitor pixels. The card follows
    // actual location details, rather than occupying the bottom-left clock/date control area.
    internal static class DeploymentPanelLayout
    {
        internal const float Height=164f, CompactHeight=124f;
        internal static bool TryFit(UiBox viewport,UiBox column,float contentBottom,
            IReadOnlyList<UiBox> nativeControls,out UiBox result)
        {
            result=default;
            float width=Math.Min(520f,Math.Min(column.Width,viewport.Width-48f));
            if(width<280f)return false;
            float left=Math.Max(viewport.X+24f,Math.Min(column.X,viewport.Right-width-24f));
            float top=Math.Min(contentBottom-22f,viewport.Top-40f);
            foreach(float height in new[]{Height,CompactHeight})
            {
                // Right-align if a wider native weather/clock panel reaches into the column.
                foreach(float x in new[]{left,viewport.Right-width-24f})
                {
                    var candidate=new UiBox(x,top-height,width,height);
                    if(candidate.Y<viewport.Y+24f)continue;
                    bool clear=true;
                    foreach(var control in nativeControls)if(candidate.Overlaps(control,16f)){clear=false;break;}
                    if(clear){result=candidate;return true;}
                }
            }
            return false; // Do not mask native input if another mod leaves no usable space.
        }
    }
}
