using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using FlowingWater.Volume;
using UnityEngine;
using UnityEngine.Audio;
using Object = UnityEngine.Object;

namespace FlowingWater.Client;

/// <summary>
/// The sound of running water round the camera. Twice a second, the places of the water the server sent that run fast
/// or fall (<see cref="SoundSpots"/>: a brick's found again only once its water changed), the strongest few within
/// earshot; each played by one of a few emitters, a stream's babble turning into the roar of rapids as it runs steeper,
/// louder the more water passes (<see cref="SoundMix"/>), through the game's sound-effects mixer so that the player's
/// volume settings apply. An emitter fades from one loudness to the next, and goes to a place far from its last only
/// once it is silent, so that no sound jumps. Main thread only.
/// </summary>
internal static class WaterSound
{
    // How often the places heard are looked for, seconds.
    private const float LookSeconds = 0.5f;

    // How long a sound takes to fade from silent to full, or back, seconds.
    private const float FadeSeconds = 0.6f;

    // How fast an emitter follows its place while it sounds, metres a second (a place moves a little from look to look).
    private const float Follow = 4f;

    // Heard at full loudness up to this far, metres, fading out to SoundMix.SoundFar.
    private const float SoundNear = 3f;

    // The most a frame gives to finding bricks' places, milliseconds: those left wait for the next frames.
    private const double FindBudget = 0.3;

    // A sound of the game's whose mixer group running water plays through (the sound effects').
    private const string MixerPrefab = "sfx_land_water";

    private static readonly FaultGate Gate = new(
        "The running water's sound failed; it is silent until a world opens",
        FlowingWaterPlugin.Log.LogError,
        Silence);

    // Each brick's places, by its coordinates, with the view they were found in.
    private static readonly Dictionary<(int X, int Y, int Z), (BrickView View, List<Spot> Spots)> Found = [];

    private static readonly HashSet<(int X, int Y, int Z)> Near = [];

    private static readonly List<(int X, int Y, int Z)> Away = [];

    private static readonly List<Spot> Candidates = [];

    private static readonly List<Spot> Chosen = [];

    private static readonly List<Emitter> Emitters = [];

    private static readonly List<Emitter> Free = [];

    private static GameObject _root;

    private static AudioClip _stream;

    private static AudioClip _rapids;

    private static AudioMixerGroup _mixer;

    private static float _lookedAt = float.NegativeInfinity;

    /// <summary>Once a frame, after the water's: the sounds faded toward their places, which are looked for twice a second.</summary>
    public static void Tick() => Gate.Run(TickOn);

    /// <summary>A world opens: the last one's sounds are gone, and off after a fault, it has a fresh try.</summary>
    public static void WorldOpened()
    {
        Silence();
        Gate.Reset();
    }

    /// <summary>What is heard, for the console ("flowingwater sound").</summary>
    public static string Describe()
    {
        if (!Gate.On)
        {
            return "running water: silent after a fault (see the log)";
        }

        int bricks = 0, places = 0;
        foreach ((BrickView _, List<Spot> spots) in Found.Values)
        {
            bricks++;
            places += spots.Count;
        }

        var text = new StringBuilder(FormattableString.Invariant(
            $"running water: {places} places heard of in {bricks} bricks within {SoundMix.SoundFar:0} m, {Chosen.Count} sounding, volume {FlowingWaterPlugin.Settings.WaterVolume.Value:0.##}"));
        foreach (Emitter emitter in Emitters)
        {
            text.Append("; ").Append(emitter.Describe());
        }

        return text.ToString();
    }

    private static void TickOn()
    {
        // (Unity's own null check, which ?. would pass by: a camera gone with its scene is not null to C#.)
        GameCamera game = GameCamera.instance;
        if (game == null)
        {
            return;
        }

        Camera camera = game.GetComponent<Camera>();
        if (camera == null)
        {
            return;
        }

        if (_root == null)
        {
            Make();
        }

        Vector3 ear = camera.transform.position;
        Find(ear);
        if (Time.time - _lookedAt >= LookSeconds)
        {
            _lookedAt = Time.time;
            Look(ear, FlowingWaterPlugin.Settings.WaterVolume.Value);
        }

        foreach (Emitter emitter in Emitters)
        {
            emitter.Step(Time.deltaTime);
        }
    }

