// FlowingWater's falls near the camera as curtains of water (FlowingWaterSpray.compute's Curtains): for each face the
// card's water runs out of over a lip, a strip from the cell's centre over to the face, then down the arc its water is
// thrown on to where it lands, the strips of a lip side by side one curtain (drawn straight from the curtains' buffer,
// as many as there are this frame, never read back). Glassy where it leaves the lip, it breaks up white as it falls,
// streaked along its fall, the streaks running down with the water; a long fall frays into streaks near its foot. Its
// ends along the lip, where no curtain is beside, spread a little and fade out. Lit by the sky and the sun, fogged as
// the water is. The water it carries is the spray's sheets' (drawn faint, its mist).
Shader "FlowingWater/Fall"
{
    Properties
    {
        // The sea's own (copied from the game's material at run time).
        _FoamTex ("Foam", 2D) = "white" {}
        _FoamColor ("Foam colour", Color) = (0.838, 0.838, 0.838, 1)

        _FallColor ("Falling water colour", Color) = (0.62, 0.74, 0.78, 1)
        _GlassAlpha ("Opacity where it leaves the lip", Range(0, 1)) = 0.35
        _WhiteAlpha ("Opacity once broken white", Range(0, 1)) = 0.8
        _Aerate ("Seconds of falling till it is all white", Float) = 0.5
        _FullFlow ("Water running over (m2/s, depth times speed) that makes a whole curtain", Float) = 0.05
        _StreakAcross ("Streaks across the curtain (per metre)", Float) = 2.5
        _StreakAlong ("Streaks along the fall (per second of it)", Float) = 1.2
        _Fray ("Share of the fall from which it frays into streaks", Range(0, 1)) = 0.65
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

            // As FlowingWaterSpray.compute's.
            struct Curtain
            {
                float4 start;
                float4 velocity;
                float4 across;
                float4 ends;
            };

            // Rows along a strip: the cell's centre, then the arc from the face to where it lands (SprayParticles draws
            // ROWS - 1 quads a curtain).
            #define ROWS 20
            #define GRAVITY 9.81

            // An open end spreads this much each way for each second fallen (m).
            #define SPREAD 0.15

            StructuredBuffer<Curtain> _Curtains;
            uint _MostCurtains;
            sampler2D _FoamTex;
            fixed4 _FoamColor;
            fixed4 _FallColor;
            float _GlassAlpha;
            float _WhiteAlpha;
            float _Aerate;
            float _FullFlow;
            float _StreakAcross;
            float _StreakAlong;
            float _Fray;
            fixed4 _LightColor0;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 strip : TEXCOORD0; // across (-1 to 1), seconds fallen, share of the fall, along the lip (m)
                float4 water : TEXCOORD1; // how whole (by the water running over), open ends (1 left, 2 right), lands
                fixed3 light : TEXCOORD2;
                UNITY_FOG_COORDS(3)
            };

            v2f vert(uint id : SV_VertexID, uint instance : SV_InstanceID)
            {
                v2f o;
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                if (instance >= _MostCurtains)
                {
                    // Counted past the buffer: every point in one place, behind the camera (nothing drawn).
                    o.pos = float4(0.0, 0.0, -2.0, 1.0);
                    return o;
                }

                Curtain c = _Curtains[instance];
                const float2 corners[6] = { float2(-1.0, 0.0), float2(1.0, 0.0), float2(1.0, 1.0), float2(-1.0, 0.0), float2(1.0, 1.0), float2(-1.0, 1.0) };
                float2 q = corners[id % 6];
                uint row = (id / 6) + (uint)q.y;

                float fall = c.start.w;
                float3 run = c.velocity.xyz;
                float speed = max(length(run), 0.001);
                float3 along = run / speed;
                float3 across = float3(c.across.x, 0.0, c.across.y);
                float width = c.across.z;
                uint open = (uint)c.across.w;

                // The first row at the cell's centre, the rest down the arc from the face, closer together near its top.
                float share = row == 0 ? 0.0 : pow((row - 1.0) / (ROWS - 2.0), 1.5);
                float t = share * fall;
                float3 place = row == 0
                    ? c.start.xyz - (along * (0.5 * width))
                    : c.start.xyz + (run * t) - float3(0.0, 0.5 * GRAVITY * t * t, 0.0);
                bool openHere = (q.x > 0.0 && (open & 1u) != 0u) || (q.x < 0.0 && (open & 2u) != 0u);
                place += across * (q.x * ((0.5 * width) + (openHere ? SPREAD * t : 0.0)));
                o.pos = UnityWorldToClipPos(place);

                // Lit as a sheet facing the eye: its normal across its way down.
                float3 down = normalize(run - float3(0.0, GRAVITY * t, 0.0));
                float3 normal = normalize(cross(across, down));
                normal *= dot(normal, _WorldSpaceCameraPos - place) < 0.0 ? -1.0 : 1.0;
                o.light = ShadeSH9(float4(normal, 1.0)) + (_LightColor0.rgb * (0.35 + (0.65 * saturate(dot(normal, _WorldSpaceLightPos0.xyz)))));

                float lip = dot(c.start.xz, c.across.xy);
                o.strip = float4(q.x, t, share, lip + (q.x * 0.5 * width));
                o.water = float4(saturate(c.velocity.w / max(_FullFlow, 0.0001)), open, c.ends.y, 0.0);
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float t = i.strip.y;
                float share = i.strip.z;

                // Streaks along the fall, running down with the water (a point of the pattern is the water that left the
                // lip at one moment), two layers.
                float2 uv = float2(i.strip.w * _StreakAcross, (_Time.y - t) * _StreakAlong);
                float streak = 0.6 * tex2D(_FoamTex, uv).r + (0.4 * tex2D(_FoamTex, (uv * float2(2.3, 1.7)) + 0.37).r);

                // Glassy at the lip, white once it has fallen a while.
                float white = saturate(t / max(_Aerate, 0.01));
                fixed3 colour = lerp(_FallColor.rgb, _FoamColor.rgb, saturate(white * (0.6 + streak))) * i.light;
                float alpha = lerp(_GlassAlpha, _WhiteAlpha, white) * (0.65 + (0.35 * streak));

                // Thin water over a lip is a thin curtain; a long fall frays into streaks towards its foot.
                alpha *= lerp(0.35, 1.0, i.water.x);
                float fray = saturate((share - _Fray) / max(1.0 - _Fray, 0.01));
                alpha *= saturate((streak * 2.0) - (fray * 1.2));

                // Its open ends fade out; where it lands, it goes into the pool's churn.
                uint open = (uint)i.water.y;
                bool openHere = (i.strip.x > 0.0 && (open & 1u) != 0u) || (i.strip.x < 0.0 && (open & 2u) != 0u);
                alpha *= openHere ? saturate((1.0 - abs(i.strip.x)) / 0.4) : 1.0;
                alpha *= i.water.z > 0.5 ? saturate((1.0 - share) / 0.05) : saturate((1.0 - share) / 0.3);

                fixed4 result = fixed4(colour, saturate(alpha));
                UNITY_APPLY_FOG(i.fogCoord, result);
                return result;
            }
            ENDCG
        }
    }

    FallBack Off
}
