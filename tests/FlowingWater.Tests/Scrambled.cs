using System;

namespace FlowingWater.Tests;

/// <summary>
/// Test data that looks random but is the same each run, from a seed: each number is the next of a counter, its bits
/// mixed (SplitMix64's steps), so that every bit of every byte varies. Not for anything secret: only to vary the
/// values a test feeds the mod.
/// </summary>
internal sealed class Scrambled
{
    private ulong _state;

    /// <summary>A sequence of its own for each seed.</summary>
    public Scrambled(int seed) => _state = (ulong)seed;

    /// <summary>The next 64 bits.</summary>
    public ulong Next()
    {
        _state += 0x9E3779B97F4A7C15UL;
        ulong z = _state;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>A whole number from <paramref name="min"/> up to, not including, <paramref name="max"/>.</summary>
    public int Next(int min, int max) => min + (int)(Next() % (ulong)(max - min));

    /// <summary>A number from 0 up to, not including, 1.</summary>
    public double NextDouble() => (Next() >> 11) * (1.0 / (1UL << 53));

    /// <summary>The bytes filled.</summary>
    public void Fill(Span<byte> bytes)
    {
        for (int i = 0; i < bytes.Length; i++)
        {
            bytes[i] = (byte)(Next() >> 56);
        }
    }
}
