using UnityEngine;
using UnityEngine.Rendering;

namespace FlowingWater.Client;

/// <summary>
/// The source and drain in the hoe's pieces: added to the hoe's piece table when the game's item list wakes up (the
/// table is the hoe prefab's own, kept from one world to the next: added once), with an icon drawn here; and the
/// values the next one placed takes.
/// </summary>
internal static class HoePieces
{
    private const string HoeName = "Hoe";

    private const int IconSize = 64;

    private static NextPlacement _next;

    /// <summary>The values the next source or drain placed takes, kept for the session (from the settings at first).</summary>
    public static NextPlacement Next => _next ??= new NextPlacement(
        FlowingWaterPlugin.Settings.DefaultSourceRate.Value,
        FlowingWaterPlugin.Settings.DefaultDrainRate.Value,
        FlowingWaterPlugin.Settings.DefaultDrainRadius.Value);

    /// <summary>
    /// Adds the source and drain to the hoe's pieces, in the category of its own (once; nothing on a game that draws
    /// nothing, a dedicated server, where no one holds a hoe).
    /// </summary>
    public static void AddTo(ObjectDB items)
    {
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
        {
            return;
        }

        GameObject hoe = items.GetItemPrefab(HoeName);
        PieceTable table = hoe?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_buildPieces;
        if (table == null)
        {
            FlowingWaterPlugin.Log.LogError($"The game's {HoeName} or its pieces not found: sources and drains cannot be placed with it (the flowingwater command still can)");
            return;
        }

        Piece.PieceCategory category = Piece.PieceCategory.Misc;
        foreach (GameObject other in table.m_pieces)
        {
            if (other != null && other.GetComponent<Piece>() is { } piece && piece.GetComponent<Marker>() == null)
            {
                category = piece.m_category;
                break;
            }
        }

        foreach (GameObject marker in Prefabs.Markers)
        {
            Piece piece = marker.GetComponent<Piece>();
            piece.m_category = category;
            if (piece.m_icon == null)
            {
                piece.m_icon = Icon(marker.GetComponent<Marker>().IsDrain);
            }

            if (!table.m_pieces.Contains(marker))
            {
                table.m_pieces.Add(marker);
            }
        }
    }

    // A source: a drop of water. A drain: a dark whirl taking it in. Drawn here, no file.
    private static Sprite Icon(bool drain)
    {
        var texture = new Texture2D(IconSize, IconSize, TextureFormat.RGBA32, false) { name = drain ? "FlowingWater drain icon" : "FlowingWater source icon" };
        var pixels = new Color32[IconSize * IconSize];
        for (int y = 0; y < IconSize; y++)
        {
            for (int x = 0; x < IconSize; x++)
            {
                // Centred, -1..1, y up.
                float u = ((x + 0.5f) / IconSize * 2f) - 1f;
                float v = ((y + 0.5f) / IconSize * 2f) - 1f;
                pixels[(y * IconSize) + x] = drain ? Whirl(u, v) : Drop(u, v);
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply();
        return Sprite.Create(texture, new Rect(0f, 0f, IconSize, IconSize), new Vector2(0.5f, 0.5f));
    }

    // A drop: a round bottom drawn to a point at the top, light on its upper left.
    private static Color32 Drop(float u, float v)
    {
        const float Radius = 0.55f;
        float cy = -0.25f;
        float r = Mathf.Sqrt((u * u) + ((v - cy) * (v - cy)));
        bool inside = r < Radius;
        if (!inside && v > cy && v < 0.9f)
        {
            // Above the round part, the sides narrow straight to the tip.
            float half = Radius * (0.9f - v) / (0.9f - cy);
            inside = Mathf.Abs(u) < half;
        }

        if (!inside)
        {
            return new Color32(0, 0, 0, 0);
        }

        float light = Mathf.Clamp01(1f - (Vector2.Distance(new Vector2(u, v), new Vector2(-0.2f, -0.05f)) / 0.9f));
        var colour = Color.Lerp(new Color(0.12f, 0.42f, 0.85f), new Color(0.7f, 0.9f, 1f), light * light);
        return colour;
    }

    // A whirl: a dark round pool, rings turning in to a black middle.
    private static Color32 Whirl(float u, float v)
    {
        float r = Mathf.Sqrt((u * u) + (v * v));
        if (r > 0.85f)
        {
            return new Color32(0, 0, 0, 0);
        }

        float turn = Mathf.Atan2(v, u) + (r * 9f);
        float ring = 0.5f + (0.5f * Mathf.Sin(turn * 2f));
        var colour = Color.Lerp(new Color(0.1f, 0.25f, 0.45f), new Color(0.45f, 0.7f, 0.9f), ring * Mathf.Clamp01(r * 1.6f));
        return Color.Lerp(Color.black, colour, Mathf.Clamp01(r * 3f));
    }
}
