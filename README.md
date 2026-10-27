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
waves or splashes. Whatever is solid in the world holds water as the terrain does: what players build (walls,
floors, a pool on posts, a plank chute), rocks, trees, the ruins of the game's places; the water runs round a rock
and off its top, and through a notch mined in it.

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
| `flowingwater wipe` | Takes all the mod's water out of the world and removes every placed source and drain (the config's fixed ones stay); the host's and the server's admins' only, whatever `CommandsForEveryone` says. On a server, the server answers on the console when it is done |
| `flowingwater info` | What is under you: ground, depth, current, the zone's water |
| `flowingwater debug` | What the block of water you are in (or the nearest over or under you) draws: material, shader, triangles |
| `flowingwater solids` | What the water treats as solid round you: a map of the voxel row at your feet (solid voxels, faces walls close) and the faces under it (floors) |
| `flowingwater gpu [colours\|columns]` | What the graphics card's water near you is doing: its square, the ground found under it, its steps a frame; `colours` draws each part in its own colour (the card's water blue, a block's surface red, sides and falls yellow), again to stop; `columns` logs the water and ground of the nine voxel columns round you |
| `flowingwater particles [m³\|clear\|spheres\|full]` | Pours a block of the prototype's particle water (default 2 m³) 1.5 m over you, kept out of the ground your card's water knows and pushed aside by you (see [Particle water](#particle-water-a-prototype)); `clear` takes it all away; `spheres` draws the particles as spheres instead of water, again for water; `full` draws the water's surface at the screen's full size instead of half, again for half |
| `flowingwater flat` | A plain colour (blue) instead of water, to see the shape |
| `flowingwater lift [m]` | Draw the water that much higher, to find it |
| `flowingwater mat custom\|card\|falls\|spray\|still <property> [values]` | Change a water material property in game: the bricks' (`custom`), the water near you (`card`), its falls' curtains (`falls`), its spray (`spray`) or the still sea's (`list` names them) |

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
| `Client.ParticleWater` | false | A prototype: the water near you is particles of water on the graphics card instead of a sheet, made from the server's water and held to it, moved as water moves (it falls, breaks up, splashes and pools, is pushed aside by you, creatures and ships, and comes out of a source all round it); the sheet and its spray are not drawn near you then (see [Particle water](#particle-water-a-prototype)) |
| `Client.ParticleSize` | 0.25 | Side of a particle of the particle water, metres: 0.25 is water as it looks; 0.5 is coarse (balls of water) and an eighth of the particles for the same water, for a slow graphics card. Takes effect when the water near you is made again |
| `Client.VolumeWater` | false | A prototype: the water near you as one body of fluid on the graphics card, in place of the sheet (and of `Client.ParticleWater`): a 3D fluid on a grid round you, its mass its own, poured in from the server's water once and at the square's edge, moving as water moves, with sources pouring into it and drains taking from it (see [Volume water](#volume-water-a-prototype)) |
| `Client.VolumeRange` | 32 | Side of the square round you the volume water fills, metres (16 to 64; the card's square is this at most then); more costs the card more, by the square |
| `Client.VolumeHeight` | 24 | Height of the volume water's grid, metres (8 to 48), centred on you: water above or below it is the server's |
| `Client.VolumeCapacity` | 1048576 | Particles of the volume water at most (65536 to 4194304; eight to a cell of 0.25 m: a million is 2000 m³) |
| `Client.VolumeFroth` | 1 | How much spray, foam and bubbles the volume water throws where it is violent (0 none, 1 as tuned, up to 4 for a wilder water); particles of their own, a quarter of `VolumeCapacity` at most |
| `Client.ParticleCapacity` | 65536 | Particles of the particle water at most (4096 to 262144): no more water is made near you once they are all in use, until some goes; more costs the card more. Takes effect when the water near you is made again |
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
- **The water's body.** What is behind or under the water is seen through so much of it along the view (more looking
  across a pool than down into it): its light goes as it does in water, red first (through 1.2 m, four fifths of its
  red, two thirds of its green and three fifths of its blue), and it blurs; the water's own colour takes its place. A
  few centimetres of water stay clear. `_BodyDepth`, `_BodyTint` and `_BodyBlur` on both water materials
  (`flowingwater mat custom\|card`).
- **The water's edge.** Near the camera, the water ends where its depth between the cells runs out, a smooth curve
  rather than the cells' steps, and stays whole up to it. Within its front's height of that edge (5 cm at most) the
  surface rounds over down to the ground, its ripples smoothed out, what is behind bent more (a lens) and darker where
  it stands steep, the outline a drop of water has. `_EdgeRound`, `_EdgeBend` and `_EdgeDark` on the near water's
  material (`flowingwater mat card`).
- **Over bumpy ground.** Near the camera, the water is drawn with its level smoothed over the cells round it (3 by 3,
  the nearer counting more; not across a ledge or a fall: only the water within 15 cm of the cell's level, whatever
  the cells' size), so a thin sheet lies smooth over the ground's small bumps instead of taking the shape of each, and
  is never drawn thinner than it is. Inside the sheet
  (water on two opposite sides of a cell), a cell is drawn 1.5 cm deep at least, and a bump under that level is drawn
  covered (10 cm at most): the sheet stays whole instead of breaking into patches. Only the look: the water moved is
  not changed.
- **Running water's surface.** Running water is drawn with waves on it, running with the current: chop that rises
  with its speed (1.5 cm for each (m/s) squared, at most 15 cm and a quarter of its depth: still water lies flat, a
  thin sheet barely ripples), lit as the waves it is, and a finer layer of ripples on fast water that catches the sun
  in glints. The long waves (2.4 m) are raised on the surface, by the blocks beyond the square too, so they run on
  past its edge; the fine ones (0.8 m) are only lit, at each point drawn, near and far alike. White water gathers on the tops of fast chop's crests and where the water slows hard into the water
  ahead (piling up against a rock or a post, a jump from shooting water to deep), besides the foam of fast water
  running with it. Water running down a steep slope (from about 20°, all of it from 45° and 1 m/s) is a cascade:
  broken white water, streaked down the slope as it runs, hiding what is behind it and mirroring nothing, near and far
  alike. Only the look: the card's water does not move by these waves.
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
- **Curtains.** A lip's fall is drawn as one curtain of water: each face of a cell its water runs out of over a lip
  is a strip from the cell's middle over the brink, then down the arc its water is thrown on to where it lands (found
  on the card each frame, drawn from there, never read back), the strips of a lip side by side one sheet. Glassy where
  it leaves the lip, it turns white as it falls (all white after half a second), streaked along its fall, the streaks
  running down with the water; a long fall frays into streaks near its foot, and its ends along the lip spread a
  little and fade. The streaks carrying the water are drawn faint (30 %), the curtain's mist. Where it lands, the pool
  churns white (wholly where water lands 5 cm deep a second or more), calming over a second once it stops. The
  curtains' material is tuned with `flowingwater mat falls`.
- **Splashes.** Falling water landing fast (over 3 m/s) on the water or the ground throws up droplets.
- **Spray.** Water at least 10 cm deep running faster than 4.5 m/s throws a few fine droplets off its surface (a thin
  sheet running down a slope throws none).

At most 32768 particles live at once (the oldest give way); one that leaves the square, or flies over ground the card
has not found, fades out there within a quarter of a second, and none is drawn much longer than a metre along its
motion. Moving them took 0.02 ms a frame on a Radeon RX 9070 XT (`tools/sim-preview.sh`, which also runs a shelf of
water poured off its lip); drawing them comes on top, and grows with how many there are on screen. A wall of water
between two levels (a ledge with water on its top and at its foot) is left to the falls: the card's surface fades out
where the water a cell away is more than half a metre higher or lower.

### Particle water (a prototype)

The sheet of shallow water the card moves is a height over the ground: it cannot curl over, break into drops, or be
pushed aside by something moving through it. For that water, a prototype of **water as particles** is in the plugin's
code (`src/Client/ParticleWater.cs`, `unity/Assets/FlowingWater/Shaders/FlowingWaterParticles.compute` and
`.shader`, `FlowingWaterParticleSurface.shader`), measured by the Unity project's preview. In the game it is off unless
`Client.ParticleWater` is on (then the water near you is particles), and the console pours some either way
(`flowingwater particles [m³]`, while the card moves the water near you). What it is:

- Each particle is a cube of water `Client.ParticleSize` metres on a side (0.25, or 0.5 for a slow card; the preview's
  scenes use 10 cm, a litre). Position based fluids (Macklin and Müller, 2013): each step guesses where every
  particle goes, then pulls the guesses a few rounds until the water round each is as dense as water at rest, no
  denser (thinner water, the surface and spray, is not pulled together: that pull, against the paper's artificial
  pressure, is stiffer than a frame's step bears), and takes each particle's speed from where it ended up. Neighbours
  are found through a hashed grid of cells the smoothing radius (twice the particle's side) wide, the particles
  counted into buckets, the buckets' starts summed, the particles sorted into them, every step.
- The ground is the card's water's (`SimGrid.Bed`: a height per cell, a wall where it steps up by more than 35 cm):
  particles stand a radius over it and off the walls' faces, pushed out of the ground along its slope (so a particle
  on a bank slides down it as water does, instead of sitting there), and the ground and the walls count as the water
  they keep out (the part of the smoothing kernel beyond them), so water by a wall is as dense as water inside and a
  pool lies still instead of jittering against its walls. What players build is as the bricks have it (the server's reading of
  the pieces' colliders, over the square and 64 rows round the camera's height, built again with the card's water when
  the bricks change): solid voxels, which a particle keeps a radius out of, so a roof holds water off from above and
  from below, a floor on posts holds a pool, and water runs off a slab's edges (the preview's roof scene: none ends
  inside the slab); and the faces pieces close between voxels (a wall thinner than a voxel), which a particle stays on
  its side of (the preview's walls scene: a pen's walls hold what is poured in). The bodies in the water push it
  aside: every character (players, creatures, as the box of its capsule), every ship (as the box its hull floats
  by, the game's own) and every other rigid body (carts, things dropped, felled trees, as the box round its collider),
  32 at most in that order; a particle keeps a radius out of each, out through the nearest face, so a hull
  sweeping on shoves the water ahead and aside as far as it moved each step, and the water takes the hull's speed: a
  fast longship raises a far bigger bow wave than a drifting karve, by its speed and hull, not its weight (what the
  water does to the ship is the server's, as the ship floats on its water). The preview's boat scene drives a hull
  through a pool: none of the water ends inside it, and the water ahead rises.
- With `Client.ParticleWater`, the particles are the water near you: the sheet is neither moved nor drawn in the
  card's square, nor its spray, and the bricks draw only the square's edge band and what the card does not draw (water
  under a roof, falling water). The particles are made from the server's water and held to it, by the voxel column:
  each frame the particles in each column are counted; a column with fewer than the server's water there is worth
  (its depth, where a particle can stand in it) gets new ones, laid still just over its water, at the sheet's own rate
  (0.8 of the lack a second, a fraction of a particle as the chance of one more), so a pool rises from below as the
  server fills it; one with more loses the same share of its own at random, so a pool sinks as the server drains it;
  a column the card does not draw is worth nothing, so water that runs where the server has none goes within a
  second or two. A particle under the sea's level is the sea's, one in the edge band is the bricks'. So the server
  keeps the say on how much water is where, and how it moves (falling, splashing, pushed aside) is the particles' own.
  The preview's own scene (a basin the server says holds water to 1 m, then 1.5 m, then none) has the particles'
  water within a tenth of the way to the server's four seconds after each change, never thrown up.
- A placed source's water comes out of it: each frame, the source's water for the frame is made as particles all
  round the rim of its marker (the golden angle from one to the next, so even a few are spread evenly), going outward
  as fast as the rate pushes water through the marker's side (1 to 8 m/s), no more a frame than the rim passes (a
  ring of particles each frame; a big source's rest comes as the columns' fill). The jet is the water (a particle in
  the air is never let go), and where it lands ahead of the server's pool the fill and the loss trade it at their rate.
  The server's config's fixed sources have no marker to see: their water comes as the square's other water does. The
  preview's spout scene (8 m³/s for 6 s) keeps the particles' water within 15 % of the server's from 3 s on.
- Drawn as water over the scene, from the screen (van der Laan, Green and Sainz, 2009): the camera first draws the
  particles as spheres into two textures, the nearest's distance at each pixel (smoothed into one surface, keeping
  the steps between one body of water and another) and the water the view goes through there (with how much of it is
  air: thin water, spray); then one triangle over the screen, drawn with the transparent things after the sheet, takes
  the surface's slope from the distances and gives it the sea's look as the sheet has it: what is behind bent and
  tinted by the water seen through, the sky and the sun on the surface, foam where the water is air, the sea's fog.
  The two textures are half the screen's size each way (a quarter of the pixels: the surface is smooth anyway, and its
  edge steps by two pixels); `flowingwater particles full` draws them at the screen's size, `spheres` shows the
  particles themselves.

`tools/sim-preview.sh <folder> ParticlePreview.Run` runs it on a box of water at rest, the dam break, water poured
off a ledge into a basin, a sphere pushed through a pool, a basin filled and emptied by the server's water (own), a
source's spout, the look of a pool with a source in it at the game's particle sizes (look: frames only), water poured
onto a slab (what players build), a pen's walls, a hull driven through a pool, a cost scene, and three blocks poured
one after another onto the same cloud (each particle at its place in its block, laid out as poured), and writes
`particles.txt` and frames
(`FW_PREVIEW_SCENES` picks scenes; `FW_PREVIEW_RELAXATION`, `FW_PREVIEW_ITERATIONS`, `FW_PREVIEW_VISCOSITY`,
`FW_PREVIEW_TENSION` and `FW_PREVIEW_SUBSTEPS` try other solver settings). On a Radeon RX 9070 XT: a box 2 m square
filled 1 m deep settles 1 % pressed down, its particles drifting 3 cm/s after 3 s; the 10 m dam break's front reaches
the far wall at 1.30 s (a real one about 1.3 to 1.5 s; the sheet 1.25 s) and runs 3 m up it; moving 64 000 particles
costs 1.1 ms a frame at 60 fps (8 rounds a step; 16 000 cost 0.8 ms); drawing 32 000 of them filling a 1280 × 720
view as water costs 0.55 ms more than the scene alone with the surface at half the screen's size (0.95 ms at its full
size; 0.45 ms as spheres), and grows with the screen.

What it is not yet: water thinner than a particle (a film on the ground is not made: the particles' pool ends where
the water is shallower than one); swum in or floated on (the particles are only drawn: the server's water says what
you swim in); met by pieces where no brick with water is (there only the ground's heights hold); pushing anything
back (a body in it is pushed by the server's water, as it floats on that; the particles are each player's own
picture); spread over the frame's budget (a step is one go, and a column gets at most 32 particles a frame, a source
256; the surface is drawn only while there are particles); a jet fountain (a source's water only goes out sideways);
its surface's silhouette is still the spheres' (bumpy at the edge), and its look is a first pass on the sheet's.
The console's block stays where the card's square leaves it.

### Volume water (a prototype)

With `Client.VolumeWater`, the water near you is one body of fluid on the graphics card (`src/Client/VolumeWater.cs`,
`unity/Assets/FlowingWater/Shaders/FlowingWaterVolume.compute`), in place of the sheet and of the particle prototype
above, with a renderer of its own (below). It is a hybrid of particles and a grid (FLIP, Zhu and Bridson 2005):
the particles carry the water, each a cube of water half a cell on a side (eight to a cell of the card's 0.25 m
cells), and move with it; each step their velocity is put on a staggered grid, gravity is added, and a pressure is
solved over the cells that hold water so that no cell makes or loses any (with a small correction for cells that hold
more or fewer particles than rest, which keeps the volume from drifting); the corrected velocity goes back to the
particles, mostly as the change (FLIP) and a little as the grid's own (PIC), and they move along it. Cells under the
ground the card's water knows, in a built voxel (the bricks' solids) or in a body (players, creatures, ships' hulls,
carts) are solid, and the faces thin pieces close between voxels are walls to the grid and to the particles: the
water goes round them, pools over them, is held in by them and breaks out where one is gone.

Its mass is its own: nothing inside the square is corrected toward the server's water. The server's water is poured
in once when the square is made (where the server has water, from the ground to its top) and each frame in a band
two metres wide along the square's edge (water coming in from beyond), and water leaving the square's sides, bottom
or top is the server's again. Where the server has water beyond a side, that side holds the square's water in, as a
wall does, so a lake the square cuts stays level at its edge (the band along it takes the server's water as it
changes); where the world beyond is dry, the side is open and the water goes out. A placed source is an inflow in the grid: its cells are kept full of particles going
out of it at about three metres a second (its reach grows with its rate: a big spring boils up over a wide patch),
scaled each step toward giving its rate; a drain takes its rate of particles within its reach. The server's config's fixed sources and
drains have no marker to see: their water comes in at the edge as the server's other water does. The square is the
card's square, no wider than `Client.VolumeRange`; the grid is `Client.VolumeHeight` tall, centred on you and following
you up and down.

`tools/sim-preview.sh <folder> VolumePreview.Run` runs it without the game and writes `volume.txt` and frames: a box
of water at rest keeps its depth and every particle over 5 s; the 10 m dam break's front reaches the far wall at
1.40 s (a real one about 1.3 to 1.5 s) and the water comes to rest near 0.5 m; water let go on a shelf pours over the
lip and pools below, none lost; a source of 8 m³/s grows the water by its rate; a box of walls holds 2 m of water and
empties through a gap opened at its foot, as solid voxels and as thin walls (closed faces); a drain of 2 m³/s takes
exactly that; a grid 20 m square with open sides, poured from a pool the server has, keeps the server's water in the
square (within 3 %, 1 m deep, none left behind) as it is moved east and north 2 m at a time with its new strip seeded,
and as it is dropped 2 m, as the game moves it after the camera (the arithmetic of that following, and of a source's
sphere, is in `VolumeFollow` and `SpringShape`, which the unit tests cover). `FW_PREVIEW_ITERATIONS`,
`FW_PREVIEW_FLIP`, `FW_PREVIEW_DRIFT` and `FW_PREVIEW_FROTH` try other solver settings, `FW_PREVIEW_SPHERES=1` draws
the particles.

Where the water is violent it throws froth: spray, foam and bubbles, after Ihmsen, Akinci, Akinci and Teschner's
unified diffuse particles (2012), read off the grid. Each step every particle of the water faster than 1.5 m/s is
given three potentials: the air it traps (how much the velocity differs across its cell: a jet into a pool, a shear),
its crest (fast water with air above it: a fall's lip, a front) and its impact (the speed the ground or what is built
just took from it), each counting the more the faster it goes, and makes that many froth particles a second (scaled
by `Client.VolumeFroth`), in its water and going as it goes with some spread, or, struck, thrown back off what it
struck. A cell already holding six froth particles takes no more (froth covers water, it does not pile up), and a
source's own sphere makes none (its outflow is no violence of the water's). Froth carries no water. Each froth
particle is what the water round it makes it: in a cell with little water
it is spray, falling and slowed by the air, the finer the more (a drop falls, a mist hangs and spreads); in the top
cell of the water it is foam, riding the surface with the water's velocity; under more water it is a bubble, rising
through it with the water to foam. Each lives one to three seconds (foam twice that), and spray down on dry ground
is gone at once. They are drawn after the water's surface as soft sprites (`FlowingWaterFroth.shader`): spray as
drops that grow into a thin mist as they fade, foam as white specks, bubbles as faint ones, lit as the sea's foam.
Their room is a quarter of the water's particles; when it is full, no more is made until some is spent.

It is drawn from its true shape (`src/Client/VolumeSurface.cs`). After each frame's steps the particles are shared
onto a grid of half-cells (a particle's size) as a density, 1 where a particle sits at rest, in a texture the card
samples smoothly, with the cells that hold water or stand next to some and the froth in each cell beside it. For each
pixel of a half-size picture, `FlowingWaterVolumeMarch.shader` marches a ray from the camera through the density,
stepping over the cells with no water and in quarter-cells through the rest, to where the density first crosses a
half: the water's surface, its normal the density's slope; and on through the water, adding up how much the ray goes
through until it leaves the grid or meets the scene. `FlowingWaterVolumeSurface.shader` then draws the water from
that with the sea's look: what is behind bent by the surface and tinted by the water seen through (red light goes
first, as the sheet's), the water's own colour where it is deep, the sky and the sun's glint on the surface, foam
where the froth gathers, the sea's fog; thin water fades out, and within two metres of the square's side the water
fades into the sheet's bricks beyond. The bricks draw nothing else inside the square while the volume is on (the
server's columns say nothing of what the volume holds), only that band and the world beyond. With the camera under the water, the scene is seen through the water up to its
surface or the scene, tinted and dimmed by that much water. The console's `flowingwater particles spheres` draws the
particles as spheres instead, for testing. The density costs the card a half-float a half-cell and two counts besides
(about 125 MB at the default grid), the march a few milliseconds a frame at half the screen's size.

What it is not yet: swum in or floated on (the server's water says what you swim in), spread over the frame's
budget, and the server does not learn what it does.

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
  when pressed in). The ground fills each voxel from its bottom up to the terrain's height; every solid object
  (building pieces, rocks, trees, stumps, the ruins and rocks of the game's places; nothing that moves by itself,
  such as a felled log or a cart), by the colliders players bump into, fills the voxels it stands in from below
  (a floor) and closes the faces between voxels it separates, so a wall thinner than a voxel holds water, a box on
  posts holds a pool, and breaking a piece or felling a tree lets the water out. A box collider is a box; a mesh
  collider is its mesh, read once a kind and placed where each object stands (a mesh the game keeps unreadable, a
  handful of furniture pieces, is the box round it, listed in the log), so a rock has its own shape: solid
  from each face of it that looks up down to the next that looks down (an overhang ends where its underside does,
  an arch lets water under it, a trunk open at its foot is solid to the ground). A rock mined bit by bit leaves out
  the bits that are gone. A solid only in a voxel's upper half is that voxel's ceiling (the water on
  it is in the voxel above), and two voxels side by side pass water only at a height where both have room: a thin
  sloping plank, a ceiling here and a floor there, keeps the water running down it on top of it. The solids are read again when an object is built, broken, mined, moved, opened
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
  builder makes them, plus the players' terrain changes; the solid objects come from their saved objects and the
  prefabs' colliders (a place's ruins from its location's prefab, through the proxy object that stands for it),
  on the layers the players' game casts the ground under its water on. Each block with water is written to its own object (`FlowingWater_Brick`) when it changed in a way a
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
  of water in front of the ground (but for the near water's rounded front), with a band of foam just off it; shallow water a few centimetres deep, a stream
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
  where its water stands open to the air (from its floor, or the water beside it, up to its level; not toward water
  beside it at most 0.35 of a voxel lower, or a voxel lower, which the surface joins it to, unless that column holds
  water higher up, which the surface joins instead, or the server says the dry voxel beside holds some ground: the
  side shows over it), and water falling through the air as a column through each voxel it falls through, as wide as
  its water, capped where the fall starts and ends, so a fall is one cascade of white water (only over open air: water
  stepping down a slope steeper than the voxels, from one to the next with ground under it, is the surface's, one
  sheet down the slope: the server marks with each wet voxel's open faces whether the voxel under it holds ground,
  which it does not send otherwise). These faces and the surface are drawn from both sides: from inside the water, it
  is still seen.
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

`FW_REC_SCENARIO` picks one scenario instead of every command: `diag` pours a cascade down the steepest face near the
spawn (or `FW_REC_AROUND`, within `FW_REC_REACH` metres; `FW_REC_DRY=1` pours none, `FW_REC_WAIT` seconds before the
shots) and shoots it from several places as it looks and in the debug colours, with `flowingwater gpu` and
`gpu columns` in the log; `flood` floods a gentle meadow near the spawn from three sources (`FW_REC_RATE` each,
`FW_REC_WAIT` seconds) and shoots it from inside the card's square, across its edge and from far off, the same way;
`at` takes the player to each place of
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
