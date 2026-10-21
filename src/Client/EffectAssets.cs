using System;
using UnityEngine;
using UnityEngine.Audio;

namespace FlowingWater.Client;

/// <summary>
/// What white water is made of, found or made once: the game's own bow-splash particles (a ship's
/// <c>vfx_WaterImpact_Karve</c>) as a template for spray, the game's sound-effects mixer group (so the
/// player's volume settings apply), and two loops of running water made in code (the game has no such
/// sound): a stream's babble and the roar of rapids.
/// </summary>
internal static class EffectAssets
{
    private const string SprayPrefab = "vfx_WaterImpact_Karve";

    private const string SoundPrefab = "sfx_land_water";

    private const int SampleRate = 44100;

    private const float LoopSeconds = 6f;

    // Overlap cross-faded at the loop's seam, seconds.
    private const float SeamSeconds = 0.25f;

    // A stream: many small air bubbles ringing as they form (1 to 8 mm across, so 3 kHz down to 375 Hz).
    private const float StreamBubblesPerSecond = 220f;

    private const float SmallestBubble = 0.001f, LargestBubble = 0.008f;

    // Rapids: a few chirps in the roar.
    private const float RapidsBubblesPerSecond = 25f;

    private static bool _looked;

    private static GameObject _spray;

    private static AudioMixerGroup _mixer;

    private static AudioClip _stream;

    private static AudioClip _rapids;

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

    /// <summary>A stream running fast: the babble of its bubbles over a soft wash, a seamless loop.</summary>
    public static AudioClip Stream => _stream ??= MakeLoop("FlowingWater stream", FillStream);

    /// <summary>Water rushing down steep ground: a roar with a few chirps, a seamless loop.</summary>
    public static AudioClip Rapids => _rapids ??= MakeLoop("FlowingWater rapids", FillRapids);

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

    // A loop made a little longer than it plays, its end cross-faded into its start so that it never
    // clicks (what is written past the end by a bubble wraps round instead), brought to a peak of 0.8.
    private static AudioClip MakeLoop(string name, Action<float[], int, System.Random> fill)
    {
        int length = (int)(SampleRate * LoopSeconds);
        int seam = (int)(SampleRate * SeamSeconds);
        float[] made = new float[length + seam];
        fill(made, length, new System.Random(20261021));
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
        return AudioClip.Create(name, length, 1, SampleRate, false, data =>
        {
            Array.Copy(loop, Math.Min(read, length), data, 0, Math.Max(0, Math.Min(data.Length, length - read)));
            read += data.Length;
        });
    }

    // A stream: a quiet wash of noise kept between about 300 Hz and 2 kHz, under a dense babble of
    // bubbles (van den Doel's liquid sound model: each rings at 3 / radius Hz, damped by its pitch,
    // rising a little as it goes; small ones far more often than large, large ones louder).
    private static void FillStream(float[] samples, int length, System.Random random)
    {
        float low = 0f;
        float high = 0f;
        for (int i = 0; i < samples.Length; i++)
        {
            float white = (float)((random.NextDouble() * 2.0) - 1.0);
            high += (white - high) * 0.25f;
            low += (high - low) * 0.042f;
            samples[i] = (high - low) * 0.12f;
        }

        for (int b = 0; b < (int)(StreamBubblesPerSecond * LoopSeconds); b++)
        {
            double size = Math.Pow(random.NextDouble(), 2.0);
            double radius = SmallestBubble + ((LargestBubble - SmallestBubble) * size);
            Ring(samples, length, random.Next(length), 3.0 / radius, 0.1 + (random.NextDouble() * 0.2), 0.15 * Math.Pow(radius / LargestBubble, 0.8));
        }
    }

    // Rapids: a hiss of filtered noise over a low rumble, with a few rising chirps.
    private static void FillRapids(float[] samples, int length, System.Random random)
    {
        float rumble = 0f;
        float hiss = 0f;
        for (int i = 0; i < samples.Length; i++)
        {
            float white = (float)((random.NextDouble() * 2.0) - 1.0);
            rumble = (rumble * 0.98f) + (white * 0.1f);
            hiss += ((white * 0.15f) - hiss) * 0.25f;
            samples[i] = (rumble * 0.6f) + (hiss * 0.8f);
        }

        for (int b = 0; b < (int)(RapidsBubblesPerSecond * LoopSeconds); b++)
        {
            Chirp(samples, length, random);
        }
    }

    // One bubble ringing (van den Doel): a sine at its pitch, rising by rise times its damping each
    // second, dying away at a damping of 0.13 f + 0.0072 f^1.5 per second; it wraps round the loop's end.
    private static void Ring(float[] samples, int length, int start, double pitch, double rise, double loudness)
    {
        double damping = (0.13 * pitch) + (0.0072 * Math.Pow(pitch, 1.5));
        int span = (int)(SampleRate * 7.0 / damping);
        double phase = 0.0;
        for (int j = 0; j < span; j++)
        {
            double t = (double)j / SampleRate;
            phase += 2.0 * Math.PI * pitch * (1.0 + (rise * damping * t)) / SampleRate;
            samples[(start + j) % length] += (float)(loudness * Math.Exp(-damping * t) * Math.Sin(phase));
        }
    }

    // One chirp: a sine rising by half its pitch over 15 to 45 ms, quick in and dying away; it wraps
    // round the loop's end.
    private static void Chirp(float[] samples, int length, System.Random random)
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
