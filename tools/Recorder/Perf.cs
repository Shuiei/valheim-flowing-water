using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace FlowingWater.Recorder;

// The perf scenario (FW_REC_SCENARIO=perf): what the water costs the player's game. Frame times with no water, then
// with a stream, a fountain, a cascade and a lake filling (each added in turn, the earlier ones left running), then
// with all of them in view; each case's frames per second, 1 % low, worst frame, the water's own time in the frame
// (the mod's measure), its bricks and vertices, and the garbage collections and memory over it. A table in log.txt,
// the same in perf.csv, a shot of each case.
public sealed partial class Recorder
{
    private static readonly string[] ColumnPieces = ["stone_pillar", "stone_wall_1x1", "wood_pole2", "wood_pole"];

    private readonly List<string> _perfRows = [];

    // The mod's own measures (internal to it, read by reflection): the water's time in the frames, its bricks.
    private PropertyInfo _waterSpent;

    private MethodInfo _waterCensus;

    private PropertyInfo _waterParts;

    private IEnumerator Perf(Vector3 spawn, Site steep, Site gentle)
    {
        // The player stands on each spring, in its water: drowning there is not what is measured.
        Player.m_localPlayer.SetGodMode(true);
        Type bricks = AccessTools.TypeByName("FlowingWater.Client.WaterBrick");
        _waterSpent = bricks == null ? null : AccessTools.Property(bricks, "SpentMilliseconds");
        _waterCensus = bricks == null ? null : AccessTools.Method(bricks, "Census");
        _waterParts = bricks == null ? null : AccessTools.Property(bricks, "SpentParts");
        if (_waterSpent == null || _waterCensus == null || _waterParts == null)
        {
            throw new InvalidOperationException("the mod's water measures (WaterBrick.SpentMilliseconds, SpentParts, Census) not found: is it the mod of this branch?");
        }

        float seconds = Seconds("FW_REC_PERF_SECONDS", 20f);
        float spread = Seconds("FW_REC_PERF_SPREAD", 30f);
        List<PerfCase> cases = PerfCases(spawn, steep, gentle);
        Note("perf cases: " + string.Join("; ", cases.Select(c => c.ToString())));
        Hud.instance.m_userHidden = true;
        _perfRows.Add("case,avg fps,1% low fps,worst ms,water ms/frame avg,water ms/frame max,frames water > 8 ms,read ms max,read start ms max,build ms max,mesh ms max,bricks,bricks drawn,vertices,gc collections,memory delta MB");

        // Each case from the same view before its water and with it: the difference is the water's, not the view's
        // (the earlier cases' water still runs, out of that view).
        foreach (PerfCase c in cases)
        {
            yield return Go(c.Stand);
            yield return Measure(c.Kind + " before", c.From, c.At, seconds);
            yield return Place(c);
            yield return new WaitForSeconds(spread);
            yield return Measure(c.Kind, c.From, c.At, seconds);
        }

        // All at once: from over the middle of them, every source running.
        Vector3 middle = cases.Aggregate(Vector3.zero, (sum, c) => sum + c.At) / cases.Count;
        float across = cases.Max(c => Vector3.Distance(c.At, middle));
        yield return Go(middle);
        yield return new WaitForSeconds(spread);
        yield return Measure("all", middle + new Vector3(0f, 20f + across, -(15f + across)), middle, seconds);

        File.WriteAllLines(Path.Combine(_out, "perf.csv"), _perfRows);
        Note("perf table (perf.csv):");
        foreach (string row in _perfRows)
        {
            Note("  " + row.Replace(",", " | "));
        }
    }

