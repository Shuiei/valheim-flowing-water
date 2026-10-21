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
| `flowingwater debug` | What the tile under you draws: materials, shaders, triangles |
| `flowingwater flat` | Plain colours instead of water (blue still, green running), to see the shape |
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
| `Server.CellSize` | 1 | Size of the simulation's cells: 1, 0.5 or 0.25 m (see [Grid size](#grid-size)) |
| `Server.StepsPerSecond` | 15 | Simulation steps per second with 1 m cells (twice that at 0.5, four times at 0.25) |
| `Server.Friction` | 0.5 | How fast a flow loses its momentum, per second, besides the bed's drag |
| `Server.Erosion` | false | Running water wears the ground away and lays it down (in progress: the simulation only) |
| `Server.ErosionSpeed` | 1 | How fast erosion goes (1 = millimetres a minute in a fast stream); higher for testing |
| `Server.BedRoughness` | 0.04 | The ground's drag on running water (Manning's n): 0.03 smooth channel, 0.04 natural stream, 0.08 grass; 0 = none |
| `Server.SendSeconds` | 1 | How often a changed zone is sent to the players near it |
| `Server.MaxTiles` | 256 | Most zones with water at once |
| `Server.MaxDepth` | 40 | Deepest a cell fills, metres |
| `Server.EmptyTileSeconds` | 30 | A dry zone without a source forgets its tile after this |
| `Server.FixedSources` | | Sources from the config, `x,z,rate; x,z,rate` |
| `Server.FixedDrains` | | Drains from the config, same form (rate 0 takes all) |
| `Server.DebugSeconds` | 0 | Log each tile as a map of depths this often (0 = off) |
| `Markers.DefaultSourceRate` | 2 | m³/s of a source placed without a rate |
| `Markers.DefaultDrainRate` | 0 | m³/s of a drain placed without a rate; 0 = all |
| `Markers.DefaultDrainRadius` | 1 | How far around it a drain placed without a radius takes water, metres |
| `Commands.CommandsForEveryone` | true | The command without cheats |
| `Client.CustomShader` | true | The mod's own water shader; off = the game's materials |
| `Client.CurrentStrength` | 1 | How strongly the current carries swimmers and floating things |
| `Client.FlowVisualMinSpeed` | 0.15 | Water faster than this (m/s) is drawn as a running stream |
| `Client.FlowTextureSpeed` | 0.5 | How fast the stream texture runs |
| `Client.Spray` | true | Spray where the water falls or runs steeply |
| `Client.WaterVolume` | 0.7 | How loud running water is (0 = silent), within the game's sound-effects volume |

### Grid size

`Server.CellSize` sets how fine the water is. Smaller cells give narrower streams and finer shores, and
cost more: four times the cells per halving, each stepped twice as often. Measured on one test world (the
same two springs, seven zones with water, a 24-thread PC):

| `CellSize` | Cells per zone | Server time per second | Frame rate looking at the stream |
|---|---|---|---|
| 1 | 64 × 64 | 9 ms | 90 fps |
| 0.5 | 128 × 128 | 53 ms (5.5×) | 78 fps |
| 0.25 | 256 × 256 | 300 ms (32×) | 32 fps |

The data sent to players grows with the cells (4× and 16× before compression). 0.5 suits most servers;
0.25 is for a few small streams. The zones are stepped in parallel, so the server's cost spreads over its cores.

Only the server's setting counts: each zone's saved water carries its own grid and players draw whatever
they receive. Changing the setting moves the water already there to the new grid (same volume), at the
next start or at once with a config manager.

## How it works

- **Grid.** One tile per zone, a square of cells of `CellSize` metres (64 × 64 at one metre), the first at
  the zone's south-west corner. A cell holds a depth of water over its ground, which is the game's
  heightmap read between its one-metre vertices as the game reads it. Water that reaches a cell under sea
  level is the sea's.
