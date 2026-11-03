// Where the water moved on the graphics card near the camera draws the water (FlowingWaterSim.shader), shared by the
// shaders that draw the water from the server's bricks, which leave those places to it.
#ifndef FLOWINGWATER_SIM_MASK_INCLUDED
#define FLOWINGWATER_SIM_MASK_INCLUDED

// The water moved on the graphics card near the camera (set for every shader while it runs): whether it does, the
// world x, z of its square's south-west corner and its side (m), and a texel per voxel column, y = 1 where it draws
// the water (a brick then does not).
float _FWSimOn;
float2 _FWSimOrigin;
float _FWSimSize;
sampler2D _FWSimTarget;
// The volume water: a box inside the square, round the camera, that it draws itself (the texels say nothing of what
// it holds), but for a band _FWSimEdge metres wide along its sides, top and bottom, where it fades out and the card's
// water draws as the texels say. _FWSimFilled is the part of its box it has filled (world x, z of its south-west
// corner, then of its north-east one): the box, but for the strip a move of it uncovered until that strip has taken the
// server's water (left to the volume, it stood empty until its water ran in from the side). _FWSimBottom and _FWSimTop
// are its grid's height (world y), which follows the camera: water above or below it is the card's, or the bricks'.
float _FWSimWhole;
float _FWSimEdge;
float4 _FWSimFilled;
float _FWSimBottom;
float _FWSimTop;

// For testing (console "flowingwater gpu colours"): each part of the water drawn in a colour of its own, to see which
// draws where (the card's water blue, a brick's surface red, the sides and falls yellow).
float _FWDebugColours;

// (A shader drawn without the surface lighting, the spray's and the falls', defines FLOWINGWATER_SIM_MASK_PLAIN before
// including this: it has no SurfaceOutputStandard.)
#ifndef FLOWINGWATER_SIM_MASK_PLAIN
void DebugColour(inout SurfaceOutputStandard o, float3 colour)
{
    if (_FWDebugColours > 0.5)
    {
        o.Albedo = colour;
        o.Emission = colour * 0.6;
        o.Alpha = max(o.Alpha, 0.7);
    }
}
#endif

// Whether the volume water draws the water at this world place (y the water's height there): inside the part of its box
// it filled, but for the band along its sides, top and bottom (a stream down a hill taller than the grid blends from
// one into the other, not a seam across it).
bool VolumeDraws(float3 world)
{
    if (_FWSimOn < 0.5 || _FWSimWhole < 0.5)
    {
        return false;
    }

    float2 edge = min(world.xz - _FWSimFilled.xy, _FWSimFilled.zw - world.xz);
    return min(edge.x, edge.y) > _FWSimEdge && world.y >= _FWSimBottom + _FWSimEdge && world.y <= _FWSimTop - _FWSimEdge;
}

// Whether the card's sheet of water draws the water at this world place: in its square, where the texels give it the
// column, and not where the volume draws it.
bool CardDraws(float3 world)
{
    if (_FWSimOn < 0.5 || VolumeDraws(world))
    {
        return false;
    }

    float2 uv = (world.xz - _FWSimOrigin) / _FWSimSize;
    if (any(uv <= 0.0) || any(uv >= 1.0))
    {
        return false;
    }

    return tex2Dlod(_FWSimTarget, float4(uv, 0.0, 0.0)).y > 0.5;
}

// Whether the water moved on the card, the sheet or the volume, draws the water at this world place (a brick then
// does not).
bool SimDraws(float3 world)
{
    return VolumeDraws(world) || CardDraws(world);
}

#endif
