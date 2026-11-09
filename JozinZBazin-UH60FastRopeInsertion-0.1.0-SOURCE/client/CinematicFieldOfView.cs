using System;
using UnityEngine;
namespace UH60FastRopeInsertion
{
    internal sealed class CinematicFieldOfView : IDisposable
    {
        private readonly Camera _camera;
        private readonly float _original;
        private bool _disposed;
        internal CinematicFieldOfView(Camera camera){_camera=camera;_original=camera.fieldOfView;}
        internal void Apply(float bonus)
        {
            if(!_disposed&&_camera!=null)_camera.fieldOfView=Mathf.Clamp(_original+Mathf.Clamp(bonus,0,18),35,100);
        }
        public void Dispose()
        {
            if(_disposed)return;_disposed=true;
            if(_camera!=null)_camera.fieldOfView=_original;
        }
    }
}
