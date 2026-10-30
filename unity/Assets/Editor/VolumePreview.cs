using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using FlowingWater.Client;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

// Runs the volume water (the plugin's VolumeWater, FlowingWaterVolume.compute) on scenes without the game, and writes
// what it measured (volume.txt) and frames (PNG). VolumeWater.cs is the plugin's own, copied into Assets/Editor/Shared
// by tools/sim-preview.sh, which runs this with a display:
//   tools/sim-preview.sh /some/folder VolumePreview.Run
// FW_PREVIEW_SCENES picks scenes; FW_PREVIEW_ITERATIONS, FW_PREVIEW_FLIP, FW_PREVIEW_DRIFT and FW_PREVIEW_SPHERES=1
// try other settings and the particles drawn as spheres.
public static class VolumePreview
{
    // The ground's cells (the card's water's), and the grid's: the same size, the grid over the whole square.
    private const int Cells = 160;

    // The cells' size: 0.25 m, or FW_PREVIEW_DX's (the game's GpuWaterCell, to try a scene at another).
    private static readonly float Dx = float.TryParse(Environment.GetEnvironmentVariable("FW_PREVIEW_DX"), NumberStyles.Float, CultureInfo.InvariantCulture, out float dx) && dx > 0f ? dx : 0.25f;

    private const int Rows = 48;

    private const float Frame = 1f / 60f;

    private static string _output;

    private static readonly StringBuilder Log = new();

    public static void Run()
    {
        try
        {
            _output = Environment.GetEnvironmentVariable("FW_PREVIEW_OUT") ?? Path.GetFullPath("preview");
            Directory.CreateDirectory(_output);
            SimPreview.Output = _output;
            BuildBundles.Prepare();
            string[] chosen = (Environment.GetEnvironmentVariable("FW_PREVIEW_SCENES") ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries);
            string[] known = { "rest", "dambreak", "ledge", "spring", "box", "fence", "drain", "follow", "slope", "bigspring", "wader" };
            string[] unknown = chosen.Where(name => Array.IndexOf(known, name) < 0).ToArray();
            if (unknown.Length > 0)
            {
                throw new ArgumentException($"FW_PREVIEW_SCENES: no scene {string.Join(", ", unknown)} ({string.Join(", ", known)})");
            }

            bool Runs(string scene) => chosen.Length == 0 || Array.IndexOf(chosen, scene) >= 0;
            if (Runs("rest"))
            {
                Rest();
            }

            if (Runs("dambreak"))
            {
                DamBreak();
            }

            if (Runs("ledge"))
            {
                Ledge();
            }

            if (Runs("spring"))
            {
                Spring();
            }

            if (Runs("box"))
            {
                Box(false);
            }

            if (Runs("fence"))
            {
                Box(true);
            }

            if (Runs("drain"))
            {
                Drain();
            }

            if (Runs("follow"))
            {
                Follow();
            }

            if (Runs("slope"))
            {
                Slope();
            }

            if (Runs("bigspring"))
            {
                BigSpring();
            }

            if (Runs("wader"))
            {
                Wader();
            }

            File.WriteAllText(Path.Combine(_output, "volume.txt"), Log.ToString());
            Debug.Log("FlowingWater volume preview:\n" + Log);
        }
        catch (Exception e)
        {
            Debug.LogError("FlowingWater volume preview failed: " + e);
            File.WriteAllText(Path.Combine(_output ?? ".", "volume.txt"), Log + "\nFAILED: " + e);
            EditorApplication.Exit(1);
            throw;
        }
    }

    // A box 4 m square filled 1 m deep: the water lies still, keeps its depth and its count.
    private static void Rest()
    {
        float[] bed = Field((x, z) => x < 18f || x > 22f || z < 18f || z > 22f ? 10f : 0f);
        using var scene = new Scene("vrest", bed);
        int added = scene.Water.Add(new Vector3(18f, 0f, 18f), new Vector3(22f, 1f, 22f));
        Log.AppendLine(Inv($"Rest: a box 4 m square filled 1 m deep: {added} particles ({added * scene.Water.Litre:0.0} m3; {scene.Water.Iterations} sweeps, FLIP {scene.Water.Flip}, drift {scene.Water.Drift})."));
        bool ok = true;
        for (int k = 1; k <= 300; k++)
        {
            scene.Step();
            if (k <= 12 || k % 60 == 0)
            {
                Measure m = Measure.Of(scene.Water, bed);
                if (k <= 12)
                {
                    float[] data = scene.Water.Read();
                    int p0 = 0, p1 = data.Length / 12 - 1;
                    Log.AppendLine(Inv($"  step {k}: level {m.Level:0.000} top {m.Top:0.000} mean {m.Speed:0.0000} fastest {m.Fastest:0.000}; first at ({data[p0 * 12]:0.00},{data[p0 * 12 + 1]:0.00},{data[p0 * 12 + 2]:0.00}) v ({data[p0 * 12 + 8]:0.000},{data[p0 * 12 + 9]:0.000},{data[p0 * 12 + 10]:0.000}) density {data[p0 * 12 + 11]:0.00}; last at ({data[p1 * 12]:0.00},{data[p1 * 12 + 1]:0.00},{data[p1 * 12 + 2]:0.00}) v ({data[p1 * 12 + 8]:0.000},{data[p1 * 12 + 9]:0.000},{data[p1 * 12 + 10]:0.000})"));
                    continue;
                }

                bool near = Mathf.Abs(m.Level - 1f) < 0.05f && m.Speed < 0.05f && m.Count == added;
                ok &= k < 120 || near;
                Log.AppendLine(Inv($"  t={k * Frame:0.0}s {m.Count} particles, the water {m.Level:0.000} m deep (1 at rest), moving {m.Speed:0.0000} m/s on average, {m.Fastest:0.000} at most, {m.Buried} in the ground{(k >= 120 ? (near ? ": OK" : ": FAILED") : string.Empty)}"));
            }

            if (k is 60 or 300)
            {
                scene.Shot(Inv($"vrest_{k * Frame:0.0}s"), new Vector3(20f, 3.5f, 14f), new Vector3(20f, 0.6f, 20f));
            }

            if (k == 300)
            {
                // From under the water, looking along the pool toward its far wall and the air above.
                scene.Shot("vrest_under", new Vector3(20f, 0.4f, 18.3f), new Vector3(20f, 1.1f, 22f));
            }
        }

        Log.AppendLine($"  Still, 1 m deep, no particle lost or made over 5 s: {Check(ok)}");
        Froth froth = Froth.Of(scene.Water);
        Log.AppendLine(Inv($"  Froth made by still water: {froth.Count}: {Check(froth.Count == 0)}"));
    }

