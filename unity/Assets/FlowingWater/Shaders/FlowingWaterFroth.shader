// FlowingWater's froth: the volume water's spray, foam and bubbles (FlowingWaterVolume.compute's froth particles),
// drawn over the water as soft round sprites, six points a particle straight from the froth's buffer, after the water's
// surface (so foam shows on it): spray as drops that spread into a thin mist as they age and fade, foam as white
// specks riding the surface, bubbles as faint ones under it; lit by the sky and the sun as the sea's foam is, fading
// into what they are close to, fogged as the sea.
Shader "FlowingWater/Froth"
{
    Properties
    {
        // The sea's own (copied from the game's material at run time).
        _FoamColor ("Foam colour", Color) = (0.838, 0.838, 0.838, 1)

        // The froth's own.
        _DropRadius ("A droplet's radius (m)", Float) = 0.018
        _DropAlpha ("A droplet's opacity", Range(0, 1)) = 0.7
        _MistRadius ("A wisp of mist's radius when born (m)", Float) = 0.2
        _MistGrowth ("How many times its radius a wisp spreads to by the end of its life", Float) = 3
        _MistAlpha ("A wisp's opacity when born", Range(0, 1)) = 0.03
        _BubbleRadius ("A bubble's radius (m)", Float) = 0.015
        _BubbleAlpha ("A bubble's opacity", Range(0, 1)) = 0.3
    }

    SubShader
    {
        // After the particle water's surface (Transparent): on it.
        Tags { "Queue" = "Transparent+1" "RenderType" = "Transparent" "IgnoreProjector" = "True" "ForceNoShadowCasting" = "True" }

        Pass
        {
            Cull Off
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #pragma multi_compile_fog
            #include "UnityCG.cginc"

            // A froth particle as the compute shader leaves it: its place, its life left and born with, its own random
            // number and kind (predicted), its velocity.
            struct Particle
            {
                float4 place;
                float4 predicted;
                float4 velocity;
            };

            StructuredBuffer<Particle> _Particles;
            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
            fixed4 _FoamColor;
            float _DropRadius;
            float _DropAlpha;
            float _MistRadius;
            float _MistGrowth;
            float _MistAlpha;
            float _BubbleRadius;
            float _BubbleAlpha;
            #define MIST_FROM 0.55

            // The sun as the game sets it for every shader (none: the scene's light, as ParticleSurface found it).
            float4 _SunDir;
            float4 _SunFogColor;
            float4 _FWSunDir;
            float4 _FWSunColour;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 screen : TEXCOORD1;
                // The sprite's radius (m), its opacity, its distance along the view (m).
                float3 look : TEXCOORD2;
            };

            // A particle's quad facing the camera, its size and opacity by its kind and age.
            v2f vert(uint id : SV_VertexID)
            {
                v2f o;
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                Particle p = _Particles[id / 6];
                float3 centre = mul(UNITY_MATRIX_V, float4(p.place.xyz, 1.0)).xyz;
                float life = p.predicted.x;
                float born = max(p.predicted.y, 0.01);
                float age = saturate(1.0 - (life / born));
                float own = p.predicted.z;
                float kind = p.predicted.w;
                // Spray in the air: a droplet (small, bright, falling) or a wisp of mist (large, faint, spreading as it
                // fades) by its own number. Foam on the surface is the surface's own to draw (a pattern of foam over the
                // water, from the froth in each cell): no sprite. A bubble under the water: a tiny faint one.
                float radius;
                float alpha;
                if (kind < 0.5 && own < MIST_FROM)
                {
                    radius = _DropRadius * lerp(0.7, 1.3, own / MIST_FROM);
                    alpha = _DropAlpha * (1.0 - (age * age));
                }
                else if (kind < 0.5)
                {
                    float wisp = (own - MIST_FROM) / (1.0 - MIST_FROM);
                    radius = _MistRadius * lerp(0.7, 1.3, wisp) * lerp(1.0, _MistGrowth, age);
                    alpha = _MistAlpha * (1.0 - age) * (1.0 - age);
                }
                else if (kind < 1.5)
                {
                    radius = 0.0;
                    alpha = 0.0;
                }
                else
                {
                    radius = _BubbleRadius * lerp(0.7, 1.3, own);
                    alpha = _BubbleAlpha * saturate(life / 0.3);
                }

                if (asuint(p.place.w) == 0xFFFFFFFFu || -centre.z < 0.3 || alpha <= 0.0)
                {
                    // Gone, at the camera, or faded out: every point in one place, behind the camera.
                    o.pos = float4(0.0, 0.0, -2.0, 1.0);
                    return o;
                }

                const float2 corners[6] = { float2(-1.0, -1.0), float2(1.0, -1.0), float2(1.0, 1.0), float2(-1.0, -1.0), float2(1.0, 1.0), float2(-1.0, 1.0) };
                float2 q = corners[id % 6];
                float3 corner = centre + float3(q * radius, 0.0);
                o.pos = mul(UNITY_MATRIX_P, float4(corner, 1.0));
                o.uv = q;
                o.screen = ComputeScreenPos(o.pos);
                o.look = float3(radius, alpha, -centre.z);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float r2 = dot(i.uv, i.uv);
                if (r2 > 1.0)
                {
                    discard;
                }

                // Soft and round (a wisp of mist softer still: no disc to be seen, only the haze the wisps add up to);
                // softer again against what is just behind it (no hard line where it meets the ground).
                float soft = (1.0 - r2) * (1.0 - r2);
                soft = i.look.x > 0.1 ? soft * (1.0 - r2) : soft;
                float2 screen = i.screen.xy / max(i.screen.w, 0.0001);
                float behind = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, screen));
                float near = saturate((behind - i.look.z) / max(i.look.x, 0.01));
                // Nor a wall of drops over the eye: within a metre of the camera they thin out.
                float close = saturate((i.look.z - 0.3) / 1.0);
                float alpha = i.look.y * soft * near * close;

                // Lit as the sea's foam: the sky's light from above and the sun's by how high it stands.
                float3 sun = dot(_SunDir.xyz, _SunDir.xyz) > 0.01 ? normalize(_SunDir.xyz) : normalize(_FWSunDir.xyz);
                float3 ambient = ShadeSH9(float4(0.0, 1.0, 0.0, 1.0));
                float3 lit = ambient + (_FWSunColour.rgb * saturate(0.4 + (0.6 * sun.y)));
                float3 colour = _FoamColor.rgb * lit;

                // Fogged as the sea, toward the sun's colour where one looks at the sun.
#if defined(FOG_LINEAR) || defined(FOG_EXP) || defined(FOG_EXP2)
                // (The view's own direction, world: the sprite's is as near as makes no difference.)
                float toSun = saturate(dot(-UNITY_MATRIX_V[2].xyz, sun));
                float3 sunFog = pow(max(_SunFogColor.rgb, 0.0), 2.2);
                float3 fogColour = dot(_SunFogColor.rgb, 1.0) > 0.0 ? lerp(unity_FogColor.rgb, sunFog, toSun) : unity_FogColor.rgb;
                UNITY_CALC_FOG_FACTOR_RAW(i.look.z);
                colour = lerp(fogColour, colour, saturate(unityFogFactor));
#endif

                return fixed4(colour, alpha);
            }
            ENDCG
        }
    }

    FallBack Off
}
