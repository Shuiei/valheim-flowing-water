# FlowingWater

A BepInEx mod for Valheim (server and client): water that flows. A **source** gives water; it runs
downhill over the terrain, pools in hollows, spills on when they fill, and goes on until a **drain**
or the sea takes it. Dig a channel with the pickaxe and the river follows it.

- The **server** simulates every river whole, players nearby or not: the spring on the mountain fills
  the lake at its foot while everyone is elsewhere. Each zone with water keeps its state in the world
  save (a `FlowingWater_Tile` object), so rivers survive restarts.
- **Players** see the water (the game's own water and running-stream materials), swim in it, float on
  it, get wet, and are carried by its current. The game's own water checks (fishing, building,
  creatures) see it too.
- Both sides need the mod. A player without it sees nothing and walks through the water (the server
  keeps the objects for the others).

Status: **0.0.1, a prototype.** The flow model is a simple cellular one (water levels out between
neighbouring cells of one metre, ten times a second); it is tuned for rivers and ponds, not for
waves or splashes. Buildings do not hold water yet: only the terrain does.

## Placing water

Console (F5) command `flowingwater`:

| Command | Does |
|---|---|
| `flowingwater source [m³/s]` | A spring under your feet (default 2 m³/s) |
| `flowingwater drain [m³/s]` | A drain under your feet; 0 or nothing takes all that reaches it |
| `flowingwater remove` | Removes the nearest source or drain within 5 m |
| `flowingwater info` | What is under you: ground, depth, current, the zone's water |
| `flowingwater debug` | What the tile under you draws: materials, shaders, triangles |
| `flowingwater flat` | Plain colours instead of water (blue still, green running), to see the shape |
| `flowingwater lift [m]` | Draw the water that much higher, to find it |
| `flowingwater mat custom\|still\|stream <property> [values]` | Change a water material property in game (`list` names them) |

The command works for everyone unless `CommandsForEveryone` is off in the config (then cheats must
be on). Hammer pieces for the spring and the drain come later.

A server can also have sources and drains with no marker placed, from its config: `FixedSources` and
`FixedDrains` take `x,z,rate` triples separated by `;` (world coordinates, cubic metres per second).

## Settings

`BepInEx/config/Tie.FlowingWater.cfg`:

| Setting | Default | What |
|---|---|---|
| `Enabled` | true | Simulate (server) and show (client) |
| `Server.StepsPerSecond` | 15 | Simulation steps per second |
| `Server.Friction` | 0.5 | How fast a flow loses its momentum, per second |
| `Server.SendSeconds` | 1 | How often a changed zone is sent to the players near it |
| `Server.MaxTiles` | 256 | Most zones with water at once |
| `Server.MaxDepth` | 40 | Deepest a cell fills, metres |
| `Server.EmptyTileSeconds` | 30 | A dry zone without a source forgets its tile after this |
| `Server.FixedSources` | | Sources from the config, `x,z,rate; x,z,rate` |
| `Server.FixedDrains` | | Drains from the config, same form (rate 0 takes all) |
| `Server.DebugSeconds` | 0 | Log each tile as a map of depths this often (0 = off) |
| `Markers.DefaultSourceRate` | 2 | m³/s of a source placed without a rate |
| `Markers.DefaultDrainRate` | 0 | m³/s of a drain placed without a rate; 0 = all |
| `Commands.CommandsForEveryone` | true | The command without cheats |
| `Client.CustomShader` | true | The mod's own water shader; off = the game's materials |
| `Client.CurrentStrength` | 1 | How strongly the current carries swimmers and floating things |
| `Client.FlowVisualMinSpeed` | 0.15 | Water faster than this (m/s) is drawn as a running stream |
| `Client.FlowTextureSpeed` | 0.5 | How fast the stream texture runs |

## How it works

- **Grid.** One tile per zone, 64 × 64 cells of one metre, each at a vertex of the zone's heightmap.
  A cell holds a depth of water over its ground. Water that reaches a cell under sea level is the sea's.
