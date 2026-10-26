// FlowingWater's waves on running water, shared by the shaders that draw the water's surface (through
// FlowingWaterLook.cginc) and by the editor's check of them (Assets/Editor/WaveProbe.compute): only the look, the water
// does not move by them. Needs Unity's _Time (a compute shader declares it).
#ifndef FLOWINGWATER_WAVES_INCLUDED
#define FLOWINGWATER_WAVES_INCLUDED

// How long the running water's two phases are (s): the ripples', the foam's and the waves'.
float _FlowCycle;

// Waves on running water, only for the look (the water does not move by them): chop running with the current, the
// higher the faster (so many metres for each (m/s) squared), no higher than so many metres nor than a share of the
// water's depth. Two lengths: the long waves are raised on the surface by both shaders' vertices (a voxel's grid, a
// metre, resolves them; the card's water and the bricks' meet at the square's edge), the fine ones only lit, a slope
// at each point drawn (a share of the long ones' height).
#define WAVE_PER_SPEED2 0.015
#define WAVE_MOST 0.15
#define WAVE_DEPTH_SHARE 0.25
#define WAVE_LENGTH 2.4
#define WAVE_FINE_LENGTH 0.8
#define WAVE_FINE_SHARE 0.5

// How high running water's waves stand at this speed (m/s) and depth (m): none on still or no water.
float WaveHeight(float speed, float depth)
{
    return depth > 0.005 ? min(min(WAVE_PER_SPEED2 * speed * speed, WAVE_MOST), WAVE_DEPTH_SHARE * depth) : 0.0;
}

float WaveHash(float2 c)
{
    return frac(sin(dot(c, float2(127.1, 311.7))) * 43758.5453);
}

// Smooth noise over the plane: its value (-1 to 1) and its slope (per unit), one feature a unit.
float3 WaveNoise(float2 x)
{
    float2 i = floor(x);
    float2 f = frac(x);
    float2 u = f * f * (3.0 - (2.0 * f));
    float2 du = 6.0 * f * (1.0 - f);
    float a = WaveHash(i);
    float b = WaveHash(i + float2(1.0, 0.0));
    float c = WaveHash(i + float2(0.0, 1.0));
    float d = WaveHash(i + float2(1.0, 1.0));
    float k = a - b - c + d;
    float value = a + ((b - a) * u.x) + ((c - a) * u.y) + (k * u.x * u.y);
    float2 slope = du * float2((b - a) + (k * u.y), (c - a) + (k * u.x));
    return float3((2.0 * value) - 1.0, 2.0 * slope);
}

// The chop of waves this long (m) at a world place on water running at this current: its height (-1 to 1) and its
// slope (per metre, east and north), dragged along at the water's speed in two phases blended, as the running ripples
// (never stretched, never seen to jump back).
float3 Chop(float2 world, float2 current, float length)
{
    float3 chop = 0.0;
    for (int k = 0; k < 2; k++)
    {
        float offset = 0.5 * k;
        float t = frac((_Time.y / _FlowCycle) + offset);
        float weight = 1.0 - abs((2.0 * t) - 1.0);
        float2 at = world - (current * t * _FlowCycle) + (offset * float2(23.7, 41.3));
        float3 n = WaveNoise(at / length);
        chop += weight * float3(n.x, n.yz / length);
    }

    return chop;
}

// The long waves raised on running water at a world place, as both shaders raise them (so the card's water and the
// blocks' meet at the square's edge): the surface's rise there (m) and its slope (east, north), for water this deep
// running at this current, so much of them shown (0 to 1: the blocks' fade at a shore); crest is the chop's height
// there (-1 to 1), for the churn.
float3 LongWaves(float2 world, float2 current, float depth, float shown, out float crest)
{
    float3 chop = Chop(world, current, WAVE_LENGTH);
    crest = chop.x;
    return WaveHeight(length(current), depth) * saturate(shown) * chop;
}

// A surface's normal (tangent space: x east, y north) tipped by the fine waves at a world place on water this deep
// running at this current: only lit, never raised.
float3 FineWaves(float3 n, float2 world, float2 current, float depth)
{
    float height = WAVE_FINE_SHARE * WaveHeight(length(current), depth);
    if (height > 0.0)
    {
        float3 fine = Chop(world, current, WAVE_FINE_LENGTH);
        n = normalize(float3(n.xy - (height * fine.yz * n.z), n.z));
    }

    return n;
}

// Water slowing this much (m/s) from two cells behind to two ahead (a jump, an obstacle) churns fully white.
#define CHURN_SLOWING 1.5

// White water (0 to 0.7): on the crests (crest, -1 to 1) of fast water's long waves (fast, 0 to 1 of the rapids'
// speed), and where the water slows hard along its way into water ahead (m/s; only the card's water knows it, the
// blocks give none).
float Churn(float crest, float fast, float slowing)
{
    return 0.7 * saturate((smoothstep(0.35, 0.8, crest) * fast * fast) + saturate(slowing / CHURN_SLOWING));
}

#endif
