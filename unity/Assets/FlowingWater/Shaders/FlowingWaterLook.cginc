// FlowingWater's look, shared by the shaders that draw the water's surface (each brick's, from the server's water, and
// the water moved on the graphics card near the camera): the sea's colours, light, ripples and fog, the running
// ripples and foam of the flowing water. A shader includes it inside its CGPROGRAM, fills an Input in its vertex
// function, and calls Look from its surface function once it knows it draws the point.
#ifndef FLOWINGWATER_LOOK_INCLUDED
#define FLOWINGWATER_LOOK_INCLUDED

sampler2D _Normal;
sampler2D _FoamTex;
sampler2D _RandomFoamTex;
sampler2D _FlowingWaterBehind;
float _Lift;
UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);

fixed4 _ColorTop;
fixed4 _ColorBottom;
fixed4 _ColorBottomShallow;
fixed4 _SurfaceColor;
fixed4 _FoamColor;
float _Glossiness;
float _Metallic;
float _NormalPower;
float _NormalScale;
float _WaveVel;
float _DepthFade;
float _ShoreFade;
float _FoamDepth;
float _RefractionScale;
float _RefractionMax;
float _DeepWater;
float _RunningDeep;
float _RunningClarity;
float _FlowTiling;
float _FlowStrength;
float _FlowFrom;
float _FlowFull;
float _RapidsSpeed;
float _RapidsTiling;
float _EdgeFade;
float _BodyDepth;
float4 _BodyTint;
float _BodyBlur;

// The game's own, set for every shader: its two winds (x, z its direction, w its strength) and how far it
// is from the first to the second; the sun's direction and the colour the fog takes towards it. Unset (a
// preview), the wind is none and the fog takes its own colour.
float4 _GlobalWind1;
float4 _GlobalWind2;
float _GlobalWindAlpha;
float4 _SunDir;
float4 _SunFogColor;

struct Input
{
    float2 world;   // world x, z
    float4 water;   // depth, foam, shore, churn (white water the card's waves make: their crests, the water slowing hard)
    float2 current; // m/s
    float steep;    // the surface's slope (rise over run), without its waves
    float3 worldPos;
    float4 screenPos;
    float4 grabPos; // the same point in the grabbed image (upside down on some platforms)
    float2 cell;    // the point's place in the brick's columns
    float facing : VFACE;
};

#include "FlowingWaterSimMask.cginc"
#include "FlowingWaterWaves.cginc"

// The scene's distance from the camera, along its view axis, at a point of the screen.
float SceneEye(float2 screen)
{
    return LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, screen));
}

// A wind's direction on the ground, and its strength; none (east, still) where the game set none.
float3 Wind(float4 wind)
{
    float length2 = dot(wind.xz, wind.xz);
    return length2 > 1e-6 ? float3(wind.xz * rsqrt(length2), wind.w) : float3(1.0, 0.0, 0.0);
}

// The sea's ripples under one wind: a large slow layer and two finer ones, all drifting down the wind.
float3 WindRipples(float2 world, float2 along)
{
    float2 uv = float2(dot(world, float2(-along.y, along.x)), dot(world, along)) * _NormalScale;
    float drift = _Time.x * _WaveVel;
    float3 large = UnpackNormal(tex2D(_Normal, (uv * 0.2) + float2(0.0, -0.2 * drift)));
    float3 fine = UnpackNormal(tex2D(_Normal, uv + float2(0.0, -0.22 * drift)));
    float3 cross = UnpackNormal(tex2D(_Normal, uv + float2(0.01 * drift, -0.2 * drift)));
    return large + (0.3 * fine) + (0.3 * cross);
}

// One phase of the flow: where the texture has run to, and how much this phase counts (it fades out at the
// jump back so the reset is never seen).
float2 FlowPhase(float2 uv, float2 run, float offset, out float weight)
{
    float t = frac((_Time.y / _FlowCycle) + offset);
    weight = 1.0 - abs((2.0 * t) - 1.0);
    return uv - (run * t * _FlowCycle) + (offset * float2(0.37, 0.61));
}

