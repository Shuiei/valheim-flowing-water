// FlowingWater's air trapped by running water, one rule for the card's foam (FlowingWaterSim.compute's CarryFoam) and
// its spray's froth (FlowingWaterSpray.compute's EmitFromWater), so both foam on the same water: where water runs into
// slower water beside it (how much faster it comes on toward each side than the water there goes, summed: a jump, a
// fall's foot, a front into a pool; Crest's sea foams where its waves pinch together), the more the faster it runs.
// The shader that includes it declares the card's water first: _U and _V (the speeds through each cell's east and north
// faces), _H (each cell's depth), _Bed (its ground: TRAPPED_WALL or more, a wall or ground not known) and Inside(c).
#ifndef FLOWINGWATER_TRAPPED_INCLUDED
#define FLOWINGWATER_TRAPPED_INCLUDED

// Water running into the water beside it TRAPPED_FROM faster than that goes (m/s, summed over its four sides) traps
// none, from TRAPPED_FULL all it will; water slower than TRAPPED_SLOW (m/s) traps none, all it will from TRAPPED_FAST.
// Beside it, water shallower than TRAPPED_WATER (m), a wall or ground not known is nothing it runs into.
#define TRAPPED_FROM 0.5
#define TRAPPED_FULL 3.0
#define TRAPPED_SLOW 1.5
#define TRAPPED_FAST 5.0
#define TRAPPED_WATER 0.01
#define TRAPPED_WALL 5000.0

// The water's velocity along the ground at a cell's middle (m/s, east and north): its faces' speeds averaged.
float2 CellSpeed(int2 c)
{
    int2 west = max(c - int2(1, 0), 0);
    int2 south = max(c - int2(0, 1), 0);
    return float2(0.5 * (_U[c] + _U[west]), 0.5 * (_V[c] + _V[south]));
}

// How much air the water of cell c traps, running at this velocity (its middle's): 0 none, 1 all it will.
float TrappedAir(int2 c, float2 speed)
{
    float fast = length(speed);
    if (fast <= TRAPPED_SLOW)
    {
        return 0.0;
    }

    int2 around[4] = { int2(1, 0), int2(-1, 0), int2(0, 1), int2(0, -1) };
    float trapped = 0.0;
    for (int s = 0; s < 4; s++)
    {
        int2 n = c + around[s];
        if (Inside(n) && _H[n] >= TRAPPED_WATER && _Bed[n] < TRAPPED_WALL)
        {
            trapped += max(dot(speed - CellSpeed(n), float2(around[s])), 0.0);
        }
    }

    float potential = saturate((trapped - TRAPPED_FROM) / (TRAPPED_FULL - TRAPPED_FROM));
    float energy = saturate(((fast * fast) - (TRAPPED_SLOW * TRAPPED_SLOW)) / ((TRAPPED_FAST * TRAPPED_FAST) - (TRAPPED_SLOW * TRAPPED_SLOW)));
    return potential * energy;
}

#endif
