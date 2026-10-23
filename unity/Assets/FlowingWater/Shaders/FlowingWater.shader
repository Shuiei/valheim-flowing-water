// FlowingWater's water surface, for the built-in render pipeline the game uses.
//
// The mesh carries the simulation per vertex:
//   uv0       world x, z (metres): texture coordinates
//   uv1       x = water depth (m), y = extra foam 0..1, z = shore 0..1 (0 = the water's very edge)
//   uv2       x, y = current (m/s, world x and z)
// Ripples are a normal map dragged along the current with two offset phases blended so the
// texture never stretches (flow mapping), plus a slow second layer so still water lives too.
// Seen from afar, a second, larger layer of ripples takes over (the small ones blur to a flat
// mirror with distance). The water is a body, not a tinted sheet: what lies behind it (the scene,
// grabbed once a frame) is seen through it, bent by the ripples, each colour dimmed by the water
// its light crossed: the sun's way down to it and the way back up to the eye (from the scene's
// depth: red goes first, so deep water turns blue-green), and the light the water itself
// scatters fills in what is lost. Foam rises with speed,
// with the surface's slope (from the mesh) and at the shore; the edge fades out to nothing, its
// light included (premultiplied blending adds reflections even where alpha is 0). The game's fog
// covers the water's own light as far off as the surface is, not what is seen through it, which was
// fogged already.
Shader "FlowingWater/Water"
{
    Properties
    {
        _DeepColor ("Colour the water scatters back (seen where it is deep)", Color) = (0.03, 0.11, 0.14, 1)
        _Absorption ("Light absorbed per metre of water (r, g, b)", Vector) = (0.45, 0.12, 0.09, 0)
        _SurfaceTint ("Share of the water's colour even where it is thin", Range(0, 1)) = 0.15
        _Refraction ("Refraction (share of the screen a ripple bends what is behind)", Range(0, 0.1)) = 0.02
        _NormalMap ("Ripple normals", 2D) = "bump" {}
        _NormalStrength ("Ripple strength", Range(0, 2)) = 0.45
        _RippleScale ("Ripple tiling (per metre)", Float) = 0.3
        _StillScale ("Still ripple tiling (per metre)", Float) = 0.12
        _StillSpeed ("Still ripple drift (m/s)", Float) = 0.05
        _FarScale ("Far ripple tiling (per metre)", Float) = 0.06
        _FarStart ("Far ripples from (m)", Float) = 12
        _FarRange ("Far ripples full after (m more)", Float) = 40
        _FarStrength ("Far ripple strength", Range(0, 2)) = 0.4
        _FlowSpeed ("Texture speed (share of the current)", Float) = 1.0
        _FlowCycle ("Flow cycle (s)", Float) = 1.5
        _FoamTex ("Foam noise", 2D) = "white" {}
        _FoamColor ("Foam colour", Color) = (0.92, 0.95, 0.97, 1)
        _FoamScale ("Foam tiling (per metre)", Float) = 0.6
        _FoamSpeed ("Current that makes full foam (m/s)", Float) = 4.0
        _ShoreFoam ("Foam at the shore", Range(0, 1)) = 0.1
        _Smoothness ("Smoothness (tight sun glints; rougher spreads them into a pale sheen)", Range(0, 1)) = 0.92
        _Reflection ("Sky reflection strength", Range(0, 1)) = 0.9
        _EdgeFade ("Edge fade (shore share)", Range(0.01, 1)) = 0.3
    }

    SubShader
    {
        Tags { "Queue" = "Transparent-10" "RenderType" = "Transparent" "IgnoreProjector" = "True" "ForceNoShadowCasting" = "True" }
        LOD 300
        ZWrite Off
        Cull Back

        // What is behind the water, once a frame for every tile.
        GrabPass { "_FlowingWaterBehind" }

        CGPROGRAM
        #pragma surface surf Standard alpha:premul vertex:vert finalcolor:EdgeFade noshadow nolightmap nodynlightmap nodirlightmap nometa nolppv noshadowmask
        #pragma target 3.5
        // A final colour function turns off the fog surface shaders add by themselves: EdgeFade adds it.
        #pragma multi_compile_fog

        sampler2D _NormalMap;
        sampler2D _FoamTex;
        sampler2D _FlowingWaterBehind;
        UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
        fixed4 _DeepColor;
        fixed4 _FoamColor;
        float4 _Absorption;
        float _SurfaceTint;
        float _Refraction;
        float _NormalStrength;
        float _RippleScale;
        float _StillScale;
        float _StillSpeed;
        float _FarScale;
        float _FarStart;
        float _FarRange;
        float _FarStrength;
        float _FlowSpeed;
        float _FlowCycle;
        float _FoamScale;
        float _FoamSpeed;
        float _ShoreFoam;
        float _Smoothness;
        float _Reflection;
        float _EdgeFade;

        struct Input
        {
            float2 world;   // world x, z
            float4 water;   // depth, foam, shore
            float2 current; // m/s
            float3 worldPos;
            float4 screenPos;
            float4 grabPos; // the same point in the grabbed image (upside down on some platforms)
        };

        // How much of the surface there is at all: 0 at the water's very edge.
        float Edge(Input i)
        {
            return smoothstep(0.0, _EdgeFade, saturate(i.water.z));
        }

        // The game's fog over the water's own light (its colour, the sky's reflection, the sun's glints),
        // as far off as the surface is. Not over the scene seen through it (the emission): grabbed from
        // the screen, it has its fog already, the whole way to it; fogged again it would go pale twice.
        // A light's own pass adds no emission and fogs towards black, as the game's fog does in it.
        // Then the reflections and the scene seen through fade out with the water: premultiplied
        // blending adds them in full even where the water is fully transparent (a rough edge showed as
        // a cream band, and the scene was added over itself where something stood just behind the
        // surface: a bright line round anything through the water). The shading fades with it as well.
        void EdgeFade(Input i, SurfaceOutputStandard o, inout fixed4 color)
        {
        #if defined(FOG_LINEAR) || defined(FOG_EXP) || defined(FOG_EXP2)
        #if defined(UNITY_PASS_FORWARDADD)
            float3 seenThrough = 0.0;
            float3 fogColour = 0.0;
        #else
            float3 seenThrough = o.Emission;
            float3 fogColour = unity_FogColor.rgb;
        #endif
            // The fog's distance as Unity's own takes it: along the view axis.
            UNITY_CALC_FOG_FACTOR_RAW(i.screenPos.w);
            color.rgb = lerp(fogColour, color.rgb - seenThrough, saturate(unityFogFactor)) + seenThrough;
        #endif
            color.rgb *= o.Alpha;
        }

        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            o.world = v.texcoord.xy;
            o.water = v.texcoord1;
            o.current = v.texcoord2.xy;
            o.grabPos = ComputeGrabScreenPos(UnityObjectToClipPos(v.vertex));
        }

        // One phase of the flow: where the texture has drifted to, and how much this phase counts
        // (it fades out at the jump back so the reset is never seen).
        float2 FlowPhase(float2 uv, float2 drift, float offset, out float weight)
        {
            float t = frac((_Time.y / _FlowCycle) + offset);
            weight = 1.0 - abs((2.0 * t) - 1.0);
            return uv - (drift * t * _FlowCycle) + (offset * float2(0.37, 0.61));
        }

        // How steeply the surface is seen: 1 from straight above, towards 0 at a grazing look.
        float Steepness(Input i)
        {
            float3 toSurface = i.worldPos - _WorldSpaceCameraPos;
            return max(abs(toSurface.y) / max(length(toSurface), 0.001), 0.15);
        }

        // The scene's distance from the camera, along its view axis, at a point of the screen.
        float SceneEye(float2 screen)
        {
            return LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, screen));
        }

        // How far the eye looks through water here, metres: from the surface to what the scene has
        // behind it, at most the water's depth over the angle it is seen at (the scene behind may be
        // missing, or far off: sky seen through a fall, or the depth texture absent).
        float Thickness(Input i, float2 screen, float depth)
        {
            float behind = max(SceneEye(screen) - i.screenPos.w, 0.0) * length(i.worldPos - _WorldSpaceCameraPos) / max(i.screenPos.w, 0.001);
            return min(behind, depth / Steepness(i));
        }

        // How far the ripples bend the view of what is behind: more through more water, less far off;
        // not at all onto something standing in front of the water (it would be pulled into it).
        float2 Bend(Input i, float2 screen, float3 normal, float thickness)
        {
            float2 bend = normal.xy * _Refraction * saturate(thickness) * saturate(10.0 / i.screenPos.w);
            return SceneEye(screen + bend) < i.screenPos.w ? float2(0.0, 0.0) : bend;
        }

        void surf(Input i, inout SurfaceOutputStandard o)
        {
            float depth = max(i.water.x, 0.0);
            float shore = saturate(i.water.z);
            float speed = length(i.current);

            // Moving ripples, at the current's speed in texture space.
            float2 drift = i.current * _FlowSpeed * _RippleScale;
            float2 uv = i.world * _RippleScale;
            float wa;
            float wb;
            float2 uva = FlowPhase(uv, drift, 0.0, wa);
            float2 uvb = FlowPhase(uv, drift, 0.5, wb);
            float3 na = UnpackNormal(tex2D(_NormalMap, uva));
            float3 nb = UnpackNormal(tex2D(_NormalMap, uvb));
            float3 moving = normalize((na * wa) + (nb * wb));

            // Slow ripples that drift whatever the current, so a still pool is not a mirror.
            float2 stillUv = (i.world * _StillScale) + (_Time.y * _StillSpeed * _StillScale * float2(0.8, 0.6));
            float3 still = UnpackNormal(tex2D(_NormalMap, stillUv));

            float3 n = normalize(float3(moving.xy + (still.xy * 0.6), moving.z * still.z));
            n.xy *= _NormalStrength * saturate(0.4 + (depth * 2.0));

            // Far off, larger ripples that still read at a distance, dragged along like the rest.
            float far = saturate((distance(_WorldSpaceCameraPos, i.worldPos) - _FarStart) / _FarRange);
            float2 farDrift = i.current * _FlowSpeed * _FarScale;
            float fa;
            float fb;
            float2 farUv = i.world * _FarScale;
            float3 fna = UnpackNormal(tex2D(_NormalMap, FlowPhase(farUv, farDrift, 0.25, fa)));
            float3 fnb = UnpackNormal(tex2D(_NormalMap, FlowPhase(farUv, farDrift, 0.75, fb)));
            float2 farRipple = normalize((fna * fa) + (fnb * fb)).xy * _FarStrength;
            n.xy = lerp(n.xy, farRipple, far);
            o.Normal = normalize(n);

            // Foam: from speed, from the shore, from the simulation; broken up by the noise,
            // which drifts with the water like the ripples do.
            // Foam covers this share of the surface: only fast water (rapids) foams much.
            float fast = saturate(speed / _FoamSpeed);
            float foamWant = saturate((fast * fast * 0.7) + ((1.0 - shore) * _ShoreFoam) + i.water.y);
            float2 foamUv = (i.world * _FoamScale) - (i.current * _FlowSpeed * _FoamScale * frac(_Time.y / (_FlowCycle * 4.0)) * _FlowCycle * 4.0);
            float foamNoise = tex2D(_FoamTex, foamUv).r;
            float foam = smoothstep(1.0 - foamWant, 1.0 - foamWant + 0.25, foamNoise) * foamWant;

            // What lies behind, seen through the water: each colour dimmed by the water it crosses,
            // the light the water scatters filling in the rest; foam hides both.
            float2 screen = i.screenPos.xy / max(i.screenPos.w, 0.0001);
            float thickness = Thickness(i, screen, depth);
            float2 bend = Bend(i, screen, o.Normal, thickness);
            // The light from what lies behind crossed the water twice: the sun's down to it (the game lit it
            // as if dry), then back up to the eye.
            float seen = Thickness(i, screen + bend, depth);
            float3 through = exp(-_Absorption.rgb * seen * (1.0 + Steepness(i))) * (1.0 - _SurfaceTint);
            float3 behind = tex2D(_FlowingWaterBehind, (i.grabPos.xy / max(i.grabPos.w, 0.0001)) + bend).rgb;
            float edge = Edge(i) * saturate(thickness / 0.03);

            o.Albedo = lerp(_DeepColor.rgb * (1.0 - through), _FoamColor.rgb, foam);
            o.Emission = behind * through * (1.0 - foam);
            o.Alpha = edge;
            o.Metallic = 0.0;
            o.Smoothness = lerp(_Smoothness, 0.35, foam);
            // Indirect light (the sky's reflection) is what makes shallow water look milky.
            o.Occlusion = _Reflection;
        }
        ENDCG
    }

    FallBack Off
}
