using System;
using FlowingWater.Volume;
using UnityEngine;

namespace FlowingWater;

/// <summary>
/// A source's shape as kept in its object (<see cref="ZdoKeys"/>): whether it faces a way, its outlet's width, spread
/// and throw; the way it faces is its piece's turn. One with no shape written (every source placed before 0.46.0) is
/// all round; one made all round keeps its other values, so it faces with them again when turned back. Read the same
/// by the server's water and the player's own.
/// </summary>
internal static class MarkerShape
{
    /// <summary>The shape a source's object holds, facing the way it is turned.</summary>
    public static SourceShape Read(ZDO zdo)
    {
        if (zdo.GetInt(ZdoKeys.Shape, 0) != 1)
        {
            return SourceShape.Round;
        }

        return new SourceShape(false, HeadingOf(zdo.GetRotation()), zdo.GetFloat(ZdoKeys.Width, 0f), zdo.GetFloat(ZdoKeys.Spread, 0f), zdo.GetFloat(ZdoKeys.Throw, 0f));
    }

    /// <summary>A source's shape into its object, which this game owns (the way it faces is its piece's own turn: not written).</summary>
    public static void Write(ZDO zdo, SourceShape shape)
    {
        zdo.Set(ZdoKeys.Shape, shape.AllRound ? 0 : 1);
        zdo.Set(ZdoKeys.Width, shape.Width);
        zdo.Set(ZdoKeys.Spread, shape.Spread);
        zdo.Set(ZdoKeys.Throw, shape.Throw);
    }

    /// <summary>The way a turn faces across the ground, degrees clockwise from north (0 to 360).</summary>
    public static float HeadingOf(Quaternion rotation)
    {
        Vector3 forward = rotation * Vector3.forward;
        float heading = (float)(Math.Atan2(forward.x, forward.z) * 180.0 / Math.PI);
        return heading < 0f ? heading + 360f : heading;
    }
}
