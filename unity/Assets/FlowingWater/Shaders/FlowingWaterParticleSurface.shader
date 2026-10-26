// FlowingWater's particle water drawn as water over the scene: one triangle over the whole screen, drawn with the
// scene's transparent things (after the sheet of water near the camera), from the two textures FlowingWaterParticles
// made this frame (ParticleSurface.cs): the nearest particle's distance at each pixel (_ParticleDepth, smoothed into
// one surface) and the water the view goes through there with how much of it is air (_ParticleThickness). The
// surface's normal comes from the distances round each pixel; the look is the sea's, as FlowingWaterLook.cginc gives
// it to the sheet: what is behind (grabbed) bent and tinted by the water seen through, the sky and the sun on the
// surface, foam where the water is air, fogged as the sea.
Shader "FlowingWater/ParticleSurface"
{
    Properties
    {
        // The sea's own (copied from the game's material at run time).
        _ColorTop ("Colour of the surface's tops", Color) = (0.315, 0.524, 0.361, 1)
        _ColorBottom ("Colour deep down", Color) = (0.098, 0.196, 0.169, 1)
        _ColorBottomShallow ("Colour down in shallow water", Color) = (0.196, 0.176, 0.106, 1)
        _SurfaceColor ("Tint of what is seen through", Color) = (0.728, 0.728, 0.728, 1)
        _FoamColor ("Foam colour", Color) = (0.838, 0.838, 0.838, 1)
        _Glossiness ("Smoothness", Range(0, 1)) = 0.94
        _Normal ("Ripple normals", 2D) = "bump" {}
        _FoamTex ("Foam", 2D) = "white" {}
        _NormalPower ("Ripple strength", Range(0, 5)) = 0.5
        _NormalScale ("Ripple tiling (per metre)", Float) = 0.04
        _WaveVel ("Ripple drift", Float) = 1
        _DepthFade ("Metres of water that hide what is behind", Float) = 15
        _RefractionScale ("Refraction", Float) = 0.1
        _RefractionMax ("Refraction at most (share of the screen)", Float) = 0.01

        // The flowing water's own (as FlowingWater.shader's).
        _DeepWater ("Water this deep (m) takes the deep colour", Float) = 6
        _BodyDepth ("Water this thick (m, seen through) takes two thirds of the green light behind it", Float) = 1.2
        _BodyTint ("How much faster each colour's light goes than at BodyDepth (red first)", Vector) = (1.6, 1.1, 0.9, 0)

        // The particle water's own.
        _EdgeThickness ("Water thinner than this along the view (m) fades out", Float) = 0.04
        _ThickMost ("Water seen through more than this (m) takes no more of the light", Float) = 3
        _LeastLight ("The least light on the water's body (share), so it is never black", Range(0, 1)) = 0.35
    }

    SubShader
    {
        // After the sheet of water (Transparent-101) and the spray (Transparent-100): what is behind it is the water.
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" "ForceNoShadowCasting" = "True" }
        GrabPass { "_FlowingWaterParticlesBehind" }

        Pass
        {
            Cull Off
            ZWrite Off
            ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #pragma multi_compile_fog
            #include "UnityCG.cginc"

            sampler2D _FlowingWaterParticlesBehind;
            sampler2D _ParticleDepth;
            float4 _ParticleDepth_TexelSize;
            sampler2D _ParticleThickness;
            sampler2D _Normal;
            fixed4 _ColorBottom;
            fixed4 _ColorBottomShallow;
            fixed4 _SurfaceColor;
            fixed4 _FoamColor;
            float _Glossiness;
            float _NormalPower;
            float _NormalScale;
            float _WaveVel;
            float _DepthFade;
            float _RefractionScale;
            float _RefractionMax;
            float _DeepWater;
            float _BodyDepth;
            float4 _BodyTint;
            float _EdgeThickness;
            float _ThickMost;
            float _LeastLight;
            // A particle's size (m), as ParticleSurface sets it: thin water is water this thick along the view at most.
            float _FWParticleSize;

            // The sun as the game sets it for every shader (none: the scene's light, as ParticleSurface found it).
            float4 _SunDir;
            float4 _SunFogColor;
            float4 _FWSunDir;
            float4 _FWSunColour;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 ndc : TEXCOORD0;
                float4 screen : TEXCOORD1;
                float4 grab : TEXCOORD2;
            };

            // One triangle over the whole screen, from three points.
            v2f vert(uint id : SV_VertexID)
            {
                v2f o;
                float2 corner = float2((id << 1) & 2, id & 2);
                o.ndc = (corner * 2.0) - 1.0;
                o.pos = float4(o.ndc, UNITY_NEAR_CLIP_VALUE, 1.0);
                o.screen = ComputeScreenPos(o.pos);
                o.grab = ComputeGrabScreenPos(o.pos);
                return o;
            }

            // The place in view space seen at a point of the screen, this far along the view (view space looks down -z).
            float3 Place(float2 ndc, float eye)
            {
                return float3(ndc.x * eye / unity_CameraProjection._m00, ndc.y * eye / unity_CameraProjection._m11, -eye);
            }

            float Eye(float2 uv)
            {
                return tex2D(_ParticleDepth, uv).r;
            }

            // The surface's slope at a pixel from the distances round it: each way the neighbour nearer in distance (not
            // across a step), so the silhouette keeps a clean edge.
            float3 Slope(float2 uv, float2 ndc, float eye, float3 here)
            {
                float2 texel = _ParticleDepth_TexelSize.xy;
                float2 step = texel * 2.0;
                float right = Eye(uv + float2(texel.x, 0.0)), left = Eye(uv - float2(texel.x, 0.0));
                float up = Eye(uv + float2(0.0, texel.y)), down = Eye(uv - float2(0.0, texel.y));
                bool useRight = right > 0.0 && (left <= 0.0 || abs(right - eye) < abs(left - eye));
                bool useUp = up > 0.0 && (down <= 0.0 || abs(up - eye) < abs(down - eye));
                float3 dx = useRight ? Place(ndc + float2(step.x, 0.0), right) - here : here - Place(ndc - float2(step.x, 0.0), left);
                float3 dy = useUp ? Place(ndc + float2(0.0, step.y), up) - here : here - Place(ndc - float2(0.0, step.y), down);
                float3 n = normalize(cross(dx, dy));
                return n.z < 0.0 ? -n : n;
            }

            float3 ViewToWorldDirection(float3 v)
            {
                return normalize(mul((float3x3)unity_CameraToWorld, float3(v.x, v.y, -v.z)));
            }

            float3 ViewToWorldPlace(float3 v)
            {
                return mul(unity_CameraToWorld, float4(v.x, v.y, -v.z, 1.0)).xyz;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv = i.screen.xy / i.screen.w;
                float eye = Eye(uv);
                if (eye <= 0.0)
                {
                    discard;
                }

                // Up on the screen is up in view space; a platform drawing into a texture upside down flips it.
                float2 ndc = float2(i.ndc.x, i.ndc.y * _ProjectionParams.x);
                float2 thick = tex2D(_ParticleThickness, uv).rg;
                float thickness = max(thick.r, 0.0);
                float air = saturate(thick.g / max(thickness, 0.001));
                float3 here = Place(ndc, eye);
                float3 n = Slope(uv, ndc, eye, here);
                float3 worldN = ViewToWorldDirection(n);
                float3 worldPos = ViewToWorldPlace(here);

                // The sea's ripples laid on the surface where it faces up, drifting as the sea's do.
                float2 ripple = UnpackNormal(tex2D(_Normal, (worldPos.xz * _NormalScale) + float2(0.0, -0.2 * _Time.x * _WaveVel))).xy;
                worldN = normalize(worldN + (float3(ripple.x, 0.0, ripple.y) * 0.3 * _NormalPower * saturate(worldN.y)));

                // What is behind, bent by the surface.
                float2 bend = clamp(n.xy * _RefractionScale * 5.0, -_RefractionMax, _RefractionMax);
                float2 grab = (i.grab.xy / i.grab.w) + bend;
                float3 behind = tex2D(_FlowingWaterParticlesBehind, grab).rgb;

                // Seen through so much water (no more than _ThickMost: a fall seen along its length is not a well): its
                // light taken as it goes (red first), the water's own colour in its place, lit by the sky at least.
                float seen = min(thickness, _ThickMost);
                float3 through = exp(-seen * _BodyTint.rgb / max(_BodyDepth, 0.001));
                through *= 1.0 - sqrt(saturate(seen / _DepthFade));
                float3 body = lerp(_ColorBottomShallow.rgb, _ColorBottom.rgb, saturate(seen / _DeepWater));
                float3 toCamera = normalize(_WorldSpaceCameraPos - worldPos);
                float3 ambient = ShadeSH9(float4(worldN, 1.0));
                float3 sun = dot(_SunDir.xyz, _SunDir.xyz) > 0.01 ? normalize(_SunDir.xyz) : normalize(_FWSunDir.xyz);
                float3 sunColour = _FWSunColour.rgb;
                float3 lit = max(ambient + (sunColour * saturate(dot(worldN, sun))), _LeastLight * ShadeSH9(float4(0.0, 1.0, 0.0, 1.0)));
                float3 colour = (behind * through * _SurfaceColor.rgb) + (body * (1.0 - through) * lit);

                // The sky in the surface, more the flatter one looks along it; the sun's glint.
                float fresnel = 0.02 + (0.98 * pow(1.0 - saturate(dot(worldN, toCamera)), 5.0));
                float3 reflected = reflect(-toCamera, worldN);
                float3 sky = DecodeHDR(UNITY_SAMPLE_TEXCUBE_LOD(unity_SpecCube0, reflected, 0.0), unity_SpecCube0_HDR);
                colour = lerp(colour, sky, fresnel * _Glossiness);
                float glint = pow(saturate(dot(worldN, normalize(sun + toCamera))), 256.0) * _Glossiness;
                colour += sunColour * glint;

                // Foam where the water is air (the churn of a fall), not on a lone drop: a drop is clear water, seen through.
                float lone = saturate(1.0 - (thickness / max(2.0 * _FWParticleSize, 0.01)));
                colour = lerp(colour, _FoamColor.rgb * (ambient + (0.6 * sunColour)), saturate(air * 1.5) * (1.0 - lone));

                // Fogged as the sea, toward the sun's colour where one looks at the sun.
#if defined(FOG_LINEAR) || defined(FOG_EXP) || defined(FOG_EXP2)
                float toSun = saturate(dot(-toCamera, sun));
                float3 sunFog = pow(max(_SunFogColor.rgb, 0.0), 2.2);
                float3 fogColour = dot(_SunFogColor.rgb, 1.0) > 0.0 ? lerp(unity_FogColor.rgb, sunFog, toSun) : unity_FogColor.rgb;
                UNITY_CALC_FOG_FACTOR_RAW(eye);
                colour = lerp(fogColour, colour, saturate(unityFogFactor));
#endif

                // Thin water at the edge fades out into the scene; a lone drop is faint, not a disc.
                return fixed4(colour, saturate(thickness / max(_EdgeThickness, 0.5 * _FWParticleSize)));
            }
            ENDCG
        }
    }

    FallBack Off
}
