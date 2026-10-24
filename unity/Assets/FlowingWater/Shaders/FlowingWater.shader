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
// it shows no water there, in the previous one, so water drying up fades out).
//
// Light: what lies behind the surface (the scene, grabbed once a frame, bent by the ripples) is seen through thin
// water, tinted by the surface colour; deeper water takes the bottom colour instead, from shallow to deep with the
// water's depth, over DepthFade metres of water behind the surface. The surface itself is lit by the game's lights
// and sky (smooth, so it mirrors them). At the shore it fades out with the scene's depth behind it, with a band of
// foam just off the water's edge.
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
        _ShoreFade ("Shore fade (m)", Float) = 0.1
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
        _ShoreOffset ("Still water thinner than this (m) is not seen", Float) = 0.15
        _RunningOffset ("Running water thinner than this (m) is not seen", Float) = 0.02
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
        // Drawn as the sea is: transparent, just before it.
        Tags { "Queue" = "Transparent-101" "RenderType" = "Transparent" "IgnoreProjector" = "True" "ForceNoShadowCasting" = "True" }
        LOD 300
        ZWrite Off
        Cull Back

        // What is behind the water, once a frame for every brick.
        GrabPass { "_FlowingWaterBehind" }

        CGPROGRAM
        #pragma surface surf Standard alpha:premul vertex:vert finalcolor:Finish noshadow nolightmap nodynlightmap nodirlightmap nometa nolppv noshadowmask
        #pragma target 3.5
        #pragma multi_compile_fog

        sampler2D _Normal;
        sampler2D _FoamTex;
        sampler2D _RandomFoamTex;
        sampler2D _FlowingWaterBehind;
        sampler2D _SurfacePrev;
        sampler2D _SurfaceNext;
        sampler2D _FlowPrev;
        sampler2D _FlowNext;
        float _BlendStart;
        float _BlendTime;
        float _VoxelSize;
        float _Lift;
        UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);

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
        float _DepthFade;
        float _ShoreFade;
        float _FoamDepth;
        float _RefractionScale;
        float _RefractionMax;
        float _DeepWater;
        float _FlowTiling;
        float _FlowStrength;
        float _FlowCycle;
        float _FlowFrom;
        float _FlowFull;
        float _RapidsSpeed;
        float _RapidsTiling;
        float _ShoreOffset;
        float _RunningOffset;
        float _EdgeFade;

        // The game's own, set for every shader: its two winds (x, z its direction, w its strength) and how far it
        // is from the first to the second; the sun's direction and the colour the fog takes towards it. Unset (a
        // preview), the wind is none and the fog takes its own colour.
        float4 _GlobalWind1;
        float4 _GlobalWind2;
        float _GlobalWindAlpha;
        float4 _SunDir;
        float4 _SunFogColor;

        struct Input
        {
            float2 world;   // world x, z
            float4 water;   // depth, foam, shore
            float2 current; // m/s
            float3 worldPos;
            float4 screenPos;
            float4 grabPos; // the same point in the grabbed image (upside down on some platforms)
            float2 cell;    // the point's place in the brick's columns
        };

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

            // Raised to the top (over the brick's floor; the object sits at the brick's centre).
            v.vertex.y = surface.x - (8.0 * _VoxelSize) + _Lift;

            // The surface's slope, from the tops half a column either side.
            float east = SurfaceAt(cell + float2(0.5, 0.0)).x - SurfaceAt(cell - float2(0.5, 0.0)).x;
            float north = SurfaceAt(cell + float2(0.0, 0.5)).x - SurfaceAt(cell - float2(0.0, 0.5)).x;
            float2 slope = float2(east, north) / max(_VoxelSize, 0.001);
            v.normal = normalize(float3(-slope.x, 1.0, -slope.y));
            v.tangent = float4(normalize(float3(1.0, slope.x, 0.0)), -1.0);

            o.world = mul(unity_ObjectToWorld, v.vertex).xz;
            o.water = float4(surface.y, flow.z, surface.z, 0.0);
            o.current = flow.xy;
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

        // The scene's distance from the camera, along its view axis, at a point of the screen.
        float SceneEye(float2 screen)
        {
            return LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, screen));
        }

        // A wind's direction on the ground, and its strength; none (east, still) where the game set none.
        float3 Wind(float4 wind)
        {
            float length2 = dot(wind.xz, wind.xz);
            return length2 > 1e-6 ? float3(wind.xz * rsqrt(length2), wind.w) : float3(1.0, 0.0, 0.0);
        }

        // The sea's ripples under one wind: a large slow layer and two finer ones, all drifting down the wind.
        float3 WindRipples(float2 world, float2 along)
        {
            float2 uv = float2(dot(world, float2(-along.y, along.x)), dot(world, along)) * _NormalScale;
            float drift = _Time.x * _WaveVel;
            float3 large = UnpackNormal(tex2D(_Normal, (uv * 0.2) + float2(0.0, -0.2 * drift)));
            float3 fine = UnpackNormal(tex2D(_Normal, uv + float2(0.0, -0.22 * drift)));
            float3 cross = UnpackNormal(tex2D(_Normal, uv + float2(0.01 * drift, -0.2 * drift)));
            return large + (0.3 * fine) + (0.3 * cross);
        }

        // One phase of the flow: where the texture has run to, and how much this phase counts (it fades out at the
        // jump back so the reset is never seen).
        float2 FlowPhase(float2 uv, float2 run, float offset, out float weight)
        {
            float t = frac((_Time.y / _FlowCycle) + offset);
            weight = 1.0 - abs((2.0 * t) - 1.0);
            return uv - (run * t * _FlowCycle) + (offset * float2(0.37, 0.61));
        }

        // Ripples that run with the current, the sea's layers finer (a stream is narrower than the sea's swell):
        // dragged along at the water's own speed, two phases blended so they never stretch.
        float3 RunningRipples(float2 world, float2 current)
        {
            float scale = _NormalScale * _FlowTiling;
            float2 uv = world * scale;
            float2 run = current * scale;
            float wa;
            float wb;
            float2 a = FlowPhase(uv, run, 0.0, wa);
            float2 b = FlowPhase(uv, run, 0.5, wb);
            float3 large = (UnpackNormal(tex2D(_Normal, a * 0.2)) * wa) + (UnpackNormal(tex2D(_Normal, b * 0.2)) * wb);
            float3 fine = (UnpackNormal(tex2D(_Normal, a)) * wa) + (UnpackNormal(tex2D(_Normal, b)) * wb);
            return large + (0.6 * fine);
        }

        // The fog over the water as over the sea: the game's fog, towards the sun's colour where one looks at the
        // sun; over all the water's light, what is seen through included, as the sea's.
        void Finish(Input i, SurfaceOutputStandard o, inout fixed4 color)
        {
        #if defined(FOG_LINEAR) || defined(FOG_EXP) || defined(FOG_EXP2)
        #if defined(UNITY_PASS_FORWARDADD)
            float3 fogColour = 0.0;
        #else
            float sun = saturate(dot(normalize(i.worldPos - _WorldSpaceCameraPos), _SunDir.xyz));
            float3 sunFog = pow(max(_SunFogColor.rgb, 0.0), 2.2);
            float3 fogColour = dot(_SunFogColor.rgb, 1.0) > 0.0 ? lerp(unity_FogColor.rgb, sunFog, sun) : unity_FogColor.rgb;
        #endif
            UNITY_CALC_FOG_FACTOR_RAW(i.screenPos.w);
            color.rgb = lerp(fogColour, color.rgb, saturate(unityFogFactor));
        #endif
            // Premultiplied: the water's light fades out with the water at its edge.
            color.rgb *= o.Alpha;
        }

        void surf(Input i, inout SurfaceOutputStandard o)
        {
            if (!Draws(i.cell))
            {
                discard;
            }

            float depth = max(i.water.x, 0.0);
            float deep = saturate(depth / _DeepWater);
            float speed = length(i.current);
            float running = smoothstep(_FlowFrom, _FlowFull, speed);

            // Ripples: the sea's under the game's wind where the water lies still, running ones where it runs.
            float3 wind1 = Wind(_GlobalWind1);
            float3 wind2 = Wind(_GlobalWind2);
            float3 still = lerp(WindRipples(i.world, wind1.xy), WindRipples(i.world, wind2.xy), _GlobalWindAlpha);
            float3 ripples = lerp(still, RunningRipples(i.world, i.current) * _FlowStrength, running);
            // Shallow still water lies flatter, as the sea's does; running water keeps its ripples.
            float3 n = normalize(lerp(float3(0.0, 0.0, 1.0), ripples, max(clamp(deep, 0.2, 1.0), running)));
            n = lerp(float3(0.0, 0.0, 1.0), n, _NormalPower);

            // What is behind, bent by the ripples, unless that bends something in front of the water into it.
            float2 screen = i.screenPos.xy / max(i.screenPos.w, 0.0001);
            float2 grab = i.grabPos.xy / max(i.grabPos.w, 0.0001);
            float eye = i.screenPos.w;
            float2 bend = clamp(n.xy * _RefractionScale * 5.0, -_RefractionMax, _RefractionMax);
            float straight = SceneEye(screen);
            float bent = SceneEye(screen + bend);
            if (bent < eye)
            {
                bend = 0.0;
                bent = straight;
            }

            float3 behind = tex2D(_FlowingWaterBehind, grab + bend).rgb;
            float hidden = sqrt(saturate(abs(bent - eye) / _DepthFade));

            // The shore: how much water there is in front of what is behind (the ripples move it a little).
            float shoreLine = abs(straight - eye) - (0.4 * n.y) - lerp(_ShoreOffset, _RunningOffset, running);
            float shore = saturate(shoreLine / _ShoreFade);
            float band = saturate(shoreLine / _FoamDepth);

            float3 colour = hidden * lerp(_ColorBottomShallow.rgb, _ColorBottom.rgb, deep);

            // Scattered foam where the wind blows on open water, as on the sea.
            float2 wobble = 0.005 * float2(sin(_Time.y + i.worldPos.x), sin((1.5 * _Time.y) + i.worldPos.z));
            float windFoam = 5.0 * lerp(wind1.z, wind2.z, _GlobalWindAlpha) * deep * (1.0 - running) * tex2D(_RandomFoamTex, (i.world * 0.03) + wobble).r;
            colour = lerp(colour, _FoamColor.rgb, saturate(windFoam * _FoamColor.a));
            float gloss = _Glossiness * (1.0 - saturate(windFoam));

            // A band of foam just off the water's edge.
            float shoreFoam = deep * min(shore, 1.0 - band);
            colour = lerp(colour, _FoamColor.rgb, saturate(8.0 * tex2D(_FoamTex, (i.world * 0.1) + wobble).r * shoreFoam * _FoamColor.a));

            // Rapids: fast water foams, its foam running with it; the simulation's own foam (falls) on top.
            float fast = saturate(speed / _RapidsSpeed);
            float want = saturate((fast * fast * 0.8) + i.water.y);
            float fa;
            float fb;
            float2 foamUv = i.world * _RapidsTiling;
            float2 foamRun = i.current * _RapidsTiling;
            float noise = (tex2D(_FoamTex, FlowPhase(foamUv, foamRun, 0.25, fa)).r * fa) + (tex2D(_FoamTex, FlowPhase(foamUv, foamRun, 0.75, fb)).r * fb);
            float rapids = smoothstep(1.0 - want, 1.0 - want + 0.25, noise) * want;
            colour = lerp(colour, _FoamColor.rgb, rapids);

            float foam = max(shoreFoam, rapids);
            o.Albedo = colour;
            o.Emission = behind * _SurfaceColor.rgb * (1.0 - hidden) * (1.0 - rapids);
            o.Normal = n;
            o.Metallic = _Metallic * (1.0 - foam);
            o.Smoothness = lerp(gloss, 0.1, foam);
            o.Alpha = shore * shore * smoothstep(0.0, _EdgeFade, saturate(i.water.z));
        }
        ENDCG
    }

    FallBack Off
}
