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
        _FlowTiling ("Running ripples tiling (times the still ones')", Float) = 6
        _FlowStrength ("Running ripples strength", Range(0, 3)) = 1.4
        _FlowCycle ("Flow cycle (s)", Float) = 1.5
        _FlowFrom ("Current from which the water runs (m/s)", Float) = 0.08
        _FlowFull ("Current at which it fully runs (m/s)", Float) = 0.5
        _RapidsSpeed ("Current that makes full foam (m/s)", Float) = 4
        _RapidsTiling ("Rapids foam tiling (per metre)", Float) = 0.35
        _EdgeFade ("Edge fade (shown share)", Range(0.01, 1)) = 0.3

        // The square's (set by the plugin).
        [HideInInspector] _SimH ("Depth", 2D) = "black" {}
        [HideInInspector] _SimBed ("Ground", 2D) = "black" {}
        [HideInInspector] _SimU ("Speed east", 2D) = "black" {}
        [HideInInspector] _SimV ("Speed north", 2D) = "black" {}
        [HideInInspector] _SimCells ("Cells along a side", Float) = 384
        [HideInInspector] _SimDx ("Cell side (m)", Float) = 0.25
        [HideInInspector] _Lift ("Lift (m)", Float) = 0
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

        #include "FlowingWaterLook.cginc"

        // Water thinner than this is not drawn, and this much deeper it is drawn whole (m), as the bricks' (1 to 3 cm).
        #define HIDE_DEPTH 0.01
        #define SHOW_DEPTH 0.02

        float At(sampler2D state, float2 p)
        {
            return tex2Dlod(state, float4(p / _SimCells, 0.0, 0.0)).r;
        }

        // The level at a place in cells (cell centres at half cells).
        float LevelAt(float2 p)
        {
            return At(_SimBed, p) + At(_SimH, p);
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
            float wet = -1e9;
            float2 sides[8] = { float2(1.0, 0.0), float2(-1.0, 0.0), float2(0.0, 1.0), float2(0.0, -1.0), float2(1.0, 1.0), float2(-1.0, 1.0), float2(1.0, -1.0), float2(-1.0, -1.0) };
            for (int k = 0; k < 8; k++)
            {
                if (At(_SimH, p + sides[k]) > HIDE_DEPTH * 0.5)
                {
                    wet = max(wet, LevelAt(p + sides[k]));
                }
            }

            float dry = (wet > -1e8 ? min(bed, wet) : bed) - 0.05;
            v.vertex.y = (depth > HIDE_DEPTH * 0.5 ? bed + depth : dry) + _Lift;

            float east = LevelAt(p + float2(1.0, 0.0)) - LevelAt(p - float2(1.0, 0.0));
            float north = LevelAt(p + float2(0.0, 1.0)) - LevelAt(p - float2(0.0, 1.0));
            float2 slope = float2(east, north) / (2.0 * _SimDx);
            v.normal = normalize(float3(-slope.x, 1.0, -slope.y));
            v.tangent = float4(normalize(float3(1.0, slope.x, 0.0)), -1.0);

            // The current at the cell's centre: the speeds through its faces either side (u at (i + 1, j + 0.5)).
            float2 current = float2(At(_SimU, p - float2(0.5, 0.0)), At(_SimV, p - float2(0.0, 0.5)));
            float2 world = mul(unity_ObjectToWorld, v.vertex).xz;
            float foam = 2.0 * frac(tex2Dlod(_FWSimTarget, float4((world - _FWSimOrigin) / _FWSimSize, 0.0, 0.0)).w);

            o.world = world;
            o.water = float4(depth, foam, saturate((depth - HIDE_DEPTH) / (SHOW_DEPTH - HIDE_DEPTH)), 0.0);
            o.current = current;
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

            Look(i, o);
            DebugColour(o, float3(0.1, 0.3, 1.0));
        }
        ENDCG
    }

    FallBack Off
}
