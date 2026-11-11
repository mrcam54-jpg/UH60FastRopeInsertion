using System;
using UnityEngine;

namespace UH60FastRopeInsertion
{
    internal sealed class InsertionEffects : IDisposable
    {
        private readonly GameObject _root;
        private readonly AudioSource[] _slides = new AudioSource[5];
        private readonly AudioSource _oneShotSource;
        private readonly AudioClip _slideClip;
        private readonly AudioClip _deployClip;
        private readonly AudioClip _landingClip;
        private readonly AudioClip _retractClip;
        private bool _paused;

        internal InsertionEffects()
        {
            _root = new GameObject("UH60FastRopeInsertion_Effects");
            UnityEngine.Object.DontDestroyOnLoad(_root);
            for (int i = 0; i < _slides.Length; i++) _slides[i] = CreateAudioSource("CableSlideAudio_" + i, true);
            _oneShotSource = CreateAudioSource("CableOneShots", false);
            float[] samples = RopeAudioData.Read(out int rate);
            _slideClip = AudioClip.Create("UH60_SoundPack_05254_QuietLoop", samples.Length, 1, rate, false);
            _slideClip.SetData(samples, 0);
            _deployClip = BuildMetalClip("UH60_CableDeploy", 0.82f, 0.48f, 0x8421, false);
            _landingClip = BuildMetalClip("UH60_CableLanding", 0.34f, 0.46f, 0x1764, false);
            _retractClip = BuildMetalClip("UH60_CableRetract", 1.45f, 0.39f, 0x7729, false);
            foreach (var source in _slides) source.clip = _slideClip;

        }

        internal void SetPosition(Vector3 position)
        {
            _root.transform.position = position;
        }

        internal void SetRotorWash(float amount)
        {
            // Rotor dust intentionally removed: no particles, textures, or draw calls.
        }

        internal void PlayDeploy()
        {
            _oneShotSource.PlayOneShot(_deployClip, 0.7f);
        }

        internal void TickSlide(int index, Vector3 position, float elapsed, float duration)
        {
            if (index < 0 || index >= _slides.Length) return;
            if (elapsed < 0 || elapsed >= duration) { StopSlide(index); return; }
            var source = _slides[index];
            source.transform.position = position;
            source.volume = Plugin.RopeVolume.Value / 100f * PresentationPolicy.SlideEnvelope(elapsed, duration)
                * (index == 0 ? 1f : .48f);
            if (!_paused && !source.isPlaying) source.Play();
        }
        internal void StopSlide(int index)
        {
            if (index >= 0 && index < _slides.Length) _slides[index].Stop();
        }
        internal void TickClock(bool paused, float scale)
        {
            foreach (var source in _slides)
            {
                if (paused && !_paused) source.Pause();
                if (!paused && _paused) source.UnPause();
                source.pitch = Mathf.Clamp(scale, .01f, 3f);
            }
            if (paused && !_paused) _oneShotSource.Pause();
            if (!paused && _paused) _oneShotSource.UnPause();
            _oneShotSource.pitch = Mathf.Clamp(scale, .01f, 3f);
            _paused = paused;
        }

        internal void PlayLanding()
        {
            _oneShotSource.PlayOneShot(_landingClip, 0.62f);
        }

        internal void PlayRetract()
        {
            _oneShotSource.PlayOneShot(_retractClip, 0.75f);
        }

        private AudioSource CreateAudioSource(string name, bool loop)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(_root.transform, false);
            AudioSource source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = loop;
            source.spatialBlend = 1f;
            source.minDistance = 3f;
            source.maxDistance = 95f;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.dopplerLevel = 0f;
            source.volume = loop ? 0.25f : 0.45f;
            return source;
        }

        private static AudioClip BuildMetalClip(string name, float duration, float gain, int seed, bool loopTexture)
        {
            const int rate = 24000;
            int sampleCount = Mathf.Max(1, Mathf.RoundToInt(duration * rate));
            float[] samples = new float[sampleCount];
            uint state = (uint)seed;
            float filtered = 0f;
            for (int i = 0; i < sampleCount; i++)
            {
                state = state * 1664525u + 1013904223u;
                float noise = ((state >> 8) & 0xFFFF) / 32767.5f - 1f;
                filtered = Mathf.Lerp(filtered, noise, 0.13f);
                float time = i / (float)rate;
                float rasp = Mathf.Sin(time * Mathf.PI * 2f * 116f)
                    + 0.43f * Mathf.Sin(time * Mathf.PI * 2f * 237f + 0.7f)
                    + 0.22f * Mathf.Sin(time * Mathf.PI * 2f * 487f + 1.9f);
                float envelope = loopTexture ? 1f : Mathf.Sin(Mathf.Clamp01(i / (float)(sampleCount - 1)) * Mathf.PI);
                samples[i] = (rasp * 0.16f + filtered * 0.34f) * gain * envelope;
            }
            AudioClip clip = AudioClip.Create(name, sampleCount, 1, rate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        public void Dispose()
        {
            foreach (var source in _slides) if (source != null) source.Stop();
            if (_oneShotSource != null) _oneShotSource.Stop();
            if (_root != null) UnityEngine.Object.Destroy(_root);
            if (_slideClip != null) UnityEngine.Object.Destroy(_slideClip);
            if (_deployClip != null) UnityEngine.Object.Destroy(_deployClip);
            if (_landingClip != null) UnityEngine.Object.Destroy(_landingClip);
            if (_retractClip != null) UnityEngine.Object.Destroy(_retractClip);
        }
    }
}

