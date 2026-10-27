// FlowingWater's volume water, found for the drawing: for each pixel (of a half-size picture, VolumeSurface.cs), a ray
// from the camera marched through the water's density (FlowingWaterVolume.compute's Fine texture, a texel a particle)
// to where it first crosses a half, the water's surface, and on through the water, adding up how much of it the ray
// goes through until it leaves the grid or meets the scene. The march steps over the cells that hold no water (the
// Coarse texture) and takes small steps where there is some. Written out: how far the surface is along the view, the
// water seen through, the foam on the surface (the froth in the cell, FlowingWaterVolume.compute's Foam texture),
// whether the camera is under the water; and the surface's normal (the density's slope) with how near the grid's side
// the surface is (the water fades out at the square's edge, where the sheet's bricks take over). The surface shader
// (FlowingWaterVolumeSurface.shader) draws the water from these.
Shader "FlowingWater/VolumeMarch"
{
    Properties
    {
        _EdgeFade ("The water fades out over this far from the square's side (m)", Float) = 2
    }

    SubShader
    {
        Tags { "Queue" = "Geometry" "RenderType" = "Opaque" }
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            Name "March"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #include "UnityCG.cginc"

            // The grid: its lowest south-west corner (world), its size (m), its cells, a cell's side (m).
            float3 _VolumeOrigin;
            float3 _VolumeSize;
            float3 _VolumeCells;
            float _VolumeCell;
            float _EdgeFade;
            sampler3D _Fine;
            sampler3D _Coarse;
            sampler3D _Foam;
            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);

            // The density the surface lies at, the fine step through water (a quarter of a cell), steps at most.
            #define SURFACE 0.5
            #define FINE_STEPS 4
            #define MOST_STEPS 640

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 ndc : TEXCOORD0;
            };

            struct Found
            {
                // How far the surface is along the view (m; 0 none), the water seen through (m), the foam at the
                // surface, 1 when the camera is under the water.
                float4 water : SV_Target0;
                // The surface's normal (world) and how near the square's side it is (0 at the side, 1 inside).
                float4 normal : SV_Target1;
            };

            v2f vert(uint id : SV_VertexID)
            {
                v2f o;
                float2 corner = float2((id << 1) & 2, id & 2);
                o.ndc = (corner * 2.0) - 1.0;
                o.pos = float4(o.ndc, UNITY_NEAR_CLIP_VALUE, 1.0);
                return o;
            }

            float3 Where(float3 place)
            {
                return (place - _VolumeOrigin) / _VolumeSize;
            }

            // The water's density at a place: the half-cells' where the water is close packed (a pool's surface is
            // theirs, sharp), with the cells' water added where both hold little (a fast sheet a particle or two thick,
            // too sparse for the half-cells, still makes a surface: three times the cells' share of rest; a full cell
            // adds nothing, so a pool's sides stay the half-cells' own, not the cells' steps).
            float Density(float3 place)
            {
                float4 at = float4(Where(place), 0.0);
                float fine = tex3Dlod(_Fine, at).r;
                float coarse = tex3Dlod(_Coarse, at).r;
                return fine + (3.0 * coarse * (1.0 - saturate(coarse)) * (1.0 - saturate(2.0 * fine)));
            }

            // Whether there is any water about a place (the cells' water, sampled smoothly, reaches a cell beyond it).
            bool Wet(float3 place)
            {
                return tex3Dlod(_Coarse, float4(Where(place), 0.0)).r > 0.001;
            }

            // The density's slope at a place, over a cell each way (wider than the density's grain, so the surface's
            // light is smooth).
            float3 Normal(float3 place)
            {
                float e = _VolumeCell;
                float3 g = float3(
                    Density(place + float3(e, 0.0, 0.0)) - Density(place - float3(e, 0.0, 0.0)),
                    Density(place + float3(0.0, e, 0.0)) - Density(place - float3(0.0, e, 0.0)),
                    Density(place + float3(0.0, 0.0, e)) - Density(place - float3(0.0, 0.0, e)));
                return dot(g, g) > 1e-8 ? -normalize(g) : float3(0.0, 1.0, 0.0);
            }

            // How far along the ray the cell round a place ends (the next cell boundary along the ray), a little past it.
            float CellExit(float3 place, float3 dir, float t)
            {
                float3 local = (place - _VolumeOrigin) / _VolumeCell;
                float3 cell = floor(local);
                float3 next = cell + step(0.0, dir);
                float3 ahead = (next - local) * _VolumeCell / dir;
                ahead = dir == 0.0 ? 1e9 : ahead;
                return t + max(min(min(ahead.x, ahead.y), ahead.z), 0.0) + (0.01 * _VolumeCell);
            }

            Found frag(v2f i)
            {
                Found o;
                o.water = 0.0;
                o.normal = float4(0.0, 1.0, 0.0, 0.0);

                // The ray through this pixel, world, and how far along it the scene is.
                float2 ndc = float2(i.ndc.x, i.ndc.y * _ProjectionParams.x);
                float3 view = float3(ndc.x / unity_CameraProjection._m00, ndc.y / unity_CameraProjection._m11, -1.0);
                float3 dir = normalize(mul((float3x3)unity_CameraToWorld, float3(view.x, view.y, -view.z)));
                float3 from = _WorldSpaceCameraPos;
                float3 forward = normalize(mul((float3x3)unity_CameraToWorld, float3(0.0, 0.0, 1.0)));
                float2 uv = (i.ndc * 0.5) + 0.5;
                uv.y = _ProjectionParams.x < 0.0 ? 1.0 - uv.y : uv.y;
                float eye = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, uv));
                float along = dot(dir, forward);
                float scene = eye / max(along, 1e-4);

                // Where the ray is inside the grid.
                float3 lo = _VolumeOrigin, hi = _VolumeOrigin + _VolumeSize;
                float3 inv = 1.0 / (abs(dir) < 1e-6 ? 1e-6 : dir);
                float3 tA = (lo - from) * inv, tB = (hi - from) * inv;
                float3 tMin = min(tA, tB), tMax = max(tA, tB);
                float t0 = max(max(max(tMin.x, tMin.y), tMin.z), 0.0);
                float t1 = min(min(min(tMax.x, tMax.y), tMax.z), scene);
                if (t1 <= t0)
                {
                    return o;
                }

                float fine = _VolumeCell / FINE_STEPS;
                bool inside = t0 <= 0.0 && Density(from) >= SURFACE;
                float surfaceAt = inside ? 0.0 : -1.0;
                float3 surfacePlace = from;
                float thickness = 0.0;
                float last = Density(from + (dir * t0));
                float t = t0;
                for (int k = 0; k < MOST_STEPS && t < t1; k++)
                {
                    float3 place = from + (dir * t);
                    if (!Wet(place))
                    {
                        // Nothing in this cell nor beside it: on to the next cell.
                        if (inside)
                        {
                            inside = false;
                        }

                        last = 0.0;
                        t = CellExit(place, dir, t);
                        continue;
                    }

                    float next = min(t + fine, t1);
                    float3 ahead = from + (dir * next);
                    float d = Density(ahead);
                    if (!inside && d >= SURFACE)
                    {
                        // Into the water: the surface between here and there, by halves.
                        float a = t, b = next;
                        for (int h = 0; h < 4; h++)
                        {
                            float m = 0.5 * (a + b);
                            if (Density(from + (dir * m)) >= SURFACE)
                            {
                                b = m;
                            }
                            else
                            {
                                a = m;
                            }
                        }

                        inside = true;
                        if (surfaceAt < 0.0)
                        {
                            surfaceAt = b;
                            surfacePlace = from + (dir * b);
                        }

                        thickness += saturate(d) * (next - b);
                    }
                    else if (inside)
                    {
                        thickness += saturate(0.5 * (d + last)) * (next - t);
                        inside = d >= SURFACE;
                    }

                    last = d;
                    t = next;
                }

                bool under = surfaceAt == 0.0;
                if (surfaceAt < 0.0 && !under)
                {
                    return o;
                }

                float3 n = under ? float3(0.0, 1.0, 0.0) : Normal(surfacePlace);
                float3 edge = min(surfacePlace - lo, hi - surfacePlace);
                float near = saturate(min(edge.x, edge.z) / max(_EdgeFade, 0.01));
                float foam = tex3Dlod(_Foam, float4(Where(surfacePlace), 0.0)).r;
                o.water = float4(under ? 0.0 : surfaceAt * along, thickness, foam, under ? 1.0 : 0.0);
                o.normal = float4(n, near);
                return o;
            }
            ENDCG
        }
    }

    FallBack Off
}
