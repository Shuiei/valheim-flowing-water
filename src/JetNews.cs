using System;
using System.Collections.Generic;
using System.IO;
using FlowingWater.Volume;

namespace FlowingWater;

/// <summary>
/// A source's jet as the server flies it, for the players' games: where the source stands (its marker's foot), its
/// shape and rate, and where each place across its outlet lands (<see cref="VoxelGrid.PourOutlet"/>'s places, in
/// order; none where it pours at its outlet). A player's game draws from it a jet whose source it has not loaded, too far
/// off: it knows neither the source nor the ground there.
/// </summary>
internal readonly struct ServerJet : IEquatable<ServerJet>
{
    public ServerJet(Vec3 foot, SourceShape shape, float rate, Vec3?[] landings)
    {
        Foot = foot;
        Shape = shape;
        Rate = rate;
        Landings = landings;
    }

    /// <summary>The source's marker's foot, world metres.</summary>
    public Vec3 Foot { get; }

    public SourceShape Shape { get; }

    /// <summary>Cubic metres a second.</summary>
    public float Rate { get; }

    /// <summary>Where each place across the outlet lands; null where it pours at the outlet.</summary>
    public Vec3?[] Landings { get; }

    public bool Equals(ServerJet other)
    {
        if (Foot != other.Foot || Shape != other.Shape || !Rate.Equals(other.Rate) || Landings.Length != other.Landings.Length)
        {
            return false;
        }

        for (int k = 0; k < Landings.Length; k++)
        {
            if (Landings[k] != other.Landings[k])
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object obj) => obj is ServerJet other && Equals(other);

    public override int GetHashCode() => (Foot, Shape, Rate, Landings.Length).GetHashCode();
}

/// <summary>
/// The server's jets packed for the players' games (<see cref="Packed"/>), their landings to a quarter metre: a jet
/// whose water rises or falls a little where it lands is not sent again for it. Without the game, so that it is tested.
/// </summary>
internal static class JetNews
{
    /// <summary>Landings are sent to this, metres.</summary>
    public const float Grain = 0.25f;

    /// <summary>
    /// The most jets a message holds, far over any world's: a reader refuses more before reading on, so that a broken
    /// or hostile message cannot make it set aside more memory than a game has (the bytes are gzip's: a few may stand
    /// for millions of empty landings).
    /// </summary>
    public const int MostJets = 1 << 14;

    /// <summary>The most landings a jet has: its outlet's places (a 10 m outlet over voxels of an eighth of a metre has 80).</summary>
    public const int MostPlaces = 1 << 10;

    /// <summary>The most landings a message holds, all its jets' together.</summary>
    public const int MostLandings = 1 << 20;

    private const int Version = 1;

    /// <summary>A landing as it is sent: to <see cref="Grain"/>.</summary>
    public static Vec3? Rounded(Vec3? landing) => landing is { } l ? new Vec3(Round(l.X), Round(l.Y), Round(l.Z)) : null;

    /// <summary>The jets packed.</summary>
    public static byte[] Write(IReadOnlyList<ServerJet> jets) => Packed.Write(writer =>
    {
        writer.Write(Version);
        writer.Write(jets.Count);
        foreach (ServerJet jet in jets)
        {
            WriteVec(writer, jet.Foot);
            writer.Write(jet.Shape.AllRound);
            writer.Write(jet.Shape.Heading);
            writer.Write(jet.Shape.Width);
            writer.Write(jet.Shape.Spread);
            writer.Write(jet.Shape.Throw);
            writer.Write(jet.Rate);
            writer.Write(jet.Landings.Length);
            foreach (Vec3? landing in jet.Landings)
            {
                writer.Write(landing.HasValue);
                if (landing is { } l)
                {
                    WriteVec(writer, l);
                }
            }
        }
    });

    /// <summary>
    /// The jets <see cref="Write"/> packed. A count over the format's most (<see cref="MostJets"/>,
    /// <see cref="MostPlaces"/>, <see cref="MostLandings"/>) is refused as it is read, before anything is made for it;
    /// one larger than the bytes ends with them (cut short).
    /// </summary>
    /// <exception cref="InvalidDataException">They are not that, or of another version, or count more than the most.</exception>
    public static List<ServerJet> Read(byte[] data) => Packed.Read(data, "the server's jets", reader =>
    {
        int version = reader.ReadInt32();
        if (version != Version)
        {
            throw new InvalidDataException($"the server's jets are of version {version}, not {Version}");
        }

        int count = reader.ReadInt32();
        if (count is < 0 or > MostJets)
        {
            throw new InvalidDataException($"the server's jets number {count} (at most {MostJets})");
        }

        var jets = new List<ServerJet>(count);
        int landingsLeft = MostLandings;
        for (int j = 0; j < count; j++)
        {
            Vec3 foot = ReadVec(reader);
            var shape = new SourceShape(reader.ReadBoolean(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            float rate = reader.ReadSingle();
            int places = reader.ReadInt32();
            if (places < 0 || places > MostPlaces || places > landingsLeft)
            {
                throw new InvalidDataException($"a jet of the server's has {places} places (at most {MostPlaces} each, {MostLandings} in all)");
            }

            landingsLeft -= places;
            var landings = new Vec3?[places];
            for (int k = 0; k < places; k++)
            {
                landings[k] = reader.ReadBoolean() ? ReadVec(reader) : null;
            }

            jets.Add(new ServerJet(foot, shape, rate, landings));
        }

        return jets;
    });

    /// <summary>Whether two lists of jets are the same, jet by jet.</summary>
    public static bool Same(IReadOnlyList<ServerJet> a, IReadOnlyList<ServerJet> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        for (int k = 0; k < a.Count; k++)
        {
            if (!a[k].Equals(b[k]))
            {
                return false;
            }
        }

        return true;
    }

    private static float Round(float v) => (float)Math.Round(v / Grain) * Grain;

    private static void WriteVec(BinaryWriter writer, Vec3 v)
    {
        writer.Write(v.X);
        writer.Write(v.Y);
        writer.Write(v.Z);
    }

    private static Vec3 ReadVec(BinaryReader reader) => new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
}