    // The dam break: a column 2.5 m long, 2 m deep let go in a tank 10 m long; the front reaches the far wall at about
    // 1.3 to 1.5 s (Ritter's 2 sqrt(g h) gives 1.2 s), and the water comes to rest 0.5 m deep.
    private static void DamBreak()
    {
        float[] bed = Field((x, z) => x < 10f || x > 20f || z < 18f || z > 22f ? 10f : 0f);
        using var scene = new Scene("vdambreak", bed);
        int added = scene.Water.Add(new Vector3(10f, 0f, 18f), new Vector3(12.5f, 2f, 22f));
        Log.AppendLine(Inv($"Dam break: 10 m tank, a 2.5 m x 2 m column: {added} particles ({added * scene.Water.Litre:0.0} m3)."));
        float hit = -1f;
        float highest = 0f;
        // 20 s: a tank 10 m long, half a metre deep, sloshes for some 9 s a swing, and is still well after 8 s.
        for (int k = 1; k <= 1200; k++)
        {
            scene.Step();
            if (k % 120 == 0 && k > 240)
            {
                Measure now = Measure.Of(scene.Water, bed);
                Log.AppendLine(Inv($"  t={k * Frame:0}s its columns' tops {MeanTop(scene.Water, 0.5f):0.000} m on average, moving {now.Speed:0.000} m/s on average, fastest {now.Fastest:0.00} m/s"));
            }

            if (k % 12 == 0 && k <= 240)
            {
                float[] data = scene.Water.Read();
                float front = 10f, wall = 0f;
                for (int p = 0; p < data.Length / 12; p++)
                {
                    front = Math.Max(front, data[p * 12]);
                    if (data[p * 12] > 19.5f)
                    {
                        wall = Math.Max(wall, data[(p * 12) + 1]);
                    }
                }

                if (hit < 0f && front >= 19.9f)
                {
                    hit = k * Frame;
                }

                highest = Math.Max(highest, wall);
                Log.AppendLine(Inv($"  t={k * Frame:0.00}s front {front - 10f:0.00} m from the tank's start, at the far wall up to {wall:0.00} m; froth {Froth.Of(scene.Water)}"));
            }

            if (k is 15 or 30 or 60 or 90 or 120 or 180 or 480 or 1200)
            {
                scene.Shot(Inv($"vdambreak_{k * Frame:0.00}s"), new Vector3(15f, 3f, 12f), new Vector3(15f, 0.8f, 20f));
            }
        }

        // Once still, its level from the top of each column (the tenth highest particles and half a spacing over them
        // stand some 6 cm over the surface of water whose particles lie as they fell, not in a lattice).
        Measure rest = Measure.Of(scene.Water, bed);
        float level = MeanTop(scene.Water, 0.5f);
        bool still = rest.Speed < 0.06f;
        Log.AppendLine(Inv($"  Reaches the far wall at {hit:0.00} s, highest there {highest:0.00} m; after 20 s the water lies {level:0.00} m deep by its columns' tops (at rest 0.50), {rest.Count} of {added} particles, moving {rest.Speed:0.000} m/s on average (still: under 0.06): {Check(hit > 1f && hit < 2f && Mathf.Abs(level - 0.5f) < 0.06f && still && rest.Count == added)}"));
    }

    // Water let go on a shelf 3 m over a basin: it pours over the lip as one body, falls, and pools below.
    private static void Ledge()
    {
        float[] bed = Field((x, z) => z < 14f || z > 26f || x < 8f || x > 32f ? 10f : x < 20f ? 3f : 0f);
        using var scene = new Scene("vledge", bed);
        int added = scene.Water.Add(new Vector3(8f, 3f, 14f), new Vector3(14f, 4f, 26f));
        Log.AppendLine(Inv($"Ledge: {added} particles ({added * scene.Water.Litre:0.0} m3) of water 1 m deep let go 6 m from a shelf's lip, 3 m over a basin's floor."));
        int most = 0;
        bool kinds = false;
        for (int k = 1; k <= 480; k++)
        {
            scene.Step();
            if (k % 60 == 0)
            {
                float[] data = scene.Water.Read();
                int shelf = 0, air = 0, floor = 0;
                for (int p = 0; p < data.Length / 12; p++)
                {
                    float x = data[p * 12], y = data[(p * 12) + 1];
                    if (x < 20f)
                    {
                        shelf++;
                    }
                    else if (y > 1.2f)
                    {
                        air++;
                    }
                    else
                    {
                        floor++;
                    }
                }

                Froth froth = Froth.Of(scene.Water);
                most = Math.Max(most, froth.Count);
                kinds |= froth.Spray > 0 && froth.Foam > 0 && froth.Bubbles > 0;
                Log.AppendLine(Inv($"  t={k * Frame:0.0}s on the shelf {shelf}, in the air {air}, on the floor {floor} ({data.Length / 12} in all); froth {froth}"));
            }

            if (k is 60 or 120 or 180 or 300 or 480)
            {
                scene.Shot(Inv($"vledge_{k * Frame:0.0}s"), new Vector3(27f, 6f, 8f), new Vector3(21f, 1.5f, 20f));
            }

            if (k is 300 or 480)
            {
                // The fall's foot from close and low, and the pool's edge where it meets the floor.
                scene.Shot(Inv($"vledge_foot_{k * Frame:0.0}s"), new Vector3(25f, 1.6f, 14f), new Vector3(20.5f, 1.2f, 20f));
                scene.Shot(Inv($"vledge_edge_{k * Frame:0.0}s"), new Vector3(29f, 1.2f, 24f), new Vector3(24f, 0.3f, 21f));
            }
        }

        Measure m = Measure.Of(scene.Water, bed);
        Log.AppendLine(Inv($"  Particles kept: {m.Count} of {added}, in the ground {m.Buried}: {Check(m.Count == added && m.Buried == 0)}"));
        Log.AppendLine(Inv($"  The fall threw froth ({most} at most, within room for {scene.Water.FrothCapacity}), of every kind at once at some second: {Check(most > 0 && most < scene.Water.FrothCapacity && kinds)}"));
    }

    // A source of 8 m3/s in a basin for 6 s: its water comes out all round it and spreads; the particles' water grows
    // by the rate.
    private static void Spring()
    {
        float[] bed = Field((x, z) => Math.Min(4f, 0.02f * (((x - 20f) * (x - 20f)) + ((z - 20f) * (z - 20f)))));
        using var scene = new Scene("vspring", bed);
        const float Rate = 8f;
        var springs = new List<VolumeWater.Spring> { VolumeWater.Spring.Source(new Vector3(20f, 0f, 20f), Rate) };
        Log.AppendLine(Inv($"Spring: a source of {Rate} m3/s in a basin's middle for 6 s (its water out of a lens {springs[0].Ring.w:0.00} m round and {springs[0].Flow.w:0.00} m high at {springs[0].Flow.x:0.0} m/s)."));
        bool ok = true;
        // The sphere's own cells are kept full from the first step: that water is there before the rate's.
        float fill = 0f;
        for (int k = 1; k <= 480; k++)
        {
            scene.Water.SetSprings(k <= 360 ? springs : new List<VolumeWater.Spring>());
            scene.Step();
            if (k == 1)
            {
                fill = Math.Max(0f, (Measure.Of(scene.Water, bed).Count * scene.Water.Litre) - (Rate * Frame));
            }

            if (k % 60 == 0)
            {
                float expected = fill + (Rate * Math.Min(k, 360) * Frame);
                Measure m = Measure.Of(scene.Water, bed);
                float have = m.Count * scene.Water.Litre;
                bool near = Mathf.Abs(have - expected) <= 0.15f * expected + 0.5f;
                ok &= near;
                (float[] scales, uint[] given) = scene.Water.ReadSources();
                int most = scene.Water.MostInACell(out Vector3 crowded);
                Log.AppendLine(Inv($"  t={k * Frame:0.0}s the most particles in a cell {most} at {crowded.x:0.00},{crowded.y:0.00},{crowded.z:0.00}"));
                Log.AppendLine(Inv($"  t={k * Frame:0.0}s the water {have:0.0} m3 (expected {expected:0.0}, the sphere's own {fill:0.0}), fastest {m.Fastest:0.0} m/s, top {m.Top:0.00} m; the source's speed scaled {scales[0]:0.00}, gave {given[0]} particles last step (wants {Rate * Frame / scene.Water.Litre:0.0}); froth {Froth.Of(scene.Water)}: {(near ? "OK" : "FAILED")}"));
            }

            if (k is 30 or 120 or 360 or 480)
            {
                scene.Shot(Inv($"vspring_{k * Frame:0.0}s"), new Vector3(16f, 2.5f, 16f), new Vector3(20f, 0.4f, 20f));
            }
        }

        float rough = Roughness(scene.Water, bed, 0.5f);
        bool level = rough < 0.12f;
        ok &= level;
        Log.AppendLine(Inv($"  Two seconds after the source stopped, the water's top spreads {rough:0.000} m (standard deviation) about its mean from column to column, a level surface, not the ground's cells: {Check(level)}"));
        Log.AppendLine($"  The water grew by the rate within 15 %, and lay level after: {Check(ok)}");
    }

