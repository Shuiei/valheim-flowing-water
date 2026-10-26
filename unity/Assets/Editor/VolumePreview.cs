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

    private const float Dx = 0.25f;

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
            string[] known = { "rest", "dambreak", "ledge", "spring", "box", "fence", "drain" };
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
        }

        Log.AppendLine($"  Still, 1 m deep, no particle lost or made over 5 s: {Check(ok)}");
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
        for (int k = 1; k <= 480; k++)
        {
            scene.Step();
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
                Log.AppendLine(Inv($"  t={k * Frame:0.00}s front {front - 10f:0.00} m from the tank's start, at the far wall up to {wall:0.00} m"));
            }

            if (k is 15 or 30 or 60 or 90 or 120 or 180 or 480)
            {
                scene.Shot(Inv($"vdambreak_{k * Frame:0.00}s"), new Vector3(15f, 3f, 12f), new Vector3(15f, 0.8f, 20f));
            }
        }

        Measure rest = Measure.Of(scene.Water, bed);
        Log.AppendLine(Inv($"  Reaches the far wall at {hit:0.00} s, highest there {highest:0.00} m; after 8 s the water holds {rest.Level:0.00} m of depth (at rest 0.50), {rest.Count} of {added} particles, moving {rest.Speed:0.000} m/s on average: {Check(hit > 1f && hit < 2f && Mathf.Abs(rest.Level - 0.5f) < 0.06f && rest.Count == added)}"));
    }

    // Water let go on a shelf 3 m over a basin: it pours over the lip as one body, falls, and pools below.
    private static void Ledge()
    {
        float[] bed = Field((x, z) => z < 14f || z > 26f || x < 8f || x > 32f ? 10f : x < 20f ? 3f : 0f);
        using var scene = new Scene("vledge", bed);
        int added = scene.Water.Add(new Vector3(8f, 3f, 14f), new Vector3(14f, 4f, 26f));
        Log.AppendLine(Inv($"Ledge: {added} particles ({added * scene.Water.Litre:0.0} m3) of water 1 m deep let go 6 m from a shelf's lip, 3 m over a basin's floor."));
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

                Log.AppendLine(Inv($"  t={k * Frame:0.0}s on the shelf {shelf}, in the air {air}, on the floor {floor} ({data.Length / 12} in all)"));
            }

            if (k is 60 or 120 or 180 or 300 or 480)
            {
                scene.Shot(Inv($"vledge_{k * Frame:0.0}s"), new Vector3(27f, 6f, 8f), new Vector3(21f, 1.5f, 20f));
            }
        }

        Measure m = Measure.Of(scene.Water, bed);
        Log.AppendLine(Inv($"  Particles kept: {m.Count} of {added}, in the ground {m.Buried}: {Check(m.Count == added && m.Buried == 0)}"));
    }

    // A source of 8 m3/s in a basin for 6 s: its water comes out all round it and spreads; the particles' water grows
    // by the rate.
    private static void Spring()
    {
        float[] bed = Field((x, z) => Math.Min(4f, 0.02f * (((x - 20f) * (x - 20f)) + ((z - 20f) * (z - 20f)))));
        using var scene = new Scene("vspring", bed);
        const float Rate = 8f;
        var springs = new List<VolumeWater.Spring> { VolumeWater.Spring.Source(new Vector3(20f, 0.4f, 20f), Rate) };
        Log.AppendLine(Inv($"Spring: a source of {Rate} m3/s in a basin's middle for 6 s (its water out of a sphere {springs[0].Ring.w:0.00} m round it at {springs[0].Flow.x:0.0} m/s)."));
        bool ok = true;
        for (int k = 1; k <= 480; k++)
        {
            scene.Water.SetSprings(k <= 360 ? springs : new List<VolumeWater.Spring>());
            scene.Step();
            if (k % 60 == 0)
            {
                float expected = Rate * Math.Min(k, 360) * Frame;
                Measure m = Measure.Of(scene.Water, bed);
                float have = m.Count * scene.Water.Litre;
                bool near = Mathf.Abs(have - expected) <= 0.15f * expected + 0.5f;
                ok &= near;
                (float[] scales, uint[] given) = scene.Water.ReadSources();
                Log.AppendLine(Inv($"  t={k * Frame:0.0}s the water {have:0.0} m3 (expected {expected:0.0}), fastest {m.Fastest:0.0} m/s, top {m.Top:0.00} m; the source's speed scaled {scales[0]:0.00}, gave {given[0]} particles last step (wants {Rate * Frame / scene.Water.Litre:0.0}): {(near ? "OK" : "FAILED")}"));
            }

            if (k is 30 or 120 or 360 or 480)
            {
                scene.Shot(Inv($"vspring_{k * Frame:0.0}s"), new Vector3(16f, 2.5f, 16f), new Vector3(20f, 0.4f, 20f));
            }
        }

        Log.AppendLine($"  The water grew by the rate within 15 %: {Check(ok)}");
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

    private static string Check(bool ok) => ok ? "OK" : "FAILED";

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
                int i = Mathf.Clamp((int)(x / Dx), 0, Cells - 1), j = Mathf.Clamp((int)(z / Dx), 0, Cells - 1);
                float ground = bed[(j * Cells) + i];
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

    private sealed class Scene : IDisposable
    {
        private readonly Texture2D _bed;

        private readonly ParticleSolids _solids = new();

        private readonly float[] _ground;

        public Scene(string name, float[] bed)
        {
            _ground = bed;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            _bed = new Texture2D(Cells, Cells, TextureFormat.RFloat, false, true) { name = name + " ground", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            _bed.SetPixelData(bed, 0);
            _bed.Apply(false);
            var compute = AssetDatabase.LoadAssetAtPath<ComputeShader>(BuildBundles.VolumeComputePath);
            Water = new VolumeWater(compute, AssetDatabase.LoadAssetAtPath<Material>(BuildBundles.ParticlesMaterialPath), 1 << 20, Cells, Rows, Cells, Dx) { Origin = Vector3.zero, OpenSides = false };
            Water.SetGround(_bed, Cells, Dx, Vector2.zero);
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

        // The box the walls scene draws, if any.
        private Bounds _walls;

        private float _gap;

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

        public void Shot(string file, Vector3 from, Vector3 at)
        {
            var drawn = new List<GameObject>();
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
            using (var surface = new ParticleSurface())
            {
                surface.Render(camera, Water, AssetDatabase.LoadAssetAtPath<Material>(BuildBundles.ParticleSurfaceMaterialPath));
                camera.Render();
            }

            SimPreview.Save(camera, target, file);
            foreach (GameObject thing in drawn)
            {
                UnityEngine.Object.DestroyImmediate(thing);
            }
        }

        public void Dispose()
        {
            Water.Dispose();
            _solids.Dispose();
            UnityEngine.Object.DestroyImmediate(_bed);
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
