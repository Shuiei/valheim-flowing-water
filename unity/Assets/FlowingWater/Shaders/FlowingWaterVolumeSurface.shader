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
        _Metallic ("Metallic", Range(0, 1)) = 0
        _Normal ("Ripple normals", 2D) = "bump" {}
        _FoamTex ("Foam", 2D) = "white" {}
        _NormalPower ("Ripple strength", Range(0, 5)) = 0.5
        _NormalScale ("Ripple tiling (per metre)", Float) = 0.04
        _WaveVel ("Ripple drift", Float) = 1
        _FlowTiling ("Running ripples: how many times finer than the sea's", Float) = 4
        _FlowCycle ("Running ripples: seconds each phase runs before it fades", Float) = 1.5
        _FlowFrom ("Water this fast (m/s) begins to show running ripples", Float) = 0.2
        _FlowFull ("Water this fast (m/s) shows them whole", Float) = 1.5
        _RapidsSpeed ("Water this fast (m/s) glints with the finest ripples", Float) = 3
        _DepthFade ("Metres of water that hide what is behind", Float) = 15
        _RefractionScale ("Refraction", Float) = 0.1
        _RefractionMax ("Refraction at most (share of the screen)", Float) = 0.01

        // The flowing water's own (as FlowingWater.shader's).
        _DeepWater ("Water this deep (m) takes the deep colour", Float) = 6
        _BodyDepth ("Water this thick (m, seen through) takes two thirds of the green light behind it", Float) = 2.5
        _BodyTint ("How much faster each colour's light goes than at BodyDepth (red first)", Vector) = (1.6, 1.1, 0.9, 0)

        // The volume water's own.
        _EdgeThickness ("Water thinner than this along the view (m) fades out", Float) = 0.2
        _ThickMost ("Water seen through more than this (m) takes no more of the light", Float) = 4
        _FoamFrom ("Froth in a cell from this share of full shows as foam", Range(0, 1)) = 0.25
        _FoamFull ("Froth at this share of full is whole foam", Range(0, 1)) = 1
        _RushFrom ("Water this fast (m/s) begins to run white", Float) = 5
        _RushFull ("Water this fast (m/s) runs white", Float) = 11
        _RushThin ("Fast water thinner than this (m) runs white; thicker, it keeps its colour", Float) = 0.4
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
            Blend One OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #pragma multi_compile_fog
            // Lit as a surface shader with alpha:premul is (the bricks' water's).
            #define _ALPHAPREMULTIPLY_ON 1
            #include "UnityCG.cginc"
            #include "UnityPBSLighting.cginc"
            #include "FlowingWaterShallow.cginc"

            int _VolumeLook;
            sampler2D _FlowingWaterVolumeBehind;
            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
            // What the march found, at half the screen's size (VolumeSurface sets them).
            sampler2D _VolumeWater;
            float4 _VolumeWater_TexelSize;
            sampler2D _VolumeFlow;
            sampler2D _Normal;
            sampler2D _FoamTex;
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
            float _FlowTiling;
            float _FlowCycle;
            float _FlowFrom;
            float _FlowFull;
            float _RapidsSpeed;
            float _DepthFade;
            float _RefractionScale;
            float _RefractionMax;
            float _DeepWater;
            float _BodyDepth;
            float4 _BodyTint;
            float _EdgeThickness;
            float _ThickMost;
            float _FoamFrom;
            float _FoamFull;
            float _RushFrom;
            float _RushFull;
            float _RushThin;
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

            // The surface's distance at a point of the march's picture, smoothly between its texels (of the four round
            // the point, the ones that hold water, by how near they are; 0 where fewer than half do): the picture is
            // half the screen's size, and a step of a texel read texel by texel would differ from one screen row to
            // the next, which the normal would show as streaks.
            float Eye(float2 uv)
            {
                float2 texel = _VolumeWater_TexelSize.xy;
                float2 at = (uv / texel) - 0.5;
                float2 base = floor(at);
                float2 f = at - base;
                float sum = 0.0;
                float weights = 0.0;
                for (int j = 0; j < 2; j++)
                {
                    for (int i = 0; i < 2; i++)
                    {
                        float x = tex2D(_VolumeWater, (base + float2(i, j) + 0.5) * texel).x;
                        float weight = x > 0.0 ? (i == 0 ? 1.0 - f.x : f.x) * (j == 0 ? 1.0 - f.y : f.y) : 0.0;
                        sum += x * weight;
                        weights += weight;
                    }
                }

                return weights >= 0.5 ? sum / weights : 0.0;
            }

            // The surface's slope at a pixel from the smoothed distances round it: each way the neighbour nearer in
            // distance (not across a step), so the silhouette keeps a clean edge; view space.
            float3 Slope(float2 uv, float2 ndc, float eye, float3 here)
            {
                float2 texel = _VolumeWater_TexelSize.xy;
                float2 step = texel * 2.0;
                float right = Eye(uv + float2(texel.x, 0.0)), left = Eye(uv - float2(texel.x, 0.0));
                float up = Eye(uv + float2(0.0, texel.y)), down = Eye(uv - float2(0.0, texel.y));
                bool useRight = right > 0.0 && (left <= 0.0 || abs(right - eye) < abs(left - eye));
                bool useUp = up > 0.0 && (down <= 0.0 || abs(up - eye) < abs(down - eye));
                // A lone texel of water (a far drop, the tip of a rivulet) has no neighbour either way: it faces the eye.
                if ((!useRight && left <= 0.0) || (!useUp && down <= 0.0))
                {
                    return float3(0.0, 0.0, 1.0);
                }

                float3 dx = useRight ? Place(ndc + float2(step.x, 0.0), right) - here : here - Place(ndc - float2(step.x, 0.0), left);
                float3 dy = useUp ? Place(ndc + float2(0.0, step.y), up) - here : here - Place(ndc - float2(0.0, step.y), down);
                float3 c = cross(dx, dy);
                if (dot(c, c) < 1e-12)
                {
                    return float3(0.0, 0.0, 1.0);
                }

                float3 n = normalize(c);
                return n.z < 0.0 ? -n : n;
            }

            float3 ViewToWorldDirection(float3 v)
            {
                return normalize(mul((float3x3)unity_CameraToWorld, float3(v.x, v.y, -v.z)));
            }

            // What the march found at a pixel, from the half-size pictures: of the four texels round it, the ones that
            // hold water, weighted by how near they are (so the surface's edge stays crisp and takes no air in).
            void Found(float2 uv, out float4 water, out float4 flow)
            {
                float2 texel = _VolumeWater_TexelSize.xy;
                float2 at = (uv / texel) - 0.5;
                float2 base = floor(at);
                float2 f = at - base;
                water = 0.0;
                flow = 0.0;
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
                            flow += tex2D(_VolumeFlow, p) * weight;
                            weights += weight;
                        }
                    }
                }

                if (weights > 0.0)
                {
                    water /= weights;
                    flow /= weights;
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

                // The surface's distance here, read as the normal's neighbours are, so the two agree (the march's own
                // where that read finds too little water about the pixel: the silhouette's last pixels).
                float smooth = Eye(uv);
                float eye = under || smooth <= 0.0 ? water.x : smooth;

                // What stands out of the water in front of its surface at this pixel (a blade of grass, a post): seen,
                // not drawn over (the march went on past it to the farthest of the pixels it stands for).
                if (!under && LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, uv)) < eye - 0.02)
                {
                    discard;
                }
                float thickness = max(water.y, 0.0);
                float2 ndc = float2(i.ndc.x, i.ndc.y * _ProjectionParams.x);
                float3 here = Place(ndc, max(eye, 0.3));
                float3 worldPos = ViewToWorldPlace(here);
                // The surface's normal from its smoothed distances (water's own smoothness), not the density's grain.
                float3 worldN = under ? float3(0.0, 1.0, 0.0) : ViewToWorldDirection(Slope(uv, ndc, eye, here));
                if (_VolumeLook == 3)
                {
                    return fixed4(saturate(thickness / 2.0).xxx, 1.0);
                }

                if (_VolumeLook == 4)
                {
                    return fixed4(frac(eye).xxx, 1.0);
                }

                if (_VolumeLook == 5)
                {
                    return fixed4((worldN * 0.5) + 0.5, 1.0);
                }

                float3 toCamera = normalize(_WorldSpaceCameraPos - worldPos);
                // A surface one sees faces the eye: at a silhouette the distances' slope can turn the normal away from
                // it (a step to the water behind read as a slope), which lit the pixel as the water's dark underside, a
                // speck along the far shore. Turned back until it faces the eye a little.
                worldN = under ? worldN : normalize(worldN - (toCamera * min(dot(worldN, toCamera) - 0.1, 0.0)));
                // The surface as found, before its ripples: what the sky's share in it and the water's depth along the
                // view are reckoned from (a surface rippled all over would be white with sky from most angles).
                float3 lieN = worldN;
                float3 sun = dot(_SunDir.xyz, _SunDir.xyz) > 0.01 ? normalize(_SunDir.xyz) : normalize(_FWSunDir.xyz);
                float3 sunColour = _FWSunColour.rgb;
                float3 skyLight = ShadeSH9(float4(0.0, 1.0, 0.0, 1.0));

                // Seen through so much water (no more than _ThickMost): its light taken as it goes (red first), the
                // water's own colour in its place, lit by the sky at least.
                // (Along the ray, scaled toward the water's depth under the surface: a ray skimming a sheet goes a
                // long way through a hand of water, and a thin edge seen along it is not a deep green rim.)
                float seen = min(thickness * saturate(0.2 + abs(dot(lieN, toCamera))), _ThickMost);
                float3 through = exp(-seen * _BodyTint.rgb / max(_BodyDepth, 0.001));
                through *= 1.0 - saturate(seen / _DepthFade);
                // The water's own colour where its light is taken, as the bricks' water has it (FlowingWaterLook.cginc):
                // the shallow water's colour, the deep colour where it is deep.
                float3 body = (1.0 - through) * lerp(_ColorBottomShallow.rgb, _ColorBottom.rgb, saturate(seen / _DeepWater));

                if (under)
                {
                    // Under the water: the scene through it, its light lost over _UnderFog (red first) as clear water
                    // takes it, not over the depth a surface's colour is reckoned from (that turned a pool's far side
                    // black a few metres off); and in its place the water's own light, scattered from the sky above:
                    // the shallow colour near, the deep colour far.
                    float3 behindUnder = tex2D(_FlowingWaterVolumeBehind, i.grab.xy / i.grab.w).rgb;
                    float fog = max(_UnderFog, 0.1);
                    // Looking down, the view stays in the water until it meets the scene (the march counts only the
                    // particles: under the floor deep water is given, it found none); looking up, it leaves through
                    // the surface, as far as the march went.
                    float3 look = normalize(Place(ndc, 1.0));
                    float toScene = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, uv)) / max(-look.z, 1e-4);
                    float path = ViewToWorldDirection(look).y < 0.0 ? max(thickness, toScene) : thickness;
                    // (Nearly all of it, a twentieth left, over _UnderFog: the scene fades into the water's colour
                    // over that far, as the setting says.)
                    float3 throughUnder = exp(-3.0 * path * _BodyTint.rgb / fog);
                    float3 lit = skyLight + (0.25 * sunColour * saturate(sun.y));
                    float3 scattered = lerp(_ColorTop.rgb, _ColorBottom.rgb, saturate(path / (2.0 * fog))) * lit;
                    float3 colourUnder = (behindUnder * throughUnder) + (scattered * (1.0 - throughUnder));
                    return fixed4(colourUnder, 1.0);
                }

                // The sea's ripples laid on the surface where it faces up, drifting as the sea's do; and where the water
                // runs, finer ripples dragged along with it at its own speed (two phases blended so they never stretch),
                // the finest glinting on fast water: running water moves, a pool lies still.
                float2 current = found.xz;
                float speed = length(current);
                float2 ripple = UnpackNormal(tex2D(_Normal, (worldPos.xz * _NormalScale) + float2(0.0, -0.2 * _Time.x * _WaveVel))).xy;
                float running = smoothstep(_FlowFrom, _FlowFull, speed);
                if (running > 0.0)
                {
                    float scale = _NormalScale * _FlowTiling;
                    float2 uv0 = worldPos.xz * scale;
                    float2 run = current * scale;
                    float ta = frac(_Time.y / _FlowCycle), tb = frac((_Time.y / _FlowCycle) + 0.5);
                    float wa = 1.0 - abs((2.0 * ta) - 1.0), wb = 1.0 - abs((2.0 * tb) - 1.0);
                    float2 a = uv0 - (run * ta * _FlowCycle);
                    float2 b = uv0 - (run * tb * _FlowCycle) + float2(0.37, 0.61);
                    float2 fine = (UnpackNormal(tex2D(_Normal, a)).xy * wa) + (UnpackNormal(tex2D(_Normal, b)).xy * wb);
                    float2 glints = (UnpackNormal(tex2D(_Normal, (a * 3.1) + 0.3)).xy * wa) + (UnpackNormal(tex2D(_Normal, (b * 3.1) + 0.7)).xy * wb);
                    ripple = lerp(ripple, fine + (0.25 * saturate(speed / _RapidsSpeed) * glints), running);
                }

                worldN = normalize(worldN + (float3(ripple.x, 0.0, ripple.y) * 0.3 * _NormalPower * (1.0 + (0.5 * running)) * saturate(worldN.y)));

                // What is behind, bent by the surface.
                float3 viewN = WorldToViewDirection(worldN);
                float2 bend = clamp(viewN.xy * _RefractionScale * 5.0, -_RefractionMax, _RefractionMax) * saturate(thickness / 0.3);
                float2 grab = (i.grab.xy / i.grab.w) + bend;
                float3 behind = tex2D(_FlowingWaterVolumeBehind, grab).rgb;

                // Foam where the froth gathers on the surface (a fall's foot, a jet's boil, a front): the sea's foam
                // texture laid over the water and dragged along with the flow (two phases, as the ripples), covering
                // the more of it the more froth there is (its lighter parts first, so it breaks up into lace at its
                // edges and closes into a sheet where the churn is), lit as the sea's foam is.
                // Fast thin water runs white (a fall's sheet, a rapid over a rock: air beaten into it); the froth's
                // foam gathers where the froth is.
                float rush = smoothstep(_RushFrom, _RushFull, length(found.xyz)) * (1.0 - smoothstep(0.4 * _RushThin, _RushThin, thickness));
                float churn = smoothstep(_FoamFrom, _FoamFull, water.z);
                // The foam's pattern: the texture at two scales (a tile 1.5 m across and one 0.4 m), the fine one
                // breaking the coarse one's blobs into lace, both dragged with the flow.
                float foamTiling = _NormalScale * 16.0;
                float2 fuv = worldPos.xz * foamTiling;
                float2 frun = current * foamTiling;
                float cycle = 2.0 * _FlowCycle;
                float fta = frac(_Time.y / cycle), ftb = frac((_Time.y / cycle) + 0.5);
                float fwa = 1.0 - abs((2.0 * fta) - 1.0), fwb = 1.0 - abs((2.0 * ftb) - 1.0);
                float2 pa = fuv - (frun * fta * cycle), pb = fuv - (frun * ftb * cycle) + 0.41;
                float coarse = (tex2D(_FoamTex, pa).r * fwa) + (tex2D(_FoamTex, pb).r * fwb);
                float fine = (tex2D(_FoamTex, pa * 4.0).r * fwa) + (tex2D(_FoamTex, (pb * 4.0) + 0.17).r * fwb);
                float lace = (0.5 * coarse) + (0.5 * fine);
                float foam = smoothstep(1.0 - churn - 0.2, 1.0 - churn + 0.2, lace) * sqrt(churn) * _FoamColor.a;
                // White water is whitened all over, the lace streaking it (a fall's sheet is white with streaks of
                // clear water, not a sheet of lace).
                foam = max(foam, rush * (0.5 + (0.5 * lace)) * _FoamColor.a);

                // Lit with Unity's standard lighting, as the bricks' water is (a surface shader on FlowingWaterLook.cginc's
                // Look, its alpha premultiplied): the same body and what is behind seen through it, the sea's smoothness
                // and metal, the sun, and the sky of the probe about the camera mirrored, so the two are one water where
                // they meet at the square's side. (Lit by a reckoning of its own, the sky a third of it at most, blurred,
                // and none on thin water, it was a green see-through square in a pond of sky.) Foam as the bricks' too.
                SurfaceOutputStandard so;
                so.Albedo = lerp(body, _FoamColor.rgb, foam);
                so.Normal = worldN;
                so.Emission = behind * through * _SurfaceColor.rgb * (1.0 - foam);
                so.Metallic = _Metallic * (1.0 - foam);
                // Thin water mirrors the sky less sharply, as the bricks' does (SHALLOW_GLOSS).
                so.Smoothness = lerp(_Glossiness * lerp(lerp(SHALLOW_GLOSS, 1.0, saturate(seen / SHALLOW_ABSORB)), 1.0, running), 0.1, foam);
                so.Occlusion = 1.0;
                // Thin water fades out into the scene (a splash's drops are not discs).
                so.Alpha = saturate(thickness / max(_EdgeThickness, 0.001));
                UnityGI gi;
                UNITY_INITIALIZE_OUTPUT(UnityGI, gi);
                gi.light.color = sunColour;
                gi.light.dir = sun;
                UnityGIInput lighting;
                UNITY_INITIALIZE_OUTPUT(UnityGIInput, lighting);
                lighting.light = gi.light;
                lighting.worldPos = worldPos;
                lighting.worldViewDir = toCamera;
                lighting.atten = 1.0;
                lighting.ambient = ShadeSH3Order(half4(worldN, 1.0));
                lighting.probeHDR[0] = unity_SpecCube0_HDR;
                lighting.probeHDR[1] = unity_SpecCube1_HDR;