    // A big source (FW_PREVIEW_RATE m3/s, 100 by default) in the middle of a grid 128 cells across (32 m at 0.25 m cells)
    // with its sides open, over flat ground under the server's water 0.9 m deep, as in the game: the water runs out to
    // the sides, where the server's level holds; how high it stands round the source, ring by ring, against the 0.9 m it
    // runs off at; and whether the water near the sides settles near the server's level, the square's water near the
    // server's.
    private static void BigSpring()
    {
        const int GridCells = 128;
        const float ServerDepth = 0.9f;
        float rate = float.TryParse(Environment.GetEnvironmentVariable("FW_PREVIEW_RATE"), NumberStyles.Float, CultureInfo.InvariantCulture, out float r) ? r : 100f;
        float span = GridCells * Dx, middle = Cells * Dx / 2f, edge = middle - (span / 2f);
        float[] bed = Field((x, z) => 0f);
        using Scene scene = OpenSides("vbigspring", bed, GridCells, ServerDepth);
        var springs = new List<VolumeWater.Spring> { VolumeWater.Spring.Source(new Vector3(middle, 0f, middle), rate) };
        scene.Water.SetSprings(springs);

        // A swimmer 6 m from the source, a box as the game hands the water a character (FW_PREVIEW_BODY=0: none).
        if (Environment.GetEnvironmentVariable("FW_PREVIEW_BODY") != "0")
        {
            scene.Boxes(new Bounds(new Vector3(middle - 6f, 0.9f, middle), new Vector3(0.6f, 1.8f, 0.6f)));
        }

        Log.AppendLine(Inv($"Big spring: {rate} m3/s in the middle of a square {span:0} m across, sides open over the server's water {ServerDepth} m deep, {scene.Water.Iterations} pressure sweeps a step; the water's top (m over the ground) at rings 0 to 14 m from the source, each second."));
        float[] data = null;
        long gave = 0;
        for (int k = 1; k <= 600; k++)
        {
            scene.Step();
            (float[] scales, uint[] given) = scene.Water.ReadSources();
            gave += given[0];
            if (k % 60 == 0)
            {
                data = scene.Water.Read();
                float[] rings = new float[8];
                float overSwimmer = 0f;
                for (int p = 0; p < data.Length / 12; p++)
                {
                    float x = data[p * 12], y = data[(p * 12) + 1], z = data[(p * 12) + 2];
                    int ring = (int)Math.Round(Mathf.Sqrt(((x - middle) * (x - middle)) + ((z - middle) * (z - middle))) / 2f);
                    if (ring < rings.Length)
                    {
                        rings[ring] = Math.Max(rings[ring], y);
                    }

                    if (Math.Abs(x - (middle - 6f)) < 0.6f && Math.Abs(z - middle) < 0.6f)
                    {
                        overSwimmer = Math.Max(overSwimmer, y);
                    }
                }

                Measure m = Measure.Of(scene.Water, bed);
                Log.AppendLine(Inv($"  t={k * Frame:0}s {scene.Water.Count * scene.Water.Litre:0} m3, source speed scaled {scales[0]:0.00} ({springs[0].Flow.x * scales[0]:0.0} m/s), gave {given[0]} a step (wants {rate * Frame / scene.Water.Litre:0}), fastest {m.Fastest:0.0} m/s, froth {Froth.Of(scene.Water)}, highest water at the swimmer {overSwimmer:0.00} m: ") + string.Join(" ", rings.Select((h, i) => Inv($"{i * 2}m:{h:0.00}"))));
            }

            if (k is 300 or 600)
            {
                scene.Shot(Inv($"vbigspring_{k * Frame:0}s"), new Vector3(middle - 12f, 6f, middle - 12f), new Vector3(middle, 0.5f, middle));
            }
        }

        // The water near the sides (columns half a metre wide within 4 m of them) settles near the server's level: its
        // tops' mean within 0.35 m over it. And the square holds within 25 % over the server's water there.
        var tops = new Dictionary<(int, int), float>();
        for (int p = 0; p < data.Length / 12; p++)
        {
            float x = data[p * 12], y = data[(p * 12) + 1], z = data[(p * 12) + 2];
            float fromSide = Math.Min(Math.Min(x - edge, edge + span - x), Math.Min(z - edge, edge + span - z));
            if (fromSide < 4f)
            {
                var key = ((int)Math.Floor(x / 0.5f), (int)Math.Floor(z / 0.5f));
                tops[key] = Math.Max(tops.TryGetValue(key, out float top) ? top : float.MinValue, y);
            }
        }

        float sides = tops.Count > 0 ? tops.Values.Average() : 0f;
        bool level = sides <= ServerDepth + 0.35f;
        Log.AppendLine(Inv($"  After 10 s the water within 4 m of the sides stands {sides:0.00} m on average (the server's {ServerDepth} m): within 0.35 m over it: {Check(level)}"));
        float held = scene.Water.Count * scene.Water.Litre, server = ServerDepth * span * span;
        bool ok = held <= 1.25f * server;
        Log.AppendLine(Inv($"  After 10 s the square holds {held:0} m3, the server's water there {server:0} m3: within 25 % over it: {Check(ok)}"));

        // The source gave its rate over the run (the water it gave leaves the square at its sides, so what the square
        // holds says nothing of it: without this, a source giving nothing passed, the server's water meeting the rest).
        float gaveWater = gave * scene.Water.Litre, wanted = rate * 600 * Frame;
        bool gives = Mathf.Abs(gaveWater - wanted) <= 0.15f * wanted;
        Log.AppendLine(Inv($"  Over 10 s the source gave {gaveWater:0} m3 (its rate's {wanted:0}): within 15 %: {Check(gives)}"));
    }

    // The water's top on average: the height of the highest particle in each column (of a width), over the columns that
    // hold water.
    private static float MeanTop(VolumeWater water, float column)
    {
        Dictionary<(int, int), float> tops = ColumnTops(water, column);
        return tops.Count > 0 ? tops.Values.Average() : 0f;
    }

