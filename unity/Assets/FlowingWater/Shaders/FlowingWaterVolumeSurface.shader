// FlowingWater's volume water drawn over the scene: one triangle over the whole screen, drawn with the scene's
// transparent things (after the sheet of water), from what the march found this frame (FlowingWaterVolumeMarch.shader,
// VolumeSurface.cs): the surface's distance, the water seen through, the foam there and the surface's normal. The look
// is the sea's, as FlowingWaterLook.cginc gives it to the sheet: what is behind (grabbed) bent and tinted by the water
// seen through, its own colour where it is deep, the sky and the sun on the surface, foam where the froth gathers;
// fogged as the sea. With the camera under the water, the scene is seen through the water up to its surface or the
// scene, tinted and dimmed by that much water.
Shader "FlowingWater/VolumeSurface"
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
        _BodyDepth ("Water this thick (m, seen through) takes two thirds of the green light behind it", Float) = 2.5
        _BodyTint ("How much faster each colour's light goes than at BodyDepth (red first)", Vector) = (1.6, 1.1, 0.9, 0)

        // The volume water's own.
        _EdgeThickness ("Water thinner than this along the view (m) fades out", Float) = 0.06
        _ThickMost ("Water seen through more than this (m) takes no more of the light", Float) = 4
        _LeastLight ("The least light on the water's body (share of the sky's), so it is never black", Range(0, 1)) = 0.45
        _FoamFrom ("Froth in a cell from this share of full shows as foam", Range(0, 1)) = 0.1
        _FoamFull ("Froth at this share of full is whole foam", Range(0, 1)) = 0.6
        _UnderFog ("Under the water, the scene fades into the water's colour over this far (m)", Float) = 12
    }

    SubShader
    {
        // After the sheet of water (Transparent-101) and the spray (Transparent-100): what is behind it is the water.
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" "ForceNoShadowCasting" = "True" }
        GrabPass { "_FlowingWaterVolumeBehind" }

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

            sampler2D _FlowingWaterVolumeBehind;
            // What the march found, at half the screen's size (VolumeSurface sets them).
            sampler2D _VolumeWater;
            float4 _VolumeWater_TexelSize;
            sampler2D _VolumeNormal;
            sampler2D _Normal;
            fixed4 _ColorTop;
            fixed4 _ColorBottom;
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
            float _FoamFrom;
            float _FoamFull;
            float _UnderFog;

            // The sun as the game sets it for every shader (none: the scene's light, as the surface's helper found it).
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

            float3 ViewToWorldPlace(float3 v)
            {
                return mul(unity_CameraToWorld, float4(v.x, v.y, -v.z, 1.0)).xyz;
            }

            float3 WorldToViewDirection(float3 w)
            {
                float3 v = mul((float3x3)unity_WorldToCamera, w);
                return float3(v.x, v.y, v.z);
            }

            // What the march found at a pixel, from the half-size pictures: of the four texels round it, the ones that
            // hold water, weighted by how near they are (so the surface's edge stays crisp and takes no air in).
            void Found(float2 uv, out float4 water, out float4 normal)
            {
                float2 texel = _VolumeWater_TexelSize.xy;
                float2 at = (uv / texel) - 0.5;
                float2 base = floor(at);
                float2 f = at - base;
                water = 0.0;
                normal = 0.0;
                float weights = 0.0;
                for (int j = 0; j < 2; j++)
                {
                    for (int i = 0; i < 2; i++)
                    {
                        float2 p = (base + float2(i, j) + 0.5) * texel;
                        float4 w = tex2D(_VolumeWater, p);
                        if (w.x > 0.0 || w.w > 0.5)
                        {
                            float weight = (i == 0 ? 1.0 - f.x : f.x) * (j == 0 ? 1.0 - f.y : f.y);
                            water += w * weight;
                            normal += tex2D(_VolumeNormal, p) * weight;
                            weights += weight;
                        }
                    }
                }

                if (weights > 0.0)
                {
                    water /= weights;
                    normal /= weights;
                }

                // Over an edge, the water takes the half the pixel lies in: less than half the texels, none.
                water = weights >= 0.5 ? water : 0.0;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv = i.screen.xy / i.screen.w;
                float4 water;
                float4 found;
                Found(uv, water, found);
                bool under = water.w > 0.5;
                if (water.x <= 0.0 && !under)
                {
                    discard;
                }

                float eye = water.x;
                float thickness = max(water.y, 0.0);
                float2 ndc = float2(i.ndc.x, i.ndc.y * _ProjectionParams.x);
                float3 here = Place(ndc, max(eye, 0.3));
                float3 worldPos = ViewToWorldPlace(here);
                float3 worldN = normalize(found.xyz);
                float3 toCamera = normalize(_WorldSpaceCameraPos - worldPos);
                float3 sun = dot(_SunDir.xyz, _SunDir.xyz) > 0.01 ? normalize(_SunDir.xyz) : normalize(_FWSunDir.xyz);
                float3 sunColour = _FWSunColour.rgb;
                float3 skyLight = ShadeSH9(float4(0.0, 1.0, 0.0, 1.0));

                // Seen through so much water (no more than _ThickMost): its light taken as it goes (red first), the
                // water's own colour in its place, lit by the sky at least.
                float seen = min(thickness, _ThickMost);
                float3 through = exp(-seen * _BodyTint.rgb / max(_BodyDepth, 0.001));
                through *= 1.0 - saturate(seen / _DepthFade);
                // The water's own colour where its light is taken: the surface's green where it is shallow, the deep
                // colour where it is deep, dimmer than the surface's own light (it is light from below, scattered).
                float3 body = 0.6 * lerp(_ColorTop.rgb, _ColorBottom.rgb, saturate(seen / _DeepWater));

                if (under)
                {
                    // Under the water: the scene through it, fading into the water's own colour with the distance.
                    float3 behindUnder = tex2D(_FlowingWaterVolumeBehind, i.grab.xy / i.grab.w).rgb;
                    float3 dim = max(skyLight, _LeastLight * skyLight);
                    float3 deep = _ColorBottom.rgb * dim;
                    float3 colourUnder = (behindUnder * through * _SurfaceColor.rgb) + (deep * (1.0 - through));
                    float far = saturate(thickness / max(_UnderFog, 0.1));
                    colourUnder = lerp(colourUnder, deep, far * far);
                    return fixed4(colourUnder, 1.0);
                }

                // The sea's ripples laid on the surface where it faces up, drifting as the sea's do.
                float2 ripple = UnpackNormal(tex2D(_Normal, (worldPos.xz * _NormalScale) + float2(0.0, -0.2 * _Time.x * _WaveVel))).xy;
                worldN = normalize(worldN + (float3(ripple.x, 0.0, ripple.y) * 0.3 * _NormalPower * saturate(worldN.y)));

                // What is behind, bent by the surface.
                float3 viewN = WorldToViewDirection(worldN);
                float2 bend = clamp(viewN.xy * _RefractionScale * 5.0, -_RefractionMax, _RefractionMax) * saturate(thickness / 0.3);
                float2 grab = (i.grab.xy / i.grab.w) + bend;
                float3 behind = tex2D(_FlowingWaterVolumeBehind, grab).rgb;

                float3 ambient = ShadeSH9(float4(worldN, 1.0));
                float3 lit = max(ambient + (sunColour * saturate(dot(worldN, sun))), _LeastLight * skyLight);
                float3 colour = (behind * through * _SurfaceColor.rgb) + (body * (1.0 - through) * lit);

                // The sky in the surface, more the flatter one looks along it (a soft mip of the probe, its brightness
                // held: the sun's disk in it would otherwise blaze over metres of water); the sun's glint on its own.
                float fresnel = 0.02 + (0.98 * pow(1.0 - saturate(dot(worldN, toCamera)), 5.0));
                float3 reflected = reflect(-toCamera, worldN);
                float3 sky = DecodeHDR(UNITY_SAMPLE_TEXCUBE_LOD(unity_SpecCube0, reflected, 3.0), unity_SpecCube0_HDR);
                sky = min(sky, 1.2);
                colour = lerp(colour, sky, 0.8 * fresnel * _Glossiness);
                float glint = pow(saturate(dot(worldN, normalize(sun + toCamera))), 256.0) * _Glossiness;
                colour += sunColour * glint;

                // Foam where the froth gathers on the surface (a fall's foot, a jet's boil, a front), lit as the sea's
                // foam is (its colour under the light), not brighter than the light.
                float foam = smoothstep(_FoamFrom, _FoamFull, water.z) * _FoamColor.a;
                float3 foamLit = 0.8 * _FoamColor.rgb * (ambient + (sunColour * saturate(dot(worldN, sun))));
                colour = lerp(colour, foamLit, foam);

                // Fogged as the sea, toward the sun's colour where one looks at the sun.
#if defined(FOG_LINEAR) || defined(FOG_EXP) || defined(FOG_EXP2)
                float toSun = saturate(dot(-toCamera, sun));
                float3 sunFog = pow(max(_SunFogColor.rgb, 0.0), 2.2);
                float3 fogColour = dot(_SunFogColor.rgb, 1.0) > 0.0 ? lerp(unity_FogColor.rgb, sunFog, toSun) : unity_FogColor.rgb;
                UNITY_CALC_FOG_FACTOR_RAW(eye);
                colour = lerp(fogColour, colour, saturate(unityFogFactor));
#endif

                // Thin water fades out into the scene (a splash's drops are not discs); at the square's side the water
                // fades into the sheet's bricks beyond.
                float alpha = saturate(thickness / max(_EdgeThickness, 0.001)) * found.w;
                return fixed4(colour, alpha);
            }
            ENDCG
        }
    }

    FallBack Off
}