// Ripples that run with the current, the sea's layers finer (a stream is narrower than the sea's swell):
// dragged along at the water's own speed, two phases blended so they never stretch; on fast water a finer layer
// besides, that catches the sun in glints.
float3 RunningRipples(float2 world, float2 current)
{
    float scale = _NormalScale * _FlowTiling;
    float2 uv = world * scale;
    float2 run = current * scale;
    float wa;
    float wb;
    float2 a = FlowPhase(uv, run, 0.0, wa);
    float2 b = FlowPhase(uv, run, 0.5, wb);
    float3 large = (UnpackNormal(tex2D(_Normal, a * 0.2)) * wa) + (UnpackNormal(tex2D(_Normal, b * 0.2)) * wb);
    float3 fine = (UnpackNormal(tex2D(_Normal, a)) * wa) + (UnpackNormal(tex2D(_Normal, b)) * wb);
    float3 glints = (UnpackNormal(tex2D(_Normal, (a * 3.1) + 0.3)) * wa) + (UnpackNormal(tex2D(_Normal, (b * 3.1) + 0.7)) * wb);
    return large + (0.6 * fine) + (0.5 * saturate(length(current) / _RapidsSpeed) * glints);
}

// Foam drawn out in streaks along the current: the running foam (two phases, as RunningFoam) sampled at points
// upstream along the current and averaged, over the way the water runs in STREAK_SECONDS (STREAK_MOST metres at most),
// finer than the rapids' foam so the streaks are thinner than they are long: half the brightest of them, half their
// mean, so a patch of foam is drawn out behind itself rather than washed out. Nothing is turned with the current (that
// drew rings where it curves): each point only looks back along its own.
#define STREAK_SAMPLES 4
#define STREAK_SECONDS 0.4
#define STREAK_MOST 3.0
#define STREAK_TILING 2.5

float RunningFoam(float2 uv, float2 run)
{
    float a;
    float b;
    float2 pa = FlowPhase(uv, run, 0.25, a);
    float2 pb = FlowPhase(uv, run, 0.75, b);
    return (tex2D(_FoamTex, pa).r * a) + (tex2D(_FoamTex, pb).r * b);
}

float Streaks(float2 world, float2 current, float speed)
{
    float2 along = speed > 0.05 ? current / speed : 0.0;
    float reach = min(speed * STREAK_SECONDS, STREAK_MOST);
    float tiling = _RapidsTiling * STREAK_TILING;
    float sum = 0.0;
    float most = 0.0;
    for (int k = 0; k < STREAK_SAMPLES; k++)
    {
        float2 at = world - (along * (reach * k / (STREAK_SAMPLES - 1.0)));
        float foam = RunningFoam(at * tiling, current * tiling);
        sum += foam;
        most = max(most, foam);
    }

    return 0.5 * (most + (sum / STREAK_SAMPLES));
}

// What is behind the water as seen through it: the grabbed picture, bent, and blurred the more water it is seen
// through (four samples round the point, as far as _BodyBlur of the screen for each metre of water, a hundredth at
// most).
float3 SeenThrough(float2 grab, float thickness)
{
    float r = min(thickness * _BodyBlur, 0.01);
    float3 sum = tex2D(_FlowingWaterBehind, grab + float2(r, 0.0)).rgb;
    sum += tex2D(_FlowingWaterBehind, grab - float2(r, 0.0)).rgb;
    sum += tex2D(_FlowingWaterBehind, grab + float2(0.0, r)).rgb;
    sum += tex2D(_FlowingWaterBehind, grab - float2(0.0, r)).rgb;
    return 0.25 * sum;
}

// The fog over the water as over the sea: the game's fog, towards the sun's colour where one looks at the
// sun; over all the water's light, what is seen through included, as the sea's.
void Finish(Input i, SurfaceOutputStandard o, inout fixed4 color)
{
#if defined(FOG_LINEAR) || defined(FOG_EXP) || defined(FOG_EXP2)
#if defined(UNITY_PASS_FORWARDADD)
    float3 fogColour = 0.0;
#else
    float sun = saturate(dot(normalize(i.worldPos - _WorldSpaceCameraPos), _SunDir.xyz));
    float3 sunFog = pow(max(_SunFogColor.rgb, 0.0), 2.2);
    float3 fogColour = dot(_SunFogColor.rgb, 1.0) > 0.0 ? lerp(unity_FogColor.rgb, sunFog, sun) : unity_FogColor.rgb;
#endif
    UNITY_CALC_FOG_FACTOR_RAW(i.screenPos.w);
    color.rgb = lerp(fogColour, color.rgb, saturate(unityFogFactor));
#endif
    // Premultiplied: the water's light fades out with the water at its edge.
    color.rgb *= o.Alpha;
}

