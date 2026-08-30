# AutoFATE

A standalone Dalamud plugin rebuild of the **Auto-FATE / Fate Tool Kit ("Date With Destiny")** module
from [Jaksuhn/ffxiv-bundleoftweaks](https://github.com/Jaksuhn/ffxiv-bundleoftweaks). Combat rotation
is handled by **Rotation Solver Reborn**, AoE avoidance/targeting by **BossMod (Reborn)**, and pathing
by **vnavmesh**.

## What it does

- FATE tracker window with configurable sort priority (bonus + Twist of Fate, progress, urgency,
  distance, …), display-name formatting, per-fate blacklist (right-click), click-to-pathfind, and
  tabs listing every command and required dependency (with live loaded status).
- Fully automated FATE grinding: picks the best eligible FATE, generates a BossMod obstacle map for
  it, pathfinds there with vnavmesh (mount/fly/teleport when faster), steers combat with an
  avoidance-only BossMod preset (auto-target capped by role pull size, AI pathfinding movement that
  dodges AoEs) while Rotation Solver Reborn runs the job rotation, activates prep-NPC fates, handles
  collect fates via TextAdvance hand-ins, revives and returns after death, waits for chained
  follow-up fates, and swaps zones when a zone is exhausted.
- Movement fallbacks: fly-path failures retry on the ground, unlandable descents relocate to the
  nearest landable mesh point, and repeated stuck retries escalate to a teleport. The U'Ghamaro
  Mines area of Outer La Noscea is always pathed on foot — its flight mesh is unpolished.
- Grind modes with completion targets: Bicolor Gemstones, Yo-kai Watch medals (auto minion/watch
  swapping), Atma (Zodiac), Luminous Crystals (Anima), Memories / Law's Order (Resistance),
  Demiatmas & Paste (Phantom), or a plain "run N fates" counter. With a mode selected, the
  minimised main window shows a compact progress tracker (like the Yo-kai one) — click any tracked
  item to run the game's item search for it.

## Required plugins

| Plugin | Used for |
|---|---|
| [Rotation Solver Reborn](https://github.com/FFXIV-CombatReborn/RotationSolverReborn) | Combat: runs your job's rotation on the targets BossMod picks |
| [BossMod Reborn](https://github.com/FFXIV-CombatReborn/BossModReborn) | AoE avoidance, targeting, positioning, fate level sync + obstacle maps (veyn's BossMod also works — both expose the same `BossMod.*` IPC) |
| [vnavmesh](https://github.com/awgil/ffxiv_navmesh) | Pathfinding and movement |
| [TextAdvance](https://github.com/NightmareXIV/TextAdvance) | Collect-fate item hand-ins |

## Commands

- `/autofate` — toggle the tracker window (aliases: `/af`, `/dwd`)
- `/autofate run <count>` — run until `<count>` fates have been completed
- `/autofate stop` — stop the grind
- `/autofate yokai` — toggle the Yo-kai Watch tracker
- `/autofate role <host|client|off>` — set this character's multibox role
- `/autofate multibox` — print multibox connection status

The Start button runs indefinitely; Ctrl+click Stop for a "finish current fate then stop" soft stop.
The Commands tab in the main window lists everything, including diagnostics.

## Multibox

Multiple game clients on the same machine can grind together. Pick one client as the **Host**
(settings cog → Multibox → Role); it selects fates and leads. Every other client set to **Client**
mirrors the host: it starts/stops when the host does, teleports to the host's zone, and engages the
host's current target fate instead of picking its own. Clients can optionally adopt the host's fate
settings (filters, sort order, grind mode, zones) so the whole group behaves identically.

Sync runs over a local named pipe (`AutoFATE.MultiboxSync.v1`) — no network, same-PC only, exactly
one host at a time. Roles are saved per character, so a shared Dalamud install keeps each
character's role straight. Party the characters up so everyone gets fate credit, and note that
instanced overworld zones aren't auto-matched: if the host is in instance 2, switch the clients
there manually.

## How the combat integration works

On install (and whenever it goes missing) the plugin pushes an avoidance-only `AutoFATE` preset over
`BossMod.Presets.Create` — auto-target with FATE priority, fate level sync, and AI pathfinding
movement that dodges AoEs via the obstacle map. It contains **no rotation modules**: while a fate is
engaged the preset is activated via `BossMod.Presets.SetActive` and Rotation Solver Reborn is put in
Manual mode over `RotationSolverReborn.ChangeOperatingMode`, so RSR fights whatever BossMod targets
and never picks targets itself. Both are switched off out of combat and on stop. Pull size is set
per role through `BossMod.Presets.AddTransientStrategy` on AutoTarget's `MaxTargets` track (tank:
unlimited, healer: 5, DPS: 3). Obstacle maps for fate areas are generated through
`BossMod.ObstacleMap.*` and discarded when their quality is too poor to navigate.

## Building

```
dotnet build AutoFATE/AutoFATE.csproj -c Release
```

Requires the XIVLauncher dev Dalamud install (`Dalamud.NET.Sdk/15`). Load
`AutoFATE/bin/Release/AutoFATE.dll` as a dev plugin.

## Credits

- [Jaksuhn (croizat)](https://github.com/Jaksuhn) — original Fate Tool Kit / Date With Destiny module
  and the `clib` task/automation framework this plugin vendors a trimmed copy of (BSD 3-Clause, see
  `LICENSE.md`).
- veyn/awgil — BossMod and the AutoTask pattern; FFXIV-CombatReborn — BossMod Reborn.
