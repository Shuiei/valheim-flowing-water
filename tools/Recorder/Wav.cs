using System;
using System.IO;
using System.Text;

namespace FlowingWater.Recorder;

/// <summary>Sound as a WAV file: 16-bit PCM, its samples (-1 to 1, interleaved by channel) clamped to that range.</summary>
internal static class Wav
{
    /// <summary>The whole file's bytes.</summary>
    /// <param name="samples">The samples, interleaved by channel.</param>
    /// <param name="channels">How many channels.</param>
    /// <param name="rate">Samples a second (per channel).</param>
    public static byte[] Encode(float[] samples, int channels, int rate)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true))
        {
            int bytes = samples.Length * 2;
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + bytes);
            writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)channels);
            writer.Write(rate);
            writer.Write(rate * channels * 2);
            writer.Write((short)(channels * 2));
            writer.Write((short)16);
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(bytes);
            foreach (float sample in samples)
            {
                writer.Write((short)(Math.Max(-1f, Math.Min(1f, sample)) * short.MaxValue));
            }
        }

        return stream.ToArray();
    }
}
