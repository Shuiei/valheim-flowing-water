# FlowingWater

A BepInEx mod for Valheim (server and client): water that flows. A **source** gives water; it runs
downhill over the terrain, pools in hollows, spills on when they fill, and goes on until a **drain**
or the sea takes it. Dig a channel with the pickaxe and the river follows it.

- The **server** simulates every river whole, players nearby or not: the spring on the mountain fills
  the lake at its foot while everyone is elsewhere. Each zone with water keeps its state in the world
  save (a `FlowingWater_Tile` object), so rivers survive restarts.
- **Players** see the water (the game's own water and running-stream materials), swim in it, float on
  it, get wet, and are carried by its current; ships are placed and sail on it. The game's own water
  checks (fishing, building, creatures) see it too.
- Both sides need the mod. A player without it sees nothing and walks through the water (the server
  keeps the objects for the others).

Status: **a prototype.** The flow model is a shallow-water one on a grid of cells (one metre by default,
fifteen steps a second); it is tuned for rivers and ponds, not for
waves or splashes. What players build holds water as the terrain does (walls, floors, a pool on posts, a plank
chute).

## Placing water

With the **hoe**: among its pieces are a **Water source**, a **Water drain** and **Remove source or drain**, free to
build. The source and the drain each go exactly where you aim, on the ground or on anything built (a floor, a wall's
top). While the source or the drain is the hoe's piece, a panel on the right of the screen shows the rate (m³/s), and
for a drain the radius (m), that the next one placed takes: **E** lets you change them (the mouse is free and the game
takes no keys while you type; **Enter** applies, **Esc** or **E** is done). They start from `DefaultSourceRate`,
`DefaultDrainRate` and `DefaultDrainRadius` and stay as you set them until you quit the game. Placed sources and
drains are invisible: they show only while the hoe is in hand with one of these three chosen, or the hammer. Looking
at a shown one tells what it does; **E** on it opens the panel to change it, or to take it away with the panel's
**Remove** button. With **Remove source or drain** chosen, the one you aim at turns red and a click takes it away; the
hoe removes nothing else. The hammer breaks one as any piece: with it in hand, the one aimed at turns red, and its
remove takes it away.

A source's rate is above 0 and a drain's 0 or more (0 takes everything that reaches it), each 100 m³/s at most: the
panel holds a higher rate to 100, as the server does, and says so. A drain's radius is 0.25 to 16 m.

Console (F5) command `flowingwater`:

| Command | Does |
|---|---|
| `flowingwater source [m³/s]` | A spring under your feet (default 2 m³/s) |
| `flowingwater drain [m³/s] [radius m]` | A drain under your feet; 0 or nothing takes all that reaches it, from every cell within the radius (default 1 m) |
| `flowingwater remove` | Removes the nearest source or drain within 5 m |
| `flowingwater info` | What is under you: ground, depth, current, the zone's water |
| `flowingwater debug` | What the block of water you are in (or the nearest over or under you) draws: material, shader, triangles |
| `flowingwater solids` | What the coming 3D water will treat as solid round you: a map of the voxel row at your feet (solid voxels, faces walls close) and the faces under it (floors) |
| `flowingwater gpu [colours\|columns]` | What the graphics card's water near you is doing: its square, the ground found under it, its steps a frame; `colours` draws each part in its own colour (the card's water blue, a block's surface red, sides and falls yellow), again to stop; `columns` logs the water and ground of the nine voxel columns round you |
| `flowingwater flat` | A plain colour (blue) instead of water, to see the shape |
| `flowingwater lift [m]` | Draw the water that much higher, to find it |
| `flowingwater mat custom\|still <property> [values]` | Change a water material property in game (`list` names them) |

Who may use the command, and place, change or remove sources and drains with the hoe: the host (and a player
alone) and the server's admins (its `adminlist.txt`) always; other players while the server's `CommandsForEveryone`
is on. The server's setting counts, not the player's: the server tells each player's game its value when they join,
and again when it changes. The hoe offers the source and the drain only to a player who may; one who may not (where
the game offers every piece, building costs off) is told why in the middle of the screen, as when changing or
removing one. A character learns the source and drain as soon as it is in a world where FlowingWater is (alone,
hosting, or on a server running it), whatever its age, and forgets them on a server without it: the hoe there offers
only the game's own pieces.

The server checks too, since a player's own setting could be changed to skip the game's check: it simulates a placed
source or drain only once it knows its placer may place one. While `CommandsForEveryone` is on, anyone's; else one
placed by the host or an admin, checked while they are on the server and remembered after, in a file of the server's
(`BepInEx/config/Tie.FlowingWater.<world>.markers`, one marker a line). One placed by someone else, or whose placer is
not on the server to be checked, is ignored, and the log says so once. The check goes by what the player's game wrote
into the marker about who placed it, as the game itself does for every piece: a game changed to write a false placer
gets past it, as it would past any check the game's world allows, so it keeps honest games to the server's setting
rather than stopping a cheat. Sources and drains placed with the console
before v0.24 carry no placer: with `CommandsForEveryone` off, turn it on for a moment to allow every marker in the
world, or place them again. The config's `FixedSources` and `FixedDrains` need no check.

A server can also have sources and drains with no marker placed, from its config: `FixedSources` and
`FixedDrains` take `x,z,rate` triples separated by `;` (world coordinates, cubic metres per second); a drain may
add its radius in metres, `x,z,rate,radius`. The server holds every source's and drain's rate, placed or fixed, to
100 m³/s at most (and a drain's radius to 16 m), and logs each one it holds once; a drain of rate 0 still takes
everything that reaches it.

## Settings

`BepInEx/config/Tie.FlowingWater.cfg`. Saving the file is enough: the server and each game read it again within a
couple of seconds, no restart needed, and log the settings that changed.

| Setting | Default | What |
|---|---|---|
| `Enabled` | true | Simulate (server) and show (client) |
| `Server.CellSize` | 1 | Side of the water's cubic voxels: 1, 0.5 or 0.25 m (see [Grid size](#grid-size)) |
| `Server.Erosion` | false | Running water wears the ground away and lays it down; not within 5 m of what players built (paused with the 3D water, back in a later version) |
| `Server.ErosionSpeed` | 1 | How fast erosion goes (1 = millimetres a minute in a fast stream); higher for testing |
| `Server.SendSeconds` | 1 | How often a block of water whose change would show is sent to the players near it |
| `Server.StepBudget` | 8 | Most milliseconds of each server frame the water may take, on average; beyond, the water slows down instead of the server |
| `Server.MaxBricks` | 1024 | Most blocks of 16 × 16 × 16 voxels with water at once (about 130 KB each while the water moves, 37 KB once still; what the last few dozen blocks to go still let go of is kept for the next to wake) |
| `Server.EmptyTileSeconds` | 30 | A block of voxels dry this long is forgotten |
| `Server.MarkerScanSeconds` | 3 | Seconds from the end of one look for new or removed sources and drains to the next (1 to 60; a look over a big world itself takes a few seconds) |
| `Server.FixedSources` | | Sources from the config, `x,z,rate; x,z,rate` |
| `Server.FixedDrains` | | Drains from the config, same form (rate 0 takes all) |
| `Server.DebugSeconds` | 0 | Log each zone with water as a map of depths this often (0 = off) |
| `Markers.DefaultSourceRate` | 2 | m³/s of a source placed without a rate (the hoe's panel starts from it) |
| `Markers.DefaultDrainRate` | 0 | m³/s of a drain placed without a rate; 0 = all (the hoe's panel starts from it) |
| `Markers.DefaultDrainRadius` | 1 | How far around it a drain placed without a radius takes water, metres (the hoe's panel starts from it) |
| `Commands.CommandsForEveryone` | true | Players who are not admins may use the command and place, change and remove sources and drains (the server's setting counts, and the server simulates only the sources and drains placed by a player who may; the host and admins always may) |
| `Client.CustomShader` | true | The mod's own water shader, with the sea's look and running water; off = the game's still sea material |
| `Client.GpuWater` | true | Move the water near you on the graphics card between the server's changes (see [Water on the graphics card](#water-on-the-graphics-card)); off = drawn as the server sends it |
| `Client.GpuWaterRange` | 96 | Side of the square round you whose water the card moves, metres (32 to 256) |
| `Client.GpuWaterCell` | 0.25 | Side of the card's cells, metres (0.125, 0.25, 0.5 or 1; never larger than the server's voxels) |
| `Client.CurrentStrength` | 1 | How strongly the current carries swimmers and floating things |
| `Client.FlowVisualMinSpeed` | 0.15 | Unused for now (the column water's): water faster than this (m/s) was drawn as a running stream |
| `Client.FlowTextureSpeed` | 0.5 | Unused for now (the column water's): how fast the stream texture ran |
| `Client.Spray` | true | With `Client.GpuWater`, the water's falls, splashes and spray as particles on the graphics card (see [Falls, splashes and spray](#falls-splashes-and-spray)); off = falls drawn by the blocks |
| `Client.WaterVolume` | 0.7 | Unused for now (waits for the water's sound on the 3D water): how loud running water is (0 = silent) |

### Water on the graphics card

The server says how much water is where, about once a second; between its changes each player's graphics card moves
the water near them as water moves. In a square round the camera (`Client.GpuWaterRange`, 96 m), cut into cells of
`Client.GpuWaterCell` (a quarter metre: sixteen to a metre voxel), the card runs shallow water: a depth over the
ground in each cell, a speed through each face between two, carried along by the water as Stelling and Duinmeijer's
scheme carries it, so a front runs out at the speed it should; pushed by the levels, slowed by the ground (Manning's
friction, as the server's). A dam of 2 m let go in a 10 m tank reaches its far wall in 1.25 s (a real one in about
1.3 s; the server alone, 2.9 s), runs up it and sloshes back; a pool tipped rocks for half a minute. Each frame the
water of each voxel column is pulled toward the server's level there (the server keeps the say on how much water is
where), so the waves finer than a voxel are the card's own.

- **Where it draws.** Where the server's water is one layer resting on the ground. Water falling through the air, a
  pool under another and water under a roof are drawn by the blocks, as before, and so is everything beyond the
  square, and a band 4 m wide along its edge (where the card's water is the server's as it is, or still pulled toward
  it). The square moves with the camera once it is a quarter of the square from its middle, its water moved with it
  where both squares have it (waves and currents go on, nothing refills); the new edge's water is the server's, and
  the blocks go on drawing each of its columns for 3 s after the card can have it, while the card's water there
  settles from the server's steps (falls too: the card throws none from a column it does not draw).
- **The ground.** Rays cast straight down onto the terrain and what players built, a few rows of cells a frame on the
  game's worker threads, all of the square in a fraction of a second, then again and again (what is built, a door
  opened); only near water. Rows where water comes near, or rises or falls more than 25 cm, are cast first, so a
  flood's front is the card's as it spreads. Under the water from just over its top, elsewhere from 3 m over the water
  near it, so walls round a pool hold the card's water; only the faces pieces close at the water's level are walls
  besides (the server's faces closed by the ground itself, each step of a slope, are left to the ground cast).
- **Running water's surface.** Running water is drawn with waves on it, running with the current: chop that rises
  with its speed (1.5 cm for each (m/s) squared, at most 15 cm and a quarter of its depth: still water lies flat, a
  thin sheet barely ripples), lit as the waves it is, and a finer layer of ripples on fast water that catches the sun
  in glints. The long waves (2.4 m) are raised on the surface, by the blocks beyond the square too, so they run on
  past its edge; the fine ones (0.8 m) are only lit, at each point drawn, near and far alike. White water gathers on the tops of fast chop's crests and where the water slows hard into the water
  ahead (piling up against a rock or a post, a jump from shooting water to deep), besides the foam of fast water
  running with it. Only the look: the card's water does not move by these waves.
- **Only the look.** Nothing of it is sent: swimming, floating, ships and the game's water checks read the server's
  water as before. On a card without compute shaders, with `Client.GpuWater` or `Client.CustomShader` off, the water
  is drawn as the server sends it.
- **Cost.** A frame takes two to four steps of a few passes over the square's cells on the graphics card: on a Radeon
  RX 9070 XT, 0.07 ms a frame at 60 fps for the 96 m square of quarter metre cells (0.13 ms at 30 fps; 0.17 ms for a
  square four times as big), measured by `tools/sim-preview.sh`; drawing its surface comes on top. Nothing more on
  the server.

`tools/sim-preview.sh <folder>` runs the card's water (the plugin's own `SimGrid` and compute shader) in the Unity
editor on a dam break, a sloshing tank, a block of water let go on a slope with a block in its way, and a basin
following a server's level, and writes what it measured (`sim.txt`, with the dam break's depth against Ritter's, and
what a frame costs the card) and frames.

### Falls, splashes and spray

With `Client.Spray` on, the graphics card also throws particles from the water it moves (nothing of them is sent
anywhere: the server's water still says how much water is where):

- **Falls.** Water running over a lip (the ground beyond more than 0.6 m lower than its own) leaves it as streaks of
  falling water, going over the brink as fast as water does (the square root of gravity times its depth), so a stream
  off a ledge arcs out. The streaks carry that water: it leaves the card's sheet at the lip and is added where they
  land, running on the way it fell, so the pool forms where the fall lands, not at the foot of the wall. Where the server's water falls through the air from
  high (more than 1.5 m over the ground under it), or into a pool, the blocks no longer draw it near you: the card
  throws falling water from the height it falls from, and keeps the pool under it. Where the edge of a build crosses a
  voxel, the water on the build and the pool at its foot each keep their own level (the ground found under the card's
  cells says which is which), so the blocks no longer draw a slanted face between them.
- **Splashes.** Falling water landing fast (over 3 m/s) on the water or the ground throws up droplets.
- **Spray.** Water at least 10 cm deep running faster than 4.5 m/s throws a few fine droplets off its surface (a thin
  sheet running down a slope throws none).

At most 32768 particles live at once (the oldest give way); one that leaves the square, or flies over ground the card
has not found, fades out there within a quarter of a second, and none is drawn much longer than a metre along its
motion. Moving them took 0.02 ms a frame on a Radeon RX 9070 XT (`tools/sim-preview.sh`, which also runs a shelf of
water poured off its lip); drawing them comes on top, and grows with how many there are on screen. A wall of water
between two levels (a ledge with water on its top and at its foot) is left to the falls: the card's surface fades out
where the water a cell away is more than half a metre higher or lower.

### Grid size

`Server.CellSize` sets the side of the water's voxels. Smaller voxels give finer streams, shores and falls. Each
halving gives up to eight times the voxels, stepped about twice as often, but moving water takes almost all the
time: still water sleeps at any size. A 20 × 20 m pool 2 m deep, stirred by a splash, took this much time per second
of water on a desktop PC (the game's server runs the same work about 3 times slower):

| `CellSize` | Moving | Still | Blocks of voxels |
|---|---|---|---|
| 1 | 6 ms | 0.1 ms | 12 |
| 0.5 | 18 ms | 0.2 ms | 24 |
| 0.25 | 95 ms | 0.7 ms | 74 |

The data sent to players and the server's memory grow with the blocks. 1 m suits most servers; 0.5 is for builds
and small streams, 0.25 for tests. The blocks of voxels are stepped in parallel, so the server's cost spreads over
its cores. Changing the setting moves the water already
there to the new grid (same volume), at once when the file is saved.

## How it works

- **Grid.** Water lives in 3D: cubic voxels of `CellSize` metres, kept in blocks of 16 × 16 × 16 (bricks)
  that exist only where water is or lately was. A voxel holds a share of its volume of water (a little more
  when pressed in). The ground fills each voxel from its bottom up to the terrain's height; building pieces, as
  the boxes of their solid colliders, fill the voxels they stand in from below (a floor) and close the faces
  between voxels they separate, so a wall thinner than a voxel holds water, a box on posts holds a pool, and
  breaking a piece lets the water out. A piece only in a voxel's upper half is that voxel's ceiling (the water on
  it is in the voxel above), and two voxels side by side pass water only at a height where both have room: a thin
  sloping plank, a ceiling here and a floor there, keeps the water running down it on top of it. The solids are read again when a piece is built, broken, moved, opened
  or closed (a door), or the ground changes. Water that reaches the sea is the sea's.
- **Step.** The pipe model in three dimensions: each face between two voxels is a pipe whose flow keeps its
  momentum and is carried along by the water coming through it (upwind, along the face's own axis: a surge running
  onto dry ground keeps the speed of the water behind it, and water off an edge keeps going the way it went), sped up
  by the difference of the two voxels' heads, and slowed by drag up and down and by the ground sideways (Manning's
  friction of a natural bed, 0.03: far more on a thin sheet than on deep water). A dam of 2 m let go in a 10 m tank
  reaches its far wall in 2.1 s (2.9 s before the momentum was carried; a real one in about 1.3 s, the graphics
  card's near you in 1.25 s). It costs the server about 40% more for each voxel of moving water. A voxel's head is its floor
  plus all the water resting on it in its column (up to air, a solid or a closed face), so a still pool is still,
  water over air falls, and a hole low in a box's side lets water out faster the deeper the box; water pressed in
  under a ceiling adds a stiff head, so a pipe carries the push of the water above it. A voxel never gives more
  than it holds. Water wets new ground as a rivulet does: sideways onto dry ground no lower than its own (across a
  slope, over flat ground) it goes only from water standing 3 cm deep, while dry ground lower down is run onto
  freely; so a stream runs down a slope as a sheet some metres wide (about 9 m at half a cubic metre a second: the
  water fanning out at its source keeps going sideways a while) instead of across the whole slope, and a sheet
  stops spreading as it thins (water already on wet ground flows as before, and none is lost). Long steps are
  split so the water stays stable 10 m deep. Water that is not a number (a ground height the game gives as none, a
  brick saved so) is emptied where it is found and counted in the log, the water round it going on. Each step works only on the part of a block round its water (and its
  borders where the next block has water), and a block whose water has barely moved for a second sleeps (skipped)
  until water is poured in, a solid changes or a block beside it moves: still pools cost almost nothing. A source
  pours 0.3 m over its point (the first open voxel there, over the piece it stands on); a drain takes from every
  voxel within its radius.
- **Server.** The simulation works on the saved objects (ZDOs) only, never on scene objects, so it runs on a
  dedicated server and where no player is. Ground heights come from the world generator as the game's heightmap
  builder makes them, plus the players' terrain changes; pieces come from their objects and the prefabs'
  colliders. Each block with water is written to its own object (`FlowingWater_Brick`) when it changed in a way a
  player would see, every `SendSeconds` (packed a few blocks a frame, written together once all are), and the game
  sends it to the players near it: a voxel's water over the thinnest drawn (1 cm) changing by more than 1 % of a
  voxel, its floor, or its current by a few steps. A running stream's water stirs all the time, its edges' traces
  coming and going: changes too small to show are held back until they add up, or ten sends at most, so a stream's
  blocks are sent about a third less often and still water's hardly at all. Before the world is saved every change
  is written as it is. A step of the water is taken a part of the blocks at a time, the frame's budget looked at
  between parts, spread over the server's processors; a block whose ground is not generated yet waits a frame or
  two for it rather than holding the frame. The column water of earlier versions is moved into voxels once, when
  the world opens (the old tile of a zone whose water finds no room is kept, not drawn, and tried again at the
  next start).
- **Shader.** The mod's own (`unity/Assets/FlowingWater/Shaders/FlowingWater.shader`, built-in render pipeline,
  standard lighting), with the look of the game's sea: when the game runs, the water takes the sea material's own
  ripple and foam textures, colours and settings, and is coloured, lit and fogged the way the sea is, so a pool or a
  river is the same water as the sea it runs into. What lies behind (the scene, grabbed once a frame, bent by the
  ripples) is seen through thin water, tinted as through the sea; deeper water takes the sea's bottom colour, from
  shallow to deep with the water's depth. Running water, churned and carrying silt, is less clear (a quarter as clear
  once it fully runs) and takes the deep colour at 1.5 m deep instead of 6 m, so a river reads as a river. Still water's ripples drift with the game's wind, as the sea's; running
  water's are dragged along the current in two offset phases (flow mapping), finer than the sea's, and it foams
  with its speed (rapids) and where the simulation says it falls. The faster it runs, the more its foam is drawn out
  in streaks along the current (each point looks back up its own current a few times, over 0.4 s of its way), and
  the foam is broken into lace by a finer foam running with it. Its edge fades out over the last two centimetres
  of water in front of the ground, with a band of foam just off it; shallow water a few centimetres deep, a stream
  from a source or a puddle, is drawn (the sea hides its shallows, ours lies on the water's own top).
- **Erosion** (off by default). Water carries ground (sediment, counted as a height over the cell): fast water
  can carry more, by its depth (up to a metre) and the square of its speed above 0.6 m/s. Carrying less, it
  picks ground up from the bed; carrying more, it sets the rest down; where it dries it sets everything down,
  and what reaches the sea is gone. The sediment moves with the water, at what each cell's water carried before
  the step. The ground never moves more than 8 m from where it was (the game's own limit on terrain changes).
  Banks fall in: where erosion leaves the ground between two cells steeper than loose ground holds (40 degrees,
  or the slope it had before when that was steeper, so natural cliffs and beds cut evenly down a steep slope
  stay), the higher one slides into the lower until it holds, so channels get sloping sides instead of walls,
  across zone edges too. The ground within 5 m of anything a player built (refreshed every 30 s), and of the
  game's own places (villages, dungeons, boss altars), is left alone. The change is the mod's own: one
  `FlowingWater_Ground` object per zone, millimetres at each of the zone's heightmap points, written by the
  server at most once a minute (and when the zone dries or the grid changes), kept with the world. Players'
  games add it to the zone's ground after the game's own terrain changes, so the look, collisions and building
  follow; the game's terrain changes are left as they are (they belong to whoever is near, and are theirs to
  write). Removing the mod takes the change away with it. At the default `ErosionSpeed`, a strong spring (4
  m³/s) down a steep meadow cuts its bed up to about 4 m deep in 20 hours of play, and lays the ground down
  where it slows. The server's debug map reports what each zone cut and laid down.
- **White water.** The running water's sound was made for the column water and went with its drawing; the 3D water
  is silent until it comes back on it in a later version (`WaterVolume` waits for it). Its falls, splashes and spray
  are the graphics card's (see [Falls, splashes and spray](#falls-splashes-and-spray)). `FlowVisualMinSpeed` and `FlowTextureSpeed` were the column water's stream
  material's, and do nothing on the 3D water.
- **Client.** Near the camera the graphics card moves the water (see [Water on the graphics card](#water-on-the-graphics-card)) and draws it where it is one layer on the ground; the blocks draw the rest. Each block of water the server sends is drawn by its own object. With the mod's own shader, the graphics
  card shapes the surface: the block's columns' tops (its highest top in its own rows, else the nearest within three
  rows over or under them, so a sheet running into the block over or under joins it), how much of each is shown
  (fading in from 1 cm to 3 cm of water) and its current go into two small textures, one column more east and north
  from the blocks beside it, and one fixed grid of points two to a voxel is raised to them, between columns as they
  are between their tops: a pool lies flat, a stream down a slope is one smooth sheet, the edge fades out. Each square
  between four columns is drawn by one block, the one whose top its first shown corner is. When the server sends a
  block's water again, the card blends from the surface as drawn to the new one over the time between the server's
  sends, so the water moves on smoothly instead of jumping; nothing is built in the game's frame but the textures.
  Beside the surface, the water is drawn as the voxels it fills where it meets the air: the server sends with each
  wet voxel which of its faces water could leave by (no piece closing it, no solid beyond), and the block draws a side
  where its water stands open to the air (from its floor, or the water beside it, up to its level), and water falling
  through the air as a column through each voxel it falls through, as wide as its water, capped where the fall starts
  and ends, so a fall is one cascade of white water. These faces and the surface are drawn from both sides: from
  inside the water, it is still seen.
  With the game's material (the shader setting off, or no shader for the platform), the surface is built as a mesh
  instead: the top of each wet voxel with no water over it, its corners shared with the tops beside it that are one
  water with it, sides where the water ends (a fall's sheet, a pool's edge over a cliff), eased to new water over 0.4
  s, at most ten times a second. A trigger box on the game's water layer tells characters and floating things the
  surface over them: the top of the stack of water they are in or just over (the server's water, not eased), so a pool
  on a raised floor floats a swimmer at its own level. Harmony patches make the game's water-level checks and the
  level any water volume reports include the flowing water, and let a swimmer drift with the current of the voxel they
  are in.

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

### In-game recorder

`tools/Recorder` is a test plugin (never shipped) that drives a real game client: once the player is in a
world it picks a steep and a gentle open slope near the spawn, runs every `flowingwater` command with several
values there (good and bad), and captures screenshots from fixed cameras plus short frame bursts for motion.
Every command and console answer goes to `log.txt`; the game quits at the end. It does nothing unless
`FW_REC_OUT` is set. Build it with `dotnet build -c Release` in `tools/Recorder`, put the dll in the client's
`BepInEx/plugins/`, and run the game hidden in a headless gamescope, joined to a local server, for example:

```
FW_REC_OUT=/tmp/rec gamescope --backend headless -W 1920 -H 1080 -- <Steam runtime entry point> --verb=waitforexitandrun -- \
  <Proton>/proton waitforexitandrun valheim.exe -console -joinserverwithcharacter 127.0.0.1:2612 <character> local -password <password>
```

(with `STEAM_COMPAT_DATA_PATH`, `STEAM_COMPAT_CLIENT_INSTALL_PATH`, `SteamAppId=892970` and the usual
`WINEDLLOVERRIDES` for BepInEx). A character new to the world rides in with the Valkyrie first; the recorder
waits for it to land. Remove the dll from the client afterwards.

`FW_REC_SCENARIO` picks one scenario instead of every command: `at` takes the player to each place of
`FW_REC_AT` (`x,z[,y]|command;...`, standing at `y` when given: on a floor, in a box) and runs the command there;
`pools` fills and films the pools of a test world (`FW_REC_POOLS`, `x,z,radius,depth;...`) in each look of
`FW_REC_LOOKS`; `prep` readies a world for the World Editor (`FW_REC_DIG`: the zones there generated, the ground
dug once). `hoe` places a source and a drain with the hoe on the gentle slope near the spawn, as a player would
(keys pressed through the game's input system, values typed into the panel), a drain on a floor it builds, changes
the source through **E**, checks a placed one is hidden with the hoe put away, removes the drain with the hoe's
remover (and not the floor under it), checks the hammer shows a second source red where it aims, and removes that
one with its panel's **Remove** button,
and logs each check as a `PASS` or `FAIL` line with shots of each step.
`perf` measures the water's cost to the game: frame rate, 1 % low, worst frame, the water's own time in the frame,
its bricks and vertices, garbage collections and memory, with no water, then a stream, a fountain, a cascade and a
lake filling (each added in turn), then all of them in view; a table in `log.txt` and `perf.csv`. Its places are
found near the spawn, or given as `FW_REC_PERF` (`x,z,stream|fountain|cascade|lake;...`); each case is measured for
`FW_REC_PERF_SECONDS` (20) after the water has run `FW_REC_PERF_SPREAD` (30) seconds.

## Checks

On every push and pull request CircleCI (`.circleci/config.yml`, `.circleci/continue.yml`) builds the
plugin and the recorder against the game's DLLs (Steam's free dedicated server) and BepInEx from
Thunderstore, with every analyzer warning an error (**code-analysis**); checks the formatting
(**formatting**, `tools/format-check.sh`); builds the Thunderstore package (**package**,
`thunderstore/build.sh`); and runs it in a real dedicated server (**server-smoke**,
`tools/server-smoke.sh`): it loads, a fixed source's water fills a zone with no error, and the saved
water comes back after a restart. It also runs the whole of the unit tests on every branch (**unit-tests**,
`tests/FlowingWater.Tests`: the mod's rules that need no game, compiled for .NET in `tests/FlowingWater.Rules`; the
release's decisions, `tools/release/ReleaseRules` and `tools/release/still-to-release.sh`; and this pipeline's wiring,
read from `.circleci/continue.yml`, which the ruleset's list below must match). On
main only, the unit tests run with their code coverage, which the **coverage** job sends to
[Codecov](https://app.codecov.io/gh/Shuiei/valheim-flowing-water) once they pass; Codecov's check fails under
95% of the rules' lines (`codecov.yml`, `tests/coverage.runsettings`). Main's ruleset requires five checks of a
pull request: code-analysis, formatting, package, server-smoke and unit-tests, on the branch up to date with main;
with an approving review (CodeRabbit's, `.coderabbit.yaml`) and CodeQL's code scanning. Codacy's analysis
(`.codacy/codacy.yaml`) reports on each pull request but is not required. What CI cannot check is the water as
players see it: try that in the game.

GitHub Actions add Scorecard, a dependency review of each pull request, Sigstore signatures on each
release's files (`SECURITY.md`) and, when main moves, main merged into every approved pull request with
auto-merge on. Dependabot opens the dependency updates.

## Releasing

CircleCI makes the release once you approve it:

1. Each pull request raises `VERSION` and adds its dated `CHANGELOG.md` heading (`## v0.6.0 — 2026-10-22`).
   Merging one releases nothing.
2. To release, open a pull request from main on a branch `release/v<VERSION>`, titled exactly
   `Release v<VERSION>`, that changes only `CHANGELOG.md` and, for notes of your own,
   `docs/releases/v<VERSION>.md` (else the notes are the changelog's sections since the last release).
   Its checks report at once: its code is main's, checked already.
3. Once it is merged, main's pipeline is a release one: open its `release` workflow in CircleCI and
   approve **hold-release**. Then the package is built and smoke-tested again, and:
   - **check-thunderstore** and **check-hexium** check all that publishing needs without publishing (the
     tokens, the version not on the site yet, the categories, the files): a site that would refuse the
     package stops the release before anything is tagged;
   - **github-release** tags the commit `v<VERSION>` and makes the GitHub release with
     `FlowingWater-<VERSION>.zip` and the notes (the Sign release workflow then signs the zip);
   - **publish-thunderstore** and **publish-hexium** publish it side by side
     (`tools/release/publish.cs`, the sites' settings in `tools/release/sites.json`, team `Tie`).

A version already on a site fails its job: no site takes a version twice. A failed job can be rerun
from CircleCI (Rerun from failed, not from start: the checks would then find the version on a site that
took it). Rerun, github-release goes on when this commit has its release and zip on GitHub already (it
fails when the tag is on another commit, or the release has no zip), and publish-hexium takes a version
an earlier run submitted (hidden) as submitted and sets its metadata. Still by hand: **unhide** the new
version on Hexium (uploads arrive hidden). Nexus Mods comes once the mod has a page there (made on the website: the API cannot): then a
`nexus` entry in `sites.json` (its mod and file ids), Moder Warehouse Plus's Nexus publishing in
`publish.cs`, and a `publish-nexus` job.

The release jobs' secrets are in CircleCI's **mod-token** context, restricted to the maintainer and
shared by the mods' projects: `GITHUB_TOKEN`, `THUNDERSTORE_TOKEN` and `HEXIUM_TOKEN`. The project needs
CircleCI's dynamic configuration (Project Settings › Advanced › Enable dynamic config using setup
workflows).

To check the publishing without publishing:
`dotnet run --file tools/release/publish.cs -- <thunderstore|hexium> <folder> --dry-run`, with the zip in
the folder and the site's token in the environment. The package's icon is drawn by
`dotnet run --file thunderstore/MakeIcon.cs`; its mod page is `thunderstore/README.md`.

## Rules

- No decompiled game code in the repo.
- One pull request per change; a feature bumps the minor version, a fix the patch; `CHANGELOG.md`
  gets an entry.
- Never tested on a live server: a local dedicated server and the game.

MIT licence.
