using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using FlowingWater.Client;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

// Runs the water moved on the graphics card (the plugin's SimGrid, FlowingWaterSim.compute) on scenes like the
// reference videos', without the game, and writes what it measured (sim.txt) and frames (PNG). SimGrid.cs is the
// plugin's own, copied into Assets/Editor/Shared by tools/sim-preview.sh, which runs this with a display:
//   FW_PREVIEW_OUT=/some/folder Unity -batchmode -quit -projectPath unity -executeMethod SimPreview.Run -logFile -
public static class SimPreview
{
    private const int Cells = 160;

    private const float Dx = 0.25f;

    private const int PerVoxel = 4;

    private const float Frame = 1f / 60f;

    private static string _output;

    private static readonly StringBuilder Log = new();

    public static void Run()
    {
        try
        {
            _output = Environment.GetEnvironmentVariable("FW_PREVIEW_OUT") ?? Path.GetFullPath("preview");
            Directory.CreateDirectory(_output);
            BuildBundles.Prepare();

            // FW_PREVIEW_ONLY (a scene's name, as "edge"): that scene alone; a name of none is refused.
            string only = Environment.GetEnvironmentVariable("FW_PREVIEW_ONLY");
            var scenes = new (string Name, Action Run)[]
            {
                ("dambreak", () => DamBreak(0.03f)),
                ("dambreak-smooth", () => DamBreak(0f)),
                ("slosh", Slosh),
                ("obstacle", Obstacle),
                ("follow", Follow),
                ("ledge", () => Ledge(true)),
                ("ledge-unthrown", () => Ledge(false)),
                ("shift", Shift),
                ("shift-thrown", ShiftThrown),
                ("spray", Spray),
                ("look", LookOnly),
                ("edge", Edge),
                ("waves", Waves),
                ("cost", Cost),
            };
            if (!string.IsNullOrEmpty(only) && scenes.All(s => s.Name != only))
            {
                throw new ArgumentException($"FW_PREVIEW_ONLY: no scene {only} ({string.Join(", ", scenes.Select(s => s.Name))})");
            }

            foreach (var scene in scenes.Where(s => string.IsNullOrEmpty(only) || s.Name == only))
            {
                scene.Run();
            }

            File.WriteAllText(Path.Combine(_output, "sim.txt"), Log.ToString());
            Debug.Log($"FlowingWater: sim preview written to {_output}");
        }
        catch (Exception e)
        {
            Debug.LogError($"FlowingWater: sim preview failed: {e}");
            EditorApplication.Exit(1);
        }
    }

    // As the server's lab: a tank 10 m long and 4 m wide, 2 m of water held over its first 2.5 m, let go.
    private static void DamBreak(float roughness)
    {
        float[] bed = Ground((x, z) => x >= 8f && x < 18f && z >= 8f && z < 12f ? 0f : 10f);
        using var scene = new Scene("dambreak", bed);
        scene.Grid.Pulled = false;
        scene.Grid.Roughness = roughness;
        Log.AppendLine(Inv($"Roughness {roughness}:"));
        scene.Grid.SetDepth(Depths((x, z) => x >= 8f && x < 10.5f && z >= 8f && z < 12f ? 2f : 0f));
        Log.AppendLine("Dam break: 10 m tank, 2.5 m x 2 m column. Server: wall at 2.92 s (now), 2.06 s (shelved momentum); real about 1.3-1.5 s.");
        float hit = -1f, highest = 0f;
        for (int k = 1; k <= 360; k++)
        {
            scene.Step();
            float[] h = scene.Read();
            float front = 0f;
            for (int i = 0; i < Cells; i++)
            {
                if (h[(40 * Cells) + i] > 0.01f)
                {
                    front = (i + 1) * Dx;
                }
            }

            float wall = h[(40 * Cells) + 71];
            highest = Math.Max(highest, wall);
            if (hit < 0f && wall > 0.01f)
            {
                hit = k * Frame;
            }

            if (k % 12 == 0 && k <= 240)
            {
                Log.AppendLine(Inv($"  t={k * Frame:0.00}s front={front - 8f:0.00} m from the tank's start, at the far wall {wall:0.00} m"));
            }

            // The depth along the tank against Ritter's (no friction, dam at 2.5 m, before the wall): every half metre.
            if (k is 18 or 36)
            {
                float t = k * Frame;
                float c = (float)Math.Sqrt(SimGrid.Gravity * 2f);
                string sim = string.Join(" ", Enumerable.Range(0, 20).Select(n => h[(40 * Cells) + 32 + (n * 2)].ToString("0.00", CultureInfo.InvariantCulture)));
                string ritter = string.Join(" ", Enumerable.Range(0, 20).Select(n =>
                {
                    float x = (n * 0.5f) + 0.125f - 2.5f;
                    float d = x <= -c * t ? 2f : x >= 2f * c * t ? 0f : ((2f * c) - (x / t)) * ((2f * c) - (x / t)) / (9f * SimGrid.Gravity);
                    return d.ToString("0.00", CultureInfo.InvariantCulture);
                }));
                Log.AppendLine(Inv($"  t={t:0.00}s depth along the tank, every 0.5 m:"));
                Log.AppendLine("    sim    " + sim);
                Log.AppendLine("    ritter " + ritter);
            }

            if (roughness > 0f && k is 6 or 15 or 30 or 45 or 60 or 90 or 120 or 180)
            {
                scene.Shot(Inv($"dambreak_{k * Frame:0.00}s"), new Vector3(13f, 2.5f, 3.5f), new Vector3(13f, 0.6f, 10f));
            }
        }

        Log.AppendLine(Inv($"  Reaches the far wall at {hit:0.00} s, highest there {highest:0.00} m (level once still 0.50 m); substeps a frame {scene.Grid.Substeps}"));
    }