#if defined(UNITY_SPECCUBE_BLENDING) || defined(UNITY_SPECCUBE_BOX_PROJECTION)
                lighting.boxMin[0] = unity_SpecCube0_BoxMin;
#endif
#ifdef UNITY_SPECCUBE_BOX_PROJECTION
                lighting.boxMax[0] = unity_SpecCube0_BoxMax;
                lighting.probePosition[0] = unity_SpecCube0_ProbePosition;
                lighting.boxMax[1] = unity_SpecCube1_BoxMax;
                lighting.boxMin[1] = unity_SpecCube1_BoxMin;
                lighting.probePosition[1] = unity_SpecCube1_ProbePosition;
#endif
                LightingStandard_GI(so, lighting, gi);
                half4 colour = LightingStandard(so, toCamera, gi);
                colour.rgb += so.Emission;

                // Fogged as the sea, toward the sun's colour where one looks at the sun (premultiplied: the fog's colour
                // as much as the water hides).
#if defined(FOG_LINEAR) || defined(FOG_EXP) || defined(FOG_EXP2)
                float toSun = saturate(dot(-toCamera, sun));
                float3 sunFog = pow(max(_SunFogColor.rgb, 0.0), 2.2);
                float3 fogColour = dot(_SunFogColor.rgb, 1.0) > 0.0 ? lerp(unity_FogColor.rgb, sunFog, toSun) : unity_FogColor.rgb;
                UNITY_CALC_FOG_FACTOR_RAW(eye);
                colour.rgb = lerp(fogColour * colour.a, colour.rgb, saturate(unityFogFactor));
#endif

                // At the square's side the water fades into the sheet's bricks beyond: all of it, its mirrored sky too
                // (premultiplied, the sky was whole to the very side, and the two waters' skies added up along it).
                return colour * found.w;
            }
            ENDCG
        }
    }

    FallBack Off
}
