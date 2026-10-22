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

The command works for everyone unless `CommandsForEveryone` is off in the config (then cheats must be
on). A server can also have sources and drains with no marker placed, from its config (`FixedSources`,
`FixedDrains`).

## Settings

`BepInEx/config/Tie.FlowingWater.cfg`. The main ones:

| Setting | Default | What |
|---|---|---|
| `Enabled` | true | Simulate (server) and show (client) |
| `Server.CellSize` | 1 | Size of the simulation's cells: 1, 0.5 or 0.25 m. Smaller gives narrower streams at a higher cost |
| `Server.MaxTiles` | 256 | Most zones with water at once |
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
