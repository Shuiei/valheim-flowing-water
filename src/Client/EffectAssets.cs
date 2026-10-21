using System;
using UnityEngine;
using UnityEngine.Audio;

namespace FlowingWater.Client;

/// <summary>
/// What white water is made of, found or made once: the game's own bow-splash particles (a ship's
/// <c>vfx_WaterImpact_Karve</c>) as a template for spray, the game's sound-effects mixer group (so the
/// player's volume settings apply), and a loop of running water made in code (the game has no such sound).
/// </summary>
internal static class EffectAssets
{
    private const string SprayPrefab = "vfx_WaterImpact_Karve";

    private const string SoundPrefab = "sfx_land_water";

    private const int SampleRate = 44100;

    private const float LoopSeconds = 6f;

    // Overlap cross-faded at the loop's seam, seconds.
    private const float SeamSeconds = 0.25f;

    private const float BubblesPerSecond = 25f;

    private static bool _looked;

    private static GameObject _spray;

    private static AudioMixerGroup _mixer;

    private static AudioClip _stream;

    /// <summary>The spray template: a particle system kept under an inactive parent; null when the game has none.</summary>
    public static GameObject Spray
    {
        get
        {
            Find();
            return _spray;
        }
    }

    /// <summary>The group the game's sound effects play through; null when none was found (then unmixed).</summary>
    public static AudioMixerGroup Mixer
    {
        get
        {
            Find();
            return _mixer;
        }
    }

    /// <summary>Running water, a seamless loop.</summary>
    public static AudioClip Stream => _stream ??= MakeStream();

    private static void Find()
    {
        if (_looked || ZNetScene.instance == null)
        {
            return;
        }

        _looked = true;
        ParticleSystem splash = ZNetScene.instance.GetPrefab(SprayPrefab)?.GetComponentInChildren<ParticleSystem>(true);
        if (splash == null)
        {
            FlowingWaterPlugin.Log.LogWarning($"No {SprayPrefab} particles in the game: white water runs without spray");
        }
        else
        {
            var holder = new GameObject("FlowingWater effect templates");
            holder.SetActive(false);
            UnityEngine.Object.DontDestroyOnLoad(holder);
            _spray = UnityEngine.Object.Instantiate(splash.gameObject, holder.transform);
            _spray.name = "FlowingWater spray";

            // Only the particles: the effect's own scripts would destroy it after its splash.
            foreach (Component component in _spray.GetComponentsInChildren<Component>(true))
            {
                if (component is not (Transform or ParticleSystem or ParticleSystemRenderer))
                {
                    UnityEngine.Object.DestroyImmediate(component);
                }
            }
        }

        _mixer = ZNetScene.instance.GetPrefab(SoundPrefab)?.GetComponentInChildren<AudioSource>(true)?.outputAudioMixerGroup;
        if (_mixer == null)
        {
            FlowingWaterPlugin.Log.LogWarning($"No {SoundPrefab} mixer group in the game: running water ignores the volume settings");
        }
    }

    // A hiss of filtered noise over a low rumble, with little bubbles (short rising chirps) here and
    // there; made a little longer than the loop and its end cross-faded into its start, so it never clicks.
    private static AudioClip MakeStream()
    {
        int length = (int)(SampleRate * LoopSeconds);
        int seam = (int)(SampleRate * SeamSeconds);
        float[] made = new float[length + seam];
        var random = new System.Random(20261021);
        float rumble = 0f;
        float hiss = 0f;
        for (int i = 0; i < made.Length; i++)
        {
            float white = (float)((random.NextDouble() * 2.0) - 1.0);
            rumble = (rumble * 0.98f) + (white * 0.1f);
            hiss += ((white * 0.15f) - hiss) * 0.25f;
            made[i] = (rumble * 0.6f) + (hiss * 0.8f);
        }

        for (int b = 0; b < (int)(BubblesPerSecond * LoopSeconds); b++)
        {
            Bubble(made, length, random);
        }

        float[] loop = new float[length];
        float peak = 0f;
        for (int i = 0; i < length; i++)
        {
            float into = i < seam ? (float)i / seam : 1f;
            loop[i] = i < seam ? (made[i] * into) + (made[length + i] * (1f - into)) : made[i];
            peak = Mathf.Max(peak, Mathf.Abs(loop[i]));
        }

        for (int i = 0; i < length; i++)
        {
            loop[i] *= 0.8f / peak;
        }

        // Filled through the reader (called at once, in pieces, for a clip that does not stream): SetData's
        // span overload cannot be told apart from the array one on .NET Framework.
        int read = 0;
        return AudioClip.Create("FlowingWater stream", length, 1, SampleRate, false, data =>
        {
            Array.Copy(loop, Math.Min(read, length), data, 0, Math.Max(0, Math.Min(data.Length, length - read)));
            read += data.Length;
        });
    }

    // One bubble: a sine rising by half its pitch over 15 to 45 ms, quick in and dying away; it wraps
    // round the loop's end.
    private static void Bubble(float[] samples, int length, System.Random random)
    {
        int start = random.Next(length);
        float pitch = 400f + (float)(random.NextDouble() * 1000.0);
        int span = (int)(SampleRate * (0.015 + (random.NextDouble() * 0.03)));
        float loudness = 0.05f + (float)(random.NextDouble() * 0.15);
        double phase = 0.0;
        for (int j = 0; j < span; j++)
        {
            float envelope = Mathf.Exp(-j / (span * 0.3f)) * (1f - Mathf.Exp(-j / 50f));
            phase += 2.0 * Math.PI * pitch * (1.0 + (0.5 * j / span)) / SampleRate;
            samples[(start + j) % length] += loudness * envelope * (float)Math.Sin(phase);
        }
    }
}