    // The emitters made (again, after the scene took the last ones), silent until a look gives them a place.
    private static void Make()
    {
        Emitters.Clear();
        Found.Clear();
        _root = new GameObject("FlowingWater running water");
        _stream ??= Clip("FlowingWater stream", WaterSounds.Stream());
        _rapids ??= Clip("FlowingWater rapids", WaterSounds.Rapids());
        if (_mixer == null && ZNetScene.instance != null)
        {
            _mixer = ZNetScene.instance.GetPrefab(MixerPrefab)?.GetComponentInChildren<AudioSource>(true)?.outputAudioMixerGroup;
            if (_mixer == null)
            {
                FlowingWaterPlugin.Log.LogWarning($"No {MixerPrefab} mixer group in the game: running water ignores the sound-effects volume");
            }
        }

        for (int i = 0; i < SoundSpots.Max; i++)
        {
            Emitters.Add(new Emitter(_root.transform, i, _stream, _rapids, _mixer));
        }
    }

    // The places of the bricks within earshot, each brick's found again once its water changed, as many as the frame's
    // budget allows (the others keep their last until a frame comes to them); bricks out of earshot forgotten.
    private static void Find(Vector3 ear)
    {
        Near.Clear();
        var watch = Stopwatch.StartNew();
        foreach (BrickView view in VoxelWater.Field.Bricks)
        {
            float span = Brick.Size * view.VoxelSize;
            var middle = new Vector3((view.X + 0.5f) * span, (view.Y + 0.5f) * span, (view.Z + 0.5f) * span);
            if ((middle - ear).magnitude > SoundMix.SoundFar + (span * 0.87f))
            {
                continue;
            }

            (int, int, int) at = (view.X, view.Y, view.Z);
            Near.Add(at);
            if ((!Found.TryGetValue(at, out (BrickView View, List<Spot> Spots) found) || found.View != view) && watch.Elapsed.TotalMilliseconds < FindBudget)
            {
                Found[at] = (view, SoundSpots.Of(view));
            }
        }

        Away.Clear();
        foreach ((int X, int Y, int Z) at in Found.Keys)
        {
            if (!Near.Contains(at))
            {
                Away.Add(at);
            }
        }

        foreach ((int X, int Y, int Z) at in Away)
        {
            Found.Remove(at);
        }
    }

    // The strongest few places found within earshot chosen, each given to the emitter nearest it, the others hushed.
    private static void Look(Vector3 ear, float volume)
    {
        Candidates.Clear();
        foreach ((BrickView _, List<Spot> spots) in Found.Values)
        {
            Candidates.AddRange(spots);
        }

        SoundSpots.Pick(Candidates, (ear.x, ear.y, ear.z), SoundMix.SoundFar, SoundSpots.Max, Chosen);
        Free.Clear();
        Free.AddRange(Emitters);
        foreach (Spot spot in Chosen)
        {
            var place = new Vector3(spot.X, spot.Y, spot.Z);
            Emitter nearest = Free[0];
            foreach (Emitter emitter in Free)
            {
                if (emitter.Away(place) < nearest.Away(place))
                {
                    nearest = emitter;
                }
            }

            Free.Remove(nearest);
            nearest.Aim(place, SoundMix.Loudness(spot.Flow, spot.Steep, volume, (place - ear).sqrMagnitude), SoundMix.Roar(spot.Steep));
        }

        foreach (Emitter emitter in Free)
        {
            emitter.Hush();
        }
    }

    // The sounds gone: their object put away (with the scene's, or after a fault), the places forgotten.
    private static void Silence()
    {
        if (_root != null)
        {
            Object.Destroy(_root);
        }

        _root = null;
        Emitters.Clear();
        Found.Clear();
        Chosen.Clear();
    }

