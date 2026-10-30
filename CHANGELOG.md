# Changelog

All notable changes to FlowingWater. Newest first.

## v0.46.0 — 2026-10-29

### Added
- A source can face a way and shape its water: out of an outlet up to 10 m wide, fanned out up to 90 degrees and
  thrown up to 60 degrees, the way its piece was turned as it was placed. A slim jet arches out of a point; a wide
  river runs out of a wide outlet along the ground. The server's water and the water near you both follow it. The
  panel to set it comes next; until then a source pours all round, as every source placed before does.

## v0.45.2 — 2026-10-29

### Fixed
- The volume water preview draws the character's box in its wader and big-source frames: the water flowed round it,
  but the frames showed only the water, with a hole where the character stood.

## v0.45.1 — 2026-10-29

### Added
- The volume water preview stands a character in a stream slow enough to wade in and checks the water heaps against
  them only as its bow wave does, with none lying on their head (the glassy pillar's case, which the preview's
  big-source swimmer, in water deeper than the game's player stood in, never showed).

## v0.45.0 — 2026-10-29

### Added
- The current carries you when you wade, not only when you swim: it adds to your speed by how deep the water is
  (half its speed at the knees, all of it from the waist up), so running down a stream is faster than on dry ground
  and walking up it slower; water fast and deep enough to take a person off their feet sweeps you along.

### Fixed
- No more glassy pillar round you near a big source: standing still in fast water, you piled it up over your head;
  now it carries you along.

## v0.44.5 — 2026-10-29

### Fixed
- The water near you takes in the server's water over the whole of its square: while you stayed near its middle,
  the brick row at its far edge could be left out. The volume water then took that edge for dry ground, its water
  ran out there, and a pool stood a metre under the server's, a dark step showing where the far water began.

## v0.44.4 — 2026-10-29

### Fixed
- The volume water's pool round a fall's foot no longer shows dark blotches: the spray and splashes over it were
  counted into the surface under them, standing it up in patches whose sides showed dark on the water.

## v0.44.3 — 2026-10-29

### Fixed
- The volume water preview's dam break checks the water once it has come to rest, by its surface: it had checked a
  tank still sloshing after 8 s, with a depth read 6 cm too high, and failed every run.

## v0.44.2 — 2026-10-29

### Fixed
- A fall farther off than the water near you no longer shows small dark squares through it. They were walls drawn
  between columns of the pool at its foot, under the spray falling into it.

## v0.44.1 — 2026-10-29

### Fixed
- The water near you no longer fills up round a big source: it runs on out into the water beyond, instead of
  standing half a metre over it.
- A source's water gushes out sideways instead of welling up, so a 100 m³/s source no longer stands as a tall green
  dome: it swells the water round it a little and boils it, and a source of 5 m³/s or more foams to show its power.

## v0.44.0 — 2026-10-29

### Changed
- The current carries floating things toward its own speed, light ones quickly and heavy ones slowly: an item or a
  crate runs nearly as fast as the river within a second or so, a log or a cart in about 2 seconds.
- The current now carries boats with someone aboard, slowly: a raft across a river drifts down it at about a sixth of
  the water's speed, faster when it points down the river. A boat sailing down faster than the water is not slowed
  by it, and one sailing up is held back. A boat with no one aboard stays where it is, as the game holds it.
- Thin, fast water carries swimmers and floating things at its real speed. It was taken as much slower where the
  water is a thin sheet or falls.

## v0.43.0 — 2026-10-29

### Added
- Running water is heard again: a stream babbles where it runs fast, and rapids and falls roar. It is louder the
  more water passes, follows the `WaterVolume` setting and the game's sound-effects volume, and
  `flowingwater sound` says what is heard.

## v0.42.6 — 2026-10-29

### Fixed
- Water farther off than the water near you no longer shows a small wall on one side because ground stands beside it
  on another. The server now says which side the ground is on. Game and server must both run this version; a world
  saved before it is read as it was.

## v0.42.5 — 2026-10-29

### Fixed
- A stream down a hill farther off than the water near you is drawn as one smooth sheet. It used to show a grid of
  small dark squares and stripes, from walls and columns of water drawn under its surface.

## v0.42.4 — 2026-10-29

### Fixed
- Under the volume water you see the pool round you, tinted green and fading with the distance, instead of near
  black a couple of metres away.

## v0.42.3 — 2026-10-29

### Fixed
- The volume water no longer runs out of room over deep water. A deep pool or a lake round you could fill every
  particle it has, and then a source or a fall there poured nothing and the water's surface showed holes. Now, when
  the water near you would not fit, the volume moves only the top few metres of deep water and the rest stays still
  beneath, so falls and sources keep pouring.

## v0.42.2 — 2026-10-29

### Fixed
- Shallow volume water among grass no longer shows dark dashes round the blades. A blade standing out of the water
  used to cut a gap in the water around it; now the blade shows in front of the water and the water runs on round it.

## v0.42.1 — 2026-10-29

### Fixed
- Thin walls, floors and roofs hold the volume water wherever they stand. Before, they only counted where the
  server's water had already reached, so water the volume threw ahead of it went straight through a wall or a roof.

## v0.42.0 — 2026-10-27

### Fixed
- The volume water runs down slopes as water does. The solver saw the ground as a stair of whole cells, so a slope
  was a run of terraces the water sat on and spilled over, in squared pools; now each cell and each face of the
  grid knows how much of it is clear of the ground, and the pressure follows the real slope. Water let go at the
  top of a ramp runs down it as a sheet and gathers on the floor below, and nothing is pushed into the ground. Thin
  water on the ground is also drawn as one smooth skin from the ground to its top, instead of blobs and ridges the
  size of the grid's cells, and the whole surface is smoothed across the screen before it is lit, so the particles'
  grain no longer shows as lumps on a pool or a stream. Running water carries ripples that move with it at its own
  speed, finer the faster it runs; a pool lies still. The volume's grid is 32 m tall now with more of it above you
  than below, and water falling from above the grid comes into it instead of staying the far water's: a stream down a
  tall hill is the volume's wherever you stand on it. The square and the grid follow you, not the camera, so a free
  camera or a far look does not drag the water away; and `Client.VolumeRange` goes up to 128 m now and `Client.VolumeHeight` to 64 m, the grid cut in
  height to what the card runs at once. Rocks and banks standing out of thin water stay dry, with the water running
  round them, and a thin film of water on the ground shows the ground, not the sky. A fall is one thin veil of
  water from its lip to its pool, not a sheet full of holes, and runs white as it speeds up; foam lies on the water
  as a lace that flows with it and closes where the churn is, instead of specks; spray is small bright droplets
  and a fine mist that hangs and fades, not balls of fluff; and a smooth sheet running down a slope throws little
  spray. A big spring wells up over a patch as wide as its rate needs instead of shooting a jet high into the sky;
  and the water no longer mirrors the ground about you where you see a surface from below. Water no longer hangs
  frozen in the air as a sheet or a ceiling where the server's idea of the ground differs from the game's (after
  some terrain edits), and a spray of drops no longer draws as a solid column of water. Where a stream runs past
  the top or bottom of the volume's grid it blends into the far water instead of ending in a line across it.
  What players built stays solid to the volume water over its grid's whole height, the grid cut to what that
  covers, and a lone far drop no longer shows as a black or white speck.

## v0.41.0 — 2026-10-27

### Changed
- The volume water has a look of its own. It is drawn from its true shape: a surface found through its density,
  the water seen through each pixel measured, so deep water is dark and thin water clear, a fall's sheet thin, with
  no halo beyond the water's edge and no whitewash over a fast stream; foam lies where the froth gathers (a fall's
  foot, a boil, a front); the water fades into the far water at the square's edge instead of ending in a seam; and
  with your head under it, you see the scene through the water, tinted and dimmed by the water between.