    // An 8 m tank, water 1 m deep, its surface tilted 0.6 m end to end, let go: its first mode's period is 5.1 s.
    private static void Slosh()
    {
        float[] bed = Ground((x, z) => x >= 8f && x < 16f && z >= 8f && z < 10f ? 0f : 10f);
        using var scene = new Scene("slosh", bed);
        scene.Grid.Pulled = false;
        scene.Grid.SetDepth(Depths((x, z) => x >= 8f && x < 16f && z >= 8f && z < 10f ? 1f + (0.3f * (1f - (2f * (x - 8f) / 8f))) : 0f));
        Log.AppendLine("Slosh: 8 m tank, 1 m deep, tilted +-0.3 m; period 5.1 s.");
        for (int k = 1; k <= 1800; k++)
        {
            scene.Step();
            if (k % 30 == 0)
            {
                float[] h = scene.Read();
                Log.AppendLine(Inv($"  t={k * Frame:0.0}s west={h[(36 * Cells) + 32]:0.000} east={h[(36 * Cells) + 63]:0.000}"));
            }

            if (k is 60 or 150)
            {
                scene.Shot(Inv($"slosh_{k * Frame:0.0}s"), new Vector3(12f, 4f, 3f), new Vector3(12f, 0.8f, 9f));
            }
        }
    }

    // A block of water let go at the top of a slope, running down onto a block standing in its way.
    private static void Obstacle()
    {
        float[] bed = Ground((x, z) =>
        {
            if (x < 6f || x > 34f || z < 6f || z > 34f)
            {
                return 20f;
            }

            bool block = x > 18f && x < 21f && z > 18f && z < 22f;
            return block ? 6f : 0.12f * (x - 6f);
        });
        using var scene = new Scene("obstacle", bed);
        scene.Grid.Pulled = false;
        scene.Grid.SetDepth(Depths((x, z) => x > 29f && x < 34f && z > 14f && z < 26f ? 1.5f : 0f));
        Log.AppendLine("Obstacle: 5 x 12 m of water 1.5 m deep let go at the top of a 12 % slope, a block in its way.");
        for (int k = 1; k <= 300; k++)
        {
            scene.Step();
            if (k is 30 or 60 or 90 or 150 or 300)
            {
                scene.Shot(Inv($"obstacle_{k * Frame:0.0}s"), new Vector3(12f, 7f, 10f), new Vector3(22f, 1.5f, 20f));
            }
        }

        float[] h = scene.Read();
        Log.AppendLine(Inv($"  after 5 s: water {h.Sum() * Dx * Dx:0.0} m3 of {5f * 12f * 1.5f:0.0}"));
    }

    // Pulled toward a server's water: a basin the server fills to 1 m, then 1.5 m, then empties.
    private static void Follow()
    {
        float[] bed = Ground((x, z) => Math.Min(4f, 0.02f * (((x - 20f) * (x - 20f)) + ((z - 20f) * (z - 20f)))));
        using var scene = new Scene("follow", bed);
        Log.AppendLine("Follow: a basin the server says holds water to 1 m, then 1.5 m from 4 s, then none from 8 s.");
        float[] target = new float[(Cells / PerVoxel) * (Cells / PerVoxel) * 4];
        void Target(float top)
        {
            for (int v = 0; v < target.Length / 4; v++)
            {
                float x = ((v % (Cells / PerVoxel)) + 0.5f) * Dx * PerVoxel;
                float z = ((v / (Cells / PerVoxel)) + 0.5f) * Dx * PerVoxel;
                float ground = Math.Min(4f, 0.02f * (((x - 20f) * (x - 20f)) + ((z - 20f) * (z - 20f))));
                target[v * 4] = ground < top ? top : SimGrid.DryTop;
                target[(v * 4) + 1] = 1f;
                target[(v * 4) + 2] = Math.Max(0f, top - ground);
            }

            scene.Grid.SetTarget(target);
        }

        Target(1f);
        for (int k = 1; k <= 720; k++)
        {
            if (k == 240)
            {
                Target(1.5f);
            }
            else if (k == 480)
            {
                Target(-1f);
            }

            scene.Step();
            if (k % 30 == 0)
            {
                float[] h = scene.Read();
                float centre = h[(80 * Cells) + 80];
                Log.AppendLine(Inv($"  t={k * Frame:0.0}s level at the centre {centre:0.000} m, water {h.Sum() * Dx * Dx:0.0} m3"));
            }

            if (k is 120 or 300 or 540)
            {
                scene.Shot(Inv($"follow_{k * Frame:0.0}s"), new Vector3(8f, 7f, 8f), new Vector3(20f, 1f, 20f));
            }
        }
    }