    // The height of the highest particle in each column (of a width) that holds water, by the column.
    private static Dictionary<(int, int), float> ColumnTops(VolumeWater water, float column)
    {
        float[] data = water.Read();
        var tops = new Dictionary<(int, int), float>();
        for (int p = 0; p < data.Length / 12; p++)
        {
            float x = data[p * 12], y = data[(p * 12) + 1], z = data[(p * 12) + 2];
            var key = ((int)(x / column), (int)(z / column));
            tops[key] = Math.Max(tops.TryGetValue(key, out float top) ? top : float.MinValue, y);
        }

        return tops;
    }

    // How uneven the water's top is: the heights of the highest particle in each column (of a width), their standard
    // deviation about the mean (m), over the columns that hold water.
    private static float Roughness(VolumeWater water, float[] bed, float column)
    {
        Dictionary<(int, int), float> tops = ColumnTops(water, column);
        if (tops.Count < 2)
        {
            return 0f;
        }

        float mean = tops.Values.Average();
        return Mathf.Sqrt(tops.Values.Sum(t => (t - mean) * (t - mean)) / tops.Count);
    }

    // A player wading in a stream too slow to sweep them (the current carries a wader wholly only past its footing
    // line, CurrentCarry.FootingLimit: under twice it they are not swept wholly): the game's box for a character (its
    // capsule's, 0.98 m across and 1.85 m high) standing in a stream 0.3 m deep running down a bed falling 4 % to the
    // east (the server's water as deep over it, the sides open). The water heaps against the box by its bow wave (up to
    // twice its speed's head, v^2 / g) and no more, and none rests on the box's top (the pillar a still player stood in
    // by a big source, its water climbing over its head and lying on the box).
    private static void Wader()
    {
        const int GridCells = 128;
        const float Depth = 0.3f, Grade = 0.04f;
        // CurrentCarry.FootingLimit, the plugin's (the preview builds without it).
        const float FootingLimit = 0.6f;
        float middle = Cells * Dx / 2f;
        var half = new Vector3(0.49f, 0.925f, 0.49f);
        float[] bed = Field((x, z) => Grade * (middle - x));
        using Scene scene = OpenSides("vwader", bed, GridCells, Depth);
        scene.Boxes(new Bounds(new Vector3(middle, half.y, middle), 2f * half));

        Log.AppendLine(Inv($"Wader: a box {2f * half.x:0.00} m across and {2f * half.y:0.00} m high in a stream {Depth} m deep down a bed falling {Grade * 100f:0} % to the east, each second: the stream's speed by the box and the water's top against it (over the bed under the box)."));
        float stream = 0f, heap = 0f;
        int onTop = 0;
        for (int k = 1; k <= 600; k++)
        {
            scene.Step();
            if (k % 60 != 0)
            {
                continue;
            }

            float[] data = scene.Water.Read();
            float flow = 0f, mean = 0f;
            int flowing = 0, beside = 0;
            heap = 0f;
            onTop = 0;
            for (int p = 0; p < data.Length / 12; p++)
            {
                float x = data[p * 12] - middle, y = data[(p * 12) + 1], z = data[(p * 12) + 2] - middle;
                // The stream: the water 2 to 4 m upstream of the box, within 2 m of its line.
                if (x > -4f && x < -2f && Math.Abs(z) < 2f)
                {
                    flow += data[(p * 12) + 8];
                    flowing++;
                }

                // Against the box: within 0.3 m of its sides.
                if (Math.Abs(x) < half.x + 0.3f && Math.Abs(z) < half.z + 0.3f)
                {
                    if (Math.Abs(x) < half.x && Math.Abs(z) < half.z)
                    {
                        onTop += y > 2f * half.y ? 1 : 0;
                    }
                    else
                    {
                        heap = Math.Max(heap, y);
                        mean += y;
                        beside++;
                    }
                }
            }

            stream = flowing > 0 ? flow / flowing : 0f;
            Log.AppendLine(Inv($"  t={k * Frame:0}s the stream at {stream:0.00} m/s east (depth times speed {Depth * stream:0.00} m2/s, the footing line {FootingLimit} m2/s), the water against the box {(beside > 0 ? mean / beside : 0f):0.00} m on average, its highest {heap:0.00} m, {onTop} particles on its top"));
        }

        // Twice its speed's head over the stream's top (a bluff body's bow wave in a stream this slow, Froude under 1), and
        // a cell more (the particles' grain).
        float allowed = Depth + (stream * stream / 9.81f) + Dx;
        bool runs = stream > 0.5f && Depth * stream < FootingLimit * 2f;
        Log.AppendLine(Inv($"  After 10 s the stream runs {stream:0.00} m/s, a wader's water: {Check(runs)}"));
        Log.AppendLine(Inv($"  The water against the box rises to {heap:0.00} m, its bow wave allowing {allowed:0.00} m: {Check(heap <= allowed)}"));
        Log.AppendLine(Inv($"  {onTop} particles lie on the box's top: none: {Check(onTop == 0)}"));
        scene.Shot("vwader_10s", new Vector3(middle - 5f, 3f, middle - 5f), new Vector3(middle, 0.5f, middle));
    }

    // A scene whose grid (gridCells across, in the middle of the ground) has its sides open over the server's water this
    // deep over the ground everywhere, poured from it, as the game's square is.
    private static Scene OpenSides(string name, float[] bed, int gridCells, float serverDepth)
    {
        float edge = (Cells * Dx / 2f) - (gridCells * Dx / 2f);
        var scene = new Scene(name, bed, gridCells);
        scene.Water.Origin = new Vector3(edge, -3f, edge);
        scene.Water.OpenSides = true;
        scene.Water.EdgeBand = VolumeFollow.Band(Dx);
        scene.Server((x, z) => serverDepth);
        scene.Water.SeedFromServer();
        return scene;
    }

