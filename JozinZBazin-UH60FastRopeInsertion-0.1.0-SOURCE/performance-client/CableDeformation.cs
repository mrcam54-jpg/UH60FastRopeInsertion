using System;
using System.Collections.Generic;
using UnityEngine;

namespace UH60Performance
{
    // Identical frame transport and vertex formula to test15.4. Reuse one interpolated frame
    // per exact longitudinal binding, NOT an approximation, decimation or lower update rate.
    internal sealed class CableDeformation
    {
        private readonly Vector3[] _source,_sourceNormals;
        private readonly Vector4[] _sourceTangents;
        private readonly Vector3[] _frameNormal,_frameTangent,_position,_normal,_tangent,_side;
        private readonly int[] _binding,_index;
        private readonly float[] _blend;
        internal readonly Vector3[] Vertices,Normals;
        internal readonly Vector4[] Tangents;
        internal int BindingCount=>_index.Length;
        internal int PathCount=>_frameNormal.Length;

        internal CableDeformation(Vector3[] source,Vector3[] normals,Vector4[] tangents,float[] along,
            int pathCount,Vector3[] vertices,Vector3[] outputNormals,Vector4[] outputTangents)
        {
            if(pathCount<2||source.Length==0||source.Length!=along.Length||vertices.Length!=source.Length
                ||outputNormals.Length!=source.Length||outputTangents.Length!=source.Length)throw new ArgumentException("Cable buffer contract changed");
            _source=source;_sourceNormals=normals;_sourceTangents=tangents;
            Vertices=vertices;Normals=outputNormals;Tangents=outputTangents;
            _frameNormal=new Vector3[pathCount];_frameTangent=new Vector3[pathCount];_binding=new int[source.Length];
            var distinct=new Dictionary<float,int>();var indices=new List<int>();var blends=new List<float>();
            for(int i=0;i<source.Length;i++)
            {
                float sample=along[i]*(pathCount-1);
                if(!distinct.TryGetValue(sample,out int binding))
                {
                    binding=indices.Count;distinct.Add(sample,binding);
                    int k=Mathf.Min((int)sample,pathCount-2);indices.Add(k);blends.Add(sample-k);
                }
                _binding[i]=binding;
            }
            _index=indices.ToArray();_blend=blends.ToArray();
            _position=new Vector3[BindingCount];_normal=new Vector3[BindingCount];
            _tangent=new Vector3[BindingCount];_side=new Vector3[BindingCount];
        }

        internal void Deform(Vector3[] points)
        {
            if(points.Length!=PathCount)throw new ArgumentException("Original rope centreline sample count changed");
            Vector3 previous=Vector3.right;
            for(int i=0;i<points.Length;i++)
            {
                Vector3 tangent=points[Mathf.Min(i+1,points.Length-1)]-points[Mathf.Max(i-1,0)];
                tangent=tangent.sqrMagnitude>0.0000001f?tangent.normalized:Vector3.down;
                Vector3 normal=Vector3.ProjectOnPlane(previous,tangent);
                if(normal.sqrMagnitude<0.0001f)normal=Vector3.Cross(tangent,Vector3.forward);
                if(normal.sqrMagnitude<0.0001f)normal=Vector3.Cross(tangent,Vector3.up);
                previous=normal.normalized;_frameNormal[i]=previous;_frameTangent[i]=tangent;
            }
            for(int i=0;i<BindingCount;i++)
            {
                int k=_index[i];float blend=_blend[i];
                Vector3 t=Vector3.Lerp(_frameTangent[k],_frameTangent[k+1],blend).normalized;
                Vector3 n=Vector3.Lerp(_frameNormal[k],_frameNormal[k+1],blend).normalized;
                _position[i]=Vector3.Lerp(points[k],points[k+1],blend);
                _tangent[i]=t;_normal[i]=n;_side[i]=-Vector3.Cross(t,n).normalized;
            }
            for(int i=0;i<_source.Length;i++)
            {
                int k=_binding[i];Vector3 t=_tangent[k],n=_normal[k],side=_side[k],p=_source[i];
                Vertices[i]=_position[k]+n*p.x+side*p.y;
                Vector3 sn=_sourceNormals.Length==_source.Length?_sourceNormals[i]:new Vector3(p.x,p.y,0f).normalized;
                Normals[i]=(n*sn.x+side*sn.y-t*sn.z).normalized;
                Vector4 st=_sourceTangents.Length==_source.Length?_sourceTangents[i]:new Vector4(1,0,0,1);
                Vector3 transformed=(n*st.x+side*st.y-t*st.z).normalized;
                Tangents[i]=new Vector4(transformed.x,transformed.y,transformed.z,st.w);
            }
        }
    }
}