// Water running down a slope this steep (rise over run) begins to break into white water, all of it this steep and
// running this fast (m/s): a cascade is aerated, not a mirror.
#define CASCADE_FROM 0.35
#define CASCADE_STEEP 1.0
#define CASCADE_SPEED 1.0

// A cascade's foam: long thin streaks down it, the foam drawn this many times finer across the current and so much
// coarser along it (per metre), running with the water (two phases, as RunningFoam). On a steep face a metre across the
// ground is several down the face: the streaks are longer still there, as a fall's are.
#define CASCADE_ACROSS 1.6
#define CASCADE_ALONG 0.2

// How much of a cascade the water is at a point (0 to 1): from its slope, as it runs.
float Cascade(float steep, float speed)
{
    return smoothstep(CASCADE_FROM, CASCADE_STEEP, steep) * smoothstep(0.1, CASCADE_SPEED, speed);
}

float CascadeStreaks(float2 world, float2 current, float speed)
{
    float2 along = speed > 0.05 ? current / speed : float2(0.0, 1.0);
    float2 uv = float2(dot(world, float2(-along.y, along.x)) * CASCADE_ACROSS, dot(world, along) * CASCADE_ALONG);
    return RunningFoam(uv, float2(0.0, speed * CASCADE_ALONG));
}

// Shallow still water (none of it as the water runs: running water looks as it did): it keeps this share of its
// ripples at least, and thinner than SHALLOW_ABSORB metres it is down to this share as glossy (what is seen through it
// is the water's body's, below).
#define SHALLOW_RIPPLES 0.5
#define SHALLOW_ABSORB 0.8
#define SHALLOW_GLOSS 0.85

