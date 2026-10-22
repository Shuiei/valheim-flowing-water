using System.Collections.Generic;
using UnityEngine;

namespace FlowingWater.Recorder;

/// <summary>
/// What the player hears: put on the object holding the game's audio listener, it keeps every sample
/// mixed for it while <see cref="On"/> is set (the audio thread calls it; the samples are read once it is off).
/// </summary>
public sealed class AudioTap : MonoBehaviour
{
    private static readonly List<float> Kept = [];

    private static volatile bool _on;

    /// <summary>Whether samples are being kept.</summary>
    public static bool On
    {
        get => _on;
        set
        {
            if (value)
            {
                lock (Kept)
                {
                    Kept.Clear();
                }
            }

            _on = value;
        }
    }

    /// <summary>Channels of the samples kept (interleaved).</summary>
    public static int Channels { get; private set; } = 2;

    /// <summary>The samples kept so far.</summary>
    public static float[] Samples()
    {
        lock (Kept)
        {
            return [.. Kept];
        }
    }

    private void OnAudioFilterRead(float[] data, int channels)
    {
        if (!_on)
        {
            return;
        }

        Channels = channels;
        lock (Kept)
        {
            Kept.AddRange(data);
        }
    }
}
