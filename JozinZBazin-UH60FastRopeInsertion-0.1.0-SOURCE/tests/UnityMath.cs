using System.Collections;
using Num=System.Numerics;
namespace UnityEngine
{
    public struct Vector3
    {
        public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
        public Num.Vector3 N=>new(x,y,z);public static Vector3 From(Num.Vector3 n)=>new(n.X,n.Y,n.Z);
        public static Vector3 zero=>new();public static Vector3 one=>new(1,1,1);public static Vector3 up=>new(0,1,0);public static Vector3 down=>-up;
        public static Vector3 right=>new(1,0,0);public static Vector3 forward=>new(0,0,1);
        public float sqrMagnitude=>x*x+y*y+z*z;public float magnitude=>MathF.Sqrt(sqrMagnitude);public Vector3 normalized=>magnitude>1e-10f?this/magnitude:zero;
        public static Vector3 operator +(Vector3 a,Vector3 b)=>From(a.N+b.N);public static Vector3 operator -(Vector3 a,Vector3 b)=>From(a.N-b.N);
        public static Vector3 operator -(Vector3 a)=>From(-a.N);public static Vector3 operator *(Vector3 a,float f)=>From(a.N*f);
        public static Vector3 operator *(float f,Vector3 a)=>a*f;public static Vector3 operator /(Vector3 a,float f)=>From(a.N/f);
        public static float Dot(Vector3 a,Vector3 b)=>Num.Vector3.Dot(a.N,b.N);public static Vector3 Cross(Vector3 a,Vector3 b)=>From(Num.Vector3.Cross(a.N,b.N));
        public static Vector3 ProjectOnPlane(Vector3 a,Vector3 n)=>a-n*Dot(a,n)/Math.Max(1e-12f,n.sqrMagnitude);
        public static float Distance(Vector3 a,Vector3 b)=>(a-b).magnitude;
        public static Vector3 Lerp(Vector3 a,Vector3 b,float t)=>a+(b-a)*Math.Clamp(t,0,1);
        public static float Angle(Vector3 a,Vector3 b)=>MathF.Acos(Math.Clamp(Dot(a,b)/Math.Max(1e-10f,a.magnitude*b.magnitude),-1,1))*Mathf.Rad2Deg;
        public static float SignedAngle(Vector3 a,Vector3 b,Vector3 axis)=>Angle(a,b)*MathF.Sign(Dot(axis,Cross(a,b)));
        public static Vector3 ClampMagnitude(Vector3 a,float m)=>a.magnitude>m?a.normalized*m:a;
    }
    public struct Quaternion
    {
        public float x,y,z,w;public Quaternion(float x,float y,float z,float w){this.x=x;this.y=y;this.z=z;this.w=w;}
        public Num.Quaternion N=>new(x,y,z,w);public static Quaternion From(Num.Quaternion q)=>new(q.X,q.Y,q.Z,q.W);
        public static Quaternion identity=>new(0,0,0,1);
        public static Quaternion operator *(Quaternion a,Quaternion b)=>From(Num.Quaternion.Normalize(a.N*b.N));
        public static Vector3 operator *(Quaternion a,Vector3 b)=>Vector3.From(Num.Vector3.Transform(b.N,a.N));
        public static Quaternion Inverse(Quaternion q)=>From(Num.Quaternion.Inverse(q.N));
        public static Quaternion Slerp(Quaternion a,Quaternion b,float t)=>From(Num.Quaternion.Slerp(a.N,b.N,Math.Clamp(t,0,1)));
        public static float Angle(Quaternion a,Quaternion b)=>2*MathF.Acos(Math.Min(1,Math.Abs(Num.Quaternion.Dot(a.N,b.N))))*Mathf.Rad2Deg;
        public static Quaternion AngleAxis(float angle,Vector3 axis)=>axis.magnitude<1e-10f?identity:From(Num.Quaternion.CreateFromAxisAngle(axis.normalized.N,angle/Mathf.Rad2Deg));
        public static Quaternion Euler(float x,float y,float z)=>AngleAxis(y,Vector3.up)*AngleAxis(x,Vector3.right)*AngleAxis(z,Vector3.forward);
        public static Quaternion FromToRotation(Vector3 a,Vector3 b)
        {
            a=a.normalized;b=b.normalized;float d=Vector3.Dot(a,b);if(d>.999999f)return identity;
            if(d<-.999999f){var axis=Vector3.Cross(a,Vector3.right);if(axis.magnitude<.001f)axis=Vector3.Cross(a,Vector3.up);return AngleAxis(180,axis);}
            var c=Vector3.Cross(a,b);return From(Num.Quaternion.Normalize(new Num.Quaternion(c.x,c.y,c.z,1+d)));
        }
        public static Quaternion LookRotation(Vector3 forward,Vector3 up)
        {
            var z=forward.normalized;var x=Vector3.Cross(up,z).normalized;var y=Vector3.Cross(z,x);
            return From(Num.Quaternion.Normalize(Num.Quaternion.CreateFromRotationMatrix(new Num.Matrix4x4(x.x,x.y,x.z,0,y.x,y.y,y.z,0,z.x,z.y,z.z,0,0,0,0,1))));
        }
    }
    public static class Mathf
    {
        public const float PI=MathF.PI,Rad2Deg=180/MathF.PI,Deg2Rad=MathF.PI/180;
        public static float Clamp01(float x)=>Math.Clamp(x,0,1);public static float Clamp(float x,float a,float b)=>Math.Clamp(x,a,b);
        public static float Max(float a,float b)=>Math.Max(a,b);public static int Max(int a,int b)=>Math.Max(a,b);
        public static float Min(float a,float b)=>Math.Min(a,b);public static float Abs(float x)=>Math.Abs(x);
        public static int Min(int a,int b)=>Math.Min(a,b);public static int Clamp(int x,int a,int b)=>Math.Clamp(x,a,b);
        public static int CeilToInt(float x)=>(int)MathF.Ceiling(x);public static int FloorToInt(float x)=>(int)MathF.Floor(x);
        public static float Sqrt(float x)=>MathF.Sqrt(x);public static float Acos(float x)=>MathF.Acos(x);
        public static float Sin(float x)=>MathF.Sin(x);public static float Cos(float x)=>MathF.Cos(x);
        public static float Lerp(float a,float b,float t)=>a+(b-a)*Clamp01(t);public static float InverseLerp(float a,float b,float t)=>Clamp01((t-a)/(b-a));
    }
    public sealed class Transform:IEnumerable<Transform>
    {
        public string name;public Transform parent;public Vector3 localPosition,localScale=Vector3.one;public Quaternion localRotation=Quaternion.identity;
        readonly List<Transform> children=new();public Transform(string name){this.name=name;}
        public void Attach(Transform p){parent=p;p.children.Add(this);}
        public Vector3 position{get=>parent==null?localPosition:parent.position+parent.rotation*localPosition;set=>localPosition=parent==null?value:Quaternion.Inverse(parent.rotation)*(value-parent.position);}
        public Quaternion rotation{get=>parent==null?localRotation:parent.rotation*localRotation;set=>localRotation=parent==null?value:Quaternion.Inverse(parent.rotation)*value;}
        public Vector3 forward=>rotation*Vector3.forward;public Vector3 up=>rotation*Vector3.up;public Vector3 right=>rotation*Vector3.right;
        public Vector3 TransformPoint(Vector3 p)=>position+rotation*p;public Vector3 InverseTransformPoint(Vector3 p)=>Quaternion.Inverse(rotation)*(p-position);
        public void SetPositionAndRotation(Vector3 p,Quaternion q){position=p;rotation=q;}
        public IEnumerable<Transform> All(){yield return this;foreach(var c in children)foreach(var t in c.All())yield return t;}
        public T[] GetComponentsInChildren<T>(bool active)=>All().Cast<T>().ToArray();
        public IEnumerator<Transform> GetEnumerator()=>children.GetEnumerator();IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();
    }
}
namespace UH60FastRopeInsertion
{
    // Old fitter entry point, forwards to production math until the fitter is replaced.
    static class CinematicActor
    {
        internal static void SolveTwoBone(UnityEngine.Transform a,UnityEngine.Transform b,UnityEngine.Transform c,UnityEngine.Vector3 p,UnityEngine.Vector3 pole,float w)=>PoseMath.SolveTwoBone(a,b,c,p,pole,w);
    }
}
