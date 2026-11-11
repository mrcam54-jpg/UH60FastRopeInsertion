using System;
using System.Collections.Generic;
using UnityEngine;

namespace UH60FastRopeInsertion
{
    internal static class NativeRopeConnector
    {
        // The oval carabiner is PART OF Rope_mesh_curve_NEW, not the aircraft's smaller swivel.
        // Split its three disconnected hardware islands locally; preserve source UVs/material.
        internal static Mesh Extract(Mesh original,out Vector3 cableSocket,out Vector3 authoredEye)
        {
            Vector3[] v=original.vertices,n=original.normals;Vector2[] uv=original.uv;
            int[] faces=original.triangles,parent=new int[v.Length],sizes=new int[v.Length];
            var welded=new Dictionary<Vector3Int,int>();
            for(int i=0;i<v.Length;i++)
            {
                parent[i]=i;var p=v[i];var key=new Vector3Int(Mathf.RoundToInt(p.x*10000),Mathf.RoundToInt(p.y*10000),Mathf.RoundToInt(p.z*10000));
                if(welded.TryGetValue(key,out int previous))Union(parent,i,previous);else welded.Add(key,i);
            }
            for(int i=0;i<faces.Length;i+=3){Union(parent,faces[i],faces[i+1]);Union(parent,faces[i+1],faces[i+2]);}
            for(int i=0;i<v.Length;i++)sizes[Find(parent,i)]++;
            var map=new Dictionary<int,int>();var verts=new List<Vector3>();var normals=new List<Vector3>();var tex=new List<Vector2>();var tris=new List<int>();
            for(int i=0;i<v.Length;i++)
                if(sizes[Find(parent,i)]<2000)
                {map.Add(i,verts.Count);verts.Add(v[i]);normals.Add(n.Length==v.Length?n[i]:Vector3.up);tex.Add(uv.Length==v.Length?uv[i]:Vector2.zero);}
            if(verts.Count<500||verts.Count>1800)throw new InvalidOperationException("Original oval-connector island split changed: "+verts.Count+" vertices.");
            float highest=float.NegativeInfinity,lowest=float.PositiveInfinity;
            foreach(var p in verts){highest=Mathf.Max(highest,p.z);lowest=Mathf.Min(lowest,p.z);}
            Vector3 top=Vector3.zero,bottom=Vector3.zero;int count=0,bottomCount=0;
            foreach(var p in verts)if(p.z>highest-.012f){top+=p;count++;}
            foreach(var p in verts)if(p.z<lowest+.012f){bottom+=p;bottomCount++;}
            top/=Mathf.Max(1,count);authoredEye=top;
            Quaternion rotation=Quaternion.FromToRotation(Vector3.forward,Vector3.up);
            bottom/=Mathf.Max(1,bottomCount);
            cableSocket=rotation*(bottom-top);
            for(int i=0;i<verts.Count;i++){verts[i]=rotation*(verts[i]-top);normals[i]=rotation*normals[i];}
            for(int i=0;i<faces.Length;i+=3)
                if(map.ContainsKey(faces[i])&&map.ContainsKey(faces[i+1])&&map.ContainsKey(faces[i+2]))
                {tris.Add(map[faces[i]]);tris.Add(map[faces[i+1]]);tris.Add(map[faces[i+2]]);}
            var result=new Mesh{name="UH60_OriginalOvalCarabinerAndEye"};result.SetVertices(verts);result.SetNormals(normals);
            result.SetUVs(0,tex);result.SetTriangles(tris,0);result.RecalculateBounds();result.RecalculateTangents();
            Plugin.Log.LogInfo("Original oval carabiner extracted from spiral: "+verts.Count+" vertices, "+tris.Count/3+" triangles; actual cable socket="+cableSocket);
            return result;
        }
        internal static Vector3 EyeForMount(Vector3 authoredEye,Bounds mount,bool mirror)
        {
            // These source MESHES share the aircraft's modelling frame (Z-up). Their scene
            // staging parents do NOT share a rotation. Use the native contact in mesh space,
            // then let the cloned mount's actual transform carry it into our flight frame.
            Vector3 eye=authoredEye;if(mirror)eye.x=-eye.x;
            if(!mount.Contains(eye))throw new InvalidOperationException("Original connector eye is outside its matching aircraft attachment; source modelling frame changed.");
            return eye;
        }
        internal static Vector3 OppositeSocket(Vector3 socket){socket.z=-socket.z;return socket;}
        internal static Mesh OppositeDoor(Mesh original)
        {
            // Rotate-Y-180 also flips the fore/aft direction and cannot mirror an asymmetric
            // hook. Reflect private geometry instead; reverse winding/normals, keep UVs and
            // rebuild tangent handedness. No negative object scale and no shared source edit.
            Vector3[] v=original.vertices,n=original.normals;int[] t=original.triangles;
            for(int i=0;i<v.Length;i++)v[i]=OppositeSocket(v[i]);
            for(int i=0;i<n.Length;i++)n[i]=OppositeSocket(n[i]);
            for(int i=0;i<t.Length;i+=3){int swap=t[i+1];t[i+1]=t[i+2];t[i+2]=swap;}
            var result=new Mesh{name="UH60_OriginalOvalCarabinerOppositeDoor",vertices=v,normals=n,uv=original.uv,triangles=t};
            result.RecalculateBounds();result.RecalculateTangents();return result;
        }
        private static int Find(int[] p,int i){while(p[i]!=i){p[i]=p[p[i]];i=p[i];}return i;}
        private static void Union(int[] p,int a,int b){p[Find(p,a)]=Find(p,b);}
    }
}
