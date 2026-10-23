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

Status: **a prototype.** The flow model is a shallow-water one on a grid of cells (one metre by default,
fifteen steps a second); it is tuned for rivers and ponds, not for
waves or splashes. Buildings do not hold water yet: only the terrain does.

## Placing water

Console (F5) command `flowingwater`:

| Command | Does |
|---|---|
| `flowingwater source [m³/s]` | A spring under your feet (default 2 m³/s) |
| `flowingwater drain [m³/s] [radius m]` | A drain under your feet; 0 or nothing takes all that reaches it, from every cell within the radius (default 1 m) |
| `flowingwater remove` | Removes the nearest source or drain within 5 m |
| `flowingwater info` | What is under you: ground, depth, current, the zone's water |
| `flowingwater debug` | What the block of water you are in (or the nearest over or under you) draws: material, shader, triangles |
| `flowingwater solids` | What the coming 3D water will treat as solid round you: a map of the voxel row at your feet (solid voxels, faces walls close) and the faces under it (floors) |
| `flowingwater flat` | A plain colour (blue) instead of water, to see the shape |
| `flowingwater lift [m]` | Draw the water that much higher, to find it |
| `flowingwater mat custom\|still\|stream <property> [values]` | Change a water material property in game (`list` names them) |

The command works for everyone unless `CommandsForEveryone` is off in the config (then cheats must
be on). Hammer pieces for the spring and the drain come later.

A server can also have sources and drains with no marker placed, from its config: `FixedSources` and
`FixedDrains` take `x,z,rate` triples separated by `;` (world coordinates, cubic metres per second); a drain may
add its radius in metres, `x,z,rate,radius`.

## Settings

`BepInEx/config/Tie.FlowingWater.cfg`:

| Setting | Default | What |
|---|---|---|
| `Enabled` | true | Simulate (server) and show (client) |
| `Server.CellSize` | 1 | Side of the water's cubic voxels: 1, 0.5 or 0.25 m (see [Grid size](#grid-size)) |
| `Server.Erosion` | false | Running water wears the ground away and lays it down; not within 5 m of what players built (paused with the 3D water, back in a later version) |
| `Server.ErosionSpeed` | 1 | How fast erosion goes (1 = millimetres a minute in a fast stream); higher for testing |
| `Server.SendSeconds` | 1 | How often a changed block of water is sent to the players near it |
| `Server.StepBudget` | 8 | Most milliseconds of each server frame the water may take, on average; beyond, the water slows down instead of the server |
| `Server.MaxBricks` | 1024 | Most blocks of 16 × 16 × 16 voxels with water at once (about 130 KB each while the water moves, 37 KB once still) |
| `Server.EmptyTileSeconds` | 30 | A block of voxels dry this long is forgotten |
| `Server.MarkerScanSeconds` | 3 | How often the server looks for new or removed sources and drains, seconds (1 to 60) |
| `Server.FixedSources` | | Sources from the config, `x,z,rate; x,z,rate` |
| `Server.FixedDrains` | | Drains from the config, same form (rate 0 takes all) |
| `Server.DebugSeconds` | 0 | Log each zone with water as a map of depths this often (0 = off) |
| `Markers.DefaultSourceRate` | 2 | m³/s of a source placed without a rate |
| `Markers.DefaultDrainRate` | 0 | m³/s of a drain placed without a rate; 0 = all |
| `Markers.DefaultDrainRadius` | 1 | How far around it a drain placed without a radius takes water, metres |
| `Commands.CommandsForEveryone` | true | The command without cheats |
| `Client.CustomShader` | true | The mod's own water shader; off = the game's materials |
| `Client.WaterClarity` | 4.5 | How far one sees into the water (m): shallow stays clear, deep takes the water's colour |
| `Client.WaterColour` | 081C24FF | The colour deep water takes (RGBA hex, RRGGBBAA) |
| `Client.CurrentStrength` | 1 | How strongly the current carries swimmers and floating things |
| `Client.FlowVisualMinSpeed` | 0.15 | Water faster than this (m/s) is drawn as a running stream |
| `Client.FlowTextureSpeed` | 0.5 | How fast the stream texture runs |
| `Client.Spray` | true | Spray where the water falls or runs steeply |
| `Client.WaterVolume` | 0.7 | How loud running water is (0 = silent), within the game's sound-effects volume |

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
there to the new grid (same volume), at the next start or at once with a config manager.

## How it works

- **Grid.** Water lives in 3D: cubic voxels of `CellSize` metres, kept in blocks of 16 × 16 × 16 (bricks)
  that exist only where water is or lately was. A voxel holds a share of its volume of water (a little more
  when pressed in). The ground fills each voxel from its bottom up to the terrain's height; building pieces, as
  the boxes of their solid colliders, fill the voxels they stand in from below (a floor) and close the faces
  between voxels they separate, so a wall thinner than a voxel holds water, a box on posts holds a pool, and
  breaking a piece lets the water out. The solids are read again when a piece is built, broken, moved, opened
  or closed (a door), or the ground changes. Water that reaches the sea is the sea's.
