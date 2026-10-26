// FlowingWater's water near the camera, as the graphics card moves it (FlowingWaterSim.compute): one grid over the
// square the water is moved in, a point per cell (uv0 = its place in cells from the square's south-west corner, the
// object at that corner, at height 0), raised to the water's level there: its ground plus its depth, read from the
// card's textures. Its current is the speeds through the cells' faces, its foam the server's (falls). It looks as the
// bricks' water does (FlowingWaterLook.cginc), and draws only where the server's water is one layer on the ground (the
// bricks draw the rest); thin water fades out as theirs does.
Shader "FlowingWater/SimWater"
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
        _RandomFoamTex ("Scattered foam", 2D) = "white" {}
        _NormalPower ("Ripple strength", Range(0, 5)) = 0.5
        _NormalScale ("Ripple tiling (per metre)", Float) = 0.04
        _WaveVel ("Ripple drift", Float) = 1
        _DepthFade ("Metres of water that hide what is behind", Float) = 15
        _ShoreFade ("Water in front of the ground over which the edge fades (m)", Float) = 0.02
        _FoamDepth ("Shore foam band (m)", Float) = 0.2
        _RefractionScale ("Refraction", Float) = 0.1
        _RefractionMax ("Refraction at most (share of the screen)", Float) = 0.01

        // The flowing water's own.
        _DeepWater ("Water this deep (m) takes the deep colour", Float) = 6
        _RunningDeep ("Running water this deep (m) takes the deep colour", Float) = 1.5
        _RunningClarity ("Running water's clarity (share of the still water's)", Range(0.05, 1)) = 0.25
        _FlowTiling ("Running ripples tiling (times the still ones')", Float) = 6
        _FlowStrength ("Running ripples strength", Range(0, 3)) = 1.4
        _FlowCycle ("Flow cycle (s)", Float) = 1.5
        _FlowFrom ("Current from which the water runs (m/s)", Float) = 0.08
        _FlowFull ("Current at which it fully runs (m/s)", Float) = 0.5
        _RapidsSpeed ("Current that makes full foam (m/s)", Float) = 4
        _RapidsTiling ("Rapids foam tiling (per metre)", Float) = 0.35
        _EdgeFade ("Edge fade (shown share)", Range(0.01, 1)) = 0.3
        _BodyDepth ("Water this thick (m, seen through) takes two thirds of the green light behind it (BodyTint: the red faster)", Float) = 1.2
        _BodyTint ("How much faster each colour's light goes than at BodyDepth (red first)", Vector) = (1.6, 1.1, 0.9, 0)
        _BodyBlur ("Blur of what is behind, share of the screen per metre of water", Float) = 0.004

        // The square's (set by the plugin).
        [HideInInspector] _SimH ("Depth", 2D) = "black" {}
        [HideInInspector] _SimBed ("Ground", 2D) = "black" {}
        [HideInInspector] _SimU ("Speed east", 2D) = "black" {}
        [HideInInspector] _SimV ("Speed north", 2D) = "black" {}
        [HideInInspector] _SimCells ("Cells along a side", Float) = 384
        [HideInInspector] _SimDx ("Cell side (m)", Float) = 0.25
        [HideInInspector] _Lift ("Lift (m)", Float) = 0
        [HideInInspector] _FadeCliffs ("Falls drawn by particles (1): drops faded", Float) = 0
    }

    SubShader
    {
        // As the bricks' water.
        Tags { "Queue" = "Transparent-101" "RenderType" = "Transparent" "IgnoreProjector" = "True" "ForceNoShadowCasting" = "True" }
        LOD 300
        ZWrite Off
        Cull Off

        GrabPass { "_FlowingWaterBehind" }

        CGPROGRAM
        #pragma surface surf Standard alpha:premul vertex:vert finalcolor:Finish noshadow nolightmap nodynlightmap nodirlightmap nometa nolppv noshadowmask
        #pragma target 3.5
        #pragma multi_compile_fog

        sampler2D _SimH;
        sampler2D _SimBed;
        sampler2D _SimU;
        sampler2D _SimV;
        float _SimCells;
        float _SimDx;
        float _FadeCliffs;

        #include "FlowingWaterLook.cginc"

        // Water thinner than this is not drawn, and this much deeper it is drawn whole (m), as the bricks' (1 to 3 cm).
        #define HIDE_DEPTH 0.01
        #define SHOW_DEPTH 0.02

        // Water this much higher or lower than the water a cell away (m, for cells of a quarter metre: as steep for others)
        // begins to fade out from it, gone this much more; only while the falls are particles (_FadeCliffs), which draw
        // that drop (else the grid joins the two levels, as the blocks would).
        #define CLIFF_FROM 0.5
        #define CLIFF_FADE 0.3
        #define CLIFF_CELL 0.25


        // A face of the grid steeper than this (the up of its normal) begins to fade, gone at the second.
        #define STEEP_FROM 0.45
        #define STEEP_GONE 0.3

        float At(sampler2D state, float2 p)
        {
            return tex2Dlod(state, float4(p / _SimCells, 0.0, 0.0)).r;
        }

        // How fast the water is at a place in cells (its faces' speeds there).
        float SpeedAt(float2 p)
        {
            return length(float2(At(_SimU, p - float2(0.5, 0.0)), At(_SimV, p - float2(0.0, 0.5))));
        }

        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            float2 p = v.texcoord.xy;
            float depth = At(_SimH, p);
            float bed = At(_SimBed, p);

            // Raised to the water's level. Where there is none, a little under the water round it (its eight neighbours:
            // the grid's squares join diagonal ones too), or under its own ground where that is lower: its squares fade
            // out at the water's edge, and never reach up to a cell whose ground is not known (a wall far over everything).
            // The four beside it (east, west, north, south), their levels and whether each has water to show, kept for
            // the slope below.
            float wet = -1e9;
            float drop = 0.0;
            float levels[4];
            bool shows[4];
            float2 sides[8] = { float2(1.0, 0.0), float2(-1.0, 0.0), float2(0.0, 1.0), float2(0.0, -1.0), float2(1.0, 1.0), float2(-1.0, 1.0), float2(1.0, -1.0), float2(-1.0, -1.0) };
            for (int k = 0; k < 8; k++)
            {
                float h = At(_SimH, p + sides[k]);
                float there = At(_SimBed, p + sides[k]) + h;
                if (k < 4)
                {
                    levels[k] = there;
                    shows[k] = h > HIDE_DEPTH;
                }

                if (h > HIDE_DEPTH * 0.5)
                {
                    wet = max(wet, there);
                    drop = max(drop, abs(there - (bed + depth)));
                }
            }

            float dry = (wet > -1e8 ? min(bed, wet) : bed) - 0.05;
            v.vertex.y = (depth > HIDE_DEPTH * 0.5 ? bed + depth : dry) + _Lift;

            // The current at the cell's centre: the speeds through its faces either side (u at (i + 1, j + 0.5)).
            float2 current = float2(At(_SimU, p - float2(0.5, 0.0)), At(_SimV, p - float2(0.0, 0.5)));
            float speed = length(current);
            float2 world = mul(unity_ObjectToWorld, v.vertex).xz;

            // The long waves of running water (FlowingWaterWaves.cginc), raised on its level, their slope with the water's
            // own for the light (the fine ones are lit by the look).
            float crest;
            float3 wave = LongWaves(world, current, depth, 1.0, crest);
            v.vertex.y += wave.x;

            float east = levels[0] - levels[1];
            float north = levels[2] - levels[3];
            float2 slope = (float2(east, north) / (2.0 * _SimDx)) + wave.yz;
            v.normal = normalize(float3(-slope.x, 1.0, -slope.y));
            v.tangent = float4(normalize(float3(1.0, slope.x, 0.0)), -1.0);

            // Churn: white water on the tops of fast chop's crests, and where the water slows hard along its way into water
            // ahead (it piles up against an obstacle, or jumps from shooting to deep; not at a front onto dry ground).
            float2 along = speed > 0.05 ? current / speed : 0.0;
            float2 ahead = p + (2.0 * along);
            float slowing = At(_SimH, ahead) > SHOW_DEPTH ? SpeedAt(p - (2.0 * along)) - SpeedAt(ahead) : 0.0;
            float churn = Churn(crest, saturate(speed / _RapidsSpeed), slowing);
            float foam = 2.0 * frac(tex2Dlod(_FWSimTarget, float4((world - _FWSimOrigin) / _FWSimSize, 0.0, 0.0)).w);

            o.world = world;
            // Water a cell from water much higher or lower (over a ledge, down a fall) is not joined to it: the grid's
            // squares between them would be a wall of water, drawn by the falls instead.
            float steep = _SimDx / CLIFF_CELL;
            float joined = 1.0 - (_FadeCliffs * saturate((drop - (CLIFF_FROM * steep)) / (CLIFF_FADE * steep)));
            o.water = float4(depth, foam, saturate((depth - HIDE_DEPTH) / (SHOW_DEPTH - HIDE_DEPTH)) * joined, churn);
            o.current = current;
            // The slope for the cascade from the cells beside with water only (a dry cell, its bank or a wall of ground
            // not known, is no slope of the water's).
            float level = bed + depth;
            float wetEast = (shows[0] ? levels[0] : level) - (shows[1] ? levels[1] : level);
            float wetNorth = (shows[2] ? levels[2] : level) - (shows[3] ? levels[3] : level);
            o.steep = length(float2(wetEast, wetNorth) / (2.0 * _SimDx));
            o.cell = p;
            o.grabPos = ComputeGrabScreenPos(UnityObjectToClipPos(v.vertex));
        }

        void surf(Input i, inout SurfaceOutputStandard o)
        {
            // Only where it has water to show and draws the server's water (else a brick does).
            if (i.water.z <= 0.0 || !SimDraws(i.worldPos.xz))
            {
                discard;
            }

            // A face of the grid standing up (between water far apart in height: a wet cell and a dry one raised by water
            // higher beside it) is no water: it fades out as it stands up.
            float up = _FadeCliffs > 0.5 ? abs(normalize(cross(ddy(i.worldPos), ddx(i.worldPos))).y) : 1.0;
            if (up < STEEP_GONE)
            {
                discard;
            }

            Look(i, o);
            DebugColour(o, float3(0.1, 0.3, 1.0));
            o.Alpha *= saturate((up - STEEP_GONE) / (STEEP_FROM - STEEP_GONE));
        }
        ENDCG
    }

    FallBack Off
}
