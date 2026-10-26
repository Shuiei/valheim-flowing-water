// FlowingWater's particle water (FlowingWaterParticles.compute), drawn as water: a screen-space surface over the
// particles (van der Laan, Green and Sainz, 2009), run by ParticleSurface.cs through a command buffer each frame:
//   Depth      each particle a sphere facing the camera (six points a particle, straight from the particles' buffer),
//              the nearest's distance kept at each pixel, none behind the scene,
//   Thickness  how much water the view goes through at each pixel: the spheres' chords added up, with how much of it is
//              air (thin water: spray, the surface),
//   Blur       the distances smoothed along one direction, keeping steps (a bilateral blur), so the spheres melt into
//              one surface,
//   Spheres    the particles as opaque spheres, for testing (the console's "flowingwater particles spheres").
// FlowingWaterParticleSurface.shader then draws the water over the scene from the two textures.
Shader "FlowingWater/Particles"
{
    Properties
    {
        // The sea's own (copied from the game's material at run time), for the spheres.
        _FoamColor ("Foam colour", Color) = (0.838, 0.838, 0.838, 1)
        _Glossiness ("Smoothness", Range(0, 1)) = 0.94

        // The flowing water's own (as FlowingWater.shader's).
        // The particles' own.
        _Size ("A particle's drawn radius (m)", Float) = 0.08
        _BlurRadius ("The surface smoothed over this far (m)", Float) = 0.4
        _BlurFalloff ("A step in distance this deep (m) is kept, not smoothed over", Float) = 0.25
        _SphereColor ("Spheres' colour (testing)", Color) = (0.2, 0.45, 0.6, 1)
    }

    CGINCLUDE
    #include "UnityCG.cginc"

    struct Particle
    {
        float4 place;
        float4 predicted;
        float4 velocity;
    };

    StructuredBuffer<Particle> _Particles;
    float _Size;
    UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);

    // The sun as the game sets it for every shader (none: the scene's light), and its colour as ParticleSurface sets it.
    float4 _SunDir;
    float4 _SunFogColor;
    float4 _FWSunDir;
    float4 _FWSunColour;

    struct sphere
    {
        float4 pos : SV_POSITION;
        float2 uv : TEXCOORD0;
        float3 centre : TEXCOORD1;
        float density : TEXCOORD2;
        float4 screen : TEXCOORD3;
    };

    // A particle's quad in view space, a little larger than its sphere so the silhouette is whole at the edge.
    sphere SphereVertex(uint id)
    {
        sphere o;
        UNITY_INITIALIZE_OUTPUT(sphere, o);
        Particle p = _Particles[id / 6];
        const float2 corners[6] = { float2(-1.0, -1.0), float2(1.0, -1.0), float2(1.0, 1.0), float2(-1.0, -1.0), float2(1.0, 1.0), float2(-1.0, 1.0) };
        float2 q = corners[id % 6];
        float3 centre = mul(UNITY_MATRIX_V, float4(p.place.xyz, 1.0)).xyz;
        float3 corner = centre + float3(q * _Size * 1.1, 0.0);
        o.pos = mul(UNITY_MATRIX_P, float4(corner, 1.0));
        o.uv = q * 1.1;
        o.centre = centre;
        o.density = p.velocity.w;
        o.screen = ComputeScreenPos(o.pos);
        return o;
    }

    // The sphere's surface at a pixel of its quad, in view space (toward the camera: view space looks down -z), its
    // normal there, and its distance from the camera along the view; false off the sphere or behind the scene.
    bool SphereSurface(sphere i, out float3 surface, out float3 normal, out float eye)
    {
        surface = 0.0;
        normal = 0.0;
        eye = 0.0;
        float r2 = dot(i.uv, i.uv);
        if (r2 > 1.0)
        {
            return false;
        }

    	normal = float3(i.uv, sqrt(1.0 - r2));
        surface = i.centre + (normal * _Size);
        eye = -surface.z;
        float2 screen = i.screen.xy / max(i.screen.w, 0.0001);
        return eye < LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, screen));
    }

    // A view-space normal or place as the world has it (Unity's camera-to-world looks down +z).
    float3 ViewToWorldDirection(float3 v)
    {
        return normalize(mul((float3x3)unity_CameraToWorld, float3(v.x, v.y, -v.z)));
    }

    float3 ViewToWorldPlace(float3 v)
    {
        return mul(unity_CameraToWorld, float4(v.x, v.y, -v.z, 1.0)).xyz;
    }

    // The sun's direction (toward it) and colour: the game's, else the scene's light's as ParticleSurface found it.
    float3 SunDirection()
    {
        return dot(_SunDir.xyz, _SunDir.xyz) > 0.01 ? normalize(_SunDir.xyz) : normalize(_FWSunDir.xyz);
    }
    ENDCG

    SubShader
    {
        Tags { "Queue" = "Geometry" "RenderType" = "Opaque" }
        Cull Off
        ZWrite Off
        ZTest Always

        // 0: the nearest particle's distance along the view at each pixel (0 where none).
        Pass
        {
            Name "Depth"
            ZWrite On
            ZTest LEqual
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5

            sphere vert(uint id : SV_VertexID)
            {
                return SphereVertex(id);
            }

            struct Fragment
            {
                float4 colour : SV_Target;
                float depth : SV_Depth;
            };

            Fragment frag(sphere i)
            {
                float3 surface;
                float3 normal;
                float eye;
                if (!SphereSurface(i, surface, normal, eye))
                {
                    discard;
                }

                float4 clip = mul(UNITY_MATRIX_P, float4(surface, 1.0));
                Fragment o;
                o.depth = clip.z / clip.w;
                o.colour = float4(eye, 0.0, 0.0, 1.0);
                return o;
            }
            ENDCG
        }

        // 1: the water the view goes through at each pixel (m), and how much of it is air (m): the spheres' chords
        // added up, a particle's chord counted as air where its water is thin (the surface, spray).
        Pass
        {
            Name "Thickness"
            Blend One One
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5

            sphere vert(uint id : SV_VertexID)
            {
                return SphereVertex(id);
            }

            float4 frag(sphere i) : SV_Target
            {
                float3 surface;
                float3 normal;
                float eye;
                if (!SphereSurface(i, surface, normal, eye))
                {
                    discard;
                }

                float chord = 2.0 * _Size * normal.z;
                float air = 1.0 - saturate((i.density - 0.4) / 0.4);
                return float4(chord, chord * air, 0.0, 0.0);
            }
            ENDCG
        }

        // 2: the distances smoothed along _BlurDirection (texels), the blur as wide on the screen as _BlurRadius metres
        // are at that distance, keeping steps deeper than _BlurFalloff (one body of water in front of another).
        Pass
        {
            Name "Blur"
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 4.5

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float2 _BlurDirection;
            float _BlurRadius;
            float _BlurFalloff;

            #define TAPS 12

            float4 frag(v2f_img i) : SV_Target
            {
                float centre = tex2D(_MainTex, i.uv).r;
                if (centre <= 0.0)
                {
                    return 0.0;
                }

                // Pixels across the radius at this distance: the projection's scale, in pixels per metre at 1 m.
                float scale = unity_CameraProjection._m11 * _ScreenParams.y * 0.5;
                float radius = clamp(_BlurRadius * scale / centre, 1.0, 24.0);
                float2 step = _BlurDirection * _MainTex_TexelSize.xy * (radius / TAPS);
                float sum = centre;
                float weights = 1.0;
                for (int k = 1; k <= TAPS; k++)
                {
                    float spatial = exp(-2.0 * (float(k) / TAPS) * (float(k) / TAPS));
                    float a = tex2D(_MainTex, i.uv + (step * k)).r;
                    float b = tex2D(_MainTex, i.uv - (step * k)).r;
                    float da = (a - centre) / _BlurFalloff;
                    float db = (b - centre) / _BlurFalloff;
                    float wa = a > 0.0 ? spatial * exp(-da * da) : 0.0;
                    float wb = b > 0.0 ? spatial * exp(-db * db) : 0.0;
                    sum += (a * wa) + (b * wb);
                    weights += wa + wb;
                }

                return float4(sum / weights, 0.0, 0.0, 1.0);
            }
            ENDCG
        }

        // 3: the particles as opaque, lit spheres (testing).
        Pass
        {
            Name "Spheres"
            ZWrite On
            ZTest LEqual
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #pragma multi_compile_fog

            fixed4 _SphereColor;
            fixed4 _FoamColor;
            float _Glossiness;

            sphere vert(uint id : SV_VertexID)
            {
                return SphereVertex(id);
            }

            struct Fragment
            {
                fixed4 colour : SV_Target;
                float depth : SV_Depth;
            };

            Fragment frag(sphere i)
            {
                float3 surface;
                float3 normal;
                float eye;
                float r2 = dot(i.uv, i.uv);
                if (r2 > 1.0)
                {
                    discard;
                }

                normal = float3(i.uv, sqrt(1.0 - r2));
                surface = i.centre + (normal * _Size);
                float4 clip = mul(UNITY_MATRIX_P, float4(surface, 1.0));
                Fragment o;
                o.depth = clip.z / clip.w;
                float3 worldNormal = ViewToWorldDirection(normal);
                float3 sun = SunDirection();
                float3 lit = ShadeSH9(float4(worldNormal, 1.0)) + (_FWSunColour.rgb * saturate(dot(worldNormal, sun)));
                float3 toCamera = normalize(_WorldSpaceCameraPos - ViewToWorldPlace(surface));
                float spec = pow(saturate(dot(worldNormal, normalize(sun + toCamera))), 48.0) * _Glossiness;
                fixed3 colour = lerp(_FoamColor.rgb, _SphereColor.rgb, saturate((i.density - 0.4) / 0.4));
                o.colour = fixed4((colour * lit) + (_FWSunColour.rgb * spec), 1.0);
                return o;
            }
            ENDCG
        }
    }

    FallBack Off
}
