# FlowingWater

Water that flows. A **source** gives water; it runs downhill over the terrain, pools in hollows, spills
on when they fill, and goes on until a **drain** or the sea takes it. Dig a channel with the pickaxe and
the river follows it.

- The **server** simulates every river whole, players nearby or not: the spring on the mountain fills
  the lake at its foot while everyone is elsewhere. Rivers are saved with the world.
- **Players** see the water (its sides and its falls too, drawn as the voxels it fills, and from inside it), swim
  in it, float on it, get wet, and are carried by its current; ships are
  placed and sail on it. The game's own water checks (fishing, building, creatures) see it too. Running water
  is silent and throws no spray for now: its sound and spray come back in a later version.

**A prototype.** The flow is tuned for rivers and ponds, not for waves. What you build holds water as the terrain
does: walls, floors, a pool on posts, a plank chute or slide the water runs down.

## Install

On the **server and every player** (a player without it sees nothing and walks through the water).
With a mod manager, install it on both; by hand, put `FlowingWater.dll` in `BepInEx/plugins/` on each.
Needs BepInExPack_Valheim.

## Placing water

With the **hoe**: its pieces include a **Water source**, a **Water drain** and **Remove source or drain**, free; the
first two go exactly where you aim, on the ground or on anything built. With the source or drain chosen, a panel on
the right shows the rate (and a drain's radius) the next one takes: **E** to change them, **Enter** to apply, **Esc**
or **E** when done. Placed ones are invisible, shown only while the hoe is in hand with one of these three chosen, or
the hammer: **E** on a shown one changes it, or removes it with the panel's **Remove** button. With **Remove source or
drain** chosen, the one aimed at turns red and a click removes it. The hammer removes one too, as any piece.

Console (F5) command `flowingwater`:

| Command | Does |
|---|---|
| `flowingwater source [m³/s]` | A spring under your feet (default 2 m³/s) |
| `flowingwater drain [m³/s] [radius m]` | A drain under your feet; 0 or nothing takes all that reaches it, from every spot within the radius (default 1 m) |
| `flowingwater remove` | Removes the nearest source or drain within 5 m |
| `flowingwater info` | What is under you: ground, depth, current, the zone's water |

The host and the server's admins may always use the command and the hoe's source and drain; other players while
the **server's** `CommandsForEveryone` is on (their own config does not count). On a server without FlowingWater the
hoe offers only the game's own pieces; any character learns the source and drain back where the mod is. A source gives at most 100 m³/s, and so does a
drain given a rate (a drain of rate 0 still takes everything that reaches it). A server can also have sources and drains with no marker placed, from its config (`FixedSources`,
`FixedDrains`).

## Settings

`BepInEx/config/Tie.FlowingWater.cfg`. Saving the file is enough, no restart needed: the server and each game read it
again within a couple of seconds. The main ones:

| Setting | Default | What |
|---|---|---|
| `Enabled` | true | Simulate (server) and show (client) |
| `Server.CellSize` | 1 | Side of the water's cubic voxels: 1, 0.5 or 0.25 m. Smaller gives finer water: moving water costs about 3× at 0.5 and 15× at 0.25 (measured on a 20 × 20 m pool; more water, more cost), still water stays cheap |
| `Server.StepBudget` | 8 | Most milliseconds of each server frame the water may take, on average; beyond, the water slows down instead of the server |
| `Server.MaxBricks` | 1024 | Most blocks of 16 × 16 × 16 voxels with water at once |
| `Commands.CommandsForEveryone` | true | Players who are not admins may use the command and place sources and drains (the server's setting counts) |
| `Client.CustomShader` | true | The mod's own water shader, with the sea's look and running water; off = the game's still sea material |
| `Client.CurrentStrength` | 1 | How strongly the current carries swimmers and floating things |
| `Client.WaterVolume` | 0.7 | Unused for now (waits for the water's sound on the 3D water): how loud running water is (0 = silent) |
| `Client.Spray` | true | Unused for now (waits for spray on the 3D water): spray where the water rushes down steep ground |

Every setting, how the simulation works and how to build it:
[the project's page](https://github.com/Shuiei/valheim-flowing-water#readme).

## Source and issues

[github.com/Shuiei/valheim-flowing-water](https://github.com/Shuiei/valheim-flowing-water): bug reports
and ideas there. MIT licence.
