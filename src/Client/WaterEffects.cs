using System.Collections.Generic;
using UnityEngine;

namespace FlowingWater.Client;

/// <summary>A place where a tile's water runs fast: its surface point, where it runs, how much and how steeply.</summary>
internal sealed class WaterSpot
{
    public WaterSpot(Vector3 position, Vector3 direction, float flow, float steep)
    {
        Position = position;
        Direction = direction;
        Flow = flow;
        Steep = steep;
    }

    /// <summary>The water surface there, world.</summary>
    public Vector3 Position { get; }

    /// <summary>Where it runs (unit, horizontal).</summary>
    public Vector3 Direction { get; }

    /// <summary>Speed times depth, square metres per second: how much water passes.</summary>
    public float Flow { get; }

    /// <summary>The surface's slope there (rise over run).</summary>
    public float Steep { get; }
}

/// <summary>
/// A tile's white water: at each of its spots (<see cref="WaterTile"/> finds them) a sound of running water,
/// louder the more water passes, a stream's babble turning into the roar of rapids as it runs steeper, and
/// spray where it does. The emitters are kept and moved, never
/// made again while the tile lives.
/// </summary>
internal sealed class WaterEffects : MonoBehaviour
{
    private const float SoundNear = 3f;

    private readonly List<Emitter> _emitters = [];

    /// <summary>Puts the effects at these spots (the strongest first); the emitters left over go quiet.</summary>
    public void Show(IReadOnlyList<WaterSpot> spots)
    {
        while (_emitters.Count < spots.Count)
        {
            _emitters.Add(new Emitter(transform, _emitters.Count));
        }

        bool spray = FlowingWaterPlugin.Settings.Spray.Value;
        float volume = FlowingWaterPlugin.Settings.WaterVolume.Value;
        Vector3 ear = GameCamera.instance != null ? GameCamera.instance.transform.position : transform.position;
        for (int i = 0; i < _emitters.Count; i++)
        {
            if (i < spots.Count)
            {
                _emitters[i].Set(spots[i], spray, volume, (spots[i].Position - ear).sqrMagnitude);
            }
            else
            {
                _emitters[i].Off();
            }
        }
    }

    /// <summary>One spot's sound and spray.</summary>
    private sealed class Emitter
    {
        private readonly GameObject _root;

        // A stream's babble, and the roar of rapids where it runs steep.
        private readonly AudioSource _stream;

        private readonly AudioSource _rapids;

        private readonly ParticleSystem _spray;

        // Where in the loop this spot's sounds start, seconds: its own, so that several never sound as one
        // (a sound stopped and played again would start at 0 like all the others).
        private readonly float _start;

        public Emitter(Transform parent, int index)
        {
            _root = new GameObject("FlowingWater white water " + index);
            _root.transform.SetParent(parent, false);
            _start = index * 0.9f % WaterSounds.LoopSeconds;
            _stream = Sound(EffectAssets.Stream, index);
            _rapids = Sound(EffectAssets.Rapids, index);
            if (EffectAssets.Spray != null)
            {
                // The template is kept under an inactive parent: the copy is switched on, whatever it was.
                GameObject copy = Instantiate(EffectAssets.Spray, _root.transform);
                copy.SetActive(true);
                _spray = copy.GetComponent<ParticleSystem>();
                SetUp(_spray);
            }
        }

        // A looping 3D sound on the spot; each spot at its own pitch and place in the loop, so that
        // several never sound as one.
        private AudioSource Sound(AudioClip clip, int index)
        {
            AudioSource sound = _root.AddComponent<AudioSource>();
            sound.clip = clip;
            sound.outputAudioMixerGroup = EffectAssets.Mixer;
            sound.loop = true;
            sound.spatialBlend = 1f;
            sound.rolloffMode = AudioRolloffMode.Linear;
            sound.minDistance = SoundNear;
            sound.maxDistance = WhiteWaterMix.SoundFar;
            sound.dopplerLevel = 0f;
            sound.pitch = 0.85f + (0.06f * (index % 5));
            return sound;
        }

        public void Set(WaterSpot spot, bool spray, float volume, float distanceSquared)
        {
            _root.transform.position = spot.Position;
            float loud = WhiteWaterMix.Loudness(spot.Flow, spot.Steep, volume, distanceSquared);
            float roar = WhiteWaterMix.Roar(spot.Steep);
            Play(_stream, loud * (1f - roar));
            Play(_rapids, loud * roar);
            if (_spray == null)
            {
                return;
            }

            float white = WhiteWaterMix.Spray(spot.Steep, spray);
            ParticleSystem.EmissionModule emission = _spray.emission;
            emission.rateOverTime = WhiteWaterMix.SprayRate(spot.Flow, white);

            // Thrown up and a little downstream.
            _spray.transform.rotation = Quaternion.LookRotation(Vector3.up + (spot.Direction * 0.5f));
            if (white > 0f && !_spray.isPlaying)
            {
                _spray.Play();
            }
        }

        public void Off()
        {
            _stream.Stop();
            _rapids.Stop();
            if (_spray != null)
            {
                ParticleSystem.EmissionModule emission = _spray.emission;
                emission.rateOverTime = 0f;
            }
        }

        // Out of earshot (or silent) a sound is stopped: it would still take one of the few voices the game
        // plays at once.
        private void Play(AudioSource sound, float volume)
        {
            sound.volume = volume;
            if (volume <= 0f)
            {
                sound.Stop();
            }
            else if (!sound.isPlaying)
            {
                sound.time = _start;
                sound.Play();
            }
        }

        // The bow splash made a steady spray: looping, no bursts, small drops thrown out of a narrow
        // cone and falling back, in the world (they stay where they were thrown).
        private static void SetUp(ParticleSystem spray)
        {
            spray.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = spray.main;
            main.loop = true;
            main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 3.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.5f, 1.5f);
            main.gravityModifier = 0.6f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 150;
            main.playOnAwake = false;
            ParticleSystem.EmissionModule emission = spray.emission;
            emission.SetBursts([]);
            emission.rateOverTime = 0f;
            ParticleSystem.ShapeModule shape = spray.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 25f;
            shape.radius = 0.4f;
            shape.rotation = Vector3.zero;
            spray.transform.localPosition = Vector3.zero;
            spray.transform.localScale = Vector3.one;
        }
    }
}