    // Water let go at the top of a ramp (22 degrees, 20 m long) onto a flat floor: it runs down as a sheet, not down
    // terraces of the grid's cells. Its front against frictionless sliding (a = g sin cos: 3.4 m/s2 along the ground),
    // how thick the sheet on the ramp is, and how much has reached the floor.
    private static void Slope()
    {
        const float Grade = 0.4f;
        // FW_PREVIEW_ROUGH=1: the ramp faceted as the game's terrain is, a lattice a metre apart with a few centimetres
        // of random height at each knot and the ground linear between (the game's heightmap mesh, as the card casts it).
        bool rough = Environment.GetEnvironmentVariable("FW_PREVIEW_ROUGH") == "1";
        // Each knot's height from a hash of its place (the same ramp every run, no random numbers).
        float[] bumps = new float[64 * 64];
        for (int k = 0; k < bumps.Length; k++)
        {
            uint hash = ((uint)k * 2654435761u) ^ 0x9E3779B9u;
            hash ^= hash >> 15;
            hash *= 2246822519u;
            hash ^= hash >> 13;
            bumps[k] = (((hash & 0xFFFFu) / 65535f) - 0.5f) * 0.16f;
        }

        float Bump(float x, float z)
        {
            int i = Mathf.Clamp((int)x, 0, 62), j = Mathf.Clamp((int)z, 0, 62);
            float fx = x - i, fz = z - j;
            return Mathf.Lerp(Mathf.Lerp(bumps[(j * 64) + i], bumps[(j * 64) + i + 1], fx), Mathf.Lerp(bumps[((j + 1) * 64) + i], bumps[((j + 1) * 64) + i + 1], fx), fz);
        }

        float[] bed = Field((x, z) => z < 8f || z > 32f || x > 38f ? 10f : x < 8f ? 8f : (x < 28f ? (28f - x) * Grade : 0f) + (rough ? Bump(x, z) : 0f));
        using var scene = new Scene("vslope", bed);
        int added = scene.Water.Add(new Vector3(9f, 6.6f, 12f), new Vector3(11f, 8.6f, 28f));
        Log.AppendLine(Inv($"Slope: {added} particles ({added * scene.Water.Litre:0.0} m3) let go at the top of a ramp of {Grade} rise over run, 20 m long, onto a flat floor."));
        bool ok = true;
        float onFloor = 0f;
        float along = 9.81f * Grade / (1f + (Grade * Grade));
        for (int k = 1; k <= 300; k++)
        {
            scene.Step();
            if (k % 60 == 0)
            {
                float t = k * Frame;
                float[] data = scene.Water.Read();
                int n = data.Length / 12;
                var xs = new List<float>(n);
                var over = new List<float>();
                int floor = 0;
                for (int p = 0; p < n; p++)
                {
                    float x = data[p * 12], y = data[(p * 12) + 1], z = data[(p * 12) + 2];
                    xs.Add(x);
                    floor += x >= 28f ? 1 : 0;
                    if (x > 12f && x < 26f)
                    {
                        over.Add(y - Ground(bed, x, z));
                    }
                }

                xs.Sort();
                over.Sort();
                float front = xs[(int)(0.95f * (xs.Count - 1))];
                float ideal = Math.Min(28f, 9f + (0.5f * along * t * t));
                float thick = over.Count > 0 ? over[(int)(0.9f * (over.Count - 1))] : 0f;
                float reached = (front - 9f) / Math.Max(ideal - 9f, 0.01f);
                onFloor = (float)floor / Math.Max(n, 1);
                bool near = k < 180 || (reached >= 0.6f && thick < 0.6f);
                ok &= near;
                Log.AppendLine(Inv($"  t={t:0.0}s the front at x = {front:0.0} m (frictionless {ideal:0.0}: {100f * reached:0} %), the sheet on the ramp {thick:0.00} m thick (nine in ten under), {100f * floor / Math.Max(n, 1):0} % on the floor{(k >= 180 ? ": " + Check(near) : string.Empty)}"));
            }

            if (k is 60 or 120 or 180 or 300)
            {
                scene.Shot(Inv($"vslope_{k * Frame:0.0}s"), new Vector3(18f, 9f, 2f), new Vector3(20f, 3f, 20f));
                // Low along the ramp, as a player sees a stream.
                scene.Shot(Inv($"vslope_low_{k * Frame:0.0}s"), new Vector3(26f, 2.5f, 14f), new Vector3(16f, 4.5f, 20f));
            }
        }

        Measure m = Measure.Of(scene.Water, bed);
        Log.AppendLine(Inv($"  The sheet runs down the ramp as frictionless sliding does, within 60 %, thinner than 0.6 m from 3 s on, and half the water at least has reached the floor at 5 s ({100f * onFloor:0} %); particles kept {m.Count} of {added}, in the ground {m.Buried}: {Check(ok && onFloor >= 0.5f && m.Count == added && m.Buried == 0)}"));
    }

    // A box of walls (what players build) 4 m square, 3 m high, filled 2 m deep; at 2 s its east wall is taken away
    // from the floor up to 1 m: the water bursts out through the gap and the box empties down to the gap's top. The
    // walls are solid voxels, or (thin) the closed faces between voxels, as a wall thinner than a voxel is to the bricks.
    private static void Box(bool thin)
    {
        float[] bed = Field((x, z) => 0f);
        string name = thin ? "vfence" : "vbox";
        using var scene = new Scene(name, bed);
        scene.Walls(16f, 20f, 16f, 20f, 3f, 0f, thin);
        int added = scene.Water.Add(new Vector3(16.3f, 0f, 16.3f), new Vector3(19.7f, 2f, 19.7f));
        Log.AppendLine(Inv($"{(thin ? "Fence" : "Box")}: a box of {(thin ? "thin walls (closed faces)" : "walls")} 4 m square filled 2 m deep ({added} particles); its east wall opened from the floor to 1 m at 2 s."));
        int before = 0;
        for (int k = 1; k <= 480; k++)
        {
            if (k == 120)
            {
                before = Inside(scene.Water.Read());
                scene.Walls(16f, 20f, 16f, 20f, 3f, 1f, thin);
            }

            scene.Step();
            if (k % 60 == 0)
            {
                float[] data = scene.Water.Read();
                int inside = 0;
                float top = 0f;
                for (int p = 0; p < data.Length / 12; p++)
                {
                    float x = data[p * 12], y = data[(p * 12) + 1], z = data[(p * 12) + 2];
                    if (x > 16f && x < 20f && z > 16f && z < 20f)
                    {
                        inside++;
                        top = Math.Max(top, y);
                    }
                }

                Log.AppendLine(Inv($"  t={k * Frame:0.0}s in the box {inside} of {data.Length / 12}, its water up to {top:0.00} m"));
            }

            if (k is 60 or 130 or 150 or 180 or 240 or 480)
            {
                scene.Shot(Inv($"{name}_{k * Frame:0.0}s"), new Vector3(26f, 3.5f, 12f), new Vector3(19f, 1f, 18f));
            }
        }

        float[] end = scene.Water.Read();
        int left = Inside(end);
        float topLeft = 0f;
        for (int p = 0; p < end.Length / 12; p++)
        {
            if (end[p * 12] > 16f && end[p * 12] < 20f && end[(p * 12) + 2] > 16f && end[(p * 12) + 2] < 20f)
            {
                topLeft = Math.Max(topLeft, end[(p * 12) + 1]);
            }
        }

        Log.AppendLine(Inv($"  Held before the gap opened ({before} of {added} inside at 2 s), then out through it: {left} left inside, up to {topLeft:0.00} m (the gap's top 1 m): {Check(before >= added * 95 / 100 && left < added / 2 && topLeft < 1.3f)}"));
    }

    // The particles inside the box scene's walls.
    private static int Inside(float[] data)
    {
        int inside = 0;
        for (int p = 0; p < data.Length / 12; p++)
        {
            inside += data[p * 12] > 16f && data[p * 12] < 20f && data[(p * 12) + 2] > 16f && data[(p * 12) + 2] < 20f ? 1 : 0;
        }

        return inside;
    }

    // A pool 6 m square 1 m deep with a drain in its middle taking 2 m3/s: the pool goes down by the rate.
    private static void Drain()
    {
        float[] bed = Field((x, z) => x < 17f || x > 23f || z < 17f || z > 23f ? 10f : 0f);
        using var scene = new Scene("vdrain", bed);
        int added = scene.Water.Add(new Vector3(17f, 0f, 17f), new Vector3(23f, 1f, 23f));
        scene.Water.SetSprings(new List<VolumeWater.Spring> { VolumeWater.Spring.Drain(new Vector3(20f, 0.3f, 20f), 1f, 2f) });
        Log.AppendLine(Inv($"Drain: a pool 6 m square 1 m deep ({added * scene.Water.Litre:0.0} m3), a drain of 2 m3/s in its middle."));
        bool ok = true;
        for (int k = 1; k <= 600; k++)
        {
            scene.Step();
            if (k % 120 == 0)
            {
                float expected = (added * scene.Water.Litre) - (2f * k * Frame);
                float have = scene.Water.Count * scene.Water.Litre;
                bool near = Mathf.Abs(have - expected) < 1f;
                ok &= near;
                Log.AppendLine(Inv($"  t={k * Frame:0.0}s the water {have:0.0} m3 (expected {expected:0.0}): {(near ? "OK" : "FAILED")}"));
            }
        }

        scene.Shot("vdrain_10.0s", new Vector3(20f, 4f, 13f), new Vector3(20f, 0.5f, 20f));
        Log.AppendLine($"  The pool went down by the rate: {Check(ok)}");
    }