- **Step.** The pipe model: every cell has a pipe to each of its eight neighbours carrying a flow that keeps
  its momentum. Each step the level difference accelerates it (gravity, over the pipe's length, on the water
  standing above the higher of the two beds: a film is pushed like a film, a weir by what spills over it, up to
  4 m deep), `Friction` slows it, and the bed's drag (Manning, `BedRoughness`) slows the cell's whole outflow
  by its speed over its depth to the 4/3, so shallow water is held back far more than deep: a stream runs
  deeper and slower instead of as a fast film. A flow may move at most half the difference (so two cells
  level out without crossing over), a flow pushed uphill dies fast, and a cell never gives more than it holds. The flows then move the water, across
  zone borders too. A cell's net flow over its width and depth is its velocity: the current. Smaller cells
  step more often, so water crosses at most about a cell a step and keeps the same speed on any grid.
  A source spreads its water over the cells about a metre around it; a drain takes from every cell within
  its radius, in its zone or the next.
- **Server.** The simulation works on the saved objects (ZDOs) only, never on scene objects, so it runs
  on a dedicated server and in zones no player has loaded. Ground heights come from the world
  generator as the game's heightmap builder makes them, plus the players' terrain changes read from
  each zone's terrain-changes object. Each step moves all tiles at once on the thread pool and skips the
  dry cells nothing flows into. A changed tile is written to its ZDO every `SendSeconds`, and the
  game sends it to the players near that zone.
- **Shader.** The mod's own (`unity/Assets/FlowingWater/Shaders/FlowingWater.shader`, built-in render pipeline,
  standard lighting): the mesh carries depth, white water, shore and current per vertex; a ripple normal map is
  dragged along the current in two offset phases (flow mapping), with a slow second layer for still water and,
  seen from afar, a larger layer that still reads at a distance; colour and opacity go from shallow to deep;
  foam rises with speed, with the surface's slope (rapids, falls) and a little at the shore; the edge fades out
  to nothing, its reflections included (the blending adds light even where the water is transparent).
- **Erosion** (off by default; in progress). Water carries ground (sediment, counted as a height over the
  cell): fast water can carry more, by its depth (up to a metre) and the square of its speed above 0.6 m/s.
  Carrying less, it picks ground up from the bed; carrying more, it sets the rest down; where it dries it sets
  everything down, and what reaches the sea is gone. The sediment moves with the water, at what each cell's
  water carried before the step. The ground never moves more than 8 m from where it was (the game's own limit
  on terrain changes). For now the change stays in the simulation: players' ground does not change yet, and
  it is forgotten on restart; the server's debug map reports what each zone cut and laid down.
- **White water.** Twice a second each tile picks where most water runs fastest (at least 1.2 m/s and 3 cm
  deep), up to six places 6 m apart, the steepest first. Each gets a sound of running water, louder the more
  water passes: a stream's babble (a loop made in code from a bubble model: each air bubble rings at a pitch
  set by its size and dies away, a couple of hundred a second over a soft wash) turning into the roar of
  rapids from a slope of 1 (45°) to 2, both played through the game's sound-effects mixer, only within 35 m.
  Where the surface slopes more than 0.25 there is spray too (a copy of the game's own bow splash made to run
  steadily, thicker with the flow and the slope).
- **Client.** The tile component eases the depths towards the last ones received and rebuilds its mesh
  (at most 20 times a second, over the wet part of the zone only):
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

## Checks

On every push and pull request CircleCI (`.circleci/config.yml`, `.circleci/continue.yml`) builds the
plugin and the recorder against the game's DLLs (Steam's free dedicated server) and BepInEx from
Thunderstore, with every analyzer warning an error (**code-analysis**); checks the formatting
(**formatting**, `tools/format-check.sh`); builds the Thunderstore package (**package**,
`thunderstore/build.sh`); and runs it in a real dedicated server (**server-smoke**,
`tools/server-smoke.sh`): it loads, a fixed source's water fills a zone with no error, and the saved
water comes back after a restart. Main's ruleset requires those four checks, CodeRabbit's approval
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
