using System;

namespace FlowingWater.Client;

/// <summary>Which of the scene's reflection probes lights what is drawn over the whole screen (SceneReflection).</summary>
internal static class ProbePick
{
    /// <summary>
    /// Of <paramref name="count"/> probes, the one whose box holds the place (<paramref name="holds"/>) and is the widest
    /// (<paramref name="area"/>, its box's across the ground: the game's two take turns, the next growing in); -1 when
    /// none holds it (the draw then mirrors Unity's default, the plain sky).
    /// </summary>
    public static int Widest(int count, Func<int, bool> holds, Func<int, float> area)
    {
        int widest = -1;
        float most = 0f;
        for (int k = 0; k < count; k++)
        {
            if (holds(k) && area(k) is var size && size > most)
            {
                most = size;
                widest = k;
            }
        }

        return widest;
    }
}