// The water's look at a point the shader draws.
void Look(Input i, inout SurfaceOutputStandard o)
{
    float depth = max(i.water.x, 0.0);
    float deep = saturate(depth / _DeepWater);
    float speed = length(i.current);
    float running = smoothstep(_FlowFrom, _FlowFull, speed);
    float cascade = Cascade(i.steep, speed);

    // Ripples: the sea's under the game's wind where the water lies still, running ones where it runs.
    float3 wind1 = Wind(_GlobalWind1);
    float3 wind2 = Wind(_GlobalWind2);
    float3 still = lerp(WindRipples(i.world, wind1.xy), WindRipples(i.world, wind2.xy), _GlobalWindAlpha);
    float3 ripples = lerp(still, RunningRipples(i.world, i.current) * _FlowStrength, running);
    // Shallow still water lies flatter, as the sea's does, but keeps half its ripples (flatter, a pool over grass was a
    // mirror of the sky, pale as ice); running water keeps them all.
    float3 n = normalize(lerp(float3(0.0, 0.0, 1.0), ripples, max(clamp(deep, lerp(SHALLOW_RIPPLES, 0.2, running), 1.0), running)));
    n = lerp(float3(0.0, 0.0, 1.0), n, _NormalPower);

    // The fine waves, lit only: their slope tips the surface.
    n = FineWaves(n, i.world, i.current, depth);

    // Seen from under the water, its underside.
    n.z *= i.facing > 0.0 ? 1.0 : -1.0;

    // What is behind, bent by the ripples, unless that bends something in front of the water into it.
    float2 screen = i.screenPos.xy / max(i.screenPos.w, 0.0001);
    float2 grab = i.grabPos.xy / max(i.grabPos.w, 0.0001);
    float eye = i.screenPos.w;
    float2 bend = clamp(n.xy * _RefractionScale * 5.0, -_RefractionMax, _RefractionMax);
    float straight = SceneEye(screen);
    float bent = SceneEye(screen + bend);
    if (bent < eye)
    {
        bend = 0.0;
        bent = straight;
    }

    // Running water is churned and carries silt: the faster it runs, the less of what is behind shows through (as if
    // RunningClarity as clear), and the shallower it takes the deep colour (RunningDeep, not DeepWater).
    float clarity = lerp(1.0, _RunningClarity, running);
    float deepColour = saturate(depth / lerp(_DeepWater, _RunningDeep, running));

    // The water's body: what is behind is seen through so much water along the view (more looking across it than down
    // into it), which takes its light as it goes, red first (_BodyTint, per _BodyDepth metres: Beer and Lambert's), and
    // blurs it; the water's own colour takes its place. Far down, the sea's own fade (DepthFade) hides the rest.
    float thickness = abs(bent - eye);
    float3 through = exp(-thickness * _BodyTint.rgb / max(_BodyDepth * clarity, 0.001));
    through *= 1.0 - sqrt(saturate(thickness / (_DepthFade * clarity)));
    // A cascade is full of air: little of what is behind shows through it.
    through *= 1.0 - (0.85 * cascade);
    float3 hidden = 1.0 - through;
    float3 behind = SeenThrough(grab + bend, thickness);

    // The shore: how much water there is in front of what is behind.
    float shoreLine = abs(straight - eye);
    float shore = saturate(shoreLine / _ShoreFade);
    float band = saturate(shoreLine / _FoamDepth);

    float3 colour = hidden * lerp(_ColorBottomShallow.rgb, _ColorBottom.rgb, deepColour);
    // Its body pale with the air in it, between the water's colour and the foam's.
    colour = lerp(colour, _FoamColor.rgb * lerp(_ColorTop.rgb, 1.0, 0.6), 0.25 * cascade);

    // Scattered foam where the wind blows on open water, as on the sea.
    float2 wobble = 0.005 * float2(sin(_Time.y + i.worldPos.x), sin((1.5 * _Time.y) + i.worldPos.z));
    float windFoam = 5.0 * lerp(wind1.z, wind2.z, _GlobalWindAlpha) * deep * (1.0 - running) * tex2D(_RandomFoamTex, (i.world * 0.03) + wobble).r;
    colour = lerp(colour, _FoamColor.rgb, saturate(windFoam * _FoamColor.a));
    // Thin water mirrors the sky less sharply (its film over the ground is never as smooth as a pool's top).
    float gloss = _Glossiness * (1.0 - saturate(windFoam)) * lerp(lerp(SHALLOW_GLOSS, 1.0, saturate(depth / SHALLOW_ABSORB)), 1.0, running);

    // A band of foam just off the water's edge.
    float shoreFoam = deep * min(shore, 1.0 - band);
    colour = lerp(colour, _FoamColor.rgb, saturate(8.0 * tex2D(_FoamTex, (i.world * 0.1) + wobble).r * shoreFoam * _FoamColor.a));

    // Rapids: fast water foams, its foam running with it, drawn out in streaks along the current the faster it runs;
    // the simulation's own foam (falls) and its waves' churn on top.
    float fast = saturate(speed / _RapidsSpeed);
    float want = saturate((fast * fast * 0.8) + i.water.y + i.water.w + (0.75 * cascade));
    float fa;
    float fb;
    float2 foamUv = i.world * _RapidsTiling;
    float2 foamRun = i.current * _RapidsTiling;
    float noise = (tex2D(_FoamTex, FlowPhase(foamUv, foamRun, 0.25, fa)).r * fa) + (tex2D(_FoamTex, FlowPhase(foamUv, foamRun, 0.75, fb)).r * fb);
    noise = fast > 0.05 ? lerp(noise, Streaks(i.world, i.current, speed), fast) : noise;
    noise = lerp(noise, CascadeStreaks(i.world, i.current, speed), cascade);
    float rapids = smoothstep(1.0 - want, 1.0 - want + 0.25, noise) * want;

    // Foam is lace, not plaster: broken up by a finer foam running with it.
    float2 laceUv = foamUv * 5.0;
    float lace = (tex2D(_FoamTex, FlowPhase(laceUv, foamRun * 5.0, 0.1, fa)).r * fa) + (tex2D(_FoamTex, FlowPhase(laceUv, foamRun * 5.0, 0.6, fb)).r * fb);
    rapids *= saturate(0.35 + (0.9 * lace));
    colour = lerp(colour, _FoamColor.rgb, rapids);

    float foam = max(shoreFoam, rapids);
    o.Albedo = colour;
    o.Emission = behind * _SurfaceColor.rgb * through * (1.0 - rapids);
    o.Normal = n;
    o.Metallic = _Metallic * (1.0 - foam);
    // Broken up, a cascade mirrors nothing.
    o.Smoothness = lerp(lerp(gloss, 0.35, cascade), 0.1, foam);
    o.Alpha = shore * smoothstep(0.0, _EdgeFade, saturate(i.water.z));
}

#endif
