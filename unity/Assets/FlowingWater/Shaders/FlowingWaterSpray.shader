// FlowingWater's falls, splashes and spray near the camera (FlowingWaterSpray.compute): each particle a soft quad
// facing the camera, stretched along its motion (at most _MostStretch each way), drawn straight from the particles'
// buffer (six points a particle, none for one whose life is over). Sheets of falling water take the water's colour, droplets and splashes the foam's
// (the sea's, copied onto this material when the game runs), lit by the sky and the sun, fogged as the water is.
Shader "FlowingWater/Spray"
{
    Properties
    {
        _FoamTex ("Foam", 2D) = "white" {}
        _FoamColor ("Foam colour", Color) = (0.838, 0.838, 0.838, 1)
        _FallColor ("Falling water colour", Color) = (0.62, 0.74, 0.78, 1)
        _FallAlpha ("Falling water opacity", Range(0, 1)) = 0.3
        _FoamAlpha ("Droplets' opacity", Range(0, 1)) = 0.35
        _Stretch ("Stretch along the motion (s of it), droplets", Float) = 0.015
        _FallStretch ("Stretch along the motion (s of it), falling water", Float) = 0.12
        _MostStretch ("Stretch at most, each way along the motion (m)", Float) = 0.5
    }

    SubShader
    {
        // With the water, just after it: what is behind it is the water.
        Tags { "Queue" = "Transparent-100" "RenderType" = "Transparent" "IgnoreProjector" = "True" "ForceNoShadowCasting" = "True" }
        ZWrite Off
        Cull Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Tags { "LightMode" = "ForwardBase" }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #pragma multi_compile_fog
            #include "UnityCG.cginc"

            struct Particle
            {
                float4 placeAge;
                float4 velocityLife;
                float4 sizeKind;
            };

            StructuredBuffer<Particle> _Particles;
            sampler2D _FoamTex;
            fixed4 _FoamColor;
            fixed4 _FallColor;
            float _FallAlpha;
            float _FoamAlpha;
            float _Stretch;
            float _FallStretch;
            float _MostStretch;

            // A particle over ground not known ends after this long there (s), fading out: FlowingWaterSpray.compute's.
            #define UNKNOWN_LIFE 0.25

            fixed4 _LightColor0;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                fixed4 tint : TEXCOORD1;
                UNITY_FOG_COORDS(2)
            };

            v2f vert(uint id : SV_VertexID)
            {
                v2f o;
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                Particle p = _Particles[id / 6];
                float age = p.placeAge.w;
                float life = p.velocityLife.w;
                if (age >= life)
                {
                    // Over: every point in one place, behind the camera (nothing drawn).
                    o.pos = float4(0.0, 0.0, -2.0, 1.0);
                    return o;
                }

                const float2 corners[6] = { float2(-1.0, -1.0), float2(1.0, -1.0), float2(1.0, 1.0), float2(-1.0, -1.0), float2(1.0, 1.0), float2(-1.0, 1.0) };
                float2 q = corners[id % 6];
                float3 place = p.placeAge.xyz;
                float3 velocity = p.velocityLife.xyz;
                float speed = length(velocity);
                float3 view = normalize(place - _WorldSpaceCameraPos);
                float3 along = speed > 0.05 ? velocity / speed : float3(0.0, 1.0, 0.0);
                float3 side = cross(view, along);
                side = dot(side, side) > 1e-4 ? normalize(side) : UNITY_MATRIX_V[0].xyz;
                along = normalize(cross(side, view));
                along *= dot(along, velocity) < 0.0 ? -1.0 : 1.0;
                float size = p.sizeKind.x;
                bool sheet = p.sizeKind.y > 0.5 && p.sizeKind.y < 1.5;
                // Stretched with its speed, but never past _MostStretch: water falling fast, or thrown fast off a lip, is
                // drawn a streak, not a line across the view.
                float stretch = min(speed * (sheet ? _FallStretch : _Stretch), _MostStretch);
                float3 world = place + (side * (q.x * size)) + (along * (q.y * (size + stretch)));
                o.pos = UnityWorldToClipPos(world);
                o.uv = q;

                // Fading in as it is thrown and out as its life ends, or as it flies over ground not known (its time
                // there, over UNKNOWN_LIFE).
                float fade = saturate((life - age) * 4.0) * saturate(age * 20.0) * saturate(1.0 - (p.sizeKind.w / UNKNOWN_LIFE));
                float3 light = ShadeSH9(float4(0.0, 1.0, 0.0, 1.0)) + (_LightColor0.rgb * 0.6);
                o.tint = sheet ? fixed4(_FallColor.rgb * light, _FallAlpha * fade) : fixed4(_FoamColor.rgb * light, _FoamAlpha * fade);
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float r = dot(i.uv, i.uv);
                if (r > 1.0)
                {
                    discard;
                }

                fixed4 colour = i.tint;
                colour.a *= (1.0 - r) * lerp(0.6, 1.0, tex2D(_FoamTex, (i.uv * 0.5) + 0.5).r);
                UNITY_APPLY_FOG(i.fogCoord, colour);
                return colour;
            }
            ENDCG
        }
    }

    FallBack Off
}
