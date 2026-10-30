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
// The volume water: it draws the whole square itself (the texels say nothing of what it holds), but for a band
// _FWSimEdge metres wide along the square's sides, where it fades out and the bricks draw as the texels say; and only
// between _FWSimBottom and _FWSimTop (world y), the grid's height, which follows the camera: water in the square above
// or below the grid is the bricks' to draw.
float _FWSimWhole;
float _FWSimEdge;
// The part of the square the volume has filled (world x, z of its south-west corner, then of its north-east one):
// the square, but for the strip a move of it uncovered until that strip has taken the server's water (the bricks
// draw it meanwhile: left to the volume, it stood empty until its water ran in from the side).
float4 _FWSimFilled;
float _FWSimBottom;
float _FWSimTop;

// For testing (console "flowingwater gpu colours"): each part of the water drawn in a colour of its own, to see which
// draws where (the card's water blue, a brick's surface red, the sides and falls yellow).
float _FWDebugColours;

void DebugColour(inout SurfaceOutputStandard o, float3 colour)
{
    if (_FWDebugColours > 0.5)
    {
        o.Albedo = colour;
        o.Emission = colour * 0.6;
        o.Alpha = max(o.Alpha, 0.7);
    }
}

// Whether the water moved on the card draws the water at this world place (y the water's height there).
bool SimDraws(float3 world)
{
    if (_FWSimOn < 0.5)
    {
        return false;
    }

    float2 uv = (world.xz - _FWSimOrigin) / _FWSimSize;
    if (any(uv <= 0.0) || any(uv >= 1.0))
    {
        return false;
    }

    float2 edge = min(world.xz - _FWSimFilled.xy, _FWSimFilled.zw - world.xz);
    if (_FWSimWhole > 0.5 && min(edge.x, edge.y) > _FWSimEdge)
    {
        // The volume draws within its grid's height but for a band as wide along its top and bottom, where it fades
        // out as at the square's sides and the bricks draw the water beyond (a stream down a hill taller than the
        // grid blends from one into the other, not a seam across it).
        return world.y >= _FWSimBottom + _FWSimEdge && world.y <= _FWSimTop - _FWSimEdge;
    }

    return tex2Dlod(_FWSimTarget, float4(uv, 0.0, 0.0)).y > 0.5;
}

#endif
