// FlowingWater's falls, splashes and spray near the camera (FlowingWaterSpray.compute): each particle a soft quad
// facing the camera, stretched along its motion (at most _MostStretch each way), drawn straight from the particles'
// buffer (six points a particle, none for one whose life is over). Sheets of falling water take the water's colour, droplets and splashes the foam's
// (the sea's, copied onto this material when the game runs), lit by the sky and the sun, fogged as the water is. With
// the froth on: foam is a patch lying on the water, laced by the foam texture, fading as it goes; a bubble a tiny faint
// dot in the water; spray in a crowd (many in its half metre, FlowingWaterSpray.compute's crowd count) is mist, wider
// and fainter the more it is crowded, so a flood's spray is a haze, not so many bright droplets.
Shader "FlowingWater/Spray"
{
    Properties
    {
        _FoamTex ("Foam", 2D) = "white" {}
        _FoamColor ("Foam colour", Color) = (0.838, 0.838, 0.838, 1)
        _FallColor ("Falling water colour", Color) = (0.62, 0.74, 0.78, 1)
        _FallAlpha ("Falling water opacity", Range(0, 1)) = 0.2
        _CarrierAlpha ("Share of it kept by the sheets carrying a lip's water (its curtain draws the fall)", Range(0, 1)) = 0.3
        _FoamAlpha ("Droplets' opacity", Range(0, 1)) = 0.35
        _Stretch ("Stretch along the motion (s of it), droplets", Float) = 0.015
        _FallStretch ("Stretch along the motion (s of it), falling water", Float) = 0.12
        _MostStretch ("Stretch at most, each way along the motion (m)", Float) = 0.5
        _FoamPatchAlpha ("Foam patches' opacity", Range(0, 1)) = 0.55
        _FoamPatchScale ("Foam patches' lace, repeats a metre", Float) = 1.5
        _BubbleAlpha ("Bubbles' opacity", Range(0, 1)) = 0.25
        _MistFrom ("Spray in a crowd of more than this is mist", Float) = 4
        _MistFull ("Wholly mist from this many more", Float) = 12
        _MistGrow ("Mist's size over a droplet's", Float) = 4
        _MistAlpha ("Mist's opacity over a droplet's", Range(0, 1)) = 0.3
        _FlowCycle ("Running water's phases (s), as the water's: the long waves foam rides", Float) = 1.5
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
            #include "FlowingWaterWaves.cginc"
            #define FLOWINGWATER_SIM_MASK_PLAIN
            #include "FlowingWaterSimMask.cginc"

            struct Particle
            {
                float4 placeAge;
                float4 velocityLife;
                float4 sizeKind;
            };

            StructuredBuffer<Particle> _Particles;
            // FlowingWaterSpray.compute's crowd count: its hashed cells of CROWD_SIZE metres, CROWD_CELLS of them.
            StructuredBuffer<uint> _Crowd;
            // The card's water under the particles (SprayParticles: its depth, where its cells start and their size and
            // count), so foam rides the long waves the water's surface is drawn with (FlowingWaterWaves.cginc).
            Texture2D<float> _SprayWaterDepth;
            Texture2D<float> _SprayWaterRipple;
            float2 _SprayWaterOrigin;
            float _SprayWaterCell;
            int _SprayWaterCells;
            #define CROWD_CELLS 65536u
            #define CROWD_SIZE 0.5
            int _Froth;
            float _FoamPatchAlpha;
            float _FoamPatchScale;
            float _BubbleAlpha;
            float _MistFrom;
            float _MistFull;
            float _MistGrow;
            float _MistAlpha;
            sampler2D _FoamTex;
            fixed4 _FoamColor;
            fixed4 _FallColor;
            float _FallAlpha;
            float _CarrierAlpha;
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
                // x 1 for a foam patch (laced by the foam texture at the world's yz here), else 0.
                float3 lace : TEXCOORD3;
                UNITY_FOG_COORDS(2)
            };

            // The card's water's depth at a world place (none past its square).
            float WaterDepthAt(float2 xz)
            {
                int2 c = (int2)floor((xz - _SprayWaterOrigin) / _SprayWaterCell);
                return all(c >= 0) && all(c < _SprayWaterCells) ? _SprayWaterDepth.Load(int3(c, 0)) : 0.0;
            }

            // The ripples the bodies push on the card's water at a world place (SimGrid.Ripple; none past its square).
            float WaterRippleAt(float2 xz)
            {
                int2 c = (int2)floor((xz - _SprayWaterOrigin) / _SprayWaterCell);
                return all(c >= 0) && all(c < _SprayWaterCells) ? _SprayWaterRipple.Load(int3(c, 0)) : 0.0;
            }

            uint CrowdIndex(float3 place)
            {
                int3 k = (int3)floor(place / CROWD_SIZE);
                return ((uint)(k.x * 73856093) ^ (uint)(k.y * 19349663) ^ (uint)(k.z * 83492791)) % CROWD_CELLS;
            }

            v2f vert(uint id : SV_VertexID)
            {
                v2f o;
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                Particle p = _Particles[id / 6];
                float age = p.placeAge.w;
                float life = p.velocityLife.w;
                if (age >= life || VolumeDraws(p.placeAge.xyz))
                {
                    // Over, or in the volume water's box (it throws its own): every point in one place, behind the
                    // camera (nothing drawn).
                    o.pos = float4(0.0, 0.0, -2.0, 1.0);
                    return o;
                }

                const float2 corners[6] = { float2(-1.0, -1.0), float2(1.0, -1.0), float2(1.0, 1.0), float2(-1.0, -1.0), float2(1.0, 1.0), float2(-1.0, 1.0) };
                float2 q = corners[id % 6];
                float3 place = p.placeAge.xyz;
                float3 velocity = p.velocityLife.xyz;
                // Foam and bubbles ride the long waves the water's surface is raised by (their velocity is the water's
                // current) and the ripples bodies push, on top of the level they are held to: not seen over a crest they
                // lie under.
                float waveKind = p.sizeKind.y;
                float crest;
                place.y += waveKind > 2.5 ? LongWaves(place.xz, velocity.xz, WaterDepthAt(place.xz), 1.0, crest).x + WaterRippleAt(place.xz) : 0.0;
                float speed = length(velocity);
                float3 view = normalize(place - _WorldSpaceCameraPos);
                float3 along = speed > 0.05 ? velocity / speed : float3(0.0, 1.0, 0.0);
                float3 side = cross(view, along);
                side = dot(side, side) > 1e-4 ? normalize(side) : UNITY_MATRIX_V[0].xyz;
                along = normalize(cross(side, view));
                along *= dot(along, velocity) < 0.0 ? -1.0 : 1.0;
                float size = p.sizeKind.x;
                float kind = p.sizeKind.y;
                bool sheet = kind > 0.5 && kind < 1.5;
                bool foam = kind > 2.5 && kind < 3.5;
                bool bubble = kind > 3.5;
                // Spray in a crowd is mist: wider, fainter, barely stretched.
                float mist = _Froth > 0 && !sheet && !foam && !bubble ? saturate(((float)_Crowd[CrowdIndex(place)] - _MistFrom) / _MistFull) : 0.0;
                size *= lerp(1.0, _MistGrow, mist);
                // Stretched with its speed, but never past _MostStretch: water falling fast, or thrown fast off a lip, is
                // drawn a streak, not a line across the view.
                float stretch = min(speed * (sheet ? _FallStretch : _Stretch), _MostStretch) * (1.0 - (0.7 * mist));
                float3 world = place + (side * (q.x * size)) + (along * (q.y * (size + stretch)));
                // Foam lies on the water: a patch along the ground, just over the top.
                world = foam ? place + float3(q.x * size, 0.01, q.y * size) : world;
                o.pos = UnityWorldToClipPos(world);
                o.uv = q;
                o.lace = float3(foam ? 1.0 : 0.0, world.xz * _FoamPatchScale);

                // Fading in as it is thrown and out as its life ends (foam over its last second), or as it flies over
                // ground not known (its time there, over UNKNOWN_LIFE).
                float fade = saturate((life - age) * (foam ? 1.0 : 4.0)) * saturate(age * 20.0) * saturate(1.0 - (p.sizeKind.w / UNKNOWN_LIFE));
                float3 light = ShadeSH9(float4(0.0, 1.0, 0.0, 1.0)) + (_LightColor0.rgb * 0.6);
                // A sheet carrying a lip's water is that fall's mist: its curtain (FlowingWaterFall.shader) draws the fall.
                float fallAlpha = _FallAlpha * (p.sizeKind.z > 0.0 ? _CarrierAlpha : 1.0);
                float alpha = foam ? _FoamPatchAlpha : bubble ? _BubbleAlpha : _FoamAlpha * lerp(1.0, _MistAlpha, mist);
                o.tint = sheet ? fixed4(_FallColor.rgb * light, fallAlpha * fade) : fixed4(_FoamColor.rgb * light, alpha * fade);
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
                // Foam: the texture's lace where it lies on the water, thinning out to the patch's rim.
                float lace = tex2D(_FoamTex, i.lace.yz).r;
                colour.a *= i.lace.x > 0.5 ? saturate((lace * 1.6) - (r * 0.9)) : (1.0 - r) * lerp(0.6, 1.0, tex2D(_FoamTex, (i.uv * 0.5) + 0.5).r);
                UNITY_APPLY_FOG(i.fogCoord, colour);
                return colour;
            }
            ENDCG
        }
    }

    FallBack Off
}
