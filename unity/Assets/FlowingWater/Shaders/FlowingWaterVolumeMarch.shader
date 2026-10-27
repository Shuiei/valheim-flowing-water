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
        _SmoothRadius ("The surface is smoothed over this far across it (m), each pass", Float) = 0.6
        _SmoothFalloff ("A step in distance this deep (m) off the surface's own slope is kept, not smoothed over (one body of water before another)", Float) = 0.3
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
            int _VolumeLook;
            float3 _VolumeOrigin;
            float3 _VolumeSize;
            float3 _VolumeCells;
            float _VolumeCell;
            float _EdgeFade;
            sampler3D _Fine;
            sampler3D _Coarse;
            sampler3D _Foam;
            // The sheet on the ground: its top in each half-cell column (height over the grid's bottom; 0 none), and
            // the ground (the card's water's heights, a texel a cell, from an origin in world x, z).
            sampler2D _Sheet;
            sampler2D _Bed;
            int _BedCells;
            float _BedDx;
            float2 _BedOrigin;
            #define STEP_UP 1.0
            // Ground steeper than this (rise over run) holds no sheet of water: what runs down it is a fall, the
            // density's to draw (the ground's heights come from the game's colliders: a tree's trunk is a steep ramp
            // in them, and a skin of water laid along it would be a curtain up the tree).
            #define SHEET_STEEPEST 1.5
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

            // The ground under a point, as the solver reads it: bilinear between the cells' heights, or the cell's own
            // where the four about the point differ by more than a step (a wall: no ramp up it); and whether the ground
            // there is too steep for a sheet of water.
            float GroundAt(float2 xz, out bool steep)
            {
                float2 at = ((xz - _BedOrigin) / _BedDx) - 0.5;
                float2 f = frac(at);
                float2 texel = 1.0 / _BedCells;
                float2 c = (floor(at) + 0.5) * texel;
                float b00 = tex2Dlod(_Bed, float4(c, 0.0, 0.0)).r;
                float b10 = tex2Dlod(_Bed, float4(c + float2(texel.x, 0.0), 0.0, 0.0)).r;
                float b01 = tex2Dlod(_Bed, float4(c + float2(0.0, texel.y), 0.0, 0.0)).r;
                float b11 = tex2Dlod(_Bed, float4(c + texel, 0.0, 0.0)).r;
                float lowest = min(min(b00, b10), min(b01, b11)), highest = max(max(b00, b10), max(b01, b11));
                steep = highest - lowest > SHEET_STEEPEST * _BedDx;
                if (highest - lowest > STEP_UP)
                {
                    return tex2Dlod(_Bed, float4((floor((xz - _BedOrigin) / _BedDx) + 0.5) * texel, 0.0, 0.0)).r;
                }

                return lerp(lerp(b00, b10, f.x), lerp(b01, b11, f.x), f.y);
            }

            float GroundAt(float2 xz)
            {
                bool steep;
                return GroundAt(xz, steep);
            }

            // The top of the water standing on the ground over a point (world y): the ground plus the columns' depth
            // there (the depth blends between the columns, to nothing across a dry one); far below where there is none.
            float SheetTop(float2 xz)
            {
                float2 uv = (xz - _VolumeOrigin.xz) / _VolumeSize.xz;
                float depth = tex2Dlod(_Sheet, float4(uv, 0.0, 0.0)).r;
                bool steep;
                float ground = GroundAt(xz, steep);
                return depth > 0.005 && !steep ? ground + depth : -1e9;
            }

            // The water's density at a place: one wherever the place lies in the water standing on the ground, between
            // the ground (a hair under it, so the water's bottom meets the ground the scene draws without a seam of air
            // at the march's step) and the column's top: water a particle or two thick, too sparse for the half-cells,
            // is one skin from its columns' tops, and a pool's surface is its columns' level, not the particles' grain.
            // Above that top, the half-cells' own density, but within a cell of the top only where it is well over the
            // half (a lump of the grain a particle high is not the surface; a wave or a splash is); and the half-cells'
            // own wherever no water stands on the ground (a fall, a splash, water over a gap).
            float Density(float3 place)
            {
                float fine = tex3Dlod(_Fine, float4(Where(place), 0.0)).r;
                if (_VolumeLook == 1)
                {
                    return fine;
                }

                float top = SheetTop(place.xz);
                if (_VolumeLook == 2)
                {
                    return place.y <= top && place.y >= GroundAt(place.xz) - (0.05 * _VolumeCell) ? 1.0 : 0.0;
                }

                if (place.y <= top && place.y >= GroundAt(place.xz) - (0.05 * _VolumeCell))
                {
                    return max(fine, 1.0);
                }

                // Within a cell and a half over the column's top the density counts only where it is well over the
                // half (1.1 and more: a wave, a splash; not a lump of the grain a particle high).
                float above = place.y - top;
                return above > 0.0 && above < 1.5 * _VolumeCell ? saturate((fine - 0.85) / 0.5) : fine;
            }

            // The sheet's normal at a point on its top: the slope of the tops about it, over a metre each way. The
            // ground under thin water is the game's terrain, faceted every metre; the tops carry its creases, and a
            // normal taken across them would draw each crease as a line of light across the water. A metre each way
            // reads the terrain as the game shades it, smooth.
            float3 SheetNormal(float2 xz)
            {
                float e = max(4.0 * _VolumeCell, 1.0);
                float dx = SheetTop(xz + float2(e, 0.0)) - SheetTop(xz - float2(e, 0.0));
                float dz = SheetTop(xz + float2(0.0, e)) - SheetTop(xz - float2(0.0, e));
                dx = abs(dx) > 1e6 ? 0.0 : dx;
                dz = abs(dz) > 1e6 ? 0.0 : dz;
                return normalize(float3(-dx, 2.0 * e, -dz));
            }

            // Whether there is any water about a place: the cells' water, sampled smoothly, reaches a cell beyond it;
            // and the water standing on the ground reaches its columns' top, which the smoothing of the tops may put
            // in a cell with no particle of its own (a ray that skipped such a cell as dry found the surface at the
            // cell's boundary instead: the water in terraces a cell high).
            bool Wet(float3 place)
            {
                return tex3Dlod(_Coarse, float4(Where(place), 0.0)).r > 0.001 || place.y <= SheetTop(place.xz) + _VolumeCell;
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

                // The surface's normal: the columns' tops' slope at or under the column's top, the density's slope a
                // cell over it, blended between (no seam where one gives way to the other).
                float overTop = surfacePlace.y - SheetTop(surfacePlace.xz);
                float ofSheet = overTop < 1e6 ? saturate(1.0 - (overTop / _VolumeCell)) : 0.0;
                float3 n = under ? float3(0.0, 1.0, 0.0) : normalize(lerp(Normal(surfacePlace), SheetNormal(surfacePlace.xz), ofSheet));
                float3 edge = min(surfacePlace - lo, hi - surfacePlace);
                float near = saturate(min(edge.x, edge.z) / max(_EdgeFade, 0.01));
                float foam = tex3Dlod(_Foam, float4(Where(surfacePlace), 0.0)).r;
                o.water = float4(under ? 0.0 : surfaceAt * along, thickness, foam, under ? 1.0 : 0.0);
                o.normal = float4(n, near);
                return o;
            }
            ENDCG
        }

        // 1: what the march found, smoothed along _BlurDirection: the surface's distance (and its water and foam with
        // it) averaged over the pixels round each, as wide on the screen as _SmoothRadius metres are at that distance,
        // keeping steps deeper than _SmoothFalloff (the water's silhouette, one body before another) and never
        // spreading water over pixels that have none. Run a few times each way, the particles' grain goes out of the
        // surface and its light is water's, not gravel's; a bilateral blur, as van der Laan, Green and Sainz (2009).
        Pass
        {
            Name "Smooth"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #include "UnityCG.cginc"

            // What the march found (or the last pass left), and its texel; the direction of this pass; the camera's
            // pixels per metre at one metre (VolumeSurface sets them: a draw into a picture of its own knows none of
            // the camera's).
            sampler2D _Found;
            float4 _FoundTexel;
            float2 _BlurDirection;
            float _PixelsPerMetre;
            float _SmoothRadius;
            float _SmoothFalloff;

            #define TAPS 12

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(uint id : SV_VertexID)
            {
                v2f o;
                float2 corner = float2((id << 1) & 2, id & 2);
                o.pos = float4((corner * 2.0) - 1.0, UNITY_NEAR_CLIP_VALUE, 1.0);
                // (The source was drawn by this same triangle into a picture like this one: the same way up.)
                o.uv = corner;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float4 centre = tex2D(_Found, i.uv);
                if (centre.x <= 0.0)
                {
                    return centre;
                }

                float radius = clamp(_SmoothRadius * _PixelsPerMetre / centre.x, 1.0, 32.0);
                float2 step = _BlurDirection * _FoundTexel.xy * (radius / TAPS);

                // The surface's slope in distance along this direction, from two taps each way (one where only one
                // side has water: the picture's edge, the water's): a tap is compared with the distance the slope
                // predicts there, so a plane is smoothed whichever way it leans and only a real step, past the
                // falloff, breaks the blur. What is averaged is each tap's distance off that plane, not its distance:
                // a neighbourhood with water on one side only (the water's edge) then leaves the surface where it is
                // instead of dragging it toward the side that has taps.
                float far1 = tex2D(_Found, i.uv + (step * 2.0)).x, far0 = tex2D(_Found, i.uv - (step * 2.0)).x;
                float slope = far1 > 0.0 && far0 > 0.0 ? 0.25 * (far1 - far0) : far1 > 0.0 ? 0.5 * (far1 - centre.x) : far0 > 0.0 ? 0.5 * (centre.x - far0) : 0.0;
                slope = abs(slope) < _SmoothFalloff ? slope : 0.0;
                float off = 0.0;
                float3 rest = centre.yzw;
                float weights = 1.0;
                for (int k = 1; k <= TAPS; k++)
                {
                    float spatial = exp(-2.0 * (float(k) / TAPS) * (float(k) / TAPS));
                    float4 a = tex2D(_Found, i.uv + (step * k));
                    float4 b = tex2D(_Found, i.uv - (step * k));
                    float ra = a.x - (centre.x + (slope * k));
                    float rb = b.x - (centre.x - (slope * k));
                    float wa = a.x > 0.0 ? spatial * exp(-(ra * ra) / (_SmoothFalloff * _SmoothFalloff)) : 0.0;
                    float wb = b.x > 0.0 ? spatial * exp(-(rb * rb) / (_SmoothFalloff * _SmoothFalloff)) : 0.0;
                    off += (wa * ra) + (wb * rb);
                    rest += (wa * a.yzw) + (wb * b.yzw);
                    weights += wa + wb;
                }

                return float4(centre.x + (off / weights), rest / weights);
            }
            ENDCG
        }
    }

    FallBack Off
}
