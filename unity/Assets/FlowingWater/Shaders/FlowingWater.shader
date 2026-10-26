// FlowingWater's water surface, for the built-in render pipeline the game uses, with the look of the game's own sea:
// its textures, colours and settings are copied from the sea's material when the game runs (the defaults here are the
// sea's, for a preview without the game), and the water is lit, coloured and fogged the way the sea is, so a pool or
// a river reads as the same water as the sea it runs into. What the sea has not, the flowing water adds: where it
// runs, its ripples run with it (flow mapping) and it foams with its speed; where it lies still, the ripples drift
// with the game's wind as the sea's do.
//
// The surface is shaped here, not on the processor: each brick draws one fixed grid (uv0 = the point's place in
// columns, from the brick's south-west column's centre), raised to the water's top read from two small textures per
// state (a column each, and one more east and north, from the bricks beside): x = the top over the brick's floor (m),
// y = depth (m), z = how much of it is shown, w = 1 where the brick draws the top (else the brick under or over it);
// and x, y = current (m/s, east and north), z = foam from falling. The previous state and the next are blended over
// the time between the server's changes, so the water moves on smoothly instead of jumping at each. Each square
// between four columns is drawn by one brick: the one whose top its first shown corner is, in the next state (where
// it shows no water there, in the previous one, so water drying up fades out). Where the water moved on the graphics
// card near the camera draws the water (FlowingWaterSim.shader), the brick does not.
//
// The look itself (light, ripples, foam, fog) is FlowingWaterLook.cginc's, shared with that shader.
//
// Light: what lies behind the surface (the scene, grabbed once a frame, bent by the ripples) is seen through thin
// water, tinted by the surface colour; deeper water takes the bottom colour instead, from shallow to deep with the
// water's depth, over DepthFade metres of water behind the surface. The surface itself is lit by the game's lights
// and sky (smooth, so it mirrors them). Where it meets the ground it fades out over the last ShoreFade metres of
// water in front of the scene (only that: the surface lies on the water's own top, so shallow water is drawn, unlike
// the sea's flat plane, which hides its shallows), with a band of foam just off the water's edge.
Shader "FlowingWater/Water"
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

        // Each brick's (set per renderer).
        [HideInInspector] _SurfacePrev ("Surface before", 2D) = "black" {}
        [HideInInspector] _SurfaceNext ("Surface after", 2D) = "black" {}
        [HideInInspector] _FlowPrev ("Current before", 2D) = "black" {}
        [HideInInspector] _FlowNext ("Current after", 2D) = "black" {}
        [HideInInspector] _BlendStart ("Blend start (s)", Float) = 0
        [HideInInspector] _BlendTime ("Blend time (s)", Float) = 1
        [HideInInspector] _VoxelSize ("Voxel size (m)", Float) = 1
        [HideInInspector] _Lift ("Lift (m)", Float) = 0
    }

    SubShader
    {
        // Drawn as the sea is: transparent, just before it; from both sides, so the water's top is seen from inside it.
        Tags { "Queue" = "Transparent-101" "RenderType" = "Transparent" "IgnoreProjector" = "True" "ForceNoShadowCasting" = "True" }
        LOD 300
        ZWrite Off
        Cull Off

        // What is behind the water, once a frame for every brick.
        GrabPass { "_FlowingWaterBehind" }

        CGPROGRAM
        #pragma surface surf Standard alpha:premul vertex:vert finalcolor:Finish noshadow nolightmap nodynlightmap nodirlightmap nometa nolppv noshadowmask
        #pragma target 3.5
        #pragma multi_compile_fog

        sampler2D _SurfacePrev;
        sampler2D _SurfaceNext;
        sampler2D _FlowPrev;
        sampler2D _FlowNext;
        float _BlendStart;
        float _BlendTime;
        float _VoxelSize;

        #include "FlowingWaterLook.cginc"

        // Columns along a side of a brick's textures.
        #define GRID_SIDE 17.0

        float2 GridUv(float2 cell)
        {
            return (cell + 0.5) / GRID_SIDE;
        }

        // How far the blend from the previous state to the next is.
        float BlendShare()
        {
            return saturate((_Time.y - _BlendStart) / max(_BlendTime, 0.001));
        }

        // The surface at a place in the columns, blended (x = top, y = depth, z = shown; w is the next state's).
        float4 SurfaceAt(float2 cell)
        {
            float4 uv = float4(GridUv(cell), 0.0, 0.0);
            float4 next = tex2Dlod(_SurfaceNext, uv);
            return float4(lerp(tex2Dlod(_SurfacePrev, uv).xyz, next.xyz, BlendShare()), next.w);
        }

        // The top of a surface sample if water shows there, else this one (a dry column's top is no slope of the water's).
        // Half a column from a column's middle, a sample is half this column's and half the next one's: water shows there
        // when more than half of it does, so a dry column beside never counts half.
        float WetTop(float4 surface, float otherwise)
        {
            return surface.z > 0.5 ? surface.x : otherwise;
        }

        float4 FlowAt(float2 cell)
        {
            float4 uv = float4(GridUv(cell), 0.0, 0.0);
            return lerp(tex2Dlod(_FlowPrev, uv), tex2Dlod(_FlowNext, uv), BlendShare());
        }

        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            float2 cell = v.texcoord.xy;
            float4 surface = SurfaceAt(cell);
            float4 flow = FlowAt(cell);

            // Raised to the top (over the brick's floor; the object sits at the brick's centre), with the long waves of
            // running water on it as the card's water near the camera has them (FlowingWaterWaves.cginc: they meet at the
            // edge of its square); the fine ones are lit by the look.
            v.vertex.y = surface.x - (8.0 * _VoxelSize) + _Lift;
            float2 world = mul(unity_ObjectToWorld, v.vertex).xz;
            float crest;
            float3 wave = LongWaves(world, flow.xy, surface.y, surface.z, crest);
            v.vertex.y += wave.x;

            // The surface's slope, from the tops half a column either side, and the waves'.
            float4 eastOf = SurfaceAt(cell + float2(0.5, 0.0));
            float4 westOf = SurfaceAt(cell - float2(0.5, 0.0));
            float4 northOf = SurfaceAt(cell + float2(0.0, 0.5));
            float4 southOf = SurfaceAt(cell - float2(0.0, 0.5));
            float east = eastOf.x - westOf.x;
            float north = northOf.x - southOf.x;
            float2 slope = (float2(east, north) / max(_VoxelSize, 0.001)) + wave.yz;
            v.normal = normalize(float3(-slope.x, 1.0, -slope.y));
            v.tangent = float4(normalize(float3(1.0, slope.x, 0.0)), -1.0);

            // White water on the tops of fast waves' crests, as the card's water has it (not where it slows: the blocks do
            // not know the water ahead).
            o.world = world;
            o.water = float4(surface.y, flow.z, surface.z, Churn(crest, saturate(length(flow.xy) / _RapidsSpeed), 0.0));
            o.current = flow.xy;
            float wetEast = WetTop(eastOf, surface.x) - WetTop(westOf, surface.x);
            float wetNorth = WetTop(northOf, surface.x) - WetTop(southOf, surface.x);
            o.steep = length(float2(wetEast, wetNorth)) / max(_VoxelSize, 0.001);
            o.cell = cell;
            o.grabPos = ComputeGrabScreenPos(UnityObjectToClipPos(v.vertex));
        }

        // A state's say on a square: 1 where this brick draws it (the brick whose top its first shown corner is), 0
        // where another does, -1 where no corner shows water.
        float Owner(sampler2D state, float2 corner)
        {
            float4 a = tex2Dlod(state, float4(GridUv(corner), 0.0, 0.0));
            float4 b = tex2Dlod(state, float4(GridUv(corner + float2(1.0, 0.0)), 0.0, 0.0));
            float4 c = tex2Dlod(state, float4(GridUv(corner + float2(0.0, 1.0)), 0.0, 0.0));
            float4 d = tex2Dlod(state, float4(GridUv(corner + float2(1.0, 1.0)), 0.0, 0.0));
            return a.z > 0.0 ? a.w : (b.z > 0.0 ? b.w : (c.z > 0.0 ? c.w : (d.z > 0.0 ? d.w : -1.0)));
        }

        // Whether this brick draws the square a point is in: as the next state has it, or where the next shows no
        // water there, as the previous did (water drying up fades out instead of going at once).
        bool Draws(float2 cell)
        {
            float2 corner = min(floor(cell), GRID_SIDE - 2.0);
            float own = Owner(_SurfaceNext, corner);
            if (own < 0.0)
            {
                own = Owner(_SurfacePrev, corner);
            }

            return own > 0.5;
        }

        void surf(Input i, inout SurfaceOutputStandard o)
        {
            // Where the water moved on the card draws it, the brick does not.
            if (!Draws(i.cell) || SimDraws(i.worldPos.xz))
            {
                discard;
            }

            Look(i, o);
            DebugColour(o, float3(1.0, 0.15, 0.1));
        }
        ENDCG
    }

    FallBack Off
}
