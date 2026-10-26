using System;
using System.Collections;
using System.Globalization;
using UnityEngine;

namespace FlowingWater.Recorder;

// The "diag" scenario: a cascade down a mountain's steep face (a source at its top), shot as the water looks and in
// the debug colours (which part draws where: the card's water, the bricks' surface, their sides and falls), near (the
// card's square round the camera) and from far off (the bricks alone), with the card's own account of the columns
// there ("flowingwater gpu", "gpu columns") in the log.
public sealed partial class Recorder
{
    private IEnumerator Diag(Vector3 spawn)
    {
        // Searched within FW_REC_REACH metres (8 to 3000; 1500 unset): a reach past that would take the game's thread for
        // minutes (or, too large for the step to count, for ever).
        float reach = float.TryParse(Environment.GetEnvironmentVariable("FW_REC_REACH"), NumberStyles.Float, CultureInfo.InvariantCulture, out float given) && given is >= 8f and <= 3000f
            ? given
            : 1500f;
        Site face = SteepestFace(spawn, reach);
        Note($"diag face {face}");
        yield return Go(face.At(0f));
        // FW_REC_DRY=1: no source, the same views of the dry face (what shows without the water).
        // A source a run before left there is taken away first (the world should be a fresh copy all the same: its water
        // stays a while).
        bool dry = Environment.GetEnvironmentVariable("FW_REC_DRY") == "1";
        Command("flowingwater remove");
        if (!dry)
        {
            Command("flowingwater source " + (Environment.GetEnvironmentVariable("FW_REC_RATE") ?? "4"));
        }

        float wait = float.TryParse(Environment.GetEnvironmentVariable("FW_REC_WAIT"), NumberStyles.Float, CultureInfo.InvariantCulture, out float waited) ? waited : 90f;
        yield return Go(face.At(-4f));
        yield return new WaitForSeconds(wait);

        Vector3 foot = face.At(14f);
        Vector3 far = face.At(10f) + (face.Across * 70f) + (face.Down * 40f) + new Vector3(0f, 35f, 0f);
        var views = new (Vector3 From, Vector3 At, string Name)[]
        {
            (face.Behind(), face.At(14f), "behind"),
            (face.Side(), face.At(12f), "side"),
            (face.Close(), face.At(7f), "close"),
            (face.At(6f) + (face.Across * 6f) + new Vector3(0f, 3f, 0f), face.At(8f), "near"),
            (face.At(8f) + (face.Across * 18f) + new Vector3(0f, 4f, 0f), face.At(8f), "across"),
            (face.At(24f) + new Vector3(0f, 2.5f, 0f), face.At(6f), "below"),
            (far, face.At(10f), "far"),
        };
        yield return HeldViews(views, "diag");

        _cameraAt = null;
        yield return Go(foot);
        Command("flowingwater gpu");
        Command("flowingwater gpu columns");
        Command("flowingwater info");
        yield return Go(face.At(7f));
        Command("flowingwater gpu columns");
    }

    // The "flood" scenario: a meadow flooded from three sources across a gentle slope, shot from inside the card's square,
    // from where its edge crosses the view, and from far off (the bricks alone), as it looks and in the debug colours,
    // with the current measured in the middle ("flowingwater info").
    private IEnumerator Flood(Vector3 spawn)
    {
        Site meadow = FindSite(spawn, 0.03f, 0.12f, true) ?? FindSite(spawn, 0.02f, 0.15f, false)
            ?? throw new InvalidOperationException($"no gentle slope within 400 m of {spawn}");
        Note($"flood meadow {meadow}");
        foreach (float side in new[] { -10f, 0f, 10f })
        {
            yield return Go(meadow.At(0f) + (meadow.Across * side));
            Command("flowingwater source " + (Environment.GetEnvironmentVariable("FW_REC_RATE") ?? "3"));
        }

        yield return Go(meadow.At(-8f));
        yield return new WaitForSeconds(float.TryParse(Environment.GetEnvironmentVariable("FW_REC_WAIT"), NumberStyles.Float, CultureInfo.InvariantCulture, out float given) ? given : 180f);

        Vector3 middle = meadow.At(25f);
        var views = new (Vector3 From, Vector3 At, string Name)[]
        {
            (middle - (meadow.Down * 12f) + new Vector3(0f, 10f, 0f), middle, "near"),
            (middle - (meadow.Down * 50f) + new Vector3(0f, 18f, 0f), middle + (meadow.Down * 10f), "edge"),
            (middle - (meadow.Down * 90f) + (meadow.Across * 25f) + new Vector3(0f, 40f, 0f), middle, "far"),
        };
        yield return HeldViews(views, "flood");

        _cameraAt = null;
        yield return Go(middle);
        Command("flowingwater info");
        Command("flowingwater gpu columns");
        yield return Go(meadow.At(12f));
        Command("flowingwater info");
    }

    // Each view held (the camera over the ground as it is, not as generated) long enough for the card's square to come
    // with it and settle, then shot as it looks and in the debug colours.
    private IEnumerator HeldViews((Vector3 From, Vector3 At, string Name)[] views, string prefix)
    {
        foreach (var view in views)
        {
            Vector3 from = OverGround(view.From, 1.5f);
            _cameraAt = from;
            _cameraLook = view.At;
            yield return new WaitForSeconds(8f);
            yield return Shot(from, view.At, $"{prefix}-{view.Name}-look");
            Command("flowingwater gpu colours");
            yield return Shot(from, view.At, $"{prefix}-{view.Name}-colours");
            Command("flowingwater gpu colours");
            Command("flowingwater gpu");
        }
    }

    // A point at least so high over the ground loaded there (the generated ground is not what a player built or dug, nor
    // the rocks on it).
    private static Vector3 OverGround(Vector3 point, float clearance)
    {
        if (Physics.Raycast(new Vector3(point.x, point.y + 500f, point.z), Vector3.down, out RaycastHit hit, 1000f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            point.y = Mathf.Max(point.y, hit.point.y + clearance);
        }

        return point;
    }

    // The steepest place within reach of a point whose ground keeps falling for 20 m downhill (a mountain's face, not a
    // notch), well over the sea, on the generated ground.
    private Site SteepestFace(Vector3 around, float reach)
    {
        WorldGenerator generator = WorldGenerator.instance;
        float sea = ZoneSystem.instance.m_waterLevel;
        Site best = null;
        // At most 200 places along a side (40 000 in all): the search runs on the game's thread, in one frame.
        float step = Mathf.Max(8f, 2f * reach / 200f);
        for (float dz = -reach; dz <= reach; dz += step)
        {
            for (float dx = -reach; dx <= reach; dx += step)
            {
                float x = around.x + dx, z = around.z + dz;
                float h = generator.GetHeight(x, z);
                if (h < sea + 30f)
                {
                    continue;
                }

                Vector2 gradient = Gradient(generator, x, z);
                float slope = gradient.magnitude;
                Vector2 down = -gradient.normalized;
                float below = generator.GetHeight(x + (down.x * 20f), z + (down.y * 20f));
                if ((best == null || slope > best.Slope) && h - below > 12f)
                {
                    best = new Site(new Vector3(x, h, z), new Vector3(down.x, 0f, down.y), slope);
                }
            }
        }

        return best ?? throw new InvalidOperationException($"no face falling 12 m in 20 m within {reach} m of {around}");
    }
}