- **Step.** The pipe model: every cell has a pipe to each of its eight neighbours carrying a flow that keeps
  its momentum. Each step the level difference accelerates it (gravity, over the pipe's length), `Friction`
  slows it, it may move at most half the difference (so two cells level out without crossing over), a flow
  pushed uphill dies fast, and a cell never gives more than it holds. The flows then move the water, across
  zone borders too. A cell's net flow divided by its depth is its velocity: the current.
- **Server.** The simulation works on the saved objects (ZDOs) only, never on scene objects, so it runs
  on a dedicated server and in zones no player has loaded. Ground heights come from the world
  generator as the game's heightmap builder makes them, plus the players' terrain changes read from
  each zone's terrain-changes object. A changed tile is written to its ZDO every `SendSeconds`, and the
  game sends it to the players near that zone.
- **Shader.** The mod's own (`unity/Assets/FlowingWater/Shaders/FlowingWater.shader`, built-in render pipeline,
  standard lighting): the mesh carries depth, shore and current per vertex; a ripple normal map is dragged
  along the current in two offset phases (flow mapping), with a slow second layer for still water; colour and
  opacity go from shallow to deep; foam rises with speed and at the shore; the edge fades out.
- **Client.** The tile component eases the depths towards the last ones received and rebuilds its mesh:
  wet vertices at the water surface (15 cm thick at least, so streams show), dry ones beside them carrying the
  surface on into the ground so the shore is where the surface meets the ground, the rest sunk out of sight. Cells faster than `FlowVisualMinSpeed` use the game's running-water material
  (`waterflow`, a scrolling flow shader) with texture coordinates laid along the flow; the rest use the
  sea water material without wind waves. A trigger box on the game's water layer tells characters and
  floating things the surface over them; Harmony patches make the game's water-level checks and the
  level any water volume reports include the flowing water, and let a swimmer drift with the current.

## Building

The water shader lives in a Unity project, `unity/`, and is built into one asset bundle per game platform
(`assets/bundles/flowingwater.windows` and `.linux`), which the plugin embeds. The bundles are committed, so
only a shader change needs Unity: the game's exact version, **6000.0.75f1** (changeset 26349cd2a5c8), with the
Windows Build Support (Mono) module.

```
Unity -batchmode -quit -projectPath unity -executeMethod BuildBundles.Build -logFile -
```

`PreviewRender.Render` (same command, with a display: `xvfb-run`, and `FW_PREVIEW_OUT=<folder>`) draws the
water on a made-up valley to PNG files. `tools/make_textures.py` makes the ripple and foam textures (tileable
noise, no game assets).

Then the plugin:

```
dotnet build -c Release
```

Targets net472 against a Valheim dedicated server's DLLs: `ValheimDir` (default: the folder two levels
up, the workspace this repo lives in) and `BepInExDir` (default `ValheimDir/BepInEx`). A Release build
copies the dll to `BepInExDir/plugins/FlowingWater/`. Analyzers run as in the Valheim world editor
(warnings are errors). The version is `VERSION`.

## Testing

Never on a live server. A local dedicated server with the dll in its `BepInEx/plugins/` and
`FixedSources = 40,40,3` plus `DebugSeconds = 20` in `Tie.FlowingWater.cfg` shows the simulation alone:
every 20 s the log draws each zone's water (`.` dry, `:` `o` `O` `#` deeper, arrows where it runs). For
the rest, the game with the dll in its own `BepInEx/plugins/`, joined to that server: `flowingwater
source` under your feet on a slope, then watch, swim, and `flowingwater info`.

## Rules

- No decompiled game code in the repo.
- One pull request per change; a feature bumps the minor version, a fix the patch; `CHANGELOG.md`
  gets an entry.
- Never tested on a live server: a local dedicated server and the game.

MIT licence.
