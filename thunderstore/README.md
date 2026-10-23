# FlowingWater

Water that flows. A **source** gives water; it runs downhill over the terrain, pools in hollows, spills
on when they fill, and goes on until a **drain** or the sea takes it. Dig a channel with the pickaxe and
the river follows it.

- The **server** simulates every river whole, players nearby or not: the spring on the mountain fills
  the lake at its foot while everyone is elsewhere. Rivers are saved with the world.
- **Players** see the water, swim in it, float on it, get wet, and are carried by its current. The
  game's own water checks (fishing, building, creatures) see it too. Fast streams babble, falls roar, and
  water rushing down steep ground throws spray.

**A prototype.** The flow is tuned for rivers and ponds, not for waves. Buildings do not
hold water yet: only the terrain does.

## Install

On the **server and every player** (a player without it sees nothing and walks through the water).
With a mod manager, install it on both; by hand, put `FlowingWater.dll` in `BepInEx/plugins/` on each.
Needs BepInExPack_Valheim.

## Placing water

Console (F5) command `flowingwater`:

| Command | Does |
|---|---|
| `flowingwater source [m³/s]` | A spring under your feet (default 2 m³/s) |
| `flowingwater drain [m³/s] [radius m]` | A drain under your feet; 0 or nothing takes all that reaches it, from every spot within the radius (default 1 m) |
| `flowingwater remove` | Removes the nearest source or drain within 5 m |
| `flowingwater info` | What is under you: ground, depth, current, the zone's water |

The host and the server's admins may always use the command; other players while the **server's**
`CommandsForEveryone` is on (their own config does not count). A source gives at most 100 m³/s, and so does a
drain given a rate (a drain of rate 0 still takes everything that reaches it). A server can also have sources and drains with no marker placed, from its config (`FixedSources`,
`FixedDrains`).

## Settings

`BepInEx/config/Tie.FlowingWater.cfg`. The main ones:

| Setting | Default | What |
|---|---|---|
| `Enabled` | true | Simulate (server) and show (client) |
| `Server.CellSize` | 1 | Side of the water's cubic voxels: 1, 0.5 or 0.25 m. Smaller gives finer water: moving water costs about 3× at 0.5 and 15× at 0.25 (measured on a 20 × 20 m pool; more water, more cost), still water stays cheap |
| `Server.StepBudget` | 8 | Most milliseconds of each server frame the water may take, on average; beyond, the water slows down instead of the server |
| `Server.MaxBricks` | 1024 | Most blocks of 16 × 16 × 16 voxels with water at once |
| `Commands.CommandsForEveryone` | true | Players who are not admins may use the command (the server's setting counts) |
| `Client.CustomShader` | true | The mod's own water shader; off = the game's materials |
| `Client.WaterClarity` | 4.5 | How far one sees into the water (m): shallow stays clear, deep takes the water's colour |
| `Client.WaterColour` | 081C24FF | The colour deep water takes (RGBA hex, RRGGBBAA) |
| `Client.CurrentStrength` | 1 | How strongly the current carries swimmers and floating things |
| `Client.WaterVolume` | 0.7 | How loud running water is (0 = silent), within the game's sound-effects volume |
| `Client.Spray` | true | Spray where the water rushes down steep ground |

Every setting, how the simulation works and how to build it:
[the project's page](https://github.com/Shuiei/valheim-flowing-water#readme).

## Source and issues

[github.com/Shuiei/valheim-flowing-water](https://github.com/Shuiei/valheim-flowing-water): bug reports
and ideas there. MIT licence.
