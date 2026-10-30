// Shallow still water as the water's shaders draw it alike (the bricks' surface, FlowingWaterLook.cginc, and the volume
// water's, FlowingWaterVolumeSurface.shader), so the two look one where they meet: it keeps this share of its ripples at
// least (none of it as the water runs: running water looks as it did), and thinner than SHALLOW_ABSORB metres it is down
// to this share as glossy (its film over the ground is never as smooth as a pool's top).
#ifndef FLOWINGWATER_SHALLOW_INCLUDED
#define FLOWINGWATER_SHALLOW_INCLUDED

#define SHALLOW_RIPPLES 0.5
#define SHALLOW_ABSORB 0.8
#define SHALLOW_GLOSS 0.85

#endif
