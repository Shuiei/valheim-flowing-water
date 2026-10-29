// FlowingWater's water as voxels where it meets the air, beside the surface (FlowingWater/Water): the sides of water
// standing open to the air, and the columns of water falling through it, so a fall reads as a cascade and water is
// seen from inside it. The same look as the surface's, the sea's (its colours and textures copied onto this material
// when the game runs): what lies behind is seen through, bent by the ripples and tinted; the ripples and the foam run
// along each face with the water's current, so a fall's foam pours down it. Drawn from both sides.
//
// Each corner carries: uv0 its place on its face in world metres (across it, up it or north), uv1 how much of it is
// foam and how much shows, uv2 the current along the face in uv0's directions (m/s).
Shader "FlowingWater/WaterShell"
{
    Properties
    {
        // The sea's own (copied from the game's material at run time).
        _ColorBottom ("Colour deep down", Color) = (0.098, 0.196, 0.169, 1)
        _ColorBottomShallow ("Colour down in shallow water", Color) = (0.196, 0.176, 0.106, 1)
        _SurfaceColor ("Tint of what is seen through", Color) = (0.728, 0.728, 0.728, 1)
        _FoamColor ("Foam colour", Color) = (0.838, 0.838, 0.838, 1)
        _Glossiness ("Smoothness", Range(0, 1)) = 0.94
        _Metallic ("Metallic", Range(0, 1)) = 0
        _Normal ("Ripple normals", 2D) = "bump" {}
        _FoamTex ("Foam", 2D) = "white" {}
        _NormalScale ("Ripple tiling (per metre)", Float) = 0.04
        _RefractionScale ("Refraction", Float) = 0.1
        _RefractionMax ("Refraction at most (share of the screen)", Float) = 0.01

        // The flowing water's own.
        _FlowTiling ("Running ripples tiling (times the still ones')", Float) = 6
        _FlowStrength ("Running ripples strength", Range(0, 3)) = 1.4
        _FlowCycle ("Flow cycle (s)", Float) = 1.5
        _Clearness ("How much of what is behind shows through", Range(0, 1)) = 0.55
        _FoamTiling ("Foam tiling (per metre)", Float) = 0.8
    }

    SubShader
    {
        // Drawn with the surface, just before the sea; both sides, so it is seen from inside the water too.
        Tags { "Queue" = "Transparent-101" "RenderType" = "Transparent" "IgnoreProjector" = "True" "ForceNoShadowCasting" = "True" }
        LOD 300
        ZWrite Off
        Cull Off

        // The same picture of what is behind as the surface's (grabbed once a frame under this name).
        GrabPass { "_FlowingWaterBehind" }

        CGPROGRAM
        #pragma surface surf Standard alpha:premul vertex:vert finalcolor:Finish noshadow nolightmap nodynlightmap nodirlightmap nometa nolppv noshadowmask
        #pragma target 3.5
        #pragma multi_compile_fog

        sampler2D _Normal;
        sampler2D _FoamTex;
        sampler2D _FlowingWaterBehind;

        fixed4 _ColorBottom;
        fixed4 _ColorBottomShallow;
        fixed4 _SurfaceColor;
        fixed4 _FoamColor;
        float _Glossiness;
        float _Metallic;
        float _NormalScale;
        float _RefractionScale;
        float _RefractionMax;
        float _FlowTiling;
        float _FlowStrength;
        float _FlowCycle;
        float _Clearness;
        float _FoamTiling;

        #include "FlowingWaterSimMask.cginc"

        // The game's, as the surface reads them (unset in a preview: the fog takes its own colour).
        float4 _SunDir;
        float4 _SunFogColor;

        struct Input
        {
            float2 place;   // on the face, world metres
            float2 water;   // foam, shown
            float2 current; // along the face, m/s
            float3 inside;  // a world place a little inside the water the face is the side of
            float3 worldPos;
            float4 screenPos;
            float4 grabPos;
            float facing : VFACE;
        };

        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            o.place = v.texcoord.xy;
            o.water = v.texcoord1.xy;
            o.current = v.texcoord2.xy;
            o.inside = mul(unity_ObjectToWorld, v.vertex).xyz - (UnityObjectToWorldNormal(v.normal) * 0.1);
            o.grabPos = ComputeGrabScreenPos(UnityObjectToClipPos(v.vertex));
        }

        // One phase of the flow (as the surface's): where the texture has run to, and how much this phase counts.
        float2 FlowPhase(float2 uv, float2 run, float offset, out float weight)
        {
            float t = frac((_Time.y / _FlowCycle) + offset);
            weight = 1.0 - abs((2.0 * t) - 1.0);
            return uv - (run * t * _FlowCycle) + (offset * float2(0.37, 0.61));
        }

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
            color.rgb *= o.Alpha;
        }

        void surf(Input i, inout SurfaceOutputStandard o)
        {
            // The side of water the card draws near the camera is the card's (its surface moves; falls stay here).
            if (SimDraws(i.inside))
            {
                discard;
            }

            // Ripples running with the current along the face, two phases blended so they never stretch.
            float scale = _NormalScale * _FlowTiling;
            float wa;
            float wb;
            float2 a = FlowPhase(i.place * scale, i.current * scale, 0.0, wa);
            float2 b = FlowPhase(i.place * scale, i.current * scale, 0.5, wb);
            float3 ripples = (UnpackNormal(tex2D(_Normal, a)) * wa) + (UnpackNormal(tex2D(_Normal, b)) * wb);
            float3 n = normalize(lerp(float3(0.0, 0.0, 1.0), ripples * _FlowStrength, 0.6));
            // Seen from inside the water, the face's other side.
            n.z *= i.facing > 0.0 ? 1.0 : -1.0;

            float2 grab = i.grabPos.xy / max(i.grabPos.w, 0.0001);
            float2 bend = clamp(n.xy * _RefractionScale * 5.0, -_RefractionMax, _RefractionMax);
            float3 behind = tex2D(_FlowingWaterBehind, grab + bend).rgb;

            // Foam pouring along with the current: a fall's is white water.
            float fa;
            float fb;
            float2 foamUv = i.place * _FoamTiling;
            float2 foamRun = i.current * _FoamTiling;
            float noise = (tex2D(_FoamTex, FlowPhase(foamUv, foamRun, 0.25, fa)).r * fa) + (tex2D(_FoamTex, FlowPhase(foamUv, foamRun, 0.75, fb)).r * fb);
            float want = saturate(i.water.x);
            float foam = smoothstep(1.0 - want, 1.0 - want + 0.3, noise) * want;

            // The water's own colour where the scene behind does not show through it; the foam's, whole.
            float3 colour = lerp(_ColorBottomShallow.rgb, _ColorBottom.rgb, 0.5) * (1.0 - _Clearness);
            o.Albedo = lerp(colour, _FoamColor.rgb, foam);
            o.Emission = behind * _SurfaceColor.rgb * _Clearness * (1.0 - foam);
            o.Normal = n;
            o.Metallic = _Metallic * (1.0 - foam);
            o.Smoothness = lerp(_Glossiness, 0.1, foam);
            o.Alpha = saturate(i.water.y) * lerp(0.85, 1.0, foam);
            DebugColour(o, float3(1.0, 0.9, 0.1));
        }
        ENDCG
    }

    FallBack Off
}