## v0.40.0 — 2026-10-27

### Added
- The volume water throws spray, foam and bubbles where it is violent: at a fall's foot, where a jet meets a pool,
  where fast water strikes a wall or the ground, along a fast front. They are particles of their own that carry no
  water, made from the water's violence (the air it traps, its crests, its impacts) and moved by what they are: spray
  falls and the finest of it hangs as a thin mist that spreads and fades, foam rides the water's surface, bubbles rise
  through it to foam. Drawn over the water as soft white specks and drops, lit as the sea's foam. The new
  `Client.VolumeFroth` setting scales how much there is (0 for none).

## v0.39.0 — 2026-10-26

### Added
- The water near you as one body of fluid on your graphics card, a prototype behind the new `Client.VolumeWater`
  setting (off unless you turn it on; it replaces the sheet and `Client.ParticleWater` near you): a 3D fluid on a grid
  round you, its mass its own. It is poured in from the server's water once and at the square's edge, and from then
  on it moves as water moves: it pools and levels, a box of walls holds it and it bursts out where a wall is broken,
  it falls off a ledge and pools below, flows round rocks and what you build, and placed sources pour into it and
  drains take from it at their rate, nothing made or lost in between; a lake the square cuts stays level at its edge,
  held by the water beyond. `Client.VolumeRange`, `Client.VolumeHeight` and
  `Client.VolumeCapacity` size it. Drawn as the particle water is for now; its own look, and spray and foam where it
  is thrown, come next.

## v0.38.2 — 2026-10-28

### Fixed
- The server knows the ground as your game shows it near the world's locations: their levelled and smoothed yards are
  now part of the ground the water runs on, and terrain edits made there (with the hoe or a world editor) land where
  you see them. The water no longer lies inside a hill or hangs over a hollow there, and you swim where it shows.

## v0.38.1 — 2026-10-27

### Fixed
- Water near you no longer stands frozen as a wall or a sheet in the air where the server's idea of the ground differs
  from your game's (after some terrain edits): the ground under it is found from over the terrain you see.

## v0.38.0 — 2026-10-26

