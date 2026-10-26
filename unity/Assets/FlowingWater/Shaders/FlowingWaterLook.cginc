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
float _FlowCycle;
float _FlowFrom;
float _FlowFull;
float _RapidsSpeed;
float _RapidsTiling;
float _EdgeFade;

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
    float3 worldPos;
    float4 screenPos;
    float4 grabPos; // the same point in the grabbed image (upside down on some platforms)
    float2 cell;    // the point's place in the brick's columns
    float facing : VFACE;
};

#include "FlowingWaterSimMask.cginc"

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

// The water's look at a point the shader draws.
void Look(Input i, inout SurfaceOutputStandard o)
{
    float depth = max(i.water.x, 0.0);
    float deep = saturate(depth / _DeepWater);
    float speed = length(i.current);
    float running = smoothstep(_FlowFrom, _FlowFull, speed);

    // Ripples: the sea's under the game's wind where the water lies still, running ones where it runs.
    float3 wind1 = Wind(_GlobalWind1);
    float3 wind2 = Wind(_GlobalWind2);
    float3 still = lerp(WindRipples(i.world, wind1.xy), WindRipples(i.world, wind2.xy), _GlobalWindAlpha);
    float3 ripples = lerp(still, RunningRipples(i.world, i.current) * _FlowStrength, running);
    // Shallow still water lies flatter, as the sea's does; running water keeps its ripples.
    float3 n = normalize(lerp(float3(0.0, 0.0, 1.0), ripples, max(clamp(deep, 0.2, 1.0), running)));
    n = lerp(float3(0.0, 0.0, 1.0), n, _NormalPower);
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
    float3 behind = tex2D(_FlowingWaterBehind, grab + bend).rgb;
    float hidden = sqrt(saturate(abs(bent - eye) / (_DepthFade * clarity)));

    // The shore: how much water there is in front of what is behind.
    float shoreLine = abs(straight - eye);
    float shore = saturate(shoreLine / _ShoreFade);
    float band = saturate(shoreLine / _FoamDepth);

    float3 colour = hidden * lerp(_ColorBottomShallow.rgb, _ColorBottom.rgb, deepColour);

    // Scattered foam where the wind blows on open water, as on the sea.
    float2 wobble = 0.005 * float2(sin(_Time.y + i.worldPos.x), sin((1.5 * _Time.y) + i.worldPos.z));
    float windFoam = 5.0 * lerp(wind1.z, wind2.z, _GlobalWindAlpha) * deep * (1.0 - running) * tex2D(_RandomFoamTex, (i.world * 0.03) + wobble).r;
    colour = lerp(colour, _FoamColor.rgb, saturate(windFoam * _FoamColor.a));
    float gloss = _Glossiness * (1.0 - saturate(windFoam));

    // A band of foam just off the water's edge.
    float shoreFoam = deep * min(shore, 1.0 - band);
    colour = lerp(colour, _FoamColor.rgb, saturate(8.0 * tex2D(_FoamTex, (i.world * 0.1) + wobble).r * shoreFoam * _FoamColor.a));

    // Rapids: fast water foams, its foam running with it; the simulation's own foam (falls) and its waves' churn on
    // top.
    float fast = saturate(speed / _RapidsSpeed);
    float want = saturate((fast * fast * 0.8) + i.water.y + i.water.w);
    float fa;
    float fb;
    float2 foamUv = i.world * _RapidsTiling;
    float2 foamRun = i.current * _RapidsTiling;
    float noise = (tex2D(_FoamTex, FlowPhase(foamUv, foamRun, 0.25, fa)).r * fa) + (tex2D(_FoamTex, FlowPhase(foamUv, foamRun, 0.75, fb)).r * fb);
    float rapids = smoothstep(1.0 - want, 1.0 - want + 0.25, noise) * want;
    colour = lerp(colour, _FoamColor.rgb, rapids);

    float foam = max(shoreFoam, rapids);
    o.Albedo = colour;
    o.Emission = behind * _SurfaceColor.rgb * (1.0 - hidden) * (1.0 - rapids);
    o.Normal = n;
    o.Metallic = _Metallic * (1.0 - foam);
    o.Smoothness = lerp(gloss, 0.1, foam);
    o.Alpha = shore * smoothstep(0.0, _EdgeFade, saturate(i.water.z));
}

#endif
