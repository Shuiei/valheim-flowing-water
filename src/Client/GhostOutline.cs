using System;
using System.Collections.Generic;
using FlowingWater.Volume;

namespace FlowingWater.Client;

/// <summary>
/// The lines a source's ghost draws over the ground while it is placed (MarkerGhost draws them), without the game, in
/// the ghost's own space (x to its right, y the way it faces), metres: facing a way, the outline of where its water
/// goes, its outlet's width fanned out by its spread as far as a jet lands (<see cref="Along"/> along the ground,
/// <see cref="MostOut"/> at the most), and an arrow out of the outlet's middle; all round, a ring round it and no
/// arrow (it faces no way: an arrow would say it pours one).
/// </summary>
internal static class GhostOutline
{
    /// <summary>How far out the outline goes for water along the ground, metres.</summary>
    public const float Along = 6f;

    /// <summary>The farthest out the outline goes for a jet's landing, metres.</summary>
    public const float MostOut = 20f;

    /// <summary>An all-round source's ring: its radius, metres, and its sides.</summary>
    public const float Ring = 1.5f;

    public const int RingSides = 32;

    /// <summary>How far out the outline goes: as far as a jet lands (<see cref="MostOut"/> at the most), else <see cref="Along"/>.</summary>
    public static float Far(SourceShape shape, float rate) => Math.Min(MostOut, SourcePreview.Lands(shape, rate) ?? Along);

    /// <summary>The outline's lines: all round, the ring's sides; facing a way, the outlet, its two sides fanned out, and its far end.</summary>
    public static List<((float X, float Y) From, (float X, float Y) To)> Outline(SourceShape shape, float rate)
    {
        var lines = new List<((float X, float Y) From, (float X, float Y) To)>();
        if (shape.AllRound)
        {
            for (int k = 0; k < RingSides; k++)
            {
                double a0 = k * 2.0 * Math.PI / RingSides, a1 = (k + 1) * 2.0 * Math.PI / RingSides;
                lines.Add((((float)Math.Sin(a0) * Ring, (float)Math.Cos(a0) * Ring), ((float)Math.Sin(a1) * Ring, (float)Math.Cos(a1) * Ring)));
            }

            return lines;
        }

        float half = shape.OutletWidth / 2f;
        float far = Far(shape, rate);
        float fan = (float)Math.Tan(shape.Spread / 2f * Math.PI / 180.0) * far;
        (float X, float Y) leftNear = (-half, 0f), rightNear = (half, 0f), leftFar = (-half - fan, far), rightFar = (half + fan, far);
        lines.Add((leftNear, rightNear));
        lines.Add((leftNear, leftFar));
        lines.Add((rightNear, rightFar));
        lines.Add((leftFar, rightFar));
        return lines;
    }

    /// <summary>The arrow the way it faces, a shaft out of the outlet's middle and a head (left, right, tip); none all round.</summary>
    public static ((float X, float Y) Tip, (float X, float Y) Left, (float X, float Y) Right, float Shaft)? Arrow(SourceShape shape, float rate)
    {
        if (shape.AllRound)
        {
            return null;
        }

        float length = Math.Min(2.5f, Far(shape, rate) * 0.6f);
        return ((0f, length + 0.6f), (-0.35f, length), (0.35f, length), length);
    }
}