### Added
- `flowingwater wipe` takes all the mod's water out of the world and removes every placed source and drain in one go
  (the server config's fixed ones stay), for the host and the server's admins only. On a server the request goes to
  the server, which answers on your console with how much water went.
- A prototype of water as particles on your graphics card, for the water a sheet cannot be: water pouring over a lip,
  a dam bursting, something pushed through a pool. With the new `Client.ParticleWater` setting (off unless you turn it
  on), the water near you is particles of water instead of the sheet: made from the server's water and held to it
  (a pool rises from below as the server fills it and sinks as it drains), moving as water moves (it falls, breaks
  up, splashes and pools), kept out of the ground and of what you build (a roof holds it off, a floor on posts holds
  a pool, a wall holds it in), pushed aside by you, by creatures, by ships' hulls, by carts and by things dropped as
  they move through it, and coming out of a placed source all round it, as fast as its rate pushes it. The sheet and
  its spray are not drawn near you then. `Client.ParticleSize` picks the particles' size (0.25 m, or 0.5 for a slow
  graphics card, at an eighth of the count). The console's `flowingwater particles [m³]` pours a block of it over you whatever
  the setting, drawn as water with the sea's look (foam where it splashes); `clear` takes it away, `spheres` shows
  the particles themselves.

## v0.37.0 — 2026-10-26

### Added
- Everything solid is ground for the water, not only what players build: rocks, trees, stumps and the ruins and rocks
  of the game's places, each by the real shape of its collider mesh (an overhang, an arch, a cave under a rock; a
  handful of furniture pieces whose mesh the game keeps unreadable are the box round it), so a source on a rock pours
  over its top and down its side instead of through it. A rock mined bit by bit lets the water through where it is
  gone. The players' game already cast its ground on the same things, so what it draws and what the server moves now
  agree there.

## v0.36.0 — 2026-10-26

### Added
- Water falling off a ledge near you pours as one curtain from the lip down to where it lands, glassy at the top and
  breaking up white as it falls, and the pool under it churns white; the streaks it used to be drawn as are now its
  mist. `flowingwater mat falls` tunes it.

## v0.35.0 — 2026-10-26

### Added
- A thin sheet of water near you lies smooth over the ground's small bumps and covers them, instead of copying each
  bump and breaking into patches where it runs thin.

## v0.34.0 — 2026-10-26

### Added
- Water near you ends in a smooth, rounded front where it meets dry ground, solid up to its edge with a thin dark
  outline, instead of fading out along the ground's grid in steps. `flowingwater mat card` tunes it.

## v0.33.0 — 2026-10-26

### Added
- Water has a body: what lies under or behind it fades, blurs and turns blue-green with how much water you look through
  it, more across a pool than straight down into it, so a deep pool or a stream reads as water rather than a tinted film.
  `flowingwater mat card` tunes the water near you as `mat custom` tunes the rest.

## v0.32.5 — 2026-10-26

### Fixed
- Shallow still water keeps more of its ripples and takes on the water's colour with its depth, so a flooded meadow
  reads less like a pale sheet of ice.

## v0.32.4 — 2026-10-26

### Fixed
- Water farther off running down a gentle slope is one smooth surface again instead of a grid of small steps.

## v0.32.3 — 2026-10-26

### Fixed
- Water running down a steep slope is white, broken water streaked down the slope instead of a smooth sheet that
  mirrors the mountain, and from afar it no longer turns into a stack of boxes (the server needs this version too
  for that). Falls near you pour as a curtain instead of thin icicles.

## v0.32.2 — 2026-10-26

### Fixed
- The water near you no longer shows a strip of bare ground, a step, or a ripple where the water your graphics card
  moves meets the blocks of water farther off: the card hands a place over only once its water there has settled, and
  never draws the edge of its square.

## v0.32.1 — 2026-10-26

### Fixed
- Water thrown fast off a lip is drawn as a short streak instead of one several metres long, and water falling past
  where the ground near you is known fades out there instead of falling on through the ground.

## v0.32.0 — 2026-10-26

### Changed
- Running water's waves no longer end where the water your graphics card moves does: farther off, the blocks of water
  raise the same waves, foam on their crests and catch the light as it does.

## v0.31.0 — 2026-10-26

### Changed
- Foam on running water is drawn out in streaks along the current, the longer the faster it runs, and broken into
  lace instead of solid white patches.

## v0.30.0 — 2026-10-26

### Changed
- Running water is darker and less clear the faster it runs, taking the sea's deep colour at a fraction of the depth
  still water needs: a river reads as deep blue water instead of a pale sheet over its bed.

## v0.29.0 — 2026-10-26

### Added
- Running water near you now has waves on it that run with the current and rise with its speed, catches the sun in
  glints on fast water, and turns white on the tops of fast chop and where it piles up against a rock or a post.
  Still water stays flat.

## v0.28.0 — 2026-10-26

### Changed
- Walking or sailing along a river, the water your graphics card moves now goes with you as it is: its waves and
  currents carry on, where before they started over (and the water refilled) every 24 m or so.

## v0.27.2 — 2026-10-26

### Fixed
- Fast water no longer throws big white flakes all round it: spray is now fine droplets, a few from deep, fast rapids
  only (a thin sheet running down a slope throws none), and splashes are smaller.

## v0.27.1 — 2026-10-26

### Fixed
- Water that dried up just before the world was saved no longer comes back at the next start, and the server no longer
  warns of a second object holding a block of water after a restart.

## v0.27.0 — 2026-10-25

### Changed
- The server's water now carries its momentum: a surge let go runs out over the ground faster and further, and water
  off an edge keeps going the way it went; sideways it is slowed by the ground, far more as a thin sheet than deep. A
  stream down a smooth slope runs as a wider sheet than before. The server spends about 40% more on moving water.

