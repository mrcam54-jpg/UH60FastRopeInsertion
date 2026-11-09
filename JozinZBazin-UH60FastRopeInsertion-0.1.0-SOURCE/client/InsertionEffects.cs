using System;
using UnityEngine;

namespace UH60FastRopeInsertion
{
    internal sealed class InsertionEffects : IDisposable
    {
        private readonly GameObject _root;
        private readonly AudioSource _cableSource;
        private readonly AudioSource _oneShotSource;
        private readonly AudioClip _slideClip;
        private readonly AudioClip _deployClip;
        private readonly AudioClip _landingClip;
        private readonly AudioClip _retractClip;

        internal InsertionEffects()
        {
            _root = new GameObject("UH60FastRopeInsertion_Effects");
            UnityEngine.Object.DontDestroyOnLoad(_root);
            _cableSource = CreateAudioSource("CableSlideAudio", true);
            _oneShotSource = CreateAudioSource("CableOneShots", false);
            _slideClip = BuildMetalClip("UH60_CableSlide", 2.6f, 0.34f, 0x3195, true);
            _deployClip = BuildMetalClip("UH60_CableDeploy", 0.82f, 0.48f, 0x8421, false);
            _landingClip = BuildMetalClip("UH60_CableLanding", 0.34f, 0.46f, 0x1764, false);
            _retractClip = BuildMetalClip("UH60_CableRetract", 1.45f, 0.39f, 0x7729, false);
            _cableSource.clip = _slideClip;

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

        internal void BeginSlide()
        {
            if (!_cableSource.isPlaying)
            {
                _cableSource.loop = true;
                _cableSource.Play();
            }
        }

        internal void EndSlide()
        {
            _cableSource.Stop();
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
            if (_cableSource != null) _cableSource.Stop();
            if (_oneShotSource != null) _oneShotSource.Stop();
            if (_root != null) UnityEngine.Object.Destroy(_root);
            if (_slideClip != null) UnityEngine.Object.Destroy(_slideClip);
            if (_deployClip != null) UnityEngine.Object.Destroy(_deployClip);
            if (_landingClip != null) UnityEngine.Object.Destroy(_landingClip);
            if (_retractClip != null) UnityEngine.Object.Destroy(_retractClip);
        }
    }
}

