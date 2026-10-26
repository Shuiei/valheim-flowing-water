// FlowingWater's particle water (FlowingWaterParticles.compute), a prototype's look: each particle a sphere facing the
// camera (a quad, shaded and given a sphere's depth), drawn straight from the particles' buffer (six points a
// particle); thin water (the surface, spray) whiter, as foam. Opaque: the shape of the water, not its look yet.
Shader "FlowingWater/Particles"
{
    Properties
    {
        _WaterColor ("Water colour", Color) = (0.2, 0.45, 0.6, 1)
        _FoamColor ("Foam colour", Color) = (0.85, 0.9, 0.92, 1)
        _Size ("A particle's drawn radius (m)", Float) = 0.065
        _Glossiness ("Smoothness", Range(0, 1)) = 0.6
    }

    SubShader
    {
        Tags { "Queue" = "Geometry" "RenderType" = "Opaque" }
        Cull Off

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
                float4 place;
                float4 predicted;
                float4 velocity;
            };

            StructuredBuffer<Particle> _Particles;
            fixed4 _WaterColor;
            fixed4 _FoamColor;
            float _Size;
            float _Glossiness;
            fixed4 _LightColor0;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 centre : TEXCOORD1;
                float density : TEXCOORD2;
                UNITY_FOG_COORDS(3)
            };

            v2f vert(uint id : SV_VertexID)
            {
                v2f o;
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                Particle p = _Particles[id / 6];
                const float2 corners[6] = { float2(-1.0, -1.0), float2(1.0, -1.0), float2(1.0, 1.0), float2(-1.0, -1.0), float2(1.0, 1.0), float2(-1.0, 1.0) };
                float2 q = corners[id % 6];
                float3 centre = mul(UNITY_MATRIX_V, float4(p.place.xyz, 1.0)).xyz;
                // The quad in view space, a little larger than the sphere so its silhouette is whole at the edge.
                float3 corner = centre + float3(q * _Size * 1.1, 0.0);
                o.pos = mul(UNITY_MATRIX_P, float4(corner, 1.0));
                o.uv = q * 1.1;
                o.centre = centre;
                o.density = p.velocity.w;
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            struct Fragment
            {
                fixed4 colour : SV_Target;
                float depth : SV_Depth;
            };

            Fragment frag(v2f i)
            {
                float r2 = dot(i.uv, i.uv);
                if (r2 > 1.0)
                {
                    discard;
                }

                // The sphere's surface here, in view space (toward the camera: view space looks down -z).
                float3 normal = float3(i.uv, sqrt(1.0 - r2));
                float3 surface = i.centre + (normal * _Size);
                float4 clip = mul(UNITY_MATRIX_P, float4(surface, 1.0));
                Fragment o;
                o.depth = clip.z / clip.w;

                float3 worldNormal = normalize(mul((float3x3)unity_CameraToWorld, normal));
                float3 light = normalize(_WorldSpaceLightPos0.xyz);
                float3 lit = (ShadeSH9(float4(worldNormal, 1.0)) + (_LightColor0.rgb * saturate(dot(worldNormal, light))));
                float3 toCamera = normalize(_WorldSpaceCameraPos - mul(unity_CameraToWorld, float4(surface, 1.0)).xyz);
                float spec = pow(saturate(dot(worldNormal, normalize(light + toCamera))), 48.0) * _Glossiness;
                fixed3 colour = lerp(_FoamColor.rgb, _WaterColor.rgb, saturate((i.density - 0.4) / 0.4));
                o.colour = fixed4((colour * lit) + (_LightColor0.rgb * spec), 1.0);
                UNITY_APPLY_FOG(i.fogCoord, o.colour);
                return o;
            }
            ENDCG
        }
    }

    FallBack Off
}
