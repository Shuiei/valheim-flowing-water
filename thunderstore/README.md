# FlowingWater

Water that flows. A **source** gives water; it runs downhill over the terrain, pools in hollows, spills
on when they fill, and goes on until a **drain** or the sea takes it. Dig a channel with the pickaxe and
the river follows it.

- The **server** simulates every river whole, players nearby or not: the spring on the mountain fills
  the lake at its foot while everyone is elsewhere. Rivers are saved with the world.
- **Players** see the water (its sides and its falls too, drawn as the voxels it fills, and from inside it), swim
  in it, float on it, get wet, and are carried by its current; ships are
  placed and sail on it. The game's own water checks (fishing, building, creatures) see it too. Running water
  is silent for now: its sound comes back in a later version.
- Near you, **your graphics card moves the water** between the server's changes, as water moves: let go, it surges
  out over the ground, runs up the walls and sloshes back; waves cross a pool and come back. The server still says
  how much water is where (and what you swim in); a card without compute shaders draws it as the server sends it.
  Water pouring off a ledge falls as one curtain that arcs out and churns the pool where it lands, and fast water
  throws spray.
- **Running water looks it**: a deeper colour that hides what is under it, foam drawn out in streaks along the
  current, and waves that rise with its speed, near you and far off.
- **Water has a body**: what lies under or behind it fades, blurs and turns blue-green with how much water you look
  through, so a pool reads as water rather than a tinted film. Near you it ends in a smooth, rounded front where it
  meets dry ground, and a thin sheet lies smooth over the ground's small bumps instead of breaking into patches.

**A prototype.** The server's flow is tuned for rivers and ponds; the waves are your graphics card's. Whatever is solid holds water as the terrain
does: what you build (walls, floors, a pool on posts, a plank chute or slide the water runs down), rocks, trees and
the ruins of the game's places, each by its own shape; the water runs round a rock and off its top, and through a
notch mined in it.

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
the **server's** `CommandsForEveryone` is on (their own config does not count). The server simulates only the sources
and drains placed by a player who may: with `CommandsForEveryone` off, the host's and the admins', checked while they
are on the server and remembered after. On a server without FlowingWater the
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
| `Commands.CommandsForEveryone` | true | Players who are not admins may use the command and place sources and drains (the server's setting counts, and it simulates only what a player who may placed) |
| `Client.CustomShader` | true | The mod's own water shader, with the sea's look and running water; off = the game's still sea material |
| `Client.GpuWater` | true | Move the water near you on the graphics card between the server's changes; off = drawn as the server sends it |
| `Client.GpuWaterRange` | 96 | Side of the square round you whose water the card moves, metres |
| `Client.GpuWaterCell` | 0.25 | Side of the card's cells, metres (0.125, 0.25, 0.5 or 1): smaller is finer and costs the card more |
| `Client.CurrentStrength` | 1 | How strongly the current carries swimmers and floating things |
| `Client.WaterVolume` | 0.7 | Unused for now (waits for the water's sound on the 3D water): how loud running water is (0 = silent) |
| `Client.Spray` | true | Falls (as curtains), splashes and spray on your graphics card (with `Client.GpuWater`) |
| `Client.ParticleWater` | false | A prototype: water running over a lip near you falls as particles of water that break up, splash and pool, and give their water back where they land (with `Client.Spray`); the console's `flowingwater particles` pours some either way |
| `Client.ParticleCapacity` | 65536 | Particles of the particle water at most (4096 to 262144); more costs the card more |

Every setting, how the simulation works and how to build it:
[the project's page](https://github.com/Shuiei/valheim-flowing-water#readme).

## Source and issues

[github.com/Shuiei/valheim-flowing-water](https://github.com/Shuiei/valheim-flowing-water): bug reports
and ideas there. MIT licence.