    // The grid following the camera over the server's water, as the game moves it (GpuWater): a pool 1 m deep the
    // server knows over a band of the ground, a grid 20 m square (half the ground's, its sides open) poured from it,
    // then moved east 2 m at a time and north once (its new strip seeded from the server's water, as a move of the
    // square does; the water left behind is the server's), then dropped 2 m (as it follows a camera climbing down,
    // poured again): after each, the square holds the server's water there, 1 m deep, nothing in the ground, nothing
    // left west of it.
    private static void Follow()
    {
        // The pool lies between banks 3 m high (a flat bed would let it spread south over dry ground, as the server's
        // own water would not, it being held there by the server).
        float[] bed = Field((x, z) => z >= 10f && z < 30f ? 0f : 3f);
        using var scene = new Scene("vfollow", bed, 80);
        scene.Water.Origin = new Vector3(5f, -6f, 5f);
        scene.Water.OpenSides = true;
        scene.Water.EdgeBand = VolumeFollow.Band(Dx);
        scene.Server((x, z) => z >= 10f && z < 30f ? 1f : 0f);
        scene.Water.SeedFromServer();
        Log.AppendLine(Inv($"Follow: a grid 20 m square, sides open, over a pool 1 m deep the server has between banks from z = 10 to 30 m; poured from the server ({scene.Water.Count} particles, {scene.Water.Count * scene.Water.Litre:0.0} m3), moved east 2 m three times and north 2 m once (the strip seeded), then dropped 2 m."));
        bool ok = true;
        for (int k = 1; k <= 20; k++)
        {
            scene.Step();
            if (k is 1 or 5 or 20)
            {
                Measure early = Measure.Of(scene.Water, bed);
                Log.AppendLine(Inv($"  step {k}: {early.Count} particles, level {early.Level:0.000}, top {early.Top:0.00}, mean {early.Speed:0.000} m/s, fastest {early.Fastest:0.00} m/s"));
            }
        }

        Followed(scene, bed, "poured", 300f, ref ok);
        for (int m = 1; m <= 4; m++)
        {
            Vector3 step = m <= 3 ? new Vector3(2f, 0f, 0f) : new Vector3(0f, 0f, 2f);
            scene.Water.Origin += step;
            scene.Water.SeedFromServer(1f, VolumeFollow.Reseed((int)(step.x / Dx), (int)(step.z / Dx), 1));
            Log.AppendLine(Inv($"  moved by ({step.x:0},{step.y:0},{step.z:0}) and the strip poured: {scene.Water.Count * scene.Water.Litre:0.0} m3"));
            Followed(scene, bed, m <= 3 ? Inv($"east {m}") : "north", m <= 3 ? 300f : 340f, ref ok);
            if (m == 1)
            {
                scene.Shot("vfollow_east", new Vector3(16f, 7f, 2f), new Vector3(17f, 0.5f, 15f));
            }
        }

        scene.Water.Origin += new Vector3(0f, -2f, 0f);
        scene.Water.SeedFromServer();
        Log.AppendLine(Inv($"  dropped 2 m and poured again: {scene.Water.Count * scene.Water.Litre:0.0} m3"));
        Followed(scene, bed, "dropped", 340f, ref ok);
        Log.AppendLine($"  The square keeps the server's water as it follows: {Check(ok)}");
    }

    // A second of the follow scene, then what the square holds against what the server has in it (m3), 1 m deep, none
    // in the ground, none left outside the square.
    private static void Followed(Scene scene, float[] bed, string after, float expected, ref bool ok)
    {
        for (int k = 1; k <= 60; k++)
        {
            scene.Step();
            if (k is 10 or 30)
            {
                Log.AppendLine(Inv($"    after {k} steps: {scene.Water.Count * scene.Water.Litre:0.0} m3"));
            }
        }

        Measure m = Measure.Of(scene.Water, bed);
        float have = m.Count * scene.Water.Litre;
        float[] data = scene.Water.Read();
        Vector3 lo = scene.Water.Origin, hi = lo + (new Vector3(scene.Water.CellsX, scene.Water.CellsY, scene.Water.CellsZ) * Dx);
        int outside = 0;
        for (int p = 0; p < data.Length / 12; p++)
        {
            var at = new Vector3(data[p * 12], data[(p * 12) + 1], data[(p * 12) + 2]);
            outside += at.x < lo.x || at.z < lo.z || at.x > hi.x || at.z > hi.z || at.y < lo.y || at.y > hi.y ? 1 : 0;
        }

        bool near = Mathf.Abs(have - expected) <= 0.03f * expected && Mathf.Abs(m.Level - 1f) < 0.06f && m.Buried == 0 && outside == 0;
        ok &= near;
        Log.AppendLine(Inv($"  {after}, square from ({lo.x:0},{lo.y:0},{lo.z:0}): {have:0.0} m3 (the server's there {expected:0.0}), {m.Level:0.000} m deep, {m.Buried} in the ground, {outside} outside the square: {Check(near)}"));
    }

    private static string Check(bool ok) => ok ? "OK" : "FAILED";

    // The ground under a point as the solver reads it: bilinear between the cells' heights (their centres), or the
    // cell's own where the four about it differ by more than a step (a wall), as the compute shader's GroundAt.
    private static float Ground(float[] bed, float x, float z)
    {
        float ax = (x / Dx) - 0.5f, az = (z / Dx) - 0.5f;
        int i0 = Mathf.Clamp(Mathf.FloorToInt(ax), 0, Cells - 1), j0 = Mathf.Clamp(Mathf.FloorToInt(az), 0, Cells - 1);
        int i1 = Mathf.Clamp(Mathf.FloorToInt(ax) + 1, 0, Cells - 1), j1 = Mathf.Clamp(Mathf.FloorToInt(az) + 1, 0, Cells - 1);
        float fx = ax - Mathf.Floor(ax), fz = az - Mathf.Floor(az);
        float b00 = bed[(j0 * Cells) + i0], b10 = bed[(j0 * Cells) + i1], b01 = bed[(j1 * Cells) + i0], b11 = bed[(j1 * Cells) + i1];
        float lowest = Mathf.Min(Mathf.Min(b00, b10), Mathf.Min(b01, b11)), highest = Mathf.Max(Mathf.Max(b00, b10), Mathf.Max(b01, b11));
        if (highest - lowest > 1f)
        {
            return bed[(Mathf.Clamp((int)(z / Dx), 0, Cells - 1) * Cells) + Mathf.Clamp((int)(x / Dx), 0, Cells - 1)];
        }

        return Mathf.Lerp(Mathf.Lerp(b00, b10, fx), Mathf.Lerp(b01, b11, fx), fz);
    }

    private static float[] Field(Func<float, float, float> at) => SimPreview.Field(Cells, Dx, at);