    // Falls, splashes and spray: 2 m of water let go on a shelf 3 m high, running off its lip onto the floor below; the
    // particles thrown, seen, and counted. Thrown, the water running over the lip leaves the sheet and lands where the
    // falling water does (else it runs into the cell past the lip): all the water, on the card and in the air, is
    // counted, and where it lies on the floor.
    private static void Ledge(bool throws)
    {
        float[] bed = Ground((x, z) => x < 6f || x > 34f || z < 12f || z > 28f ? 20f : x < 20f ? 3f : 0f);
        string name = throws ? "ledge" : "ledge-unthrown";
        using var scene = new Scene(name, bed);
        scene.Grid.Pulled = false;
        scene.Grid.Throws = throws;
        scene.Grid.SetDepth(Depths((x, z) => x > 6f && x < 12f && z > 12f && z < 28f ? 2f : 0f));
        var compute = AssetDatabase.LoadAssetAtPath<ComputeShader>(BuildBundles.SprayComputePath);
        using var spray = new SprayParticles(compute, AssetDatabase.LoadAssetAtPath<Material>(BuildBundles.SprayMaterialPath));
        scene.Spray = spray;
        Log.AppendLine(Inv($"Ledge{(throws ? string.Empty : " (water not thrown)")}: 2 m of water (192 m3) let go on a shelf 3 m high, running off its lip at x = 20 m."));
        for (int k = 1; k <= 300; k++)
        {
            scene.Step();
            spray.Step(scene.Grid, Vector2.zero, Frame);
            if (k % 30 == 0)
            {
                int alive = spray.Alive(out int sheets, out float flying);
                float[] depth = scene.Read();
                float sheet = depth.Sum() * Dx * Dx;
                float air = Read(scene.Grid.Airborne).Sum() + (ReadWhole(scene.Grid.Landed).Sum(n => (float)n) / SimGrid.LandScale);

                // The water on the floor: how much, and how far past the lip it lies, on average.
                float floor = 0f, moment = 0f;
                for (int j = 0; j < Cells; j++)
                {
                    for (int i = 0; i < Cells; i++)
                    {
                        float x = (i + 0.5f) * Dx;
                        if (x > 20f && x < 34f)
                        {
                            floor += depth[(j * Cells) + i] * Dx * Dx;
                            moment += depth[(j * Cells) + i] * Dx * Dx * (x - 20f);
                        }
                    }
                }

                Log.AppendLine(Inv($"  t={k * Frame:0.0}s particles {alive} (sheets {sheets}); water {sheet + air + flying:0.00} m3 (sheet {sheet:0.00}, thrown or landed {air:0.000}, flying {flying:0.000}); on the floor {floor:0.00} m3, {(floor > 0f ? moment / floor : 0f):0.00} m past the lip"));
            }

            if (throws && k is 45 or 60 or 75 or 90 or 120 or 180)
            {
                scene.Shot(Inv($"ledge_{k * Frame:0.00}s"), new Vector3(27f, 6f, 6f), new Vector3(21f, 1.5f, 20f));
            }
        }

        scene.Spray = null;
    }

    private static int[] ReadWhole(RenderTexture texture)
    {
        AsyncGPUReadbackRequest request = AsyncGPUReadback.Request(texture);
        request.WaitForCompletion();
        return request.GetData<int>().ToArray();
    }

    private static float[] Read(RenderTexture texture)
    {
        AsyncGPUReadbackRequest request = AsyncGPUReadback.Request(texture);
        request.WaitForCompletion();
        return request.GetData<float>().ToArray();
    }

    // The square moved on with the camera: a sloshing pool shifted 8 cells (2 m) east and 4 north mid-slosh keeps its water
    // where it was in the world (its middle moves 8 and 4 cells the other way in the square), and its sloshing goes on.
    private static void Shift()
    {
        float[] bed = Ground((x, z) => x < 8f || x > 32f || z < 8f || z > 32f ? 20f : 0f);
        using var scene = new Scene("shift", bed);
        scene.Grid.Pulled = false;
        scene.Grid.SetDepth(Depths((x, z) => x > 8f && x < 32f && z > 8f && z < 32f ? (x < 20f ? 1.5f : 0.5f) : 0f));
        for (int k = 0; k < 60; k++)
        {
            scene.Step();
        }

        (float before, float beforeX, float beforeZ) = Middle(scene.Read());
        scene.Grid.Shift(8, 4);
        (float after, float afterX, float afterZ) = Middle(scene.Read());
        for (int k = 0; k < 60; k++)
        {
            scene.Step();
        }

        (float later, _, _) = Middle(scene.Read());
        string Check(bool ok) => ok ? "OK" : "FAILED";
        Log.AppendLine(Inv($"Shift: a sloshing pool moved 8 cells east and 4 north: water {before:0.00} -> {after:0.00} m3 (then {later:0.00}); its middle {beforeX:0.00}, {beforeZ:0.00} -> {afterX:0.00}, {afterZ:0.00} cells: {Check(Mathf.Abs(after - before) < 0.01f && Mathf.Abs(beforeX - afterX - 8f) < 0.01f && Mathf.Abs(beforeZ - afterZ - 4f) < 0.01f)}"));

    }

    // Water thrown over a shelf's lip (no spray to carry it, so it stays in the air) is let go when the square moves. (A
    // scene of its own: a new scene unloads the textures of one still open.)
    private static void ShiftThrown()
    {
        string Check(bool ok) => ok ? "OK" : "FAILED";
        float[] shelf = Ground((x, z) => x < 6f || x > 34f || z < 12f || z > 28f ? 20f : x < 20f ? 3f : 0f);
        using var thrown = new Scene("shift-thrown", shelf);
        thrown.Grid.Pulled = false;
        thrown.Grid.Throws = true;
        thrown.Grid.SetDepth(Depths((x, z) => x > 6f && x < 12f && z > 12f && z < 28f ? 2f : 0f));
        for (int k = 0; k < 150; k++)
        {
            thrown.Step();
        }

        float air = Read(thrown.Grid.Airborne).Sum();
        thrown.Grid.Shift(4, 0);
        float left = Read(thrown.Grid.Airborne).Sum() + ReadWhole(thrown.Grid.Landed).Sum(n => (float)n);
        Log.AppendLine(Inv($"Shift: water in the air {air:0.00} m3 before, {left:0.00} after: {Check(air > 0f && left <= 0f)}"));
    }

    // The water in all (m3), and its middle (cells).
    private static (float Water, float X, float Z) Middle(float[] h)
    {
        float sum = 0f, x = 0f, z = 0f;
        for (int k = 0; k < h.Length; k++)
        {
            sum += h[k];
            x += h[k] * (k % Cells);
            z += h[k] * (k / Cells);
        }

        return (sum * Dx * Dx, x / sum, z / sum);
    }

