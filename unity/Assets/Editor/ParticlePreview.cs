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

// Runs the particle water (the plugin's ParticleWater, FlowingWaterParticles.compute) on scenes like the card's water's
// (SimPreview), without the game, and writes what it measured (particles.txt) and frames (PNG). ParticleWater.cs is the
// plugin's own, copied into Assets/Editor/Shared by tools/sim-preview.sh, which runs this with a display:
//   tools/sim-preview.sh /some/folder ParticlePreview.Run
public static class ParticlePreview
{
    private const int Cells = 160;

    private const float Dx = 0.25f;

    private const float Frame = 1f / 60f;

    // The scenes' particles' side (m) and water (m3): a litre.
    private const float Spacing = 0.1f;

    private const float Litre = Spacing * Spacing * Spacing;

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
            // FW_PREVIEW_SCENES picks some scenes (names apart by commas); else all run. A name of none is refused.
            string[] chosen = (Environment.GetEnvironmentVariable("FW_PREVIEW_SCENES") ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries);
            string[] known = { "rest", "dambreak", "ledge", "ball", "own", "spout", "look", "roof", "walls", "boat", "cost", "surface", "pour" };
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

            if (Runs("pour"))
            {
                Pour();
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

            if (Runs("own"))
            {
                Own(0.5f);
                Own(0.25f);
            }

            if (Runs("spout"))
            {
                Spout(0.25f);
            }

            if (Runs("look"))
            {
                Look(0.5f);
                Look(0.25f);
            }

            if (Runs("roof"))
            {
                Roof();
            }

            if (Runs("walls"))
            {
                Walls();
            }

            if (Runs("boat"))
            {
                Boat();
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
    // Water poured onto a cloud that holds water already (the console pours again, the preview's scenes pour onto
    // their own): each block's particles go after the ones there, each at its place with its layout, none of the
    // earlier ones touched (the card takes the floats from the array's start at the particles' own place in the buffer,
    // twelve a particle).
    private static void Pour()
    {
        float[] bed = Field((x, z) => 0f);
        using var scene = new Scene("ppour", bed, 8000);
        var boxes = new List<(Vector3 Min, Vector3 Max, int Count)>();
        foreach ((Vector3 min, Vector3 max) in new[] { (new Vector3(10f, 0f, 10f), new Vector3(11f, 0.5f, 11f)), (new Vector3(20f, 1f, 20f), new Vector3(22f, 1.5f, 21f)), (new Vector3(30f, 0f, 30f), new Vector3(30.5f, 0.5f, 30.5f)) })
        {
            boxes.Add((min, max, scene.Water.Add(min, max)));
        }

        int poured = boxes.Sum(b => b.Count);
        float[] data = scene.Water.Read();
        int misplaced = 0, malformed = 0, p = 0;
        foreach ((Vector3 min, Vector3 max, int count) in boxes)
        {
            for (int k = 0; k < count; k++, p++)
            {
                var at = new Vector3(data[p * 12], data[(p * 12) + 1], data[(p * 12) + 2]);
                float slack = 0.03f * scene.Water.Spacing;
                bool inside = at.x >= min.x - slack && at.y >= min.y - slack && at.z >= min.z - slack && at.x <= max.x + slack && at.y <= max.y + slack && at.z <= max.z + slack;
                misplaced += inside ? 0 : 1;
                var predicted = new Vector3(data[(p * 12) + 4], data[(p * 12) + 5], data[(p * 12) + 6]);
                bool formed = data[(p * 12) + 3] == 1f && predicted == at && data[(p * 12) + 8] == 0f && data[(p * 12) + 9] == 0f && data[(p * 12) + 10] == 0f && data[(p * 12) + 11] == 1f;
                malformed += formed ? 0 : 1;
            }
        }

        bool ok = scene.Water.Count == poured && data.Length == poured * 12 && misplaced == 0 && malformed == 0;
        Log.AppendLine(Inv($"Pour: three blocks poured one after another onto the same cloud ({string.Join(", ", boxes.Select(b => b.Count))} particles; {poured} in all, the card holds {scene.Water.Count}): {misplaced} out of their block, {malformed} not laid out as poured: {Check(ok)}"));
    }

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
        Log.AppendLine(Inv($"Dam break: 10 m tank, 2.5 m x 2 m column: {added} particles ({added * Litre:0.0} m3). Card's sheet: wall at 1.25 s; real about 1.3-1.5 s."));
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
        Log.AppendLine(Inv($"Ledge: {added} particles ({added * Litre:0.0} m3) of water 1 m deep let go 6 m from a shelf's lip, 3 m over a basin's floor (full, 0.67 m)."));
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

    // The particles as the water near the camera, held to the server's (the sheet's Follow scene): a basin the server
    // says holds water to 1 m, then 1.5 m from 4 s, then none from 8 s; the particles fill it from below, rise with it,
    // and go as it empties, at the pull's rate (0.8 of the difference a second: four seconds leave 4 % of it), so four
    // seconds after each change their water is within a tenth of the way to the server's (counting the water deep
    // enough for a particle to stand in: the rim thinner than one is not made), and never thrown up high.
    private static void Own(float spacing)
    {
        float[] bed = Field((x, z) => Math.Min(4f, 0.02f * (((x - 20f) * (x - 20f)) + ((z - 20f) * (z - 20f)))));
        using var scene = new SimPreview.Scene(Inv($"pown-{spacing}"), bed);
        scene.Grid.Pulled = false;
        var compute = AssetDatabase.LoadAssetAtPath<ComputeShader>(BuildBundles.ParticlesComputePath);
        using var water = new ParticleWater(compute, AssetDatabase.LoadAssetAtPath<Material>(BuildBundles.ParticlesMaterialPath), 262144, spacing);
        water.SetGround(scene.Grid.Bed, Cells, Dx, Vector2.zero, 50f);
        water.SetOwner(scene.Grid.Target, 4, SimGrid.BorderCells, float.NegativeInfinity);
        scene.Particles = water;
        // Drawn as spheres: this scene (the sheet's) has no sky for the water's surface to show.
        bool wereSpheres = ParticleSurface.Spheres;
        ParticleSurface.Spheres = true;
        const int Columns = Cells / 4;
        float[] target = new float[Columns * Columns * 4];
        // The server's water in the basin: all of it, and what a particle can stand in (the rim thinner than a
        // particle is not made, but water that runs into it is kept up to the server's; the particles' water lies
        // between the two).
        (float All, float Standing) Target(float top)
        {
            float all = 0f, standing = 0f;
            for (int v = 0; v < Columns * Columns; v++)
            {
                float x = ((v % Columns) + 0.5f) * Dx * 4, z = ((v / Columns) + 0.5f) * Dx * 4;
                float ground = Math.Min(4f, 0.02f * (((x - 20f) * (x - 20f)) + ((z - 20f) * (z - 20f))));
                int cx = v % Columns, cz = v / Columns;
                bool edge = cx * 4 < SimGrid.BorderCells || cz * 4 < SimGrid.BorderCells || (cx * 4) + 3 >= Cells - SimGrid.BorderCells || (cz * 4) + 3 >= Cells - SimGrid.BorderCells;
                target[v * 4] = ground < top ? top : SimGrid.DryTop;
                target[(v * 4) + 1] = 1f;
                target[(v * 4) + 2] = Math.Max(0f, top - ground);
                float column = ground < top && !edge ? (top - ground) * Dx * 4 * Dx * 4 : 0f;
                all += column;
                standing += top - ground >= spacing ? column : 0f;
            }

            scene.Grid.SetTarget(target);
            return (all, standing);
        }

        Log.AppendLine(Inv($"Own ({spacing} m particles): a basin the server says holds water to 1 m, then 1.5 m from 4 s, then none from 8 s."));
        (float worth, float standing) = Target(1f);
        float have = 0f;
        float gap = worth;
        bool ok = true;
        float highest = 0f;
        for (int k = 1; k <= 720; k++)
        {
            if (k == 240)
            {
                (worth, standing) = Target(1.5f);
                gap = Mathf.Abs(have - worth);
            }
            else if (k == 480)
            {
                (worth, standing) = Target(-1f);
                gap = Mathf.Abs(have - worth);
            }

            water.Step(Frame);
            if (k % 60 == 0 || (k + 1) % 240 == 0)
            {
                float[] data = water.Read();
                int alive = 0;
                float top = float.MinValue;
                for (int p = 0; p < data.Length / 12; p++)
                {
                    if (ParticleWater.Alive(data[(p * 12) + 3]))
                    {
                        alive++;
                        top = Math.Max(top, data[(p * 12) + 1]);
                    }
                }

                have = alive * water.Litre;
                highest = Math.Max(highest, alive > 0 ? top : 0f);
                int strays = 0;
                float fastest = 0f;
                Vector3 where = Vector3.zero;
                for (int p = 0; p < data.Length / 12; p++)
                {
                    if (!ParticleWater.Alive(data[(p * 12) + 3]))
                    {
                        continue;
                    }

                    var at = new Vector3(data[p * 12], data[(p * 12) + 1], data[(p * 12) + 2]);
                    float ground = Math.Min(4f, 0.02f * (((at.x - 20f) * (at.x - 20f)) + ((at.z - 20f) * (at.z - 20f))));
                    strays += at.y > Math.Max(worth > 0f ? 1.5f : 0f, ground) + 0.5f ? 1 : 0;
                    var v = new Vector3(data[(p * 12) + 8], data[(p * 12) + 9], data[(p * 12) + 10]);
                    if (v.magnitude > fastest)
                    {
                        fastest = v.magnitude;
                        where = at;
                    }
                }

                Log.AppendLine(Inv($"    strays over the water or the ground by 0.5 m: {strays}; fastest {fastest:0.0} m/s at ({where.x:0.0}, {where.y:0.0}, {where.z:0.0})"));
                // Settled: four seconds after each change of the server's water, the gap a tenth of what it was.
                bool settled = (k + 1) % 240 == 0;
                float slack = Mathf.Max(0.1f * gap, 2f * water.Litre);
                bool near = have >= standing - slack && have <= worth + slack;
                ok &= !settled || near;
                Log.AppendLine(Inv($"  t={k * Frame:0.00}s the server's water {worth:0.0} m3 ({standing:0.0} a particle can stand in), the particles' {have:0.0} m3 ({alive}), their top {(alive > 0 ? top : 0f):0.00} m{(settled ? (near ? ": OK" : ": FAILED") : string.Empty)}"));
            }

            if (k is 60 or 180 or 420 or 540 or 720)
            {
                scene.Shot(Inv($"pown-{spacing}_{k * Frame:0.0}s"), new Vector3(8f, 7f, 8f), new Vector3(20f, 1f, 20f));
            }
        }

        Log.AppendLine(Inv($"  The particles' water came within a tenth of the way to the server's four seconds after each change: {Check(ok)}"));
        Log.AppendLine(Inv($"  Never thrown up: highest {highest:0.00} m (the water's top 1.5 m, a slosh of two particles allowed): {Check(highest < 1.5f + (2f * spacing))}"));
        ParticleSurface.Spheres = wereSpheres;
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
        int inside = 0, under = 0, onTop = 0, floor = 0, mostOnTop = 0;
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

                mostOnTop = Math.Max(mostOnTop, onTop);
                Log.AppendLine(Inv($"  t={k * Frame:0.0}s on the slab {onTop}, under it {under}, inside it {inside}, on the floor round it {floor}"));
            }

            if (k is 30 or 90 or 180 or 300)
            {
                scene.Shot(Inv($"proof_{k * Frame:0.0}s"), new Vector3(14f, 4.5f, 7f), new Vector3(14f, 1.2f, 14f));
            }
        }

        Log.AppendLine(Inv($"  On the slab at most: {mostOnTop} (it landed there, not through it); inside the slab at the end: {inside}; on the floor round it: {floor}: {Check(mostOnTop > added / 2 && inside == 0 && floor > 0)}"));
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
                    if (!pen)
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

    // A hull through the water (a ship's float box): a box 4 m long, 1.6 m wide and 1 m high, half under, driven across
    // a pool 0.8 m deep at 3 m/s and turned 20 degrees off its way: the water is shoved ahead and aside and none ends
    // inside the hull.
    private static void Boat()
    {
        float[] bed = Field((x, z) => x < 8f || x > 22f || z < 10f || z > 16f ? 10f : 0f);
        using var scene = new Scene("pboat", bed, 72000);
        int added = scene.Water.Add(new Vector3(8f, 0f, 10f), new Vector3(22f, 0.8f, 16f));
        Log.AppendLine(Inv($"Boat: a pool 14 x 6 m, 0.8 m deep ({added} particles), a hull 4 x 1.6 x 1 m driven through at 3 m/s from 1 s."));
        for (int k = 0; k < 90; k++)
        {
            scene.Step();
        }

        var half = new Vector3(2f, 0.5f, 0.8f);
        var turn = Quaternion.Euler(0f, 20f, 0f);
        var bodies = new List<ParticleWater.Body>();
        int inside = 0;
        float bow = 0f;
        for (int k = 1; k <= 240; k++)
        {
            float t = k * Frame;
            var centre = new Vector3(Mathf.Min(6f + (3f * t), 20f), 0.8f, 13f);
            bodies.Clear();
            bodies.Add(ParticleWater.Body.Box(centre, half, turn));
            scene.Water.SetBodies(bodies);
            scene.Boat = (new Bounds(centre, 2f * half), turn);
            scene.Step();
            if (k % 30 == 0)
            {
                float[] data = scene.Water.Read();
                inside = 0;
                bow = 0f;
                for (int p = 0; p < data.Length / 12; p++)
                {
                    var at = new Vector3(data[p * 12], data[(p * 12) + 1], data[(p * 12) + 2]);
                    Vector3 local = Quaternion.Inverse(turn) * (at - centre);
                    inside += Mathf.Abs(local.x) < half.x - 0.02f && Mathf.Abs(local.y) < half.y - 0.02f && Mathf.Abs(local.z) < half.z - 0.02f ? 1 : 0;
                    bow = at.x > centre.x + 1.5f && at.x < centre.x + 3.5f ? Mathf.Max(bow, at.y) : bow;
                }

                Log.AppendLine(Inv($"  t={t:0.0}s hull at x={centre.x:0.0}: inside it {inside}, the water ahead up to {bow:0.00} m (0.8 at rest)"));
            }

            if (k is 30 or 90 or 150 or 240)
            {
                scene.Shot(Inv($"pboat_{t:0.0}s"), new Vector3(15f, 4f, 6f), new Vector3(15f, 0.5f, 13f));
            }
        }

        Log.AppendLine(Inv($"  Inside the hull at the end: {inside}: {Check(inside == 0 && bow > 0.85f)}"));
    }

    // A water source in the basin's middle, 8 m3/s for 6 s: its water comes out all round its rim at the rate's speed
    // (8 m/s, the most: 14 through a 0.3 m sphere's side), while the server's water in the basin rises by the same 8 m3/s; the
    // particles' water keeps to the server's as it comes (the jet is the water, not a loss; where it lands ahead of the
    // server's pool, the loss there and the fill in the middle trade it at the pull's rate, a second or two behind),
    // and the jet is seen.
    private static void Spout(float spacing)
    {
        float[] bed = Field((x, z) => Math.Min(4f, 0.02f * (((x - 20f) * (x - 20f)) + ((z - 20f) * (z - 20f)))));
        using var scene = new SimPreview.Scene(Inv($"pspout-{spacing}"), bed);
        scene.Grid.Pulled = false;
        var compute = AssetDatabase.LoadAssetAtPath<ComputeShader>(BuildBundles.ParticlesComputePath);
        using var water = new ParticleWater(compute, AssetDatabase.LoadAssetAtPath<Material>(BuildBundles.ParticlesMaterialPath), 262144, spacing);
        water.SetGround(scene.Grid.Bed, Cells, Dx, Vector2.zero, 50f);
        water.SetOwner(scene.Grid.Target, 4, SimGrid.BorderCells, float.NegativeInfinity);
        scene.Particles = water;
        bool wereSpheres = ParticleSurface.Spheres;
        ParticleSurface.Spheres = true;
        const float Rate = 8f;
        const int Columns = Cells / 4;
        float[] target = new float[Columns * Columns * 4];
        var spouts = new List<ParticleWater.Spout> { ParticleWater.Spout.At(new Vector3(20f, 0.3f, 20f), 0.3f, Rate, 0.5f * spacing) };
        Log.AppendLine(Inv($"Spout ({spacing} m particles): a source of {Rate} m3/s in the basin's middle for 6 s, its water out of its rim at {spouts[0].Flow.x:0.0} m/s."));
        bool ok = true;
        float fastest = 0f;
        for (int k = 1; k <= 420; k++)
        {
            // The server's water: the basin (0.02 r2 deep) holds the volume so far.
            float volume = Rate * Math.Min(k, 360) * Frame;
            float top = Mathf.Sqrt(volume * 0.04f / Mathf.PI);
            for (int v = 0; v < Columns * Columns; v++)
            {
                float x = ((v % Columns) + 0.5f) * Dx * 4, z = ((v / Columns) + 0.5f) * Dx * 4;
                float ground = Math.Min(4f, 0.02f * (((x - 20f) * (x - 20f)) + ((z - 20f) * (z - 20f))));
                target[v * 4] = ground < top ? top : SimGrid.DryTop;
                target[(v * 4) + 1] = 1f;
                target[(v * 4) + 2] = Math.Max(0f, top - ground);
            }

            scene.Grid.SetTarget(target);
            water.SetSpouts(k <= 360 ? spouts : new List<ParticleWater.Spout>());
            water.Step(Frame);
            if (k % 60 == 0 || k == 30)
            {
                float[] data = water.Read();
                int alive = 0;
                float speed = 0f;
                for (int p = 0; p < data.Length / 12; p++)
                {
                    if (ParticleWater.Alive(data[(p * 12) + 3]))
                    {
                        alive++;
                        speed = Math.Max(speed, new Vector3(data[(p * 12) + 8], data[(p * 12) + 9], data[(p * 12) + 10]).magnitude);
                    }
                }

                fastest = Math.Max(fastest, speed);
                float have = alive * water.Litre;
                bool near = Mathf.Abs(have - volume) <= Mathf.Max(0.15f * volume, 2f * water.Litre);
                ok &= k < 180 || near;
                Log.AppendLine(Inv($"  t={k * Frame:0.0}s the server's water {volume:0.0} m3, the particles' {have:0.0} m3 ({alive}), fastest {speed:0.0} m/s{(k >= 180 ? (near ? ": OK" : ": FAILED") : string.Empty)}"));
            }

            if (k is 30 or 120 or 360 or 420)
            {
                scene.Shot(Inv($"pspout-{spacing}_{k * Frame:0.0}s"), new Vector3(16f, 2.5f, 16f), new Vector3(20f, 0.4f, 20f));
            }
        }

        Log.AppendLine(Inv($"  The particles' water kept within 15 % of the server's from 3 s on: {Check(ok)}; the jet seen (fastest over 3 m/s): {Check(fastest > 3f)}"));
        ParticleSurface.Spheres = wereSpheres;
    }

    // How the water near the camera looks, drawn as water, with the game's particles (0.5 m, 0.25 m): a pool 12 m
    // square a metre deep at rest in a shallow basin, with a source in it; seen from above as a player sees it, and
    // from the bank. Frames only: it should read as one sheet of water, not as balls, with foam only at the spout.
    private static void Look(float spacing)
    {
        float[] bed = Field((x, z) => Math.Min(3f, 0.03f * Math.Max(0f, Math.Max(Math.Abs(x - 20f), Math.Abs(z - 20f)) - 6f) * 10f));
        using var scene = new Scene(Inv($"plook-{spacing}"), bed, 65536, spacing);
        int added = scene.Water.Add(new Vector3(14f, 0f, 14f), new Vector3(26f, 1f, 26f));
        var spouts = new List<ParticleWater.Spout> { ParticleWater.Spout.At(new Vector3(20f, 1.3f, 20f), 0.3f, 2f, 0.5f * spacing) };
        for (int k = 1; k <= 240; k++)
        {
            scene.Water.SetSpouts(k > 120 ? spouts : new List<ParticleWater.Spout>());
            scene.Step();
            if (k is 120 or 240)
            {
                scene.Shot(Inv($"plook-{spacing}_{k * Frame:0.0}s_above"), new Vector3(20f, 9f, 11f), new Vector3(20f, 0.5f, 20f));
                scene.Shot(Inv($"plook-{spacing}_{k * Frame:0.0}s_bank"), new Vector3(20f, 1.8f, 12f), new Vector3(20f, 0.8f, 20f));
            }
        }

        Log.AppendLine(Inv($"Look ({spacing} m particles): a pool 12 m square 1 m deep ({added} particles), a 2 m3/s source in it from 2 s; frames plook-{spacing}_*.png"));
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

    // What a frame costs the card: so many particles in a tank, settled and then stirred (a block of water dropped in,
    // a quarter as many again: the scene has room for it), the time of many frames' steps, waited for, over their count.
    private static void Cost()
    {
        foreach (int target in new[] { 16384, 32768, 65536 })
        {
            float[] bed = Field((x, z) => x < 4f || x > 36f || z < 4f || z > 36f ? 20f : 0f);
            using var scene = new Scene("pcost", bed, target + (target / 4));
            // A square block of water 1 m deep whose lattice holds about so many particles.
            float side = Mathf.Sqrt(target * Litre);
            int added = scene.Water.Add(new Vector3(20f - (side / 2f), 0f, 20f - (side / 2f)), new Vector3(20f + (side / 2f), 1f, 20f + (side / 2f)));
            for (int k = 0; k < 180; k++)
            {
                scene.Step();
            }

            float still = Measure(scene.Water, 300);
            float drop = Mathf.Sqrt(target / 4f * Litre);
            int dropped = scene.Water.Add(new Vector3(20f - (drop / 2f), 3f, 20f - (drop / 2f)), new Vector3(20f + (drop / 2f), 4f, 20f + (drop / 2f)));
            float moving = Measure(scene.Water, 120);
            Log.AppendLine(Inv($"Cost: {added} particles, frames of 60 fps ({scene.Water.Substeps} steps of {scene.Water.Iterations} rounds each): {still:0.000} ms a frame at rest, {moving:0.000} ms stirred (with {dropped} more dropped in)"));
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
        float level = heights.Skip(n - tenth).Average() + (0.5f * water.Spacing);
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

        public Scene(string name, float[] bed, int capacity, float spacing = 0.1f)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            _bed = new Texture2D(Cells, Cells, TextureFormat.RFloat, false, true) { name = name + " ground", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            _bed.SetPixelData(bed, 0);
            _bed.Apply(false);
            var compute = AssetDatabase.LoadAssetAtPath<ComputeShader>(BuildBundles.ParticlesComputePath);
            Water = new ParticleWater(compute, AssetDatabase.LoadAssetAtPath<Material>(BuildBundles.ParticlesMaterialPath), capacity, spacing);
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

        // A hull drawn with each shot, if any (a body in the water, in the boat scene): its box, turned.
        public (Bounds Box, Quaternion Turn) Boat { get; set; }

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

            GameObject boat = null;
            if (Boat.Box.size.sqrMagnitude > 0f)
            {
                boat = GameObject.CreatePrimitive(PrimitiveType.Cube);
                boat.transform.SetPositionAndRotation(Boat.Box.center, Boat.Turn);
                boat.transform.localScale = Boat.Box.size;
                boat.GetComponent<MeshRenderer>().sharedMaterial = new Material(Shader.Find("Standard")) { color = new Color(0.45f, 0.3f, 0.2f) };
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

            SimPreview.Save(camera, target, file);
            if (ball != null)
            {
                UnityEngine.Object.DestroyImmediate(ball);
            }

            if (slab != null)
            {
                UnityEngine.Object.DestroyImmediate(slab);
            }

            if (boat != null)
            {
                UnityEngine.Object.DestroyImmediate(boat);
            }
        }

        public void Dispose()
        {
            Water.Dispose();
            UnityEngine.Object.DestroyImmediate(_bed);
        }
    }
}