## v0.26.0 — 2026-10-25

### Added
- Water pouring off a ledge now arcs out as a stream and splashes where it lands, falls from high are drawn as falling
  water instead of blocks, and fast water throws spray: all on your graphics card, with `Client.Spray` (on by
  default) and the water moved on the card. The pool at the foot of a fall, and water by the edge of a build, are drawn
  smooth instead of as slanted blocks. Falling water carries its water: the pool forms where the fall lands, and the
  water landing runs on the way it fell. The console's `flowingwater gpu columns` logs the water and ground round you.

## v0.25.0 — 2026-10-25

### Added
- Near you, your graphics card now moves the water between the server's changes, as water moves: let go, it surges
  out over the ground, runs up the walls and sloshes back, and waves cross a pool and come back. The server still
  says how much water is where, and what you swim in. Settings `Client.GpuWater`, `Client.GpuWaterRange` and
  `Client.GpuWaterCell`; the console command `flowingwater gpu` tells what it is doing. On a graphics card without
  compute shaders, the water is drawn as the server sends it.

## v0.24.0 — 2026-10-25

### Changed
- The server simulates only the sources and drains placed by a player who may place them: with `CommandsForEveryone`
  off, the host's and the admins', checked while they are on the server and remembered after in a file of the
  server's. It goes by what the player's game writes of the placer, as the game does for every piece, so it keeps
  honest games to the server's setting rather than stopping a changed one. Markers placed with the console before
  this version carry no placer: turn `CommandsForEveryone` on for a moment to allow every marker in the world, or
  place them again.
- Less work for the server: a block of water beside a running stream steps only the voxels its neighbour's water can
  reach, a block sleeps unless a neighbour's water moves at the face between them, a drain looks only in the blocks
  round it, bricks reading their solids again after a hoe stroke are spread over frames, and the frame's budget is
  not given up when a step ends in it. Less work in the game too: nothing on a dedicated server or far from water for
  the game's water checks, fewer uploads per block, no reflection each frame while a tool is in hand.
- The settings for spray, sound and the stream texture say in the config file that they are unused for now.

### Fixed
- A single voxel of water that was not a number (from a ground height the game gave as none) froze the whole water
  for good, the server's frame budget burnt every frame on it. Such water is emptied, counted once in the log, and the
  rest goes on.
- A block of water the server could not read back at a start (its blocks capped, or the ground risen under it) was
  left in the world beside the block made later, drawn for ever, and its water doubled at the next start. A saved
  block keeps its object now; one that cannot be read back is removed and told of.
- A corrupt saved block of water stopped the whole water loading on the game's own runtime; it is told of and skipped.
- A settings file deleted was warned of every second; once.
- `flowingwater source` with a `DefaultSourceRate` of 0 placed a source the server ignored; the console refuses it
  and the setting is bounded.
- Once the blocks' cap had been hit, a source standing where no water has room was never warned of again.
- A block fed only by water the sea takes was forgotten and made again every half minute for as long as the water
  ran.
- A game update giving one of the patched methods a twin would have left every patch of the mod out; such a method
  is skipped with an error instead. A missing body field would have failed at every character's move; the current
  is left out with an error instead.
- Blocks side by side blend to new water on one clock: no step between a block and its neighbour while the surface
  moves.
- Water the server keeps inside a solid voxel (a piece built over a pool with nowhere for the water to go) is not
  drawn, and no one swims in it.
- A server running another version of the mod is told of once, not once per block per send.
- The mod's shader, its shell and the sea's look are tried again each world instead of staying off for the whole
  game after one failure.
- `E` no longer both uses a door or chest and opens the source-and-drain panel in one press.
- Pieces scaled by another mod with one number for all three axes are read at that scale.
- A fixed source or drain whose place is not a number is refused as written wrong.

## v0.23.0 — 2026-10-24

### Added
- The water is drawn as the voxels it fills where it meets the air: the sides of water standing open to the air, and
  water falling from a height as a cascade of white water. You see the water from inside it too.

## v0.22.1 — 2026-10-24

### Fixed
- Water runs down sloping plank floors (chutes, slides, ramps) on top of them instead of leaking through to the
  ground, and a source standing on a floor pours onto it instead of under it.

## v0.22.0 — 2026-10-24

### Added
- Settings take effect when `Tie.FlowingWater.cfg` is saved, on the server and in each game, without a restart; the
  log lists the settings that changed.

## v0.21.1 — 2026-10-24

### Fixed
- Shallow water shows again: a source's stream and puddles a few centimetres deep were drawn see-through, so only
  deep pools could be seen.

## v0.21.0 — 2026-10-24

### Changed
- The server sends a block of water to the players only when the change would show: a running stream's water stirs
  all the time, and changes too small to see are held back until they add up. A stream is sent about a third less
  often, less work for the server and less to download; the world is still saved exactly.

## v0.20.0 — 2026-10-24

### Changed
- Water now runs down a slope as a stream instead of fanning out into a thin sheet: it wets new ground beside it
  only once it stands a few centimetres deep, as a real rivulet does. A spill on flat ground stops spreading as it
  thins, so there is less water for the server to work on.