    // The waves drawn on running water are only the look: a dam let go and drawn every half second (the surface shader
    // raising its waves, its churn) moves exactly as the same dam never drawn.
    private static void LookOnly()
    {
        float[] Run(bool drawn)
        {
            float[] bed = Ground((x, z) => x >= 8f && x < 18f && z >= 8f && z < 12f ? 0f : 10f);
            using var scene = new Scene(drawn ? "look-drawn" : "look-not-drawn", bed);
            scene.Grid.Pulled = false;
            scene.Grid.SetDepth(Depths((x, z) => x >= 8f && x < 10.5f && z >= 8f && z < 12f ? 2f : 0f));
            for (int k = 1; k <= 90; k++)
            {
                scene.Step();
                if (drawn && k % 30 == 0)
                {
                    scene.Shot(Inv($"look_{k * Frame:0.0}s"), new Vector3(14f, 4f, 3f), new Vector3(14f, 0.5f, 10f));
                }
            }

            return scene.Read();
        }

        float[] drawnDepth = Run(true), plainDepth = Run(false);
        float most = drawnDepth.Zip(plainDepth, (a, b) => Mathf.Abs(a - b)).Max();
        Log.AppendLine(Inv($"Look: a dam drawn with its waves and the same never drawn differ by {most:0.000000} m at most: {(most <= 0f ? "OK" : "FAILED")}"));
    }

    // Water let go at the top of a gentle slope, its ground bumpy by a few centimetres: it runs down in a thin sheet whose
    // fronts and sides meet dry ground, shot close and low, then from over it.
    private static void Edge()
    {
        float[] bed = Ground((x, z) => (0.04f * (40f - x)) + (0.03f * Mathf.Sin(x * 2.1f) * Mathf.Cos(z * 1.7f)) + (0.02f * Mathf.Sin((x + z) * 4.3f)));
        using var scene = new Scene("edge", bed);
        scene.Grid.Pulled = false;
        scene.Grid.SetDepth(Depths((x, z) => x > 2f && x < 7f && z > 16f && z < 24f ? 0.25f : 0f));
        Log.AppendLine("Edge: 10 m3 let go on a 4% slope, bumpy by 5 cm.");
        for (int k = 1; k <= 360; k++)
        {
            scene.Step();
            if (k is 120 or 360)
            {
                float[] h = scene.Read();
                float front = 0f;
                for (int i = 0; i < Cells; i++)
                {
                    if (h[(80 * Cells) + i] > 0.01f)
                    {
                        front = (i + 1) * Dx;
                    }
                }

                // The last wet cell (front is its far side).
                int last = (int)(front / Dx) - 1;
                string behind = string.Join(" ", Enumerable.Range(0, 12).Select(n => h[(80 * Cells) + Math.Max(last - n, 0)].ToString("0.000", CultureInfo.InvariantCulture)));
                Log.AppendLine(Inv($"  t={k * Frame:0.0}s front at x = {front:0.00} m along z = 20 m; depth back from it, every 0.25 m: {behind}"));
                float Height(float x) => 0.04f * (40f - x);
                scene.Shot(Inv($"edge_{k * Frame:0.0}s_low"), new Vector3(front + 1.2f, Height(front) + 0.5f, 18f), new Vector3(front - 0.6f, Height(front), 20f));
                scene.Shot(Inv($"edge_{k * Frame:0.0}s_near"), new Vector3(front + 3f, Height(front) + 2f, 15f), new Vector3(front - 1f, Height(front), 19f));
                scene.Shot(Inv($"edge_{k * Frame:0.0}s_over"), new Vector3(front - 2f, Height(front) + 5f, 13f), new Vector3(front - 2f, Height(front), 20f));
            }
        }
    }

