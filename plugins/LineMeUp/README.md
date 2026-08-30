# Line Me Up

A Dalamud plugin that **walks** your character onto the spot a target is standing on, then turns you
to face the same way they do.

Your character gets there under its own power, at its own speed, colliding with whatever it would
normally collide with — the plugin steers, it doesn't place you.

## How it walks

The plugin hooks the game reading its own movement input (`RMIWalk` / `RMIFly`) and, only when you
aren't pressing anything yourself, substitutes a direction toward the destination. This is the same
mechanism vnavmesh, visland and Lifestream use.

The important part is that movement input is a **magnitude, not a switch** — an analog stick pushed
halfway walks at half speed. So as the gap closes the input eases down, and the final step is small
enough to land on the mark instead of stomping past it. That's the *Slow-down radius* and
*Minimum crawl* settings.

There is **no pathfinding** — the walk is a straight line. It's built for someone standing a few
yalms away. If something is in the way, the walk gives up rather than grinding against it.

## Usage

| Command | Effect |
|---|---|
| `/lineup` | Walk to the target's spot, then match their facing |
| `/lineup pos` | Position only |
| `/lineup rot` | Facing only, no walking |
| `/lineup follow` | Toggle continuous follow — stays on their mark as they move |
| `/lineup stop` | Stop right now and hand control back to you |
| `/lineup config` | Open the window |

`/lmu` is an alias. Hotkeys are configurable; pressing the walk key again mid-walk cancels it, and so
does touching a movement key.

## How close does it get

Within a few centimetres. The chat message tells you exactly how close every time
(`Lined up on ... — 2.3cm off their mark`).

It won't be perfect to the decimal, and it can't be: walking moves you `speed / framerate` at a time,
so there is always one last step you can't subdivide. To tighten it, lower *Minimum crawl* and
*Arrival tolerance* together — but keep one frame of movement smaller than the tolerance, or your
character will jitter on the spot instead of settling.

Two things walking genuinely cannot do:

- **Close a vertical gap.** If they're on a ledge or a different floor, you'll walk to the point below
  or above them. The window warns when the vertical gap is over half a yalm.
- **Route around obstacles.** No pathfinding, by design.

## Facing

Two modes:

- **Instant** (default) — turns you to their exact facing the moment you arrive, using the game's own
  native setter. This is the one value the plugin sets directly, and it only affects which way you
  look, not where you stand.
- **Turn** — rotates you into place with real turn input, the way holding a turn key would. Slower,
  and it only settles within about a degree, but it sets nothing at all. If your character turns the
  wrong way, tick *Invert turn direction*.

Either way, facing is applied only once you've stopped moving. While walking, your character faces
the way it's travelling, so anything applied earlier would just be undone.

## Offsets

Expressed in the *target's* local frame, in yalms: Forward is the way they're facing, Right is their
right, Up is world up (only meaningful while flying). All zero means you walk onto the spot they
occupy. `Face them` sets the facing offset to 180° so you look at each other.

FFXIV uses `+X` east, `+Y` up, `+Z` south, with yaw in radians on `(-π, π]` where `0` faces south:

```
forward(r) = ( sin r, 0,  cos r)
right(r)   = (-cos r, 0,  sin r)
```

Sanity check: at `r = 0` you face south, so your right hand points west — and `right(0)` is
`(-1, 0, 0)`, which is west.

## If walking stops working after a game patch

The movement hook is signature-scanned, and signatures break on patches. When that happens the plugin
doesn't crash — it disables walking, says so at the top of the window, and everything else keeps
working. The two patterns live at the top of `MovementOverride.cs`; the upstream copies in
[vnavmesh](https://github.com/awgil/ffxiv_navmesh) are the place to get refreshed ones.

## Building

Requires the Dalamud dev libraries at `%AppData%\XIVLauncher\addon\Hooks\dev\` (XIVLauncher installs
these) and the .NET 10 SDK.

```bash
dotnet build "LineMeUp/LineMeUp.csproj" -c Release
```

Output lands in `LineMeUp/bin/Release/`. To load it: Dalamud settings → *Experimental* →
*Dev Plugin Locations* → add that folder → enable **Line Me Up** in the plugin installer.

Built against Dalamud API level 15 (`Dalamud.NET.Sdk/15.0.0`).

## Layout

| File | Role |
|---|---|
| `Plugin.cs` | Entry point, command registration, lifecycle |
| `MovementOverride.cs` | The input hook — turns "go here" into real movement input |
| `Aligner.cs` | The job: target resolution, offset math, walk/follow state machine, hotkeys |
| `Configuration.cs` | Persisted settings |
| `MainWindow.cs` | ImGui window, including the live gap readout |
| `Svc.cs` | Injected Dalamud services |