    // A loop's samples as a clip. Filled through its reader (SetData's span overload cannot be told apart from the array
    // one on .NET Framework), which hands them out round the loop however often it is asked.
    private static AudioClip Clip(string name, float[] samples)
    {
        var reader = new LoopReader(samples);
        return AudioClip.Create(name, samples.Length, 1, WaterSounds.SampleRate, false, reader.Read, reader.SetPosition);
    }

    // One place's sound: a stream's babble and the roar of rapids, mixed by how steeply it runs.
    private sealed class Emitter
    {
        private readonly GameObject _object;

        private readonly AudioSource _stream;

        private readonly AudioSource _rapids;

        // Where in the loop its sounds start, seconds: its own, so that several never sound as one (a sound stopped
        // and played again would start at 0 like all the others).
        private readonly float _start;

        private Vector3 _place;

        private float _loud;

        private float _roar;

        private bool _placed;

        public Emitter(Transform parent, int index, AudioClip stream, AudioClip rapids, AudioMixerGroup mixer)
        {
            _object = new GameObject("FlowingWater running water " + index);
            _object.transform.SetParent(parent, false);
            _start = index * 0.9f % WaterSounds.LoopSeconds;
            _stream = Sound(stream, index, mixer);
            _rapids = Sound(rapids, index, mixer);
        }

        // How far it is from a place, metres; one never placed is as far as can be.
        public float Away(Vector3 place) => _placed ? (place - _object.transform.position).magnitude : float.MaxValue;

        public void Aim(Vector3 place, float loud, float roar)
        {
            _place = place;
            _loud = loud;
            _roar = roar;
        }

        public void Hush() => _loud = 0f;

        // Its loudness faded toward its place's, and it moved there: following a place near it, going to one far off
        // only once silent.
        public void Step(float seconds)
        {
            bool far = !_placed || (_place - _object.transform.position).sqrMagnitude > SoundSpots.Spacing * SoundSpots.Spacing;
            bool silent = _stream.volume <= 0f && _rapids.volume <= 0f;
            float stream = _loud * (1f - _roar), rapids = _loud * _roar;
            if (far && !silent)
            {
                stream = 0f;
                rapids = 0f;
            }
            else if (far)
            {
                _object.transform.position = _place;
                _placed = true;
            }
            else
            {
                _object.transform.position = Vector3.MoveTowards(_object.transform.position, _place, Follow * seconds);
            }

            float step = seconds / FadeSeconds;
            Play(_stream, Mathf.MoveTowards(_stream.volume, stream, step));
            Play(_rapids, Mathf.MoveTowards(_rapids.volume, rapids, step));
        }

        public string Describe()
        {
            Vector3 at = _object.transform.position;
            return FormattableString.Invariant(
                $"({at.x:0},{at.y:0},{at.z:0}) stream {_stream.volume:0.00}{(_stream.isPlaying ? " playing" : "")} rapids {_rapids.volume:0.00}{(_rapids.isPlaying ? " playing" : "")}");
        }

        // A looping 3D sound, at its own pitch, so that several never sound as one.
        private AudioSource Sound(AudioClip clip, int index, AudioMixerGroup mixer)
        {
            AudioSource sound = _object.AddComponent<AudioSource>();
            sound.clip = clip;
            sound.outputAudioMixerGroup = mixer;
            sound.loop = true;
            sound.playOnAwake = false;
            sound.volume = 0f;
            sound.spatialBlend = 1f;
            sound.rolloffMode = AudioRolloffMode.Linear;
            sound.minDistance = SoundNear;
            sound.maxDistance = SoundMix.SoundFar;
            sound.dopplerLevel = 0f;
            sound.pitch = 0.85f + (0.06f * (index % 5));
            return sound;
        }

        // Silent, a sound is stopped: it would still take one of the few voices the game plays at once.
        private void Play(AudioSource sound, float volume)
        {
            sound.volume = volume;
            if (volume <= 0f)
            {
                if (sound.isPlaying)
                {
                    sound.Stop();
                }
            }
            else if (!sound.isPlaying)
            {
                sound.time = _start;
                sound.Play();
            }
        }
    }
}