    // The waves drawn on running water (the shaders' own functions, asked on the card): their height by speed and depth;
    // the noise's and the chop's slopes those of their heights; the chop's height within -1 to 1 at every moment (its
    // two phases' weights adding to one), carried with the current, its second phase the first's half a cycle later,
    // and never jumping as a phase starts again.
    private static void Waves()
    {
        const float Cycle = 4f, Length = 2.4f;
        var probe = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Editor/WaveProbe.compute");
        probe.SetFloat("_FlowCycle", Cycle);
        probe.SetFloat("_Length", Length);
        Vector4[] Ask(string kernel, Vector4[] questions, float time)
        {
            probe.SetVector("_Time", new Vector4(time / 20f, time, 2f * time, 3f * time));
            probe.SetInt("_Count", questions.Length);
            using var input = new ComputeBuffer(questions.Length, 16);
            using var output = new ComputeBuffer(questions.Length, 16);
            input.SetData(questions);
            int k = probe.FindKernel(kernel);
            probe.SetBuffer(k, "_In", input);
            probe.SetBuffer(k, "_Out", output);
            probe.Dispatch(k, (questions.Length + 63) / 64, 1, 1);
            var answers = new Vector4[questions.Length];
            output.GetData(answers);
            return answers;
        }

        string Check(bool ok) => ok ? "OK" : "FAILED";

        // Speed, depth, the height expected: still water, by the speed squared, the most, a share of shallow water's
        // depth, no water.
        float[][] heights =
        {
            new[] { 0f, 1f, 0f }, new[] { 2f, 1f, 0.06f }, new[] { 10f, 1f, 0.15f }, new[] { 4f, 0.1f, 0.025f }, new[] { 4f, 0.003f, 0f },
        };
        Vector4[] height = Ask("Heights", heights.Select(q => new Vector4(q[0], q[1], 0f, 0f)).ToArray(), 0f);
        float heightOff = heights.Select((q, i) => Mathf.Abs(height[i].x - q[2])).Max();
        Log.AppendLine(Inv($"Waves: heights still, 2 m/s, 10 m/s, 4 m/s on 10 cm and on 3 mm: {string.Join(", ", height.Select(a => a.x.ToString("0.000", CultureInfo.InvariantCulture)))} m: {Check(heightOff < 1e-5f)}"));

        // Points inside the noise's cells (not by their edges, where its curvature changes at once), each with its
        // neighbours a step east, west, north and south.
        static float Within(int i, float spread) => 0.05f + (0.9f * Mathf.Repeat(i * spread, 1f));
        Vector2[] points = Enumerable.Range(0, 256).Select(i => new Vector2(-40 + (5 * (i % 16)) + Within(i, 0.618034f), -40 + (5 * (i / 16)) + Within(i, 0.754878f))).ToArray();
        Vector4[] Asked(string kernel, Vector2 current, float time, float step) =>
            Ask(kernel, points.SelectMany(p => new[] { Vector2.zero, new(step, 0f), new(-step, 0f), new(0f, step), new(0f, -step) }.Select(d => new Vector4((p + d).x, (p + d).y, current.x, current.y))).ToArray(), time);
        // The most a slope given differs from the slope of the values given a step around it, and the largest value's
        // size.
        (float Slope, float Value) Compare(Vector4[] answers, float step)
        {
            float slope = 0f, value = 0f;
            for (int i = 0; i < points.Length; i++)
            {
                Vector4 at = answers[5 * i];
                float east = (answers[(5 * i) + 1].x - answers[(5 * i) + 2].x) / (2f * step);
                float north = (answers[(5 * i) + 3].x - answers[(5 * i) + 4].x) / (2f * step);
                slope = Mathf.Max(slope, Mathf.Max(Mathf.Abs(at.y - east), Mathf.Abs(at.z - north)));
                value = Mathf.Max(value, Mathf.Abs(at.x));
            }

            return (slope, value);
        }

        var noise = Compare(Asked("Noises", Vector2.zero, 0f, 1e-2f), 1e-2f);
        Log.AppendLine(Inv($"Waves: noise's slope off its values' by {noise.Slope:0.0000} at most, values within {noise.Value:0.000}: {Check(noise.Slope < 0.02f && noise.Value <= 1f)}"));

        // The chop over a cycle (a millimetre's step: its noise's cells are not avoided there): its slope its height's,
        // its height within 1.
        float chopSlope = 0f, chopValue = 0f;
        foreach (float time in new[] { 0.3f, 1.1f, 2.5f, 3.9f })
        {
            var chop = Compare(Asked("Chops", new Vector2(1.5f, -0.7f), time, 1e-3f), 1e-3f);
            chopSlope = Mathf.Max(chopSlope, chop.Slope);
            chopValue = Mathf.Max(chopValue, chop.Value);
        }

        Log.AppendLine(Inv($"Waves: chop's slope off its heights' by {chopSlope:0.0000} /m at most, heights within {chopValue:0.000}: {Check(chopSlope < 0.02f && chopValue <= 1f)}"));

        // Half a cycle in, the first phase alone is seen, carried half a cycle's run of the current: as still water's chop
        // that far upstream. The second phase at the start is the first half a cycle later, moved by its offset.
        Vector2 current = new(1.5f, -0.7f), offset = 0.5f * new Vector2(23.7f, 41.3f);
        Vector4[] places = points.Select(p => new Vector4(p.x, p.y, current.x, current.y)).ToArray();
        Vector4[] carried = Ask("Chops", places, 0.5f * Cycle);
        Vector4[] upstream = Ask("Chops", places.Select(p => new Vector4(p.x - (current.x * 0.5f * Cycle), p.y - (current.y * 0.5f * Cycle), 0f, 0f)).ToArray(), 0.5f * Cycle);
        Vector4[] start = Ask("Chops", places, 0f);
        Vector4[] later = Ask("Chops", places.Select(p => new Vector4(p.x + offset.x, p.y + offset.y, p.z, p.w)).ToArray(), 0.5f * Cycle);
        float carriedOff = carried.Zip(upstream, (a, b) => (a - b).magnitude).Max();
        float phaseOff = start.Zip(later, (a, b) => (a - b).magnitude).Max();
        Log.AppendLine(Inv($"Waves: chop carried with the current off still water's upstream by {carriedOff:0.000000}, second phase off the first's by {phaseOff:0.000000}: {Check(carriedOff < 1e-4f && phaseOff < 1e-4f)}"));

        // A phase starting again (its weight none then): the chop a millisecond before and after differs by little.
        Vector4[] before = Ask("Chops", places, Cycle - 0.001f), after = Ask("Chops", places, Cycle + 0.001f);
        float jump = before.Zip(after, (a, b) => Mathf.Abs(a.x - b.x)).Max();
        Log.AppendLine(Inv($"Waves: chop across a phase's start changes by {jump:0.0000} at most: {Check(jump < 0.01f)}"));

        // The long waves as the shaders raise them: the card's water and the blocks' fully shown, 1 m deep, at the same
        // places give the same rise and slope (both world places: they meet at the square's edge); that rise is the
        // long chop's only (no fine waves raised) times the waves' height; the blocks fading at a shore raise half as
        // much at half shown, none at none; still water rises none.
        Vector4[] Long(Vector4[] at, float depth, float shown)
        {
            probe.SetFloat("_Depth", depth);
            probe.SetFloat("_Shown", shown);
            return Ask("Longs", at, 1.7f);
        }

        float runs = current.magnitude, rapids = WaveHeight(runs, 1f);
        probe.SetFloat("_Length", Length);
        Vector4[] longChop = Ask("Chops", places, 1.7f);
        Vector4[] card = Long(places, 1f, 1f), blocks = Long(places, 1f, 1f), half = Long(places, 1f, 0.5f), none = Long(places, 1f, 0f);
        Vector4[] still = Long(places.Select(p => new Vector4(p.x, p.y, 0f, 0f)).ToArray(), 1f, 1f);
        float edgeOff = card.Zip(blocks, (a, b) => (a - b).magnitude).Max();
        float longOff = card.Zip(longChop, (a, c) => Mathf.Max(Mathf.Abs(a.x - (rapids * c.x)), Mathf.Abs(a.y - (rapids * c.y)), Mathf.Abs(a.z - (rapids * c.z)), Mathf.Abs(a.w - c.x))).Max();
        float fadeOff = half.Zip(card, (h, c) => Mathf.Abs(h.x - (0.5f * c.x))).Concat(none.Select(n => Mathf.Abs(n.x))).Max();
        float stillRise = still.Max(a => Mathf.Abs(a.x) + Mathf.Abs(a.y) + Mathf.Abs(a.z));
        float risen = card.Max(a => Mathf.Abs(a.x));
        Log.AppendLine(Inv($"Waves: long waves at {runs:0.00} m/s on 1 m, up to {risen:0.000} m: card's off the blocks' by {edgeOff:0.000000}, off the long chop's by {longOff:0.000000}, faded off by {fadeOff:0.000000}, still water's {stillRise:0.000000}: {Check(risen > 0.5f * rapids && edgeOff <= 0f && longOff < 1e-6f && fadeOff < 1e-6f && stillRise <= 0f)}"));

        // The fine waves, only lit: a flat surface's normal on fast deep water is tipped by their slope (the fine chop's
        // times half the waves' height), the same near and far (one function, the look's); on still water not at all.
        Vector4[] Fine(Vector4[] at)
        {
            probe.SetFloat("_Depth", 1f);
            return Ask("Fines", at, 1.7f);
        }

        Vector4[] fast = places.Select(p => new Vector4(p.x, p.y, 4f, 0f)).ToArray();
        probe.SetFloat("_Length", 0.8f);
        Vector4[] fineChop = Ask("Chops", fast, 1.7f);
        Vector4[] tipped = Fine(fast), flat = Fine(places.Select(p => new Vector4(p.x, p.y, 0f, 0f)).ToArray());
        float fineHeight = 0.5f * WaveHeight(4f, 1f);
        float fineOff = tipped.Zip(fineChop, (n, c) => (n - (Vector4)new Vector3(-fineHeight * c.y, -fineHeight * c.z, 1f).normalized).magnitude).Max();
        float tilt = tipped.Max(n => Mathf.Acos(Mathf.Clamp01(n.z)) * Mathf.Rad2Deg);
        float flatTilt = flat.Max(n => Mathf.Abs(n.x) + Mathf.Abs(n.y) + Mathf.Abs(1f - n.z));
        Log.AppendLine(Inv($"Waves: fine waves tip fast water's surface up to {tilt:0.0} degrees, off their slope by {fineOff:0.000000}; still water's by {flatTilt:0.000000}: {Check(tilt > 1f && fineOff < 1e-5f && flatTilt <= 0f)}"));

        // The churn: a fast crest white (0.7), a fast trough and a still crest not, a slow crest a quarter as much as a
        // fast one, still water slowing hard white; the blocks' (giving no slowing) only on their crests.
        float[][] churns =
        {
            new[] { 1f, 1f, 0f, 0.7f }, new[] { -1f, 1f, 0f, 0f }, new[] { 1f, 0f, 0f, 0f }, new[] { 1f, 0.5f, 0f, 0.175f }, new[] { 0f, 0f, 1.5f, 0.7f }, new[] { 0f, 0f, 0f, 0f },
        };
        Vector4[] churn = Ask("Churns", churns.Select(q => new Vector4(q[0], q[1], q[2], 0f)).ToArray(), 0f);
        float churnOff = churns.Select((q, i) => Mathf.Abs(churn[i].x - q[3])).Max();
        Log.AppendLine(Inv($"Waves: churn on a fast crest, trough, still crest, half-fast crest, still water slowing, still water: {string.Join(", ", churn.Select(a => a.x.ToString("0.000", CultureInfo.InvariantCulture)))}: {Check(churnOff < 1e-5f)}"));
    }

