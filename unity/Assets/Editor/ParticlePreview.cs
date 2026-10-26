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

// Runs the particle water (the plugin's ParticleWater, FlowingWaterParticles.compute) on scenes like the card's water's
// (SimPreview), without the game, and writes what it measured (particles.txt) and frames (PNG). ParticleWater.cs is the
// plugin's own, copied into Assets/Editor/Shared by tools/sim-preview.sh, which runs this with a display:
//   tools/sim-preview.sh /some/folder ParticlePreview.Run
public static class ParticlePreview
{
    private const int Cells = 160;

    private const float Dx = 0.25f;

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
            // FW_PREVIEW_SCENES picks some scenes (names apart by commas); else all run.
            string[] chosen = (Environment.GetEnvironmentVariable("FW_PREVIEW_SCENES") ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries);
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

            if (Runs("ball"))
            {
                Ball();
            }

            if (Runs("sheet"))
            {
                Sheet();
            }

            if (Runs("roof"))
            {
                Roof();
            }

            if (Runs("walls"))
            {
                Walls();
            }

            if (Runs("cost"))
            {
                Cost();
            }

            if (Runs("surface"))
            {
                SurfaceCost();
            }

            File.WriteAllText(Path.Combine(_output, "particles.txt"), Log.ToString());
            Debug.Log($"FlowingWater: particle preview written to {_output}");
        }
        catch (Exception e)
        {
            Debug.LogError($"FlowingWater: particle preview failed: {e}");
            EditorApplication.Exit(1);
        }
    }

    // Water at rest: a box 2 m square filled 1 m deep. After one frame its inside is as dense as water at rest (the
    // neighbours found, the kernels summed as the lattice says) and nothing has been thrown; over three seconds it
    // stays 1 m deep (not pressed down by its own weight, within 3 %) and comes to a stop (a pool lies still: its
    // particles drift under 5 cm/s on average by then, still slowing, none faster than a quarter of a metre a second).
    private static void Rest()
    {
        float[] bed = Field((x, z) => x < 19f || x > 21f || z < 19f || z > 21f ? 20f : 0f);
        using var scene = new Scene("prest", bed, 8000);
        int added = scene.Water.Add(new Vector3(19f, 0f, 19f), new Vector3(21f, 1f, 21f));
        scene.Step();
        float[] data = scene.Water.Read();
        int n = scene.Water.Count;
        float[] shares = new float[n];
        float lambdaLow = float.MaxValue, lambdaHigh = float.MinValue, fastest = 0f;
        int inside = 0;
        float insideShare = 0f;
        for (int p = 0; p < n; p++)
        {
            float x = data[p * 12], y = data[(p * 12) + 1], z = data[(p * 12) + 2];
            shares[p] = data[(p * 12) + 11];
            lambdaLow = Mathf.Min(lambdaLow, data[(p * 12) + 7]);
            lambdaHigh = Mathf.Max(lambdaHigh, data[(p * 12) + 7]);
            fastest = Mathf.Max(fastest, new Vector3(data[(p * 12) + 8], data[(p * 12) + 9], data[(p * 12) + 10]).magnitude);
            if (x > 19.3f && x < 20.7f && z > 19.3f && z < 20.7f && y > 0.3f && y < 0.7f)
            {
                inside++;
                insideShare += shares[p];
            }
        }

        Array.Sort(shares);
        insideShare /= Mathf.Max(1, inside);
        Log.AppendLine(Inv($"Rest: a box 2 m square filled 1 m deep ({added} particles; relaxation {scene.Water.Relaxation}, {scene.Water.Substeps} steps of {scene.Water.Iterations} rounds, viscosity {scene.Water.Viscosity}, tension {scene.Water.Tension}) after one frame: density shares (of rest) 10th {shares[n / 10]:0.000}, median {shares[n / 2]:0.000}, 90th {shares[n * 9 / 10]:0.000}, inside {insideShare:0.000} ({inside}); lambda {lambdaLow:0.00000} to {lambdaHigh:0.00000} m2; fastest {fastest:0.000} m/s: {Check(Mathf.Abs(insideShare - 1f) < 0.05f && fastest < 1f)}"));
        for (int k = 2; k <= 180; k++)
        {
            scene.Step();
            if (k % 30 == 0)
            {
                var now = Settled(scene.Water);
                bool last = k == 180;
                Log.AppendLine(Inv($"  t={k * Frame:0.0}s the water lies {now.Level:0.000} m deep (1 at rest), moving {now.Speed:0.0000} m/s on average, {now.Fastest:0.000} at most{(last ? ": " + Check(Mathf.Abs(now.Level - 1f) < 0.03f && now.Speed < 0.05f && now.Fastest < 0.25f) : string.Empty)}"));
                Log.AppendLine("    " + Where(scene.Water, new Vector3(19f, 0f, 19f), new Vector3(21f, now.Level, 21f)));
            }

            if (k is 60 or 180)
            {
                scene.Shot(Inv($"prest_{k * Frame:0.0}s"), new Vector3(20f, 3f, 16.5f), new Vector3(20f, 0.6f, 20f));
            }
        }

        Log.AppendLine(Inv($"  Water in the ground or the walls: {Leaked(scene.Water, bed)}: {Check(Leaked(scene.Water, bed) == 0)}"));
    }

    // As the card's water's lab (SimPreview.DamBreak): a tank 10 m long and 4 m wide, 2 m of water held over its first
    // 2.5 m, let go. The front against Ritter's, when it reaches the far wall (the card's sheet: 1.25 s, a real one about
    // 1.3 to 1.5 s), and whether the water settles.
    private static void DamBreak()
    {
        float[] bed = Field((x, z) => x >= 8f && x < 18f && z >= 8f && z < 12f ? 0f : 10f);
        using var scene = new Scene("pdambreak", bed, 24000);
        int added = scene.Water.Add(new Vector3(8f, 0f, 8f), new Vector3(10.5f, 2f, 12f));
        Log.AppendLine(Inv($"Dam break: 10 m tank, 2.5 m x 2 m column: {added} particles ({added * ParticleWater.Spacing * ParticleWater.Spacing * ParticleWater.Spacing:0.0} m3). Card's sheet: wall at 1.25 s; real about 1.3-1.5 s."));
        float hit = -1f, highest = 0f;
        for (int k = 1; k <= 480; k++)
        {
            scene.Step();
            if (k % 6 == 0 || k is 3 or 9)
            {
                float[] data = scene.Water.Read();
                float front = 0f, wall = 0f;
                for (int p = 0; p < scene.Water.Count; p++)
                {
                    float x = data[p * 12], y = data[(p * 12) + 1];
                    // The front: the water on the floor (not a drop thrown ahead).
                    if (y < 0.15f)
                    {
                        front = Mathf.Max(front, x);
                    }

                    if (x > 17.75f)
                    {
                        wall = Mathf.Max(wall, y);
                    }
                }

                highest = Mathf.Max(highest, wall);
                if (hit < 0f && wall > 0f)
                {
                    hit = k * Frame;
                }

                if (k % 12 == 0 && k <= 240)
                {
                    float t = k * Frame;
                    float c = Mathf.Sqrt(SimGrid.Gravity * 2f);
                    float ritter = Mathf.Min(10f, 2.5f + (2f * c * t));
                    Log.AppendLine(Inv($"  t={t:0.00}s front={front - 8f:0.00} m from the tank's start (Ritter {ritter:0.00}), at the far wall up to {wall:0.00} m"));
                }
            }

            if (k is 6 or 15 or 30 or 45 or 60 or 90 or 120 or 180 or 480)
            {
                scene.Shot(Inv($"pdambreak_{k * Frame:0.00}s"), new Vector3(13f, 2.5f, 3.5f), new Vector3(13f, 0.6f, 10f));
            }
        }

        // The depth it holds: twice its particles' mean height (a slosh still going moves the top about, not the mean).
        var rest = Settled(scene.Water);
        float[] after = scene.Water.Read();
        float held = 2f * Enumerable.Range(0, scene.Water.Count).Average(p => after[(p * 12) + 1]);
        Log.AppendLine(Inv($"  Reaches the far wall at {hit:0.00} s, highest there {highest:0.00} m; after 8 s the water holds {held:0.00} m of depth (at rest 0.50), its particles moving {rest.Speed:0.000} m/s on average (a slosh goes on), {rest.Fastest:0.00} at most: {Check(hit > 0.9f && hit < 2f && Mathf.Abs(held - 0.5f) < 0.05f && rest.Speed < 1f && rest.Fastest < 6.3f)}"));
        Log.AppendLine(Inv($"  Water in the ground or the walls: {Leaked(scene.Water, bed)}: {Check(Leaked(scene.Water, bed) == 0)}; steps a frame {scene.Water.Substeps}"));
    }

    // Falls: water let go in a channel 6 m wide on a shelf 3 m high, running off its lip at x = 20 m onto the floor of a
    // basin 6 m long below, where it lands and pools (the card's sheet throws particles for this, SimPreview.Ledge; here
    // the water itself goes over).
    private static void Ledge()
    {
        float[] bed = Field((x, z) => x < 6f || x > 26f || z < 17f || z > 23f ? 20f : x < 20f ? 3f : 0f);
        using var scene = new Scene("pledge", bed, 24000);
        int added = scene.Water.Add(new Vector3(10f, 3f, 17f), new Vector3(14f, 4f, 23f));
        Log.AppendLine(Inv($"Ledge: {added} particles ({added * ParticleWater.Spacing * ParticleWater.Spacing * ParticleWater.Spacing:0.0} m3) of water 1 m deep let go 6 m from a shelf's lip, 3 m over a basin's floor (full, 0.67 m)."));
        for (int k = 1; k <= 360; k++)
        {
            scene.Step();
            if (k % 30 == 0)
            {
                float[] data = scene.Water.Read();
                int shelf = 0, air = 0, floor = 0;
                float moment = 0f, furthest = 0f;
                for (int p = 0; p < scene.Water.Count; p++)
                {
                    float x = data[p * 12], y = data[(p * 12) + 1];
                    if (x < 20f)
                    {
                        shelf++;
                    }
                    else if (y > 1f)
                    {
                        air++;
                    }
                    else
                    {
                        floor++;
                        moment += x - 20f;
                        furthest = Mathf.Max(furthest, x - 20f);
                    }
                }

                Log.AppendLine(Inv($"  t={k * Frame:0.0}s on the shelf {shelf}, in the air {air}, on the floor {floor} ({(floor > 0 ? moment / floor : 0f):0.00} m past the lip on average, {furthest:0.00} at the furthest)"));
            }

            if (k is 30 or 45 or 60 or 90 or 120 or 180 or 360)
            {
                scene.Shot(Inv($"pledge_{k * Frame:0.00}s"), new Vector3(27f, 6f, 8f), new Vector3(21f, 1.5f, 20f));
            }
        }

        Log.AppendLine(Inv($"  Water in the ground or the walls: {Leaked(scene.Water, bed)}: {Check(Leaked(scene.Water, bed) == 0)}"));
    }

    // Something pushed through water (the reference videos' ball): a pool 6 m square, 0.8 m deep, a sphere 0.6 m across
    // driven into it from its west wall and through it at 2 m/s, half under.
    private static void Ball()
    {
        float[] bed = Field((x, z) => x < 10f || x > 16f || z < 10f || z > 16f ? 10f : 0f);
        using var scene = new Scene("pball", bed, 32000);
        int added = scene.Water.Add(new Vector3(10f, 0f, 10f), new Vector3(16f, 0.8f, 16f));
        Log.AppendLine(Inv($"Ball: a pool 6 m square 0.8 m deep ({added} particles), a sphere 0.6 m across pushed through at 2 m/s from its west wall."));
        for (int k = 0; k < 120; k++)
        {
            scene.Step();
        }

        var before = Settled(scene.Water);
        Log.AppendLine(Inv($"  settled before: {before.Level:0.00} m deep, moving {before.Speed:0.000} m/s on average, {before.Fastest:0.00} at most: {Check(Mathf.Abs(before.Level - 0.8f) < 0.05f && before.Speed < 0.1f)}"));
        for (int k = 1; k <= 300; k++)
        {
            float t = k * Frame;
            // Stopped a metre short of the far wall: water pinched between the sphere and a wall squirts up.
            float x = Mathf.Min(9.4f + (2f * t), 14.4f);
            scene.Sphere = new Vector4(x, 0.5f, 13f, 0.6f);
            scene.Water.SetSphere(new Vector3(x, 0.5f, 13f), 0.6f);
            scene.Step();
            if (k % 30 == 0)
            {
                var now = Settled(scene.Water);
                Log.AppendLine(Inv($"  t={t:0.0}s sphere at x={x:0.0}: the water moves {now.Speed:0.000} m/s on average, {now.Fastest:0.00} at most, its top up to {now.Top:0.00} m"));
            }

            if (k is 30 or 60 or 90 or 120 or 150 or 300)
            {
                scene.Shot(Inv($"pball_{t:0.0}s"), new Vector3(13f, 4f, 7f), new Vector3(13f, 0.4f, 13f));
            }
        }

        Log.AppendLine(Inv($"  Water in the ground or the walls: {Leaked(scene.Water, bed)}: {Check(Leaked(scene.Water, bed) == 0)}"));
    }

    // Trading water with the card's sheet (SimPreview.Ledge's shelf: 2 m of water let go on a shelf 3 m high, running off
    // its lip at x = 20 m): the water the sheet throws over the lip becomes particles, which fall, splash and pool, and
    // give their water back to the sheet where they land, so the pool below is the sheet's. All the water is counted:
    // the sheet's, the particles', thrown but not yet taken, landed but not yet taken in.
    private static void Sheet()
    {
        float[] bed = Field((x, z) => x < 6f || x > 34f || z < 12f || z > 28f ? 20f : x < 20f ? 3f : 0f);
        using var scene = new SimPreview.Scene("psheet", bed);
        scene.Grid.Pulled = false;
        scene.Grid.Throws = true;
        scene.Grid.SetDepth(Field((x, z) => x > 6f && x < 12f && z > 12f && z < 28f ? 2f : 0f));
        var compute = AssetDatabase.LoadAssetAtPath<ComputeShader>(BuildBundles.ParticlesComputePath);
        using var water = new ParticleWater(compute, AssetDatabase.LoadAssetAtPath<Material>(BuildBundles.ParticlesMaterialPath), 65536);
        water.SetGround(scene.Grid.Bed, Cells, Dx, Vector2.zero, 50f);
        water.SetSheet(scene.Grid);
        scene.Particles = water;
        const float Total = 192f;
        Log.AppendLine(Inv($"Sheet: {Total:0} m3 of the card's water let go on a shelf 3 m high, running off its lip at x = 20 m as particles."));
        float start = -1f, most = 0f, worst = 0f;
        int mostParticles = 0;
        for (int k = 1; k <= 480; k++)
        {
            scene.Step();
            water.Step(Frame);
            if (k % 30 == 0)
            {
                // The particles still there (one gone this step is in the card's count until the next, its water the
                // sheet's already), and those down on the floor past the lip.
                float[] data = water.Read();
                int particles = 0;
                float floor = 0f;
                for (int p = 0; p < data.Length / 12; p++)
                {
                    if (data[(p * 12) + 3] > 0.5f)
                    {
                        particles++;
                        floor += data[p * 12] > 20f && data[(p * 12) + 1] < 1f ? 1f : 0f;
                    }
                }

                mostParticles = Math.Max(mostParticles, particles);
                most = Mathf.Max(most, floor);
                float sheet = scene.Read().Sum() * Dx * Dx;
                float thrown = ReadFloats(scene.Grid.Airborne).Sum();
                float landed = ReadInts(scene.Grid.Landed).Sum(n => (float)n) / SimGrid.LandScale;
                float carried = particles * ParticleWater.Spacing * ParticleWater.Spacing * ParticleWater.Spacing;
                float all = sheet + thrown + landed + carried;
                start = start < 0f ? all : start;
                worst = Mathf.Max(worst, Mathf.Abs(all - start));
                Log.AppendLine(Inv($"  t={k * Frame:0.0}s sheet {sheet:0.00} m3, particles {particles} ({carried:0.00} m3, {floor:0} down on the floor), thrown {thrown:0.000}, landed {landed:0.000}: {all:0.00} m3 in all"));
                if (k == 480)
                {
                    Log.AppendLine(Inv($"  Water in all off its start by {worst:0.000} m3 at most; particles {mostParticles} at most, {most:0} of them down on the floor at most, {floor:0} at the end (they give their water to the sheet there): {Check(worst < 0.2f && mostParticles > 100 && floor < 0.25f * most)}"));
                }
            }

            if (k is 60 or 120 or 180 or 300 or 480)
            {
                scene.Shot(Inv($"psheet_{k * Frame:0.0}s"), new Vector3(27f, 6f, 6f), new Vector3(21f, 1.5f, 20f));
            }
        }
    }

    // What players built (ParticleSolids): a slab 4 m square and 1 m thick standing 1 m over the floor of a box 10 m
    // square, with water poured onto it: the water lands on the slab (not through it), runs off its edges, and some
    // gets under it (the slab's underside holds it down); no particle ends inside the slab.
    private static void Roof()
    {
        float[] bed = Field((x, z) => x < 10f || x > 20f || z < 10f || z > 20f ? 10f : 0f);
        using var scene = new Scene("proof", bed, 8000);
        const int Columns = Cells / 4;
        const float Voxel = Dx * 4;
        byte[] solids = new byte[Columns * ParticleSolids.Rows * Columns];
        for (int z = 12; z < 16; z++)
        {
            for (int x = 12; x < 16; x++)
            {
                solids[ParticleSolids.Index(Columns, x, 1, z)] = ParticleSolids.Solid;
            }
        }

        using var built = new ParticleSolids();
        built.Take(solids, Columns, Voxel, 0, true);
        scene.Water.SetSolids(built.Texture, Columns, ParticleSolids.Rows, Voxel, 0f);
        scene.Slab = new Bounds(new Vector3(14f, 1.5f, 14f), new Vector3(4f, 1f, 4f));
        int added = scene.Water.Add(new Vector3(13f, 3f, 13f), new Vector3(15f, 3.75f, 15f));
        Log.AppendLine(Inv($"Roof: {added} particles poured from 3 m onto a slab 4 m square, 1 m thick, 1 m over the floor."));
        int inside = 0, under = 0, onTop = 0, floor = 0;
        for (int k = 1; k <= 300; k++)
        {
            scene.Step();
            if (k % 60 == 0 || k == 300)
            {
                float[] data = scene.Water.Read();
                inside = under = onTop = floor = 0;
                for (int p = 0; p < data.Length / 12; p++)
                {
                    float x = data[p * 12], y = data[(p * 12) + 1], z = data[(p * 12) + 2];
                    bool over = x > 12f && x < 16f && z > 12f && z < 16f;
                    inside += over && y > 1.02f && y < 1.98f ? 1 : 0;
                    under += over && y < 1f ? 1 : 0;
                    onTop += over && y >= 1.98f && y < 2.5f ? 1 : 0;
                    floor += !over && y < 0.5f ? 1 : 0;
                }

                Log.AppendLine(Inv($"  t={k * Frame:0.0}s on the slab {onTop}, under it {under}, inside it {inside}, on the floor round it {floor}"));
            }

            if (k is 30 or 90 or 180 or 300)
            {
                scene.Shot(Inv($"proof_{k * Frame:0.0}s"), new Vector3(14f, 4.5f, 7f), new Vector3(14f, 1.2f, 14f));
            }
        }

        Log.AppendLine(Inv($"  Inside the slab at the end: {inside}; on the floor round it: {floor}: {Check(inside == 0 && floor > 0)}"));
    }

    // Walls thinner than a voxel (closed faces, as the server has a wall piece): a pen 2 m square and 8 m high (the
    // pour's splash throws drops metres up) on the floor of a box, its walls closed faces of the voxels inside and
    // outside, with water poured into it: it fills the pen and none gets out.
    private static void Walls()
    {
        float[] bed = Field((x, z) => x < 10f || x > 20f || z < 10f || z > 20f ? 10f : 0f);
        using var scene = new Scene("pwalls", bed, 8000);
        const int Columns = Cells / 4;
        const float Voxel = Dx * 4;
        byte[] solids = new byte[Columns * ParticleSolids.Rows * Columns];
        for (int y = 0; y < 8; y++)
        {
            for (int k = 12; k < 14; k++)
            {
                solids[ParticleSolids.Index(Columns, 12, y, k)] |= ParticleSolids.ClosedWest;
                solids[ParticleSolids.Index(Columns, 11, y, k)] |= ParticleSolids.ClosedEast;
                solids[ParticleSolids.Index(Columns, 13, y, k)] |= ParticleSolids.ClosedEast;
                solids[ParticleSolids.Index(Columns, 14, y, k)] |= ParticleSolids.ClosedWest;
                solids[ParticleSolids.Index(Columns, k, y, 12)] |= ParticleSolids.ClosedSouth;
                solids[ParticleSolids.Index(Columns, k, y, 11)] |= ParticleSolids.ClosedNorth;
                solids[ParticleSolids.Index(Columns, k, y, 13)] |= ParticleSolids.ClosedNorth;
                solids[ParticleSolids.Index(Columns, k, y, 14)] |= ParticleSolids.ClosedSouth;
            }
        }

        using var built = new ParticleSolids();
        built.Take(solids, Columns, Voxel, 0, true);
        scene.Water.SetSolids(built.Texture, Columns, ParticleSolids.Rows, Voxel, 0f);
        int added = scene.Water.Add(new Vector3(12.2f, 2.5f, 12.2f), new Vector3(13.8f, 3.5f, 13.8f));
        Log.AppendLine(Inv($"Walls: {added} particles poured into a pen 2 m square, 8 m high, with walls thinner than a voxel (closed faces)."));
        int outside = 0, inside = 0;
        for (int k = 1; k <= 240; k++)
        {
            scene.Step();
            if (k % 60 == 0)
            {
                float[] data = scene.Water.Read();
                outside = inside = 0;
                var where = new System.Text.StringBuilder();
                for (int p = 0; p < data.Length / 12; p++)
                {
                    float x = data[p * 12], y = data[(p * 12) + 1], z = data[(p * 12) + 2];
                    bool pen = x > 12f && x < 14f && z > 12f && z < 14f;
                    inside += pen ? 1 : 0;
                    if (!pen && y < 2f)
                    {
                        outside++;
                        if (outside <= 5)
                        {
                            where.Append(Inv($" ({x:0.00}, {y:0.00}, {z:0.00})"));
                        }
                    }
                }

                var now = Settled(scene.Water);
                Log.AppendLine(Inv($"  t={k * Frame:0.0}s in the pen {inside}, out of it {outside}{where}, the water {now.Level:0.00} m deep (0.64 if all stays in)"));
            }

            if (k is 30 or 120 or 240)
            {
                scene.Shot(Inv($"pwalls_{k * Frame:0.0}s"), new Vector3(13f, 3.5f, 8.5f), new Vector3(13f, 0.6f, 13f));
            }
        }

        Log.AppendLine(Inv($"  Out of the pen at the end: {outside}: {Check(outside == 0 && inside > added * 9 / 10)}"));
    }

    private static float[] ReadFloats(RenderTexture texture)
    {
        AsyncGPUReadbackRequest request = AsyncGPUReadback.Request(texture);
        request.WaitForCompletion();
        return request.GetData<float>().ToArray();
    }

    private static int[] ReadInts(RenderTexture texture)
    {
        AsyncGPUReadbackRequest request = AsyncGPUReadback.Request(texture);
        request.WaitForCompletion();
        return request.GetData<int>().ToArray();
    }

    // What a frame costs the card: so many particles in a tank, settled and then stirred (a block of water dropped in),
    // the time of many frames' steps, waited for, over their count.
    private static void Cost()
    {
        foreach (int target in new[] { 16384, 32768, 65536 })
        {
            float[] bed = Field((x, z) => x < 4f || x > 36f || z < 4f || z > 36f ? 20f : 0f);
            using var scene = new Scene("pcost", bed, target);
            // A square block of water 1 m deep whose lattice holds about so many particles.
            float side = Mathf.Sqrt(target * ParticleWater.Spacing * ParticleWater.Spacing * ParticleWater.Spacing);
            int added = scene.Water.Add(new Vector3(20f - (side / 2f), 0f, 20f - (side / 2f)), new Vector3(20f + (side / 2f), 1f, 20f + (side / 2f)));
            for (int k = 0; k < 180; k++)
            {
                scene.Step();
            }

            float still = Measure(scene.Water, 300);
            scene.Water.Add(new Vector3(18f, 3f, 18f), new Vector3(22f, 4f, 22f));
            float moving = Measure(scene.Water, 120);
            Log.AppendLine(Inv($"Cost: {added} particles, frames of 60 fps ({scene.Water.Substeps} steps of {scene.Water.Iterations} rounds each): {still:0.000} ms a frame at rest, {moving:0.000} ms stirred (with {scene.Water.Count - added} more dropped in)"));
        }
    }

    // What drawing the surface costs: a pool of 32 000 particles filling the view, drawn 60 times with no particles
    // (the scene alone), as spheres, as water at half the screen's size and at its full size (1280 x 720), the time of
    // each render waited for.
    private static void SurfaceCost()
    {
        float[] bed = Field((x, z) => x < 10f || x > 20f || z < 10f || z > 20f ? 10f : 0f);
        using var scene = new Scene("psurface", bed, 32000);
        scene.Water.Add(new Vector3(10f, 0f, 10f), new Vector3(16f, 0.9f, 16f));
        for (int k = 0; k < 60; k++)
        {
            scene.Step();
        }

        var material = AssetDatabase.LoadAssetAtPath<Material>(BuildBundles.ParticleSurfaceMaterialPath);
        float Time(bool spheres, float scale, bool none)
        {
            ParticleSurface.Spheres = spheres;
            ParticleSurface.Scale = scale;
            var camera = new GameObject("Camera").AddComponent<Camera>();
            camera.transform.position = new Vector3(13f, 3.5f, 8f);
            camera.transform.LookAt(new Vector3(13f, 0.4f, 13f));
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.depthTextureMode = DepthTextureMode.Depth;
            camera.fieldOfView = 60f;
            var target = new RenderTexture(1280, 720, 24);
            camera.targetTexture = target;
            using var surface = new ParticleSurface();
            using var empty = new ParticleWater(AssetDatabase.LoadAssetAtPath<ComputeShader>(BuildBundles.ParticlesComputePath), scene.Water.Material, 16);
            ParticleWater drawn = none ? empty : scene.Water;
            for (int k = 0; k < 5; k++)
            {
                surface.Render(camera, drawn, material);
                camera.Render();
            }

            var watch = System.Diagnostics.Stopwatch.StartNew();
            for (int k = 0; k < 60; k++)
            {
                surface.Render(camera, drawn, material);
                camera.Render();
            }

            AsyncGPUReadback.Request(target).WaitForCompletion();
            float ms = (float)(watch.Elapsed.TotalMilliseconds / 60);
            camera.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(camera.gameObject);
            UnityEngine.Object.DestroyImmediate(target);
            return ms;
        }

        // Each way twice, the second time counted: the first renders of a way compile its shaders.
        Time(true, 0.5f, false);
        Time(false, 0.5f, false);
        Time(false, 1f, false);
        Time(false, 0.5f, true);
        float spheres = Time(true, 0.5f, false);
        float half = Time(false, 0.5f, false);
        float full = Time(false, 1f, false);
        float nothing = Time(false, 0.5f, true);
        ParticleSurface.Spheres = false;
        ParticleSurface.Scale = 0.5f;
        Log.AppendLine(Inv($"Surface: 32 000 particles filling a 1280 x 720 view, a render with none {nothing:0.00} ms, as spheres {spheres:0.00} ms, as water at half the size {half:0.00} ms, at the full size {full:0.00} ms"));
    }

    // Milliseconds a frame over so many frames, waited for.
    private static float Measure(ParticleWater water, int frames)
    {
        water.Read();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        for (int k = 0; k < frames; k++)
        {
            water.Step(Frame);
        }

        water.Read();
        return (float)(watch.Elapsed.TotalMilliseconds / frames);
    }

    // The water at rest: the level its particles reach (the mean of the highest tenth), their mean and fastest speed.
    private static (float Level, float Speed, float Fastest, float Top) Settled(ParticleWater water)
    {
        float[] data = water.Read();
        int n = water.Count;
        float[] heights = new float[n];
        float speed = 0f, fastest = 0f;
        for (int p = 0; p < n; p++)
        {
            heights[p] = data[(p * 12) + 1];
            float v = new Vector3(data[(p * 12) + 8], data[(p * 12) + 9], data[(p * 12) + 10]).magnitude;
            speed += v;
            fastest = Mathf.Max(fastest, v);
        }

        Array.Sort(heights);
        int tenth = Mathf.Max(1, n / 10);
        float level = heights.Skip(n - tenth).Average() + (0.5f * ParticleWater.Spacing);
        return (level, speed / n, fastest, heights[n - 1]);
    }

    // Where the water in a box (its floor and walls at min, its walls and top at max) moves: the mean speed of the
    // particles by the floor, by a wall, at the top and inside, the speed half, nine tenths and 99 % of them are under,
    // and the three fastest (where, how fast, their density).
    private static string Where(ParticleWater water, Vector3 min, Vector3 max)
    {
        float[] data = water.Read();
        int n = water.Count;
        float[] speeds = new float[n];
        float[] sums = new float[4];
        int[] counts = new int[4];
        var fastest = new (float Speed, string Text)[3];
        for (int p = 0; p < n; p++)
        {
            var at = new Vector3(data[p * 12], data[(p * 12) + 1], data[(p * 12) + 2]);
            float speed = new Vector3(data[(p * 12) + 8], data[(p * 12) + 9], data[(p * 12) + 10]).magnitude;
            speeds[p] = speed;
            const float Band = 0.12f;
            int kind = at.y < min.y + Band ? 0 : at.x < min.x + Band || at.x > max.x - Band || at.z < min.z + Band || at.z > max.z - Band ? 1 : at.y > max.y - Band ? 2 : 3;
            sums[kind] += speed;
            counts[kind]++;
            for (int f = 0; f < 3; f++)
            {
                if (speed > fastest[f].Speed)
                {
                    for (int g = 2; g > f; g--)
                    {
                        fastest[g] = fastest[g - 1];
                    }

                    fastest[f] = (speed, Inv($"({at.x - min.x:0.00}, {at.y - min.y:0.00}, {at.z - min.z:0.00}) {speed:0.00} m/s at {data[(p * 12) + 11]:0.00}"));
                    break;
                }
            }
        }

        Array.Sort(speeds);
        string Mean(int kind) => counts[kind] > 0 ? (sums[kind] / counts[kind]).ToString("0.000", CultureInfo.InvariantCulture) : "-";
        return Inv($"by the floor {Mean(0)} ({counts[0]}), by a wall {Mean(1)} ({counts[1]}), at the top {Mean(2)} ({counts[2]}), inside {Mean(3)} ({counts[3]}) m/s; half under {speeds[n / 2]:0.000}, 90 % under {speeds[n * 9 / 10]:0.000}, 99 % under {speeds[n * 99 / 100]:0.000}; fastest {fastest[0].Text}, {fastest[1].Text}, {fastest[2].Text}");
    }

    // Particles under the ground of their cell, or in a wall's.
    private static int Leaked(ParticleWater water, float[] bed)
    {
        float[] data = water.Read();
        int leaked = 0;
        for (int p = 0; p < water.Count; p++)
        {
            int i = Mathf.Clamp(Mathf.FloorToInt(data[p * 12] / Dx), 0, Cells - 1);
            int j = Mathf.Clamp(Mathf.FloorToInt(data[(p * 12) + 2] / Dx), 0, Cells - 1);
            if (data[(p * 12) + 1] < bed[(j * Cells) + i])
            {
                leaked++;
            }
        }

        return leaked;
    }

    private static string Check(bool ok) => ok ? "OK" : "FAILED";

    private static float[] Field(Func<float, float, float> at) => SimPreview.Field(Cells, Dx, at);

    private static string Inv(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    // A scene: the ground as a mesh and as the water's texture, the particles, a sphere if any, a light, a camera.
    private sealed class Scene : IDisposable
    {
        private readonly Texture2D _bed;

        public Scene(string name, float[] bed, int capacity)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            _bed = new Texture2D(Cells, Cells, TextureFormat.RFloat, false, true) { name = name + " ground", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            _bed.SetPixelData(bed, 0);
            _bed.Apply(false);
            var compute = AssetDatabase.LoadAssetAtPath<ComputeShader>(BuildBundles.ParticlesComputePath);
            Water = new ParticleWater(compute, AssetDatabase.LoadAssetAtPath<Material>(BuildBundles.ParticlesMaterialPath), capacity);
            Water.SetGround(_bed, Cells, Dx, Vector2.zero, 50f);
            // The solver's settings, tried from the environment (FW_PREVIEW_RELAXATION, FW_PREVIEW_ITERATIONS, FW_PREVIEW_VISCOSITY,
            // FW_PREVIEW_TENSION, FW_PREVIEW_SUBSTEPS: steps a frame).
            if (float.TryParse(Environment.GetEnvironmentVariable("FW_PREVIEW_RELAXATION"), NumberStyles.Float, CultureInfo.InvariantCulture, out float relaxation))
            {
                Water.Relaxation = relaxation;
            }

            if (int.TryParse(Environment.GetEnvironmentVariable("FW_PREVIEW_SUBSTEPS"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int substeps))
            {
                Water.Substep = Frame / substeps;
            }

            if (float.TryParse(Environment.GetEnvironmentVariable("FW_PREVIEW_TENSION"), NumberStyles.Float, CultureInfo.InvariantCulture, out float tension))
            {
                Water.Tension = tension;
            }

            if (int.TryParse(Environment.GetEnvironmentVariable("FW_PREVIEW_ITERATIONS"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int iterations))
            {
                Water.Iterations = iterations;
            }

            if (float.TryParse(Environment.GetEnvironmentVariable("FW_PREVIEW_VISCOSITY"), NumberStyles.Float, CultureInfo.InvariantCulture, out float viscosity))
            {
                Water.Viscosity = viscosity;
            }
            SimPreview.MakeGround(bed, Cells, Dx);
            // A sun and a sky, as PreviewRender's: the water's surface reflects the sky.
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

        public ParticleWater Water { get; }

        // Drawn with each shot (xyz its centre, w its radius; none at 0).
        public Vector4 Sphere { get; set; }

        // A slab drawn with each shot, if any (what players built, in the roof scene).
        public Bounds Slab { get; set; }

        public void Step() => Water.Step(Frame);

        public void Shot(string file, Vector3 from, Vector3 at)
        {
            GameObject ball = null;
            if (Sphere.w > 0f)
            {
                ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                ball.transform.position = Sphere;
                ball.transform.localScale = Vector3.one * (2f * Sphere.w);
                ball.GetComponent<MeshRenderer>().sharedMaterial = new Material(Shader.Find("Standard")) { color = new Color(0.9f, 0.9f, 0.9f) };
            }

            GameObject slab = null;
            if (Slab.size.sqrMagnitude > 0f)
            {
                slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
                slab.transform.position = Slab.center;
                slab.transform.localScale = Slab.size;
                slab.GetComponent<MeshRenderer>().sharedMaterial = new Material(Shader.Find("Standard")) { color = new Color(0.55f, 0.4f, 0.3f) };
            }

            var camera = new GameObject("Camera").AddComponent<Camera>();
            camera.transform.position = from;
            camera.transform.LookAt(at);
            camera.clearFlags = CameraClearFlags.Skybox;
            // As the game's camera: the water reads the scene's depth.
            camera.depthTextureMode = DepthTextureMode.Depth;
            camera.fieldOfView = 60f;
            var target = new RenderTexture(1280, 720, 24);
            camera.targetTexture = target;
            // The particles, drawn as water (or as spheres, FW_PREVIEW_SPHERES=1) with the next render of this camera only.
            ParticleSurface.Spheres = Environment.GetEnvironmentVariable("FW_PREVIEW_SPHERES") == "1";
            using (var surface = new ParticleSurface())
            {
                surface.Render(camera, Water, AssetDatabase.LoadAssetAtPath<Material>(BuildBundles.ParticleSurfaceMaterialPath));
                camera.Render();
            }
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
            if (ball != null)
            {
                UnityEngine.Object.DestroyImmediate(ball);
            }

            if (slab != null)
            {
                UnityEngine.Object.DestroyImmediate(slab);
            }
        }

        public void Dispose()
        {
            Water.Dispose();
            UnityEngine.Object.DestroyImmediate(_bed);
        }
    }
}