    private static string Inv(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    private sealed class Measure
    {
        public int Count { get; set; }

        public float Level { get; set; }

        public float Speed { get; set; }

        public float Fastest { get; set; }

        public float Top { get; set; }

        public int Buried { get; set; }

        // The water's depth as the tenth highest particles have it (over the ground under them), its mean and top
        // speed, and the particles under the ground.
        public static Measure Of(VolumeWater water, float[] bed)
        {
            float[] data = water.Read();
            int n = data.Length / 12;
            var heights = new List<float>(n);
            float speed = 0f, fastest = 0f, top = float.MinValue;
            int buried = 0;
            for (int p = 0; p < n; p++)
            {
                float x = data[p * 12], y = data[(p * 12) + 1], z = data[(p * 12) + 2];
                float ground = Ground(bed, x, z);
                heights.Add(y - ground);
                buried += y < ground - 0.01f ? 1 : 0;
                top = Math.Max(top, y);
                float v = new Vector3(data[(p * 12) + 8], data[(p * 12) + 9], data[(p * 12) + 10]).magnitude;
                speed += v;
                fastest = Math.Max(fastest, v);
            }

            heights.Sort();
            int tenth = Math.Max(1, n / 10);
            float level = n > 0 ? heights.Skip(n - tenth).Average() + (0.5f * water.Spacing) : 0f;
            return new Measure { Count = n, Level = level, Speed = n > 0 ? speed / n : 0f, Fastest = fastest, Top = n > 0 ? top : 0f, Buried = buried };
        }
    }

    // The froth as read back: how many of each kind.
    private sealed class Froth
    {
        public int Spray { get; private set; }

        public int Foam { get; private set; }

        public int Bubbles { get; private set; }

        public int Count => Spray + Foam + Bubbles;

        public static Froth Of(VolumeWater water)
        {
            float[] data = water.ReadFroth();
            var froth = new Froth();
            for (int i = 0; i < data.Length / 12; i++)
            {
                float kind = data[(i * 12) + 7];
                if (kind < 0.5f)
                {
                    froth.Spray++;
                }
                else if (kind < 1.5f)
                {
                    froth.Foam++;
                }
                else
                {
                    froth.Bubbles++;
                }
            }

            return froth;
        }

        public override string ToString() => Inv($"{Count} ({Spray} spray, {Foam} foam, {Bubbles} bubbles)");
    }

    private sealed class Scene : IDisposable
    {
        private readonly Texture2D _bed;

        private readonly ParticleSolids _solids = new();

        private readonly float[] _ground;

        // The grid over the whole square, or (gridCells) a smaller one within it.
        public Scene(string name, float[] bed, int gridCells = Cells)
        {
            _ground = bed;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            _bed = new Texture2D(Cells, Cells, TextureFormat.RFloat, false, true) { name = name + " ground", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            _bed.SetPixelData(bed, 0);
            _bed.Apply(false);
            var compute = AssetDatabase.LoadAssetAtPath<ComputeShader>(BuildBundles.VolumeComputePath);
            var particles = AssetDatabase.LoadAssetAtPath<Material>(BuildBundles.ParticlesMaterialPath);
            var froth = AssetDatabase.LoadAssetAtPath<Material>(BuildBundles.FrothMaterialPath);
            Water = new VolumeWater(compute, particles, froth, 1 << 20, gridCells, Rows, gridCells, Dx) { Origin = Vector3.zero, OpenSides = false };
            Water.SetGround(_bed, Cells, Dx, Vector2.zero);
            if (float.TryParse(Environment.GetEnvironmentVariable("FW_PREVIEW_FROTH"), NumberStyles.Float, CultureInfo.InvariantCulture, out float frothRate))
            {
                Water.FrothRate = frothRate;
            }

            if (int.TryParse(Environment.GetEnvironmentVariable("FW_PREVIEW_ITERATIONS"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int iterations))
            {
                Water.Iterations = iterations;
            }

            if (float.TryParse(Environment.GetEnvironmentVariable("FW_PREVIEW_FLIP"), NumberStyles.Float, CultureInfo.InvariantCulture, out float flip))
            {
                Water.Flip = flip;
            }

            if (float.TryParse(Environment.GetEnvironmentVariable("FW_PREVIEW_DRIFT"), NumberStyles.Float, CultureInfo.InvariantCulture, out float drift))
            {
                Water.Drift = drift;
            }

            SimPreview.MakeGround(bed, Cells, Dx);
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.94f, 0.82f);
            sun.intensity = 1.1f;
            sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            RenderSettings.sun = sun;
            RenderSettings.skybox = new Material(Shader.Find("Skybox/Procedural"));
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            DynamicGI.UpdateEnvironment();
        }

        public VolumeWater Water { get; }

        // The server's water over the ground (SimGrid.Target's layout, a texel a cell of the ground): this deep over
        // each cell's ground (0: none).
        public void Server(Func<float, float, float> depth)
        {
            float[] texels = new float[Cells * Cells * 4];
            for (int j = 0; j < Cells; j++)
            {
                for (int i = 0; i < Cells; i++)
                {
                    float x = (i + 0.5f) * Dx, z = (j + 0.5f) * Dx;
                    float d = depth(x, z);
                    int at = ((j * Cells) + i) * 4;
                    texels[at] = d > 0f ? _ground[(j * Cells) + i] + d : -10000f;
                    texels[at + 1] = d > 0f ? 1f : 0f;
                    texels[at + 2] = d;
                }
            }

            _target = new Texture2D(Cells, Cells, TextureFormat.RGBAFloat, false, true) { name = "server water", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            _target.SetPixelData(texels, 0);
            _target.Apply(false);
            Water.SetServer(_target, 1);
        }

        private Texture2D _target;

        // The box the walls scene draws, if any.
        private Bounds _walls;

        private float _gap;

        // The characters' boxes the water flows round, drawn in the shots.
        private Bounds[] _boxes = Array.Empty<Bounds>();

        public void Step() => Water.Step(Frame);

        // Four walls round a box (world x0..x1, z0..z1, up to a height), the east one open from the floor to the gap's
        // height; as built voxels (ParticleSolids), the voxel the ground's cell: a voxel thick, or thin (the closed
        // faces of the voxels just inside the box).
        public void Walls(float x0, float x1, float z0, float z1, float height, float gap, bool thin)
        {
            byte[] solids = new byte[Cells * ParticleSolids.Rows * Cells];
            int i0 = (int)(x0 / Dx), i1 = (int)(x1 / Dx) - 1, j0 = (int)(z0 / Dx), j1 = (int)(z1 / Dx) - 1, top = (int)(height / Dx), open = (int)(gap / Dx);
            for (int y = 0; y < top; y++)
            {
                for (int i = i0; i <= i1; i++)
                {
                    solids[ParticleSolids.Index(Cells, i, y, j0)] |= thin ? ParticleSolids.ClosedSouth : ParticleSolids.Solid;
                    solids[ParticleSolids.Index(Cells, i, y, j1)] |= thin ? ParticleSolids.ClosedNorth : ParticleSolids.Solid;
                }

                for (int j = j0; j <= j1; j++)
                {
                    solids[ParticleSolids.Index(Cells, i0, y, j)] |= thin ? ParticleSolids.ClosedWest : ParticleSolids.Solid;
                    if (y >= open)
                    {
                        solids[ParticleSolids.Index(Cells, i1, y, j)] |= thin ? ParticleSolids.ClosedEast : ParticleSolids.Solid;
                    }
                }
            }

            _solids.Take(solids, Cells, Dx, 0, true);
            Water.SetSolids(_solids.Texture, Cells, ParticleSolids.Rows, Dx, Vector2.zero, 0f);
            _walls = new Bounds(new Vector3((x0 + x1) / 2f, height / 2f, (z0 + z1) / 2f), new Vector3(x1 - x0, height, z1 - z0));
            _gap = gap;
        }

        // Characters as the game hands them to the water (upright boxes, ParticleWater.Body.Box).
        public void Boxes(params Bounds[] boxes)
        {
            Water.SetBodies(boxes.Select(b => ParticleWater.Body.Box(b.center, b.extents, Quaternion.identity)).ToList());
            _boxes = boxes;
        }

        public void Shot(string file, Vector3 from, Vector3 at)
        {
            var drawn = new List<GameObject>();
            if (_boxes.Length > 0)
            {
                var material = new Material(Shader.Find("Standard")) { color = new Color(0.7f, 0.25f, 0.2f) };
                drawn.AddRange(_boxes.Select(b => Slab(b.center, b.size, material)));
            }

            if (_walls.size.sqrMagnitude > 0f)
            {
                // The walls as thin slabs (the east one from the gap up).
                var material = new Material(Shader.Find("Standard")) { color = new Color(0.55f, 0.4f, 0.3f) };
                Vector3 c = _walls.center, s = _walls.size;
                drawn.Add(Slab(new Vector3(c.x, c.y, c.z - (s.z / 2f) + (Dx / 2f)), new Vector3(s.x, s.y, Dx), material));
                drawn.Add(Slab(new Vector3(c.x, c.y, c.z + (s.z / 2f) - (Dx / 2f)), new Vector3(s.x, s.y, Dx), material));
                drawn.Add(Slab(new Vector3(c.x - (s.x / 2f) + (Dx / 2f), c.y, c.z), new Vector3(Dx, s.y, s.z), material));
                drawn.Add(Slab(new Vector3(c.x + (s.x / 2f) - (Dx / 2f), _gap + ((s.y - _gap) / 2f), c.z), new Vector3(Dx, s.y - _gap, s.z), material));
            }

            var camera = new GameObject("Camera").AddComponent<Camera>();
            camera.transform.position = from;
            camera.transform.LookAt(at);
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.depthTextureMode = DepthTextureMode.Depth;
            camera.fieldOfView = 60f;
            var target = new RenderTexture(1280, 720, 24);
            camera.targetTexture = target;
            ParticleSurface.Spheres = Environment.GetEnvironmentVariable("FW_PREVIEW_SPHERES") == "1";
            if (int.TryParse(Environment.GetEnvironmentVariable("FW_PREVIEW_SMOOTH"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int smoothing))
            {
                VolumeSurface.Smoothing = smoothing;
            }

            if (ParticleSurface.Spheres)
            {
                using var surface = new ParticleSurface();
                surface.Render(camera, Water, AssetDatabase.LoadAssetAtPath<Material>(BuildBundles.ParticleSurfaceMaterialPath), Water.Froth);
                camera.Render();
            }
            else
            {
                using var surface = new VolumeSurface();
                var march = AssetDatabase.LoadAssetAtPath<Material>(BuildBundles.VolumeMarchMaterialPath);
                // The smoothing's radius and falloff tried from the environment (FW_PREVIEW_SMOOTH_RADIUS, FW_PREVIEW_SMOOTH_FALLOFF, m).
                if (float.TryParse(Environment.GetEnvironmentVariable("FW_PREVIEW_SMOOTH_RADIUS"), NumberStyles.Float, CultureInfo.InvariantCulture, out float radius))
                {
                    march.SetFloat("_SmoothRadius", radius);
                }

                if (float.TryParse(Environment.GetEnvironmentVariable("FW_PREVIEW_SMOOTH_FALLOFF"), NumberStyles.Float, CultureInfo.InvariantCulture, out float falloff))
                {
                    march.SetFloat("_SmoothFalloff", falloff);
                }

                VolumeSurface.OnlyDirection = Environment.GetEnvironmentVariable("FW_PREVIEW_SMOOTH_ONLY");
                if (int.TryParse(Environment.GetEnvironmentVariable("FW_PREVIEW_LOOK"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int look))
                {
                    VolumeSurface.Look = look;
                }

                surface.Render(camera, Water, march, AssetDatabase.LoadAssetAtPath<Material>(BuildBundles.VolumeSurfaceMaterialPath), Water.Froth);
                camera.Render();
                if (Environment.GetEnvironmentVariable("FW_PREVIEW_DUMP") == "1")
                {
                    DumpFound(surface.Found, file);
                }
            }

            SimPreview.Save(camera, target, file);
            foreach (GameObject thing in drawn)
            {
                UnityEngine.Object.DestroyImmediate(thing);
            }
        }

        // What the march found, as a picture (distance in red over 8 m, thickness in green over 2 m, foam in blue)
        // and the distances down the middle column, for the diagnostics of the surface's smoothing.
        private static void DumpFound(RenderTexture found, string file)
        {
            RenderTexture was = RenderTexture.active;
            RenderTexture.active = found;
            var read = new Texture2D(found.width, found.height, TextureFormat.RGBAHalf, false, true);
            read.ReadPixels(new Rect(0, 0, found.width, found.height), 0, 0);
            read.Apply(false);
            RenderTexture.active = was;
            Color[] pixels = read.GetPixels();
            var shown = new Color32[pixels.Length];
            var column = new StringBuilder();
            var row = new StringBuilder();
            int middle = found.width / 2;
            int midRow = found.height / 2;
            for (int i = 0; i < pixels.Length; i++)
            {
                Color p = pixels[i];
                shown[i] = new Color32((byte)Mathf.Clamp(p.r / 8f * 255f, 0f, 255f), (byte)Mathf.Clamp(p.g / 2f * 255f, 0f, 255f), (byte)Mathf.Clamp(p.b * 255f, 0f, 255f), 255);
                if (i % found.width == middle && (i / found.width) % 2 == 0)
                {
                    column.Append(Inv($"{p.r:0.00}/{p.g:0.00} "));
                }

                if (i / found.width == midRow && (i % found.width) % 2 == 0)
                {
                    row.Append(Inv($"{p.r:0.000} "));
                }
            }

            var png = new Texture2D(found.width, found.height, TextureFormat.RGBA32, false);
            png.SetPixels32(shown);
            png.Apply(false);
            File.WriteAllBytes(Path.Combine(_output, file + "_found.png"), png.EncodeToPNG());
            File.WriteAllText(Path.Combine(_output, file + "_found.txt"), "column: " + column + "\nrow: " + row);
            UnityEngine.Object.DestroyImmediate(read);
            UnityEngine.Object.DestroyImmediate(png);
        }

        public void Dispose()
        {
            Water.Dispose();
            _solids.Dispose();
            UnityEngine.Object.DestroyImmediate(_bed);
            if (_target != null)
            {
                UnityEngine.Object.DestroyImmediate(_target);
            }
        }

        private static GameObject Slab(Vector3 centre, Vector3 size, Material material)
        {
            var slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            slab.transform.position = centre;
            slab.transform.localScale = size;
            slab.GetComponent<MeshRenderer>().sharedMaterial = material;
            return slab;
        }
    }
}
