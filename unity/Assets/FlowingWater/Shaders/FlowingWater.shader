// FlowingWater's water surface, for the built-in render pipeline the game uses.
//
// The mesh carries the simulation per vertex, so nothing here reads the depth buffer:
//   uv0       world x, z (metres): texture coordinates
//   uv1       x = water depth (m), y = extra foam 0..1, z = shore 0..1 (0 = the water's very edge)
//   uv2       x, y = current (m/s, world x and z)
// Ripples are a normal map dragged along the current with two offset phases blended so the
// texture never stretches (flow mapping), plus a slow second layer so still water lives too.
// Seen from afar, a second, larger layer of ripples takes over (the small ones blur to a flat
// mirror with distance). Colour goes from clear and light where shallow to dark where deep; foam
// rises with speed, with the surface's slope (from the mesh) and at the shore; the edge fades out
// to nothing, its light included (premultiplied blending adds reflections even where alpha is 0).
Shader "FlowingWater/Water"
{
    Properties
    {
        _ShallowColor ("Shallow colour (alpha = opacity)", Color) = (0.2, 0.32, 0.3, 0.15)
        _DeepColor ("Deep colour (alpha = opacity)", Color) = (0.03, 0.11, 0.14, 0.9)
        _DeepDepth ("Depth of the full deep colour (m)", Float) = 3
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
        _Smoothness ("Smoothness", Range(0, 1)) = 0.8
        _Reflection ("Sky reflection strength", Range(0, 1)) = 0.9
        _EdgeFade ("Edge fade (shore share)", Range(0.01, 1)) = 0.3
    }

    SubShader
    {
        Tags { "Queue" = "Transparent-10" "RenderType" = "Transparent" "IgnoreProjector" = "True" "ForceNoShadowCasting" = "True" }
        LOD 300
        ZWrite Off
        Cull Back

        CGPROGRAM
        #pragma surface surf Standard alpha:premul vertex:vert finalcolor:EdgeFade noshadow nolightmap nodynlightmap nodirlightmap nometa nolppv noshadowmask
        #pragma target 3.5

        sampler2D _NormalMap;
        sampler2D _FoamTex;
        fixed4 _ShallowColor;
        fixed4 _DeepColor;
        fixed4 _FoamColor;
        float _DeepDepth;
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
        };

        // How much of the surface there is at all: 0 at the water's very edge.
        float Edge(Input i)
        {
            return smoothstep(0.0, _EdgeFade, saturate(i.water.z));
        }

        // The edge fades the reflections out too: premultiplied blending adds them over the ground
        // even where the water is fully transparent (a rough edge showed as a cream band).
        void EdgeFade(Input i, SurfaceOutputStandard o, inout fixed4 color)
        {
            color.rgb *= Edge(i);
        }

        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            o.world = v.texcoord.xy;
            o.water = v.texcoord1;
            o.current = v.texcoord2.xy;
        }

        // One phase of the flow: where the texture has drifted to, and how much this phase counts
        // (it fades out at the jump back so the reset is never seen).
        float2 FlowPhase(float2 uv, float2 drift, float offset, out float weight)
        {
            float t = frac((_Time.y / _FlowCycle) + offset);
            weight = 1.0 - abs((2.0 * t) - 1.0);
            return uv - (drift * t * _FlowCycle) + (offset * float2(0.37, 0.61));
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

            // Colour by depth; opacity by depth too, and to nothing at the edge.
            float deep = saturate(depth / _DeepDepth);
            fixed4 water = lerp(_ShallowColor, _DeepColor, deep);
            float edge = Edge(i);

            o.Albedo = lerp(water.rgb, _FoamColor.rgb, foam);
            o.Alpha = saturate(max(water.a, foam * 0.85) * edge);
            o.Metallic = 0.0;
            o.Smoothness = lerp(_Smoothness, 0.35, foam);
            // Indirect light (the sky's reflection) is what makes shallow water look milky.
            o.Occlusion = _Reflection;
        }
        ENDCG
    }

    FallBack Off
}