## v0.19.0 — 2026-10-24

### Changed
- The water's surface is now shaped by the graphics card: one smooth sheet down a slope instead of steps, its edges
  and thin water fading out instead of flickering, and it moves on smoothly between the server's changes instead
  of jumping to each. Less work for the game's own frame too.

## v0.18.0 — 2026-10-24

### Changed
- The flowing water now looks like the game's own sea: the same ripples, colours, reflections, foam and shore, so a
  pool or a river reads as the same water as the sea it runs into. Where it runs, its ripples run with it and fast
  water foams.
- The WaterClarity and WaterColour settings are gone: the water takes the sea's colours.

## v0.17.4 — 2026-10-24

### Fixed
- A ship could not be placed on the flowing water: the game took it for dry ground. Aimed at its surface, a ship now
  goes on it as on the sea.

## v0.17.3 — 2026-10-24

### Fixed
- The hammer could no longer remove a source or drain once they were hidden. It now does, as any piece: with the
  hammer in hand they show, and the one aimed at turns red.

## v0.17.2 — 2026-10-24

### Fixed
- Remove source or drain was missing from the hoe's pieces: the game's menu leaves pieces of its own remove kind out
  of the hoe's. It is now an ordinary piece, there with the source and drain.

## v0.17.1 — 2026-10-24

### Fixed
- With another mod letting the hoe remove pieces, Remove source or drain removed whatever it was aimed at: it now
  removes only sources and drains. With a source or drain chosen, the remove key no longer takes away a placed one.
- If the game's hoe could not be found, placed sources and drains stayed hidden and out of reach: they now show at
  all times then, as before they were hidden.

## v0.17.0 — 2026-10-24

### Added
- The hoe has a third water piece, Remove source or drain: chosen, it shows the sources and drains around, the one
  aimed at turns red, and a click removes it.

### Changed
- Placed sources and drains are now invisible: they show only while the hoe is in hand with one of its water pieces
  chosen, and only then can they be aimed at, changed with E, or removed. E aimed at a placed one now changes it even
  while a source or drain is chosen to place.

## v0.16.7 — 2026-10-23

### Fixed
- No one could swim or float in the flowing water: characters walked along the bottom of a deep pool, and boats and
  items sank through it. Its water now holds them up as the game's own water does.

## v0.16.6 — 2026-10-23

### Fixed
- Water running down a slope was drawn as a stair of one-metre blocks, with a wall of water standing at every step
  and shards along its edges. It is now one smooth sheet down the slope, with sides only where water really ends: a
  fall's sheet, a pool's edge over a cliff (none into the ground, none along a thin sheet, whose edge fades).
- Water's surface jumped at each change the server sent, and a thin sheet's patches switched on and off all the
  time instead of running as a steady stream. The water drawn now moves smoothly from one change to the next, and
  water too thin to see (under 3 cm) is left out, a patch drawn staying drawn until its water is clearly gone.

## v0.16.5 — 2026-10-23

### Fixed
- A character made before the hoe's Water source and Water drain did not see them in the hoe until something in its
  inventory changed: it now learns them as it enters a world where FlowingWater is.
- On a server without FlowingWater, the hoe offered the source and drain (which did nothing there) to the server's
  admins and kept them learned: it now offers only the game's own pieces there, and the character forgets them until
  it plays where the mod is again.

## v0.16.4 — 2026-10-23

### Fixed
- A fault in the source and drain panel would have stopped a host's or server's water too, every frame after it: the
  panel now logs its error once, puts itself away (the keys and mouse back to the game) and stays off until a world is
  next opened, and the water goes on.
- A game update renaming one of the methods the hoe's sources and drains rely on would have stopped the whole mod from
  loading, its water with it: the mod now logs an error naming that method and leaves out only what needs it.

## v0.16.3 — 2026-10-23

### Fixed
- Still water kept the server busy: a lake or pool over ground ending just under the top of a voxel (a centimetre or
  so of room left there) never quite settled, so its water stayed awake, was worked out every frame and sent to the
  players every second for as long as the server ran. It now settles and sleeps as any other.
- With all the water still, the server still looked over every block of it each frame: it now does nothing for it
  until water is poured or drained, a piece or the ground changes, or moving water comes near. The debug log tells
  how many blocks are awake.
- In a big world the look for sources and drains went on every frame, a look taking longer than `MarkerScanSeconds`:
  that wait now counts from the end of each look.

## v0.16.2 — 2026-10-23

### Fixed
- With water moving, single server frames still went 3 to 5 times over `StepBudget` now and then, though the average
  held. Changed blocks of water are packed for sending a few per frame (and three times faster), instead of all in
  one frame each second. A block where the water first reaches a zone waits a frame or two for the game to generate
  the ground there, instead of holding the frame.
- With `DebugSeconds` set, the server's log now tells each part of the water's time (in all, its most in a frame,
  its longest frame and what took it).

## v0.16.1 — 2026-10-23

### Fixed
- With much water changing at once (a lake filling, several streams in view), the game stuttered every second or so:
  each brick of water the server sent was read in the frame, all of them in the same one. They are now read away
  from the game's frame and handed to the water a few at a time, within the time a frame gives the water.