    // The waves' height at this speed (m/s) and depth (m), by the shaders' rule (FlowingWaterWaves.cginc).
    private static float WaveHeight(float speed, float depth) => depth > 0.005f ? Mathf.Min(0.015f * speed * speed, 0.15f, 0.25f * depth) : 0f;

    // The spray off running water, by its rules (with no friction, so a thin sheet runs as fast as deep water): a thin
    // sheet shooting down a ramp throws no droplet, a dam let go throws some from its deep fast water, a still pool none;
    // and every droplet is fine (at most 2.5 cm across), short-lived (0.7 s at most) and thrown up at 1.5 m/s at most.
    private static void Spray()
    {
        var compute = AssetDatabase.LoadAssetAtPath<ComputeShader>(BuildBundles.SprayComputePath);
        var material = AssetDatabase.LoadAssetAtPath<Material>(BuildBundles.SprayMaterialPath);
        // The most droplets alive at once while all the water is shallower than thinUnder (m), and the fastest current
        // then; the droplets' largest size, longest life and fastest rise.
        (int Droplets, float Current, float Largest, float Longest, float Fastest) Throw(string name, float[] bed, float[] depth, float thinUnder)
        {
            using var scene = new Scene(name, bed);
            scene.Grid.Pulled = false;
            scene.Grid.Roughness = 0f;
            scene.Grid.SetDepth(depth);
            using var spray = new SprayParticles(compute, material);
            int droplets = 0;
            float current = 0f, largest = 0f, longest = 0f, fastest = 0f;
            for (int k = 1; k <= 120; k++)
            {
                scene.Step();
                spray.Step(scene.Grid, Vector2.zero, Frame);
                if (k % 6 == 0)
                {
                    float[] data = spray.Read();
                    int alive = 0;
                    for (int p = 0; p < SprayParticles.Count; p++)
                    {
                        int at = p * 12;
                        if (data[at + 3] < data[at + 7] && data[at + 9] < 0.5f)
                        {
                            alive++;
                            largest = Mathf.Max(largest, 2f * data[at + 8]);
                            longest = Mathf.Max(longest, data[at + 7]);
                            fastest = Mathf.Max(fastest, data[at + 5]);
                        }
                    }

                    if (scene.Read().Max() < thinUnder)
                    {
                        droplets = Math.Max(droplets, alive);
                        current = Mathf.Max(current, Read(scene.Grid.SpeedEast).Max(Mathf.Abs));
                    }
                }
            }

            return (droplets, current, largest, longest, fastest);
        }

        var thin = Throw("spray-thin", Ground((x, z) => x < 6f || x > 34f || z < 12f || z > 28f ? 40f : 0.8f * (34f - x)), Depths((x, z) => x > 6f && x < 14f && z > 12f && z < 28f ? 0.03f : 0f), 0.1f);
        var deep = Throw("spray-deep", Ground((x, z) => x < 6f || x > 34f || z < 12f || z > 28f ? 20f : 0f), Depths((x, z) => x > 6f && x < 12f && z > 12f && z < 28f ? 2f : 0f), float.MaxValue);
        var still = Throw("spray-still", Ground((x, z) => x < 6f || x > 34f || z < 12f || z > 28f ? 20f : 0f), Depths((x, z) => x > 6f && x < 34f && z > 12f && z < 28f ? 1f : 0f), float.MaxValue);
        string Check(bool ok) => ok ? "OK" : "FAILED";
        Log.AppendLine(Inv($"Spray: thin sheet down a ramp (under 10 cm), running up to {thin.Current:0.0} m/s, {thin.Droplets} droplets: {Check(thin.Droplets == 0 && thin.Current > 4.5f)}"));
        Log.AppendLine(Inv($"Spray: dam let go, {deep.Droplets} droplets at most: {Check(deep.Droplets > 0)}"));
        Log.AppendLine(Inv($"Spray: still pool, {still.Droplets} droplets: {Check(still.Droplets == 0)}"));
        Log.AppendLine(Inv($"Spray: droplets at most {deep.Largest * 100f:0.0} cm across, living {deep.Longest:0.00} s, rising {deep.Fastest:0.00} m/s: {Check(deep.Largest <= 0.025f && deep.Longest <= 0.7f && deep.Fastest <= 1.5f)}"));
    }

