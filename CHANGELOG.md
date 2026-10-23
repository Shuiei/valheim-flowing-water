# Changelog

All notable changes to FlowingWater. Newest first.

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
