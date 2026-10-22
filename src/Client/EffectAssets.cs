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
    public static AudioClip Stream => _stream ??= Clip("FlowingWater stream", WaterSounds.Stream());

    /// <summary>Water rushing down steep ground: a roar with a few chirps, a seamless loop.</summary>
    public static AudioClip Rapids => _rapids ??= Clip("FlowingWater rapids", WaterSounds.Rapids());

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
            Object.DontDestroyOnLoad(holder);
            _spray = Object.Instantiate(splash.gameObject, holder.transform);
            _spray.name = "FlowingWater spray";

            // Only the particles: the effect's own scripts would destroy it after its splash.
            foreach (Component component in _spray.GetComponentsInChildren<Component>(true))
            {
                if (component is not (Transform or ParticleSystem or ParticleSystemRenderer))
                {
                    Object.DestroyImmediate(component);
                }
            }
        }

        _mixer = ZNetScene.instance.GetPrefab(SoundPrefab)?.GetComponentInChildren<AudioSource>(true)?.outputAudioMixerGroup;
        if (_mixer == null)
        {
            FlowingWaterPlugin.Log.LogWarning($"No {SoundPrefab} mixer group in the game: running water ignores the volume settings");
        }
    }

    // A loop's samples as a clip. Filled through its reader (SetData's span overload cannot be told apart
    // from the array one on .NET Framework), which hands them out round the loop however often it is asked.
    private static AudioClip Clip(string name, float[] samples)
    {
        var reader = new LoopReader(samples);
        return AudioClip.Create(name, samples.Length, 1, WaterSounds.SampleRate, false, reader.Read, reader.SetPosition);
    }
}
