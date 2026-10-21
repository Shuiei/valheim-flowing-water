# Changelog

All notable changes to FlowingWater. Newest first.

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