    // What a frame costs the card at the game's size (384 cells a side, 96 m of 0.25 m), the square full of moving water:
    // the time of many frames' steps, waited for, over their count.
    private static void Cost()
    {
        var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(BuildBundles.SimComputePath);
        foreach (int cells in new[] { 384, 768 })
        {
            using var grid = new SimGrid(shader, cells, 0.25f, 4);
            float[] bed = new float[cells * cells];
            float[] depth = new float[cells * cells];
            for (int k = 0; k < bed.Length; k++)
            {
                int i = k % cells, j = k / cells;
                bed[k] = 0.3f * Mathf.Sin(i * 0.05f) * Mathf.Cos(j * 0.07f);
                depth[k] = i < cells / 3 ? 2f : 0.2f;
            }

            grid.WriteBed(0, bed, cells);
            grid.SetDepth(depth);
            float[] target = new float[grid.Columns * grid.Columns * 4];
            for (int v = 0; v < grid.Columns * grid.Columns; v++)
            {
                target[v * 4] = 1f;
                target[(v * 4) + 1] = 1f;
            }

            grid.SetTarget(target);
            foreach (float frame in new[] { 1f / 60f, 1f / 30f })
            {
                for (int k = 0; k < 30; k++)
                {
                    grid.Step(frame);
                }

                AsyncGPUReadback.Request(grid.Depth).WaitForCompletion();
                var watch = System.Diagnostics.Stopwatch.StartNew();
                const int Frames = 600;
                for (int k = 0; k < Frames; k++)
                {
                    grid.Step(frame);
                }

                AsyncGPUReadback.Request(grid.Depth).WaitForCompletion();
                Log.AppendLine(Inv($"Cost: {cells} cells a side, frames of {1f / frame:0} fps ({grid.Substeps} steps each): {watch.Elapsed.TotalMilliseconds / Frames:0.000} ms a frame"));
            }

            // The particles over that water (moved only, not drawn), at 60 fps once the ring is full.
            var sprayShader = AssetDatabase.LoadAssetAtPath<ComputeShader>(BuildBundles.SprayComputePath);
            using var spray = new SprayParticles(sprayShader, AssetDatabase.LoadAssetAtPath<Material>(BuildBundles.SprayMaterialPath));
            for (int k = 0; k < 120; k++)
            {
                grid.Step(1f / 60f);
                spray.Step(grid, Vector2.zero, 1f / 60f);
            }

            int alive = spray.Alive(out _, out _);
            var sprayWatch = System.Diagnostics.Stopwatch.StartNew();
            for (int k = 0; k < 600; k++)
            {
                spray.Step(grid, Vector2.zero, 1f / 60f);
            }

            AsyncGPUReadback.Request(grid.Depth).WaitForCompletion();
            Log.AppendLine(Inv($"Cost: particles over {cells} cells ({alive} alive of {SprayParticles.Count}): {sprayWatch.Elapsed.TotalMilliseconds / 600:0.000} ms a frame to move"));
        }
    }

    private static float[] Ground(Func<float, float, float> height) => Field((x, z) => height(x, z));

    private static float[] Depths(Func<float, float, float> depth) => Field(depth);

    // A value per cell, at its centre (world x, z from the square's corner).
    private static float[] Field(Func<float, float, float> at)
    {
        float[] values = new float[Cells * Cells];
        for (int j = 0; j < Cells; j++)
        {
            for (int i = 0; i < Cells; i++)
            {
                values[(j * Cells) + i] = at((i + 0.5f) * Dx, (j + 0.5f) * Dx);
            }
        }

        return values;
    }

