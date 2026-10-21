# Changelog

All notable changes to FlowingWater. Newest first.

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
