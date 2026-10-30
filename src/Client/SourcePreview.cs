using System;
using FlowingWater.Volume;

namespace FlowingWater.Client;

/// <summary>
/// The panel's two small drawings of a source's water, without the game, as RGBA bytes a texture takes (its rows from
/// the bottom up): from above, its outlet and how it fans out the way it faces (down the drawing; all round, a ring
/// round it); from the side, its throw's arc out to where it lands, as fast as its outlet sends it (or along the ground),
/// as thick as it is wide.
/// </summary>
internal static class SourcePreview
{
    /// <summary>A drawing's width, pixels.</summary>
    public const int Width = 160;

    /// <summary>A drawing's height, pixels.</summary>
    public const int Height = 120;

    // Metres a side-view pixel stands for, the width the top view's outlet spans at its widest, and its length.
    private const float SideScale = 1f / 7f;
    private const float TopHalfWidth = 60f, TopLength = 96f;

    private static readonly byte[] Ground = [31, 42, 34, 255];
    private static readonly byte[] Water = [95, 179, 201, 255];
    private static readonly byte[] Source = [47, 111, 214, 255];
    private static readonly byte[] Bed = [107, 85, 56, 255];

    /// <summary>The view from above.</summary>
    public static byte[] Top(SourceShape shape)
    {
        byte[] pixels = Blank();
        if (shape.AllRound)
        {
            Fill(pixels, (x, y) => Math.Sqrt(((x - 80) * (x - 80)) + ((y - 60) * (y - 60))) <= 46.0, Water, 0.45f);
            Fill(pixels, (x, y) => Math.Sqrt(((x - 80) * (x - 80)) + ((y - 60) * (y - 60))) <= 7.0, Source, 1f);
            return pixels;
        }

        float hw = shape.Width / SourceShape.MostWidth * TopHalfWidth;
        float spread = (float)Math.Tan(shape.Spread / 2f * Math.PI / 180.0) * TopLength;
        Fill(pixels, (x, y) => y >= 18 && y <= 18 + TopLength && Math.Abs(x - 80f) <= hw + 1f + ((y - 18) / TopLength * spread), Water, 0.45f);
        float rx = Math.Max(6f, hw);
        Fill(pixels, (x, y) => (((x - 80f) * (x - 80f)) / (rx * rx)) + (((y - 14f) * (y - 14f)) / 36f) <= 1f, Source, 1f);
        return pixels;
    }

    /// <summary>The view from the side, for a rate (the arc's length goes by the outlet's speed).</summary>
    public static byte[] Side(SourceShape shape, float rate)
    {
        byte[] pixels = Blank();
        Fill(pixels, (_, y) => y is >= 103 and <= 105, Bed, 1f);
        float thick = 2f + (4f * Math.Min(1f, shape.Width / 4f));
        float lastX = 14f, lastY = 100f;
        foreach ((float x, float y) in Arc(shape, rate))
        {
            Line(pixels, lastX, lastY, x, y, thick);
            lastX = x;
            lastY = y;
        }

        Fill(pixels, (x, y) => ((x - 14) * (x - 14)) + ((y - 100) * (y - 100)) <= 36, Source, 1f);
        return pixels;
    }

    /// <summary>How far out its water lands, metres: where its arc meets the ground again (null along the ground or all round).</summary>
    public static float? Lands(SourceShape shape, float rate)
    {
        if (!shape.Flies)
        {
            return null;
        }

        float v = shape.Speed(rate);
        double up = shape.Throw * Math.PI / 180.0;
        return (float)(v * v * Math.Sin(2.0 * up) / VoxelSolver.Gravity);
    }

    // The arc's points in the side view: a jet's flight out at its speed and throw, cut at the drawing's edges; along
    // the ground (or all round), a straight run across it.
    private static (float X, float Y)[] Arc(SourceShape shape, float rate)
    {
        if (!shape.Flies)
        {
            return [(156f, 101f)];
        }

        float v = shape.Speed(rate);
        double up = shape.Throw * Math.PI / 180.0;
        double flight = (2.0 * v * Math.Sin(up) / VoxelSolver.Gravity) + 0.05;
        var points = new (float X, float Y)[25];
        for (int i = 1; i <= 24; i++)
        {
            double t = flight * i / 24.0;
            double x = v * Math.Cos(up) * t, y = (v * Math.Sin(up) * t) - (0.5 * VoxelSolver.Gravity * t * t);
            points[i - 1] = (Math.Min(156f, 14f + (float)(x / SideScale)), Math.Max(4f, 100f - (float)(y / SideScale)));
        }

        points[24] = (156f, 101f);
        return points;
    }

    private static byte[] Blank()
    {
        byte[] pixels = new byte[Width * Height * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            Array.Copy(Ground, 0, pixels, i, 4);
        }

        return pixels;
    }

    // Every pixel the test takes (x right, y down from the drawing's top) given the colour, mixed over by this much.
    private static void Fill(byte[] pixels, Func<float, float, bool> inside, byte[] colour, float alpha)
    {
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                if (inside(x + 0.5f, y + 0.5f))
                {
                    Paint(pixels, x, y, colour, alpha);
                }
            }
        }
    }

    // A line this thick between two points.
    private static void Line(byte[] pixels, float x0, float y0, float x1, float y1, float thick)
    {
        float half = thick / 2f;
        int left = Math.Max(0, (int)(Math.Min(x0, x1) - half)), right = Math.Min(Width - 1, (int)(Math.Max(x0, x1) + half));
        int top = Math.Max(0, (int)(Math.Min(y0, y1) - half)), bottom = Math.Min(Height - 1, (int)(Math.Max(y0, y1) + half));
        float dx = x1 - x0, dy = y1 - y0, length2 = Math.Max(1e-6f, (dx * dx) + (dy * dy));
        for (int y = top; y <= bottom; y++)
        {
            for (int x = left; x <= right; x++)
            {
                float px = x + 0.5f, py = y + 0.5f;
                float t = Math.Min(1f, Math.Max(0f, (((px - x0) * dx) + ((py - y0) * dy)) / length2));
                float ex = px - (x0 + (t * dx)), ey = py - (y0 + (t * dy));
                if ((ex * ex) + (ey * ey) <= half * half)
                {
                    Paint(pixels, x, y, Water, 0.8f);
                }
            }
        }
    }

    // A pixel (y from the top) mixed toward a colour, in the texture's rows from the bottom up.
    private static void Paint(byte[] pixels, int x, int y, byte[] colour, float alpha)
    {
        int i = ((((Height - 1) - y) * Width) + x) * 4;
        for (int c = 0; c < 3; c++)
        {
            pixels[i + c] = (byte)Math.Round((pixels[i + c] * (1f - alpha)) + (colour[c] * alpha));
        }

        pixels[i + 3] = 255;
    }

    /// <summary>Whether the pixel at x, y (from the drawing's top left) is not the ground's colour: something is drawn there.</summary>
    public static bool Drawn(byte[] pixels, int x, int y)
    {
        int i = ((((Height - 1) - y) * Width) + x) * 4;
        return pixels[i] != Ground[0] || pixels[i + 1] != Ground[1] || pixels[i + 2] != Ground[2];
    }
}
