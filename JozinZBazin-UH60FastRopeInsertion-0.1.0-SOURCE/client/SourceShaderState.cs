using EFT.Weather;
using UnityEngine;

namespace UH60FastRopeInsertion
{
    // EFT uses its own ambient/reflection globals, not just Unity RenderSettings.
    // Snapshot only the audited source-load side effects, never freeze weather per frame.
    internal sealed class SourceShaderState
    {
        private const string Simple="USE_SIMPLE_REFLECTION_PROBE";
        private static readonly string[] Vectors={"_SHAr","_SHAg","_SHAb","_SHBr","_SHBg","_SHBb","_SHC","_EFT_Ambient"};
        private static readonly string[] Textures={"_MyGlobalReflectionProbe","_MySimpleGlobalReflectionProbe"};
        private readonly Vector4[] _vectors=new Vector4[Vectors.Length];
        private readonly Texture[] _textures=new Texture[Textures.Length];
        private readonly bool _simple=Shader.IsKeywordEnabled(Simple);
        private readonly ITODSky _provider=TODSkyProvider.Instance;
        internal SourceShaderState()
        {
            for(int i=0;i<Vectors.Length;i++)_vectors[i]=Shader.GetGlobalVector(Vectors[i]);
            for(int i=0;i<Textures.Length;i++)_textures[i]=Shader.GetGlobalTexture(Textures[i]);
        }
        internal void Restore()
        {
            bool changed=Shader.IsKeywordEnabled(Simple)!=_simple;
            if(_simple)Shader.EnableKeyword(Simple);else Shader.DisableKeyword(Simple);
            for(int i=0;i<Vectors.Length;i++)Shader.SetGlobalVector(Vectors[i],_vectors[i]);
            for(int i=0;i<Textures.Length;i++)Shader.SetGlobalTexture(Textures[i],_textures[i]);
            TODSkyProvider.RegisterInstance(_provider);
            // A previous test may already have captured a stale keyword. The active native
            // simple-sky component is authoritative about its own cubemap/mode; do not guess.
            if(_provider is TODSkySimple simple&&simple.isActiveAndEnabled)simple.ApplyGlobalReflectionProbe();
            Plugin.Log.LogInfo("Reference EFT shader state restored once: simple reflections="+_simple
                +"; source changed keyword="+changed+"; sky provider="+(_provider!=null?_provider.GetType().Name:"none")
                +"; 8 ambient vectors/2 reflection bindings restored. No weather/grade override.");
        }
    }
}