- **Step.** The pipe model in three dimensions: each face between two voxels is a pipe whose flow keeps its
  momentum, sped up by the difference of the two voxels' heads and slowed by drag. A voxel's head is its floor
  plus all the water resting on it in its column (up to air, a solid or a closed face), so a still pool is still,
  water over air falls, and a hole low in a box's side lets water out faster the deeper the box; water pressed in
  under a ceiling adds a stiff head, so a pipe carries the push of the water above it. A voxel never gives more
  than it holds. Long steps are split so the water stays stable 10 m deep. Each step works only on the part of a
  block round its water (and its borders where the next block has water), and a block whose water has barely
  moved for a second sleeps (skipped) until water is poured in, a solid changes or a block beside it moves:
  still pools cost almost nothing. A source pours at its point (the first
  open voxel there); a drain takes from every voxel within its radius.
- **Server.** The simulation works on the saved objects (ZDOs) only, never on scene objects, so it runs on a
  dedicated server and where no player is. Ground heights come from the world generator as the game's
  heightmap builder makes them, plus the players' terrain changes; pieces come from their objects and the
  prefabs' colliders. Each block with water is written to its own object (`FlowingWater_Brick`) when it changed,
  every `SendSeconds`, and the game sends it to the players near it. The column water of earlier versions is
  moved into voxels once, when the world opens (a zone's that finds no room is kept, not drawn, and tried again at
  the next start).
- **Shader.** The mod's own (`unity/Assets/FlowingWater/Shaders/FlowingWater.shader`, built-in render pipeline,
  standard lighting): the mesh carries depth, white water, shore and current per vertex; a ripple normal map is
  dragged along the current in two offset phases (flow mapping), with a slow second layer for still water and,
  seen from afar, a larger layer that still reads at a distance; the water is a body, not a tinted sheet: what
  lies behind it (the scene, grabbed once a frame) is seen through it, bent by the ripples, each colour dimmed
  by how much water the eye looks through, from the scene's depth (red goes first, so a pool turns blue-green
  and deeper water hides its bed: `WaterClarity`, `WaterColour`), with 15% of the water's colour even where it
  is thin; foam rises with speed, with the surface's slope (rapids, falls) and a little at the shore; the edge
  fades out to nothing, its reflections included (the blending adds light even where the water is transparent).
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
- **White water.** The running water's sound and spray were made for the column water; they are silent on the
  3D water until they come back on it in a later version.
- **Client.** Each block of water the server sends is drawn by its own object: the surface over each wet voxel
  with no water over it, its corners at the mean level of the tops beside them (a pool lies flat, a stream slopes
  smoothly, the edge fades where fewer columns hold water), and the sides of the water where it borders a voxel
  without (a fall's sheet, water spilling over a wall), with the water's material. It is drawn again at most ten
  times a second, with the blocks beside it when its water changes. A trigger box on the game's water layer tells
  characters and floating things the surface over them: the top of the stack of water they are in or just over,
  so a pool on a raised floor floats a swimmer at its own level. Harmony patches make the game's water-level
  checks and the level any water volume reports include the flowing water, and let a swimmer drift with the
  current of the voxel they are in.

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
dug once).

## Checks

On every push and pull request CircleCI (`.circleci/config.yml`, `.circleci/continue.yml`) builds the
plugin and the recorder against the game's DLLs (Steam's free dedicated server) and BepInEx from
Thunderstore, with every analyzer warning an error (**code-analysis**); checks the formatting
(**formatting**, `tools/format-check.sh`); builds the Thunderstore package (**package**,
`thunderstore/build.sh`); and runs it in a real dedicated server (**server-smoke**,
`tools/server-smoke.sh`): it loads, a fixed source's water fills a zone with no error, and the saved
water comes back after a restart. It also runs the unit tests (**unit-tests**, `tests/FlowingWater.Tests`: the
mod's rules that need no game, compiled for .NET in `tests/FlowingWater.Rules`). On main only, the unit tests run
with their code coverage, which the **coverage** job sends to
[Codecov](https://app.codecov.io/gh/Shuiei/valheim-flowing-water) once they pass; Codecov's check fails under
95% of the rules' lines (`codecov.yml`, `tests/coverage.runsettings`). Main's ruleset requires those four checks, CodeRabbit's approval
(`.coderabbit.yaml`), Codacy's analysis (`.codacy/codacy.yaml`) and CodeQL's code scanning. What CI
cannot check is the water as players see it: try that in the game.

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
   - **github-release** tags the commit `v<VERSION>` and makes the GitHub release with
     `FlowingWater-<VERSION>.zip` and the notes (the Sign release workflow then signs the zip);
   - **publish-thunderstore** and **publish-hexium** publish it side by side
     (`tools/release/publish.cs`, the sites' settings in `tools/release/sites.json`, team `Tie`).

A version already on a site fails its job: no site takes a version twice. A failed job can be rerun
from CircleCI (Rerun from failed). Still by hand: **unhide** the new version on Hexium (uploads arrive
hidden). Nexus Mods comes once the mod has a page there (made on the website: the API cannot): then a
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
