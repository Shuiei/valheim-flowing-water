using System;

namespace FlowingWater.Client;

/// <summary>
/// The sounds of running water, made in code (the game has none): a stream's babble and the roar of rapids,
/// each a seamless mono loop of samples between -0.8 and 0.8, the same every time.
/// </summary>
internal static class WaterSounds
{
    /// <summary>Samples a second.</summary>
    public const int SampleRate = 44100;

    /// <summary>A loop's length, seconds.</summary>
    public const float LoopSeconds = 6f;

    /// <summary>The loudest sample.</summary>
    public const float Peak = 0.8f;

    // Overlap cross-faded at the loop's seam, seconds.
    private const float SeamSeconds = 0.25f;

    // A stream: many small air bubbles ringing as they form (1 to 8 mm across, so 3 kHz down to 375 Hz).
    private const float StreamBubblesPerSecond = 220f;

    private const double SmallestBubble = 0.001, LargestBubble = 0.008;

    // Rapids: a few chirps in the roar.
    private const float RapidsBubblesPerSecond = 25f;

    private const int Seed = 20261021;

    /// <summary>A stream running fast: the babble of its bubbles over a soft wash.</summary>
    public static float[] Stream() => Loop(FillStream);

    /// <summary>Water rushing down steep ground: a roar with a few chirps.</summary>
    public static float[] Rapids() => Loop(FillRapids);

    // A loop made a little longer than it plays, its end cross-faded into its start so that it never
    // clicks (what a bubble writes past the end wraps round instead), brought to the peak.
    private static float[] Loop(Action<float[], int, Random> fill)
    {
        int length = (int)(SampleRate * LoopSeconds);
        int seam = (int)(SampleRate * SeamSeconds);
        float[] made = new float[length + seam];

        // Not for anything secret: a fixed seed so that the sound is the same on every start.
#pragma warning disable S2245
        fill(made, length, new Random(Seed));
#pragma warning restore S2245
        float[] loop = new float[length];
        float peak = 0f;
        for (int i = 0; i < length; i++)
        {
            float into = i < seam ? (float)i / seam : 1f;
            loop[i] = i < seam ? (made[i] * into) + (made[length + i] * (1f - into)) : made[i];
            peak = Math.Max(peak, Math.Abs(loop[i]));
        }

        for (int i = 0; i < length; i++)
        {
            loop[i] *= Peak / peak;
        }

        return loop;
    }

    // A stream: a quiet wash of noise kept between about 300 Hz and 2 kHz, under a dense babble of
    // bubbles (van den Doel's liquid sound model: each rings at 3 / radius Hz, damped by its pitch,
    // rising a little as it goes; small ones far more often than large, large ones louder).
    private static void FillStream(float[] samples, int length, Random random)
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
    private static void FillRapids(float[] samples, int length, Random random)
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
    private static void Chirp(float[] samples, int length, Random random)
    {
        int start = random.Next(length);
        double pitch = 400.0 + (random.NextDouble() * 1000.0);
        int span = (int)(SampleRate * (0.015 + (random.NextDouble() * 0.03)));
        double loudness = 0.05 + (random.NextDouble() * 0.15);
        double phase = 0.0;
        for (int j = 0; j < span; j++)
        {
            double envelope = Math.Exp(-j / (span * 0.3)) * (1.0 - Math.Exp(-j / 50.0));
            phase += 2.0 * Math.PI * pitch * (1.0 + (0.5 * j / span)) / SampleRate;
            samples[(start + j) % length] += (float)(loudness * envelope * Math.Sin(phase));
        }
    }
}

/// <summary>
/// Hands a loop's samples out in pieces, as an audio clip's reader asks for them: from where it was set,
/// round the loop's end and on, however often it is asked (never silent).
/// </summary>
/// <param name="loop">The samples.</param>
internal sealed class LoopReader(float[] loop)
{
    private int _position;

    /// <summary>The next samples, from the current position, wrapping round.</summary>
    /// <param name="data">Filled whole.</param>
    public void Read(float[] data)
    {
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = loop[_position];
            _position = (_position + 1) % loop.Length;
        }
    }

    /// <summary>Where the next read starts (wrapped into the loop).</summary>
    /// <param name="position">A sample index.</param>
    public void SetPosition(int position) => _position = ((position % loop.Length) + loop.Length) % loop.Length;
}
