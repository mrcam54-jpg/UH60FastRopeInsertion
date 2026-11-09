using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace UH60FastRopeInsertion
{
    // Data only. No executable scripts, Unity asset imports, network, Animator or live-pawn changes.
    internal struct CabinBonePose
    {
        internal Vector3 Position;
        internal Quaternion Rotation;
        internal CabinBonePose(Vector3 p,Quaternion q) { Position=p;Rotation=q; }
    }
    internal sealed class CabinClipData
    {
        internal const string Magic="UH60_CABIN_V1";
        internal readonly int Fps,FrameCount;
        internal readonly bool Loop;
        internal readonly Dictionary<int,Dictionary<string,CabinBonePose[]>> Roles;
        private CabinClipData(int fps,int count,bool loop,Dictionary<int,Dictionary<string,CabinBonePose[]>> roles)
        {Fps=fps;FrameCount=count;Loop=loop;Roles=roles;}
        internal static bool AllowedName(string name) => name=="$actor" || name=="Root_Joint"
            || (name.StartsWith("Base Human",StringComparison.Ordinal) && name.Length<=64
                && name.All(c=>char.IsLetterOrDigit(c)||c==' '||c=='_'));
        internal static CabinClipData Read(string path)
        {
            if(new FileInfo(path).Length>20000000)throw new InvalidDataException("Cabin clip exceeds 20MB.");
            return Parse(File.ReadAllText(path));
        }
        internal static CabinClipData Parse(string text)
        {
            if(text==null||text.Length>20000000)throw new InvalidDataException("Invalid clip size.");
            var roles=new Dictionary<int,Dictionary<string,CabinBonePose[]>>();
            var seen=new HashSet<string>(StringComparer.Ordinal);
            using(var reader=new StringReader(text))
            {
                var h=(reader.ReadLine()??"").TrimStart('\uFEFF').Split(',');
                if(h.Length!=4||h[0]!=Magic||!int.TryParse(h[1],out int fps)||!int.TryParse(h[2],out int count)
                    ||fps<1||fps>60||count<1||count>1801||(h[3]!="loop"&&h[3]!="hold"))
                    throw new InvalidDataException("Invalid cabin clip header.");
                string line;int rowCount=0;
                while((line=reader.ReadLine())!=null)
                {
                    if(line.Length==0||line.Length>512)throw new InvalidDataException("Invalid cabin row.");
                    var cells=line.Split(',');
                    if(cells.Length!=10||!int.TryParse(cells[0],out int frame)||!int.TryParse(cells[1],out int role)
                        ||frame<0||frame>=count||role<0||role>4||!AllowedName(cells[2]))
                        throw new InvalidDataException("Invalid cabin frame/role/bone.");
                    var v=new float[7];
                    for(int i=0;i<7;i++)
                        if(!float.TryParse(cells[i+3],NumberStyles.Float,CultureInfo.InvariantCulture,out v[i])
                            ||float.IsNaN(v[i])||float.IsInfinity(v[i]))throw new InvalidDataException("Nonfinite cabin transform.");
                    float norm=v[3]*v[3]+v[4]*v[4]+v[5]*v[5]+v[6]*v[6];
                    if(Math.Abs(v[0])>8||Math.Abs(v[1])>8||Math.Abs(v[2])>8||Math.Abs(norm-1)>.01f)
                        throw new InvalidDataException("Unbounded position or non-unit quaternion.");
                    if(!seen.Add(frame+"/"+role+"/"+cells[2]))throw new InvalidDataException("Duplicate cabin frame/bone.");
                    if(!roles.TryGetValue(role,out var bones))roles.Add(role,bones=new Dictionary<string,CabinBonePose[]>(StringComparer.Ordinal));
                    if(!bones.TryGetValue(cells[2],out var frames))
                    {
                        if(bones.Count>=80)throw new InvalidDataException("Too many cabin bones.");
                        bones.Add(cells[2],frames=new CabinBonePose[count]);
                    }
                    float inv=1f/(float)Math.Sqrt(norm);
                    frames[frame]=new CabinBonePose(new Vector3(v[0],v[1],v[2]),new Quaternion(v[3]*inv,v[4]*inv,v[5]*inv,v[6]*inv));
                    rowCount++;
                }
                if(roles.Count==0||rowCount!=roles.Values.Sum(b=>b.Count)*count)
                    throw new InvalidDataException("Incomplete cabin frame set.");
                foreach(var bones in roles.Values)
                {
                    foreach(string required in new[]{"$actor","Root_Joint","Base HumanPelvis","Base HumanHead","Base HumanLPalm","Base HumanRPalm"})
                        if(!bones.ContainsKey(required))throw new InvalidDataException("Missing required cabin bone: "+required);
                    if(bones.Count>80)throw new InvalidDataException("Too many cabin bones.");
                    if(h[3]=="loop"&&count>1)
                        foreach(var frames in bones.Values)
                            if(Vector3.Distance(frames[0].Position,frames[count-1].Position)>.002f
                                ||Quaternion.Angle(frames[0].Rotation,frames[count-1].Rotation)>.6f)
                                throw new InvalidDataException("Loop endpoints do not match.");
                }
                return new CabinClipData(fps,count,h[3]=="loop",roles);
            }
        }
        internal CabinBonePose Sample(CabinBonePose[] track,float seconds)
        {
            if(float.IsNaN(seconds)||float.IsInfinity(seconds))throw new ArgumentOutOfRangeException(nameof(seconds));
            double frame=Math.Max(0,seconds)*(double)Fps;
            if(Loop&&FrameCount>1)frame%=FrameCount-1;
            else frame=Math.Min(frame,FrameCount-1);
            int a=(int)frame,b=Math.Min(a+1,FrameCount-1);float t=(float)(frame-a);
            return new CabinBonePose(Vector3.Lerp(track[a].Position,track[b].Position,t),Quaternion.Slerp(track[a].Rotation,track[b].Rotation,t));
        }
        internal static void WriteSnapshot(string path,IEnumerable<KeyValuePair<int,Dictionary<string,CabinBonePose>>> roles)
        {
            var text=new StringBuilder(Magic+",30,1,hold\n");
            foreach(var role in roles)
                foreach(var bone in role.Value.OrderBy(b=>b.Key,StringComparer.Ordinal))
                {
                    var p=bone.Value.Position;var q=bone.Value.Rotation;
                    text.Append("0,").Append(role.Key).Append(',').Append(bone.Key);
                    foreach(float v in new[]{p.x,p.y,p.z,q.x,q.y,q.z,q.w})text.Append(',').Append(v.ToString("R",CultureInfo.InvariantCulture));
                    text.Append('\n');
                }
            Parse(text.ToString()); // validate everything BEFORE touching previous capture
            string full=Path.GetFullPath(path);Directory.CreateDirectory(Path.GetDirectoryName(full));
            string temp=full+".tmp";File.WriteAllText(temp,text.ToString(),new UTF8Encoding(false));
            if(File.Exists(full))File.Replace(temp,full,full+".bak");else File.Move(temp,full);
        }
    }
}