    private static float Seconds(string variable, float fallback)
    {
        string text = Environment.GetEnvironmentVariable(variable);
        return !string.IsNullOrEmpty(text) && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) && value > 0f ? value : fallback;
    }

    // The places (FW_REC_PERF: "x,z,kind;..." with kind stream, fountain, cascade or lake), else found near the spawn:
    // the stream on the gentle slope, the fountain on flat ground beside it, the cascade on the steep slope, the lake in
    // the nearest hollow (left out when there is none).
    private List<PerfCase> PerfCases(Vector3 spawn, Site steep, Site gentle)
    {
        var cases = new List<PerfCase>();
        string given = Environment.GetEnvironmentVariable("FW_REC_PERF");
        if (!string.IsNullOrEmpty(given))
        {
            foreach (string item in given.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] parts = item.Split(',');
                if (parts.Length != 3 || !Numbers(parts[0] + "," + parts[1], out float[] xz) || parts[2].Trim() is not ("stream" or "fountain" or "cascade" or "lake"))
                {
                    Note("perf place left out (not x,z,stream|fountain|cascade|lake): " + item);
                    continue;
                }

                cases.Add(PerfCase.Of(parts[2].Trim(), SiteAt(xz[0], xz[1])));
            }

            return cases.Count > 0 ? cases : throw new InvalidOperationException("FW_REC_PERF has no usable place");
        }

        cases.Add(PerfCase.Of("stream", gentle));
        cases.Add(PerfCase.Of("fountain", SiteAt(gentle.Origin.x + (gentle.Across.x * 40f), gentle.Origin.z + (gentle.Across.z * 40f))));
        cases.Add(PerfCase.Of("cascade", steep));
        if (FindHollow(spawn) is { } hollow)
        {
            cases.Add(PerfCase.Of("lake", SiteAt(hollow.x, hollow.z)));
        }
        else
        {
            Note("no hollow near the spawn: no lake");
        }

        return cases;
    }

    // A site at a point of the generated ground, facing downhill.
    private static Site SiteAt(float x, float z)
    {
        WorldGenerator generator = WorldGenerator.instance;
        var gradient = new Vector2(generator.GetHeight(x + 2f, z) - generator.GetHeight(x - 2f, z), generator.GetHeight(x, z + 2f) - generator.GetHeight(x, z - 2f)) / 4f;
        Vector2 down = gradient.sqrMagnitude > 1e-8f ? -gradient.normalized : Vector2.up;
        return new Site(new Vector3(x, generator.GetHeight(x, z), z), new Vector3(down.x, 0f, down.y), gradient.magnitude);
    }

    // The nearest point within 300 m of the spawn, on land over the sea, at least a metre under the ground 10 m round
    // it every way: a hollow a lake fills.
    private static Vector3? FindHollow(Vector3 spawn)
    {
        WorldGenerator generator = WorldGenerator.instance;
        float sea = ZoneSystem.instance.m_waterLevel;
        Vector3? best = null;
        float bestDistance = float.MaxValue;
        for (float dz = -300f; dz <= 300f; dz += 5f)
        {
            for (float dx = -300f; dx <= 300f; dx += 5f)
            {
                float x = spawn.x + dx;
                float z = spawn.z + dz;
                float h = generator.GetHeight(x, z);
                float distance = new Vector2(dx, dz).magnitude;
                if (h < sea + 2f || distance >= bestDistance)
                {
                    continue;
                }

                bool hollow = true;
                for (int k = 0; k < 8 && hollow; k++)
                {
                    float angle = k * Mathf.PI / 4f;
                    hollow = generator.GetHeight(x + (Mathf.Cos(angle) * 10f), z + (Mathf.Sin(angle) * 10f)) > h + 1f;
                }

                if (hollow)
                {
                    best = new Vector3(x, h, z);
                    bestDistance = distance;
                }
            }
        }

        return best;
    }

    // A case's source: through the console command where it stands, on a column built for the fountain.
    private IEnumerator Place(PerfCase c)
    {
        switch (c.Kind)
        {
            case "fountain":
                Vector3 top = Column(OnGround(c.At), 4f);
                Own(FormattableString.Invariant($"!source {top.x},{top.y},{top.z},1"));
                break;
            case "stream":
                Command("flowingwater source 0.5");
                break;
            case "cascade":
                Command("flowingwater source 3");
                break;
            default:
                Command("flowingwater source 5");
                break;
        }

        yield return null;
    }

    // A column of the game's pieces this high, built from the ground up (the first of ColumnPieces the game has); its
    // top.
    private Vector3 Column(Vector3 ground, float height)
    {
        GameObject piece = ColumnPieces.Select(n => ZNetScene.instance.GetPrefab(n)).FirstOrDefault(p => p != null)
            ?? throw new InvalidOperationException("none of the column pieces found: " + string.Join(", ", ColumnPieces));
        float top = ground.y;
        int count = 0;
        while (top < ground.y + height && count < 20)
        {
            GameObject placed = Instantiate(piece, new Vector3(ground.x, top, ground.z), Quaternion.identity);
            placed.GetComponent<ZNetView>().GetZDO().Set(ZDOVars.s_creator, Player.m_localPlayer.GetPlayerID());
            Physics.SyncTransforms();
            Bounds bounds = placed.GetComponentsInChildren<Collider>().Where(k => !k.isTrigger).Select(k => k.bounds)
                .Aggregate((Bounds?)null, (all, b) => all is { } a ? Grown(a, b) : b) ?? new Bounds(placed.transform.position, Vector3.one);
            top = Mathf.Max(top + 0.25f, bounds.max.y);
            count++;
        }

        Note(FormattableString.Invariant($"column of {count} {piece.name} at {ground.ToString("F1")}, top {top:0.0}"));
        return new Vector3(ground.x, top, ground.z);
    }

    private static Bounds Grown(Bounds a, Bounds b)
    {
        a.Encapsulate(b);
        return a;
    }

    // The frames over these seconds from this view: their rate, 1 % low (the 99th percentile's frame time), worst; the
    // water's time in them; garbage collections and memory.
    private IEnumerator Measure(string name, Vector3 from, Vector3 at, float seconds)
    {
        _cameraAt = Clear(from, at);
        _cameraLook = at;
        yield return new WaitForSeconds(1f);
        int collections = GC.CollectionCount(0);
        long memory = GC.GetTotalMemory(false);
        double spent = Spent();
        double[] parts = Parts();
        double[] partsMax = new double[parts.Length];
        var frames = new List<float>();
        var water = new List<double>();
        float until = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < until)
        {
            yield return null;
            frames.Add(Time.unscaledDeltaTime * 1000f);
            double now = Spent();
            water.Add(now - spent);
            spent = now;
            double[] nowParts = Parts();
            for (int i = 0; i < parts.Length; i++)
            {
                partsMax[i] = Math.Max(partsMax[i], nowParts[i] - parts[i]);
            }

            parts = nowParts;
        }

        int gcs = GC.CollectionCount(0) - collections;
        double megabytes = (GC.GetTotalMemory(false) - memory) / (1024.0 * 1024.0);
        var census = new object[] { 0, 0 };
        int bricks = (int)_waterCensus.Invoke(null, census);
        float[] sorted = [.. frames.OrderBy(f => f)];
        float total = frames.Sum();
        float p99 = sorted[Mathf.Min(sorted.Length - 1, (int)(sorted.Length * 0.99f))];
        string row = FormattableString.Invariant($"{name},{frames.Count * 1000f / total:0.0},{1000f / p99:0.0},{sorted[sorted.Length - 1]:0.0},{water.Average():0.000},{water.Max():0.000},{water.Count(w => w > 8.0)},{partsMax[0]:0.000},{(partsMax.Length > 3 ? partsMax[3] : 0):0.000},{partsMax[1]:0.000},{partsMax[2]:0.000},{bricks},{census[0]},{census[1]},{gcs},{megabytes:0.0}");
        _perfRows.Add(row);
        Note($"perf {name}: {row}");
        yield return Shot(from, at, "perf-" + name.Replace(' ', '-'));
    }

    private double Spent() => (double)_waterSpent.GetValue(null, null);

    // The water's time so far in its parts: reading data, starting builds, handing meshes over.
    private double[] Parts() => (double[])_waterParts.GetValue(null, null);

    // A case: its kind, where the player stands, and the view of it.
    private sealed class PerfCase
    {
        private PerfCase(string kind, Vector3 stand, Vector3 from, Vector3 at)
        {
            Kind = kind;
            Stand = stand;
            From = from;
            At = at;
        }

        public string Kind { get; }

        public Vector3 Stand { get; }

        public Vector3 From { get; }

        public Vector3 At { get; }

        // A stream or cascade runs downhill from the site, a fountain or lake stays round it: each seen from uphill,
        // low, under the trees' leaves (they stop no ray, so a view in or over a wood sees only leaves; their trunks
        // are kept out of the view). The player stands on the spring (the command places it under them), but beside
        // the fountain's column.
        public static PerfCase Of(string kind, Site site) => kind switch
        {
            "stream" or "cascade" => new PerfCase(kind, site.At(0f), Over(site, site.At(14f)), site.At(14f)),
            "fountain" => new PerfCase(kind, site.At(0f) - (site.Across * 5f), Over(site, site.At(0f)), site.At(0f)),
            _ => new PerfCase(kind, site.At(0f), Over(site, site.At(0f)), site.At(0f)),
        };

        private static Vector3 Over(Site site, Vector3 at)
        {
            Vector3 back = at - (site.Down * 12f);
            return new Vector3(back.x, WorldGenerator.instance.GetHeight(back.x, back.z) + 3f, back.z);
        }

        public override string ToString() => FormattableString.Invariant($"{Kind} at {Stand.ToString("F0")}");
    }
}