## v0.16.0 — 2026-10-23

### Added
- Sources and drains are placed with the hoe: its pieces now include a Water source and a Water drain, free, set
  exactly where you aim, on the ground or on anything built (a floor, a wall's top). A panel on the right of the
  screen shows the rate, and a drain's radius, that the next one takes; **E** changes them. Looking at a placed one
  tells what it does, **E** on it opens the panel to change it or remove it (its **Remove** button), and the hammer
  removes it as any piece. The `flowingwater` command still works as before.
- The players who may use the `flowingwater` command are the ones who may place, change and remove sources and
  drains (the host and the server's admins always, others while the server's `CommandsForEveryone` is on): the hoe
  offers them to no one else, and a player refused is told why.

## v0.15.13 — 2026-10-23

### Fixed
- A zone whose generated ground the game could not give, with terrain changes in it, made the server build it again
  and refill all the water over it every two seconds: it is now tried again only when the terrain there changes.
- A fixed source or drain (`FixedSources`, `FixedDrains`) with no water near it made the server wait on the game's
  ground every few seconds: its ground is now kept.

## v0.15.12 — 2026-10-23

### Fixed
- The water (the mod's own shader) took none of the game's fog: far off, in mist or at night, it stood out darker
  and sharper than the land round it. It now fades into the fog as the land does.

## v0.15.11 — 2026-10-23

### Fixed
- In a big world the server hitched every few seconds while it looked for sources and drains: the looking is now
  spread over frames.
- A server kept the ground of every zone its water had ever reached, and checked it again every two seconds, so the
  water cost more the longer the server ran: zones the water has left are now let go, and a terrain change no longer
  rebuilds the zone's whole ground.
- Switching the water off (`Enabled`) and on again read every piece broken meanwhile in one frame: the pieces are now
  looked over a zone at a time, as usual.
- Water that had just reached a new place could ignore a terrain change made there right after, until the next one.
- A source buried in rock logged "no room" every three seconds, and a source or drain with a rate that is no use
  logged it as often: each is now told once.
- Old column water the server could not move into voxels yet was still drawn over the water, where no one could swim,
  and took the players' frame time: it is kept for the server to move later, no longer drawn.
- At 0.25 m voxels (`CellSize`), a thin sheet of water could stop moving before it was level.
- Thin films of water vanished, and the rest shifted a little, at every restart and `CellSize` change: a block of
  voxels now keeps its volume.
- A broken terrain change could stop the server's water for good: the ground there is now taken as generated, and a
  saved block of water holding numbers that are not numbers is refused as unreadable.
- `flowingwater debug`, `flat` and `lift` did nothing on the 3D water: they work on it now, and `flat` no longer makes
  a new material each time. `mat stream` is gone: nothing is drawn with the stream material any more (`mat custom`
  and `mat still` stay).
- A bright line ran round anything standing in the water (the mod's own shader): the water's light now fades out
  there.
- Drawing dense changing water could still take more of the frame than its 2 ms: the last of the surface's work is
  now done on another core too.

## v0.15.10 — 2026-10-23

### Fixed
- Still water cost the server a few milliseconds every frame (more on a big world) just to find which blocks of water
  had dried: only blocks whose water changed are looked at now.
- A pool against a wall or on the ground kept making and dropping the block of water past it, each time reading the
  pieces there again (a hitch near big bases) and taking room from real water under `MaxBricks`: a block the water
  cannot enter is no longer made again.
- With `CustomShader` off (or no shader for the platform), water drawn with the game's own material stood still and
  could vanish at a distance: it now moves and is drawn as far as the game's own water.
- A dedicated server built the water's surface for the blocks round the world's zero, for no one, and warned it had
  no shader: it no longer draws them (creatures it simulates there still swim in the water).
- The water's traffic to players was about twice what it needed: the server no longer takes each block of water back
  from the player near it before sending it.
- Any player could turn `CommandsForEveryone` on in their own config, and with it off even a server's admins could not
  use the command: the server's setting counts now, and the host and admins always may. A source gives, and a drain
  given a rate takes, at most 100 m³/s: a huge rate no longer floods the world at once.
- A release whose upload failed on one mod site could not be finished: both sites are checked before anything is
  tagged, and a failed release can be run again.

## v0.15.9 — 2026-10-23

### Fixed
- Water held at the edge of the room it had (`MaxBricks` reached, or a busy server still making the next block of
  water) went still there and never ran on once there was room: it now flows on as soon as the next block is made.
- Swimmers, floating things and fish in the sea where a river meets it kept losing the sea for a moment, and sank or
  shook: the river's water no longer hides the sea's from what is in both.
- A block of water's object destroyed by something else (another mod, an admin's clean-up) could leave the water
  writing into, and later destroying, whatever object the game made next: dropped items or a player's pieces. The
  water now only ever touches its own objects.
- An error while the water was sent just before a world save stopped the world from being saved: the error is now
  logged and the world is saved, with the water as it was last sent.

## v0.15.8 — 2026-10-22

### Fixed
- After deep water had moved, a world whose water had all gone still kept the server busy each frame checking it in
  many tiny steps: still water is now checked once a frame.

## v0.15.7 — 2026-10-22

### Fixed
- Water beside a big base made the server hitch every two seconds while it looked for built or broken pieces: the
  looking is now spread over frames, a little at a time.

## v0.15.6 — 2026-10-22

### Fixed
- Still water took as much of the server's memory as moving water: a still lake now takes about a quarter of it
  (37 KB a block of voxels instead of 133 KB).

## v0.15.5 — 2026-10-22

### Fixed
- Water spreading into a new place near buildings made the server hitch while it read which pieces stand there: that
  reading is now about three times quicker.

## v0.15.4 — 2026-10-22

### Fixed
- A big flood could still stall single server frames: the water's work and the making of new blocks of water are
  now spread over several frames within `StepBudget`.

## v0.15.3 — 2026-10-22

### Fixed
- The water cost the game's server much more than it needed to: the same water now takes about 40% less of the
  server's time.

## v0.15.2 — 2026-10-22

### Fixed
- Drawing changing water took the game's frame time: the water's surface is now worked out on another core, and only
  handed to the graphics in the frame, so moving water stutters less.

## v0.15.1 — 2026-10-22

### Fixed
- Water deeper than about 12 voxels (12 m at the default `CellSize`, 3 m at 0.25 m; a flooded pit or a tall tank)
  never settled once stirred: it sloshed for ever and kept the server busy. It now settles level at any depth.

## v0.15.0 — 2026-10-22

### Added
- Setting `StepBudget` (8 ms by default): the most of each server frame the water may take, on average. A big flood
  that needs more slows down, sources and drains with it, instead of lagging the server for every player, and the
  server log says so once.

## v0.14.5 — 2026-10-22

### Fixed
- Moving water could make the game stutter about once a second, when the changed water was drawn again all at
  once: only the water next to what changed is drawn again, and the drawing is spread over several frames.

## v0.14.4 — 2026-10-22

### Fixed
- The `CellSize` setting's description overstated the cost of smaller voxels (8 and 64 times): measured, moving water
  costs about 3 times as much at 0.5 m and 15 times at 0.25 m, and still water stays cheap at any size.

## v0.14.3 — 2026-10-22

### Fixed
- Building or breaking a piece near water made the server re-read every piece and the ground for the whole 64 m
  area around it, a hitch when building beside a lake: only the water within a few metres of the piece is read
  again now.

## v0.14.2 — 2026-10-22

### Fixed
- The server no longer re-reads and re-packs still water every second to check whether it changed: a big calm lake
  costs almost nothing to keep in step with the players.

## v0.14.1 — 2026-10-21

### Fixed
- The 3D water cost the server too much: it now works only where water is and moves, and still pools and lakes
  cost almost nothing until something stirs them.

## v0.14.0 — 2026-10-21

### Changed
- Players see the 3D water: pools on raised floors, water spilling over walls and falling, and they swim at the
  water's own level wherever it stands. The running water's sound and spray are silent until they come back.

## v0.13.0 — 2026-10-21

### Changed
- The server simulates the water in 3D: walls and floors you build hold it, a raised wooden box keeps its pool,
  and breaking a piece lets the water run out and fall. Water of earlier versions is carried over, for good: an
  earlier version cannot read it back. Players' games draw it from 0.14.0 on, not in this version.
- Settings `StepsPerSecond`, `Friction`, `BedRoughness` and `MaxDepth` are gone; `MaxTiles` is now `MaxBricks`.
  Erosion is paused until it comes back on the 3D water.

## v0.12.0 — 2026-10-21

### Added
- Console `flowingwater solids`: a map of what the coming 3D water will treat as solid round you (the ground, the
  walls and floors you built), to check a pool or a tank before filling it.

## v0.11.0 — 2026-10-21

### Changed
- Water now looks like a body of water: you see the ground through it, bent by the ripples, and the deeper it
  is the more it takes its own colour, until deep pools hide their bed. Settings `WaterClarity` (how far you
  see into it) and `WaterColour` change it live.
- Sunlight glints off the ripples in sharp sparkles instead of a pale sheen over the whole surface.

## v0.10.0 — 2026-10-21

### Changed
- Banks cut by erosion fall in until they hold, so streams carve sloping valleys instead of sheer-walled
  trenches. Natural cliffs are left as they are.

## v0.9.0 — 2026-10-21

### Added
- Erosion now changes the ground players walk and build on, and is kept with the world. The ground within 5 m
  of anything players built, and of villages, dungeons and other places of the game, is left alone.

## v0.8.0 — 2026-10-21

### Added
- Erosion, in progress and off by default (server setting `Erosion`, speed `ErosionSpeed`): running water wears
  the ground away where it runs fast and lays it down where it slows, so a stream cuts its own bed. For now
  only the simulation sees it: players' ground does not change yet.

## v0.7.0 — 2026-10-21

### Added
- Running water now sounds: where streams run fast you hear them babble, louder the more water passes, and
  roar where the water falls. Setting `WaterVolume` (0 turns it off); it follows the game's sound-effects volume.
- Spray where water rushes down steep ground. Setting `Spray`.

## v0.6.1 — 2026-10-21

### Fixed
- Straight lines showed across the water where one zone meets the next: the two sides were lit (and sometimes
  set) a little differently. They now meet seamlessly.

## v0.6.0 — 2026-10-21

### Changed
- The cream-coloured band along the water's edge is gone: the edge now fades out to nothing.
- Water reads better: a little more colour in shallow water, a softer sky reflection, and larger ripples that
  still show from afar.
- White water now comes from how steep the water runs (rapids and falls foam, gentle streams do not), with only
  a trace of foam at the shore.

## v0.5.0 — 2026-10-21

### Changed
- Water runs more like water: shallow water is no longer pushed as if it were a metre deep, and the ground
  holds it back the shallower it is (new server setting `BedRoughness`). Streams run deeper and slower
  instead of as a thin film racing down slopes.

### Added
- Drains take water from every spot within 1 m of them by default (setting
  `DefaultDrainRadius`), or as given: `flowingwater drain [m³/s] [radius]`. They reach into the next zone too.

## v0.4.0 — 2026-10-21

### Added
- Server setting `CellSize`: the water can be simulated on cells of 1, 0.5 or 0.25 metres, for narrower
  streams and finer shores at a higher cost. Water already there moves to the new size.
- The current can now be up to 16 m/s instead of 6.35, with finer steps for slow water.

### Changed
- The water costs much less: the server steps the zones in parallel and skips dry ground, and players
  draw only the wet part of each zone, rebuilt at most 20 times a second.

## v0.3.6 — 2026-10-21

### Fixed
- `flowingwater mat custom list` answered as if "list" were an unknown property; it now just lists them.

## v0.3.5 — 2026-10-21

### Fixed
- Turning `Client.CustomShader` off or on (with a config manager, in game) changed nothing until the water
  was redrawn for another reason; it now applies at once.

## v0.3.4 — 2026-10-21

### Fixed
- `flowingwater source`, `drain` and `lift` with a rate that is not a number, or is negative, silently used the
  default; they now refuse it and say why.

## v0.3.3 — 2026-10-21

### Fixed
- `flowingwater remove` run twice in a row failed with an error instead of saying nothing was left nearby.

## v0.3.2 — 2026-10-21

### Fixed
- The water was drawn pale and milky: the shader's material still carried its first, much lighter colours and
  heavier foam instead of the tuned ones. It now always takes the shader's own values.

## v0.3.1 — 2026-10-21

### Fixed
- On a world created in the same session (a new local world or a new server world), the water never flowed:
  the simulation waited for a save to be read and none was. It now starts on every world.
- Source and drain markers came out black instead of blue and dark purple.

## v0.3.0 — 2026-10-21

### Added
- **The mod's own water shader.** Ripples run downstream with the current, the water is clear where shallow
  and dark where deep, fast water foams, and the edge fades out instead of ending in a hard line. Built for
  the game's Windows build (also under Proton) and its Linux build; elsewhere, or with the new setting
  `Client.CustomShader` off, the game's own materials are used as before. `flowingwater mat custom <property>`
  tunes it in game.

## v0.2.0 — 2026-10-21

### Changed
- **Water with momentum.** The flow model is now the "pipe" one: the water between two cells keeps its speed,
  gravity pushes it down a slope and friction slows it (setting `Friction`, replacing `FlowRate`). Streams rush
  and gather instead of oozing evenly, pools overflow in a surge and settle, and the current is the real one.
  Fifteen steps a second by default.
- **Softer look.** Running water is drawn clearer and lighter, thinning out towards its edges, and the drawn
  surface is smoothed around the shore so the one-metre steps show less.

### Added
- Console `flowingwater mat still|stream <property> [values]` changes the water materials in game, to tune their
  look without a rebuild (`list` names the properties).

## v0.1.1 — 2026-10-21

### Fixed
- Running water drew as thin blue stripes: the game's stream material masks its water into trickles, and the
  mask is now replaced by a full sheet. The drawn surface is averaged over neighbouring cells, so the
  step-by-step spreading no longer shows as rings and terraces, and a source feeds a 3 by 3 metre patch instead
  of one spiking cell.

## v0.1.0 — 2026-10-21

### Changed
- **Water that can be seen.** Running water is drawn at least 15 cm thick and tinted, so a thin stream on a
  slope no longer fades to nothing; still water shows as shallow sea water. The water's edge now runs on into
  the ground instead of stopping at a cell's edge, so shores look like shores and not like steps.
- **Rounder flow.** Water moves to all eight neighbouring cells, not four, so pools spread round and streams
  on a slope fan out instead of running in a straight-edged band. The stream texture runs at the water's speed.

### Fixed
- Source and drain markers showed as pink spheres; `flowingwater remove` crashed the game; an error was logged
  when leaving a world.

### Added
- Console `flowingwater debug`, `flat` and `lift [m]` to see what is drawn, and `Server.FixedSources` /
  `Server.FixedDrains` / `Server.DebugSeconds` for servers and tests without a client.

## v0.0.1 — 2026-10-21

### Added
- **First prototype.** Water springs from placed sources, runs downhill over the terrain, pools, spills
  on and vanishes in drains or the sea. The server simulates every river whole, whether players are near
  or not; players see the water, swim in it, float on it and are carried by its current. Console command
  `flowingwater source | drain | remove | info` places and inspects it.
