using System;

namespace UH60FastRopeInsertion
{
    internal static class InsertionTiming
    {
        internal const float MainExit=2.5f;
        internal const float Rise=1.25f;
        internal const float Walk=2.2f;
        internal const float Exit=1.8f;
        internal const float FollowerPreparation=Rise+Walk+Exit;
        internal const float Touchdown=.65f;
        internal static int Side(int index) => index<2?-1:index%2==0?1:-1;
        internal static float[] Starts(float duration,bool userChoreography=false)
        {
            if(float.IsNaN(duration)||float.IsInfinity(duration)||duration<=0f)
                throw new ArgumentOutOfRangeException(nameof(duration));
            if(userChoreography)
            {
                const float main=5.2f,right=6.15f;
                return new[]{main,main+duration+.90f,right,main+duration*2+1.8f,right+duration+1.4f};
            }
            return new[]{MainExit,MainExit+duration+.90f,MainExit+duration+2.45f,
                MainExit+duration*2+1.90f,MainExit+duration*2+3.45f};
        }
        internal static float[] UserStarts(float duration,float[] preparation)
        {
            if(preparation==null||preparation.Length!=5)throw new ArgumentException("Five authored role preparation times required.");
            var result=Starts(duration,true);float left=0,right=0;
            for(int i=0;i<result.Length;i++)
            {
                if(float.IsNaN(preparation[i])||float.IsInfinity(preparation[i])||preparation[i]<0)throw new ArgumentException("Invalid preparation time.");
                float previous=Side(i)<0?left:right;
                result[i]=Math.Max(result[i],Math.Max(preparation[i],previous));
                if(Side(i)<0)left=result[i]+duration+.9f;else right=result[i]+duration+.9f;
            }
            return result;
        }
    }
}