    private static string Inv(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    // A scene: the ground as a mesh, the sim's water drawn over it with the plugin's material, a light, a camera.
    private sealed class Scene : IDisposable
    {
        private readonly string _name;

        private readonly Material _water;

        public Scene(string name, float[] bed)
        {
            _name = name;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(BuildBundles.SimComputePath);
            Grid = new SimGrid(shader, Cells, Dx, PerVoxel);
            Grid.WriteBed(0, bed, Cells);
            float[] owned = new float[Grid.Columns * Grid.Columns * 4];
            for (int v = 0; v < Grid.Columns * Grid.Columns; v++)
            {
                owned[v * 4] = SimGrid.DryTop;
                owned[(v * 4) + 1] = 1f;
            }

            Grid.SetTarget(owned);
            Shader.SetGlobalFloat("_FWSimOn", 1f);
            Shader.SetGlobalVector("_FWSimOrigin", Vector2.zero);
            Shader.SetGlobalFloat("_FWSimSize", Cells * Dx);
            Shader.SetGlobalTexture("_FWSimTarget", Grid.Target);
            _water = new Material(AssetDatabase.LoadAssetAtPath<Material>(BuildBundles.SimMaterialPath));
            MakeGround(bed);
            MakeWater();
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.1f;
            sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            RenderSettings.ambientLight = new Color(0.45f, 0.5f, 0.6f);
        }

        public SimGrid Grid { get; }

        // Particles drawn with each shot, if any.
        public SprayParticles Spray { get; set; }

        public void Step() => Grid.Step(Frame);

        public float[] Read()
        {
            AsyncGPUReadbackRequest request = AsyncGPUReadback.Request(Grid.Depth);
            request.WaitForCompletion();
            return request.GetData<float>().ToArray();
        }

        public void Shot(string file, Vector3 from, Vector3 at)
        {
            _water.SetTexture("_SimH", Grid.Depth);
            _water.SetTexture("_SimBed", Grid.Bed);
            _water.SetTexture("_SimU", Grid.SpeedEast);
            _water.SetTexture("_SimV", Grid.SpeedNorth);
            _water.SetFloat("_SimCells", Cells);
            _water.SetFloat("_SimDx", Dx);
            var camera = new GameObject("Camera").AddComponent<Camera>();
            camera.transform.position = from;
            camera.transform.LookAt(at);
            camera.backgroundColor = new Color(0.55f, 0.7f, 0.85f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.depthTextureMode = DepthTextureMode.Depth;
            camera.fieldOfView = 60f;
            var target = new RenderTexture(1280, 720, 24);
            camera.targetTexture = target;
            if (Spray != null)
            {
                // Drawn with the next render of this camera only.
                var commands = new CommandBuffer();
                commands.DrawProcedural(Matrix4x4.identity, Spray.Material, 0, MeshTopology.Triangles, SprayParticles.Count * 6, 1, Spray.Properties);
                camera.AddCommandBuffer(CameraEvent.AfterForwardAlpha, commands);
            }

            camera.Render();
            RenderTexture.active = target;
            var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            image.Apply();
            File.WriteAllBytes(Path.Combine(_output, file + ".png"), image.EncodeToPNG());
            RenderTexture.active = null;
            camera.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(camera.gameObject);
            UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(image);
        }

        public void Dispose() => Grid.Dispose();

        // The ground, a checker of 2 m squares (rock where it rises past 5 m).
        private static void MakeGround(float[] bed)
        {
            var vertices = new Vector3[Cells * Cells];
            var uvs = new Vector2[Cells * Cells];
            for (int j = 0; j < Cells; j++)
            {
                for (int i = 0; i < Cells; i++)
                {
                    vertices[(j * Cells) + i] = new Vector3((i + 0.5f) * Dx, bed[(j * Cells) + i], (j + 0.5f) * Dx);
                    uvs[(j * Cells) + i] = new Vector2((i + 0.5f) * Dx / 4f, (j + 0.5f) * Dx / 4f);
                }
            }

            var mesh = new Mesh { indexFormat = IndexFormat.UInt32, vertices = vertices, uv = uvs, triangles = Triangles(Cells) };
            mesh.RecalculateNormals();
            var checker = new Texture2D(2, 2, TextureFormat.RGB24, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat };
            checker.SetPixels(new[] { new Color(0.62f, 0.58f, 0.48f), new Color(0.48f, 0.45f, 0.38f), new Color(0.48f, 0.45f, 0.38f), new Color(0.62f, 0.58f, 0.48f) });
            checker.Apply();
            var ground = new GameObject("Ground");
            ground.AddComponent<MeshFilter>().sharedMesh = mesh;
            ground.AddComponent<MeshRenderer>().sharedMaterial = new Material(Shader.Find("Standard")) { mainTexture = checker };
        }

        private void MakeWater()
        {
            var vertices = new Vector3[Cells * Cells];
            var cells = new Vector2[Cells * Cells];
            for (int j = 0; j < Cells; j++)
            {
                for (int i = 0; i < Cells; i++)
                {
                    vertices[(j * Cells) + i] = new Vector3((i + 0.5f) * Dx, 0f, (j + 0.5f) * Dx);
                    cells[(j * Cells) + i] = new Vector2(i + 0.5f, j + 0.5f);
                }
            }

            var mesh = new Mesh { indexFormat = IndexFormat.UInt32, vertices = vertices, uv = cells, triangles = Triangles(Cells) };
            mesh.bounds = new Bounds(new Vector3(Cells * Dx / 2f, 0f, Cells * Dx / 2f), new Vector3(Cells * Dx, 100f, Cells * Dx));
            var water = new GameObject(_name + " water");
            water.AddComponent<MeshFilter>().sharedMesh = mesh;
            water.AddComponent<MeshRenderer>().sharedMaterial = _water;
        }

        private static int[] Triangles(int side)
        {
            int[] triangles = new int[(side - 1) * (side - 1) * 6];
            int t = 0;
            for (int j = 0; j < side - 1; j++)
            {
                for (int i = 0; i < side - 1; i++)
                {
                    int a = (j * side) + i;
                    int b = a + side;
                    triangles[t++] = a;
                    triangles[t++] = b;
                    triangles[t++] = b + 1;
                    triangles[t++] = a;
                    triangles[t++] = b + 1;
                    triangles[t++] = a + 1;
                }
            }

            return triangles;
        }
    }
}
