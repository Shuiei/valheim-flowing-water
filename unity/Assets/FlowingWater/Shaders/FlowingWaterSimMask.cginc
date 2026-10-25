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

// Whether the water moved on the card draws the water at this world x, z.
bool SimDraws(float2 world)
{
    if (_FWSimOn < 0.5)
    {
        return false;
    }

    float2 uv = (world - _FWSimOrigin) / _FWSimSize;
    return all(uv > 0.0) && all(uv < 1.0) && tex2Dlod(_FWSimTarget, float4(uv, 0.0, 0.0)).y > 0.5;
}

#endif
