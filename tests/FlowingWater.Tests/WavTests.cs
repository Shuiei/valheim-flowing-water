using System;
using System.Text;
using FlowingWater.Recorder;
using Xunit;

namespace FlowingWater.Tests;

/// <summary>The recorder's sound files: a 16-bit PCM WAV header that matches the samples, the samples clamped and scaled.</summary>
public class WavTests
{
    [Fact]
    public void TheHeaderSaysSixteenBitPcmWithTheChannelsAndRate()
    {
        byte[] wav = Wav.Encode(new float[6], 2, 48000);

        Assert.Equal("RIFF", Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal(36 + 12, BitConverter.ToInt32(wav, 4));
        Assert.Equal("WAVEfmt ", Encoding.ASCII.GetString(wav, 8, 8));
        Assert.Equal(1, BitConverter.ToInt16(wav, 20));
        Assert.Equal(2, BitConverter.ToInt16(wav, 22));
        Assert.Equal(48000, BitConverter.ToInt32(wav, 24));
        Assert.Equal(48000 * 2 * 2, BitConverter.ToInt32(wav, 28));
        Assert.Equal(4, BitConverter.ToInt16(wav, 32));
        Assert.Equal(16, BitConverter.ToInt16(wav, 34));
        Assert.Equal("data", Encoding.ASCII.GetString(wav, 36, 4));
        Assert.Equal(12, BitConverter.ToInt32(wav, 40));
        Assert.Equal(44 + 12, wav.Length);
    }

    [Fact]
    public void SamplesAreScaledAndClampedToSixteenBits()
    {
        byte[] wav = Wav.Encode([-2f, -1f, 0f, 0.5f, 1f, 2f], 1, 44100);

        short[] written = new short[6];
        for (int i = 0; i < written.Length; i++)
        {
            written[i] = BitConverter.ToInt16(wav, 44 + (i * 2));
        }

        Assert.Equal([(short)-32767, (short)-32767, (short)0, (short)16383, (short)32767, (short)32767], written);
    }
}
