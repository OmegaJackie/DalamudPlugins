# Bozja Buddy Reborn

A Dalamud plugin that orchestrates the Bozjan Southern Front / Zadnor field-operation loop:
it reads live Bozja state, travels to Critical Engagements as they open, farms skirmish FATEs
in between, coordinates several game clients, and tracks Resistance relic progress.

Built against **Dalamud.NET.Sdk 15.0.0** (API 15, net10.0-windows), verified compiling against
Dalamud 15.0.2.3 / FFXIVClientStructs 7.51.0.8543 / Lumina.Excel 7.5.1.0. Debug and Release
both build with 0 warnings.

```bash
dotnet build BozjaBuddyReborn.csproj -c Release --nologo
```

**Build discipline.** Every change is compile-verified in Debug *and* Release, and the version is
bumped by one on the third component so the dev plugin actually reloads — an unchanged version
number makes the in-game reload a silent no-op. `<Version>`, `<AssemblyVersion>` and
`<FileVersion>` are set together. The changelog lives in the comment block above `<Version>` in
the csproj, newest first, recording what each build changed and why. Current version: **1.0.24.0**.

The Debug build writes straight to the path Dalamud loads as a dev plugin:

```
C:\Users\Jackie\Documents\Bozja Buddy Reborn\BozjaBuddyReborn\bin\Debug\BozjaBuddyReborn.dll
```

with `BozjaBuddyReborn.json` and `ECommons.dll` alongside it (the ECommons `ProjectReference`
copies into output, so the runtime dependency resolves). Release output goes elsewhere and is
*not* what the game loads.

## Commands

| Command | Effect |
|---|---|
| `/bbr` | toggle the main window |
| `/bbr start` / `/bbr stop` | run / stop the orchestrator |
| `/bbr config` | settings |
| `/bbrelic` (or `/bbr relic`) | relic progress window |
| `/bbr duty` (or `/bbr actions`) | group duty-action hotbar |
| `/bbr boxes` (or `/bbr mb`) | multiboxer console — loadouts, errands, per-box control |

## The combat role split

This is the central design decision and the reason both combat plugins can run at once.

| Plugin | Role | How it is enforced |
|---|---|---|
| **BossMod** (Reborn *or* the original) | AoE avoidance and positioning **only** | per-fork guards, below |
| **RSR** | the rotation | `RotationSolverReborn.ChangeOperatingMode(Auto)` |

The usual failure when people enable BossMod and RSR together is that BossMod's autorotation
and RSR both queue actions and stall each other. `BossModAvoidance.ApplyAvoidanceOnlyConfig()`
removes BossMod from the action-queue business entirely. **What that takes differs per fork**,
because the two forks' AI is built completely differently — see "Which BossMod" below.

**Reborn** — three independent guards, because each closes a different hole:

1. `/bmrai forbidactions on` — the AI itself queues no actions.
2. `AI.SetPreset("")` — the AI has no rotation preset to run.
3. `Presets.SetForceDisabled()` — the **global** autorotation cannot fire either. This is the
   guard that actually matters if the user had a preset active, since that runs independently
   of the AI.

**Original** — the AI *is* an autorotation preset ("VBM Multibox": AutoTarget + FollowSlot +
NormalMovement, no class module), so it has nothing to press to begin with, and the guards are:

1. `AIConfig.ForbidActions = true` — AutoTarget goes Passive; RSR owns targeting.
2. `AIConfig.ForbidMovement = false` — NormalMovement stays on Pathfind; that *is* the dodging.
3. `Presets.ClearActive` whenever a **user** preset is in the active list — nothing but the AI
   preset runs alongside RSR.
4. **Never** `SetForceDisabled` — in the original, `RotationModuleManager` only adds the AI
   preset `if (_aiConfig.Enabled && !Presets.Contains(ForceDisable))`, so force-disabling would
   remove the dodge engine along with everything else. The original also force-disables *itself*
   on death in combat and on a ninja pull; the heartbeat re-clears that whenever it appears.

`ForbidMovement` is deliberately left **false** in both — dodging *is* movement.

Everything changed is snapshotted first and restored on stop, zone-out, and dispose. Reborn:
prior autorotation preset, prior AI preset, `ForbidActions`/`ForbidMovement` read back through
the `BossMod.Configuration` console gate. Original: `AIConfig.Enabled`/`ForbidActions`/
`ForbidMovement` through the same gate, plus the full active-preset list via
`Presets.GetActiveList` (the original runs several presets at once, so `GetActive` — "the name
iff exactly one is active" — is useless there). Stopping hands BossMod back as it was found.

**Aggro while travelling — run through it.** `Travel()` turns the rotation off so RSR does not
pull things en route, and by default it *stays* off for the whole route even when something has
already aggroed: the runner keeps pathing and lets the mob leash off rather than stopping to kill
it. Bozja and Zadnor pull things onto the line constantly, and answering each one turns a single
run into a string of fights that earn nothing and burn the registration window.

Standing **at** the objective is not "en route" — there is nowhere further to run, and a Critical
Engagement's registration window has to be waited out where you stand — so attackers there are
always answered: stop, dismount, clear, resume. Settings ▸ Engagements can switch the travelling
half to the same behaviour ("Stop and fight back").

Either path keys on hostiles actually targeting you rather than `ConditionFlag.InCombat`, which
lingers after the last mob dies and would make the runner stop for phantom fights. Note that you
cannot mount in combat, so a chase puts the run on foot until the mob leashes.

**Objective stickiness.** Objectives are re-ranked every tick, so without hysteresis a skirmish
that spawns and ranks higher yanks the character out of the fight it is already in. A committed
objective is kept while it is still worth doing — a skirmish still running and incomplete, an
engagement still registering with enough margin to arrive.

**Two movement sources, one winner.** BossMod's avoidance and vnavmesh both want to steer, and
**both forks refuse to steer while a vnavmesh path is running** — their `MovementOverride`
computes `movementAllowed` with `&& !FollowPathActive()`, which reads vnavmesh's shared
`vnav.PathIsRunning` flag. So the controller has to actually *stop* the path to let a dodge
happen, not just stop issuing new ones. A dodge overridden by a travel path is a death.

**But "is BossMod dodging?" is not a question either fork answers directly**, and getting it wrong
stops the runner dead. Reborn's `AI.IsNavigating` is `NaviTargetPos != null` — *"the AI has
somewhere it would like to be"*, set for uptime, positionals, following, staying in range, walking
to an interact target. It is non-null essentially always once the AI is on, and says nothing about
danger. Keyed on that alone (1.0.12.0 and earlier) the controller yielded on every tick, never
issued a path, and reported "dodging a mechanic" forever — and, because BossMod is only free to
move while *we* are not pathing, the permanent yield handed the character to BossMod's own agenda,
which is how it ends up walking off toward a quest NPC.

The gate is therefore `Hints.ForbiddenZonesCount > 0` **and** `AI.IsNavigating`: BossMod must both
see live telegraphed danger and want to move because of it. Anything else is repositioning, and
travel keeps the path — correct, since BossMod defers to a running path anyway. A **6-second cap**
backs it up: a permanent yield is always a bug, so past that the controller takes movement back,
logs a warning and carries on. The Dependencies tab shows both signals separately and the
resulting decision, because conflating them is what caused this.

The **original fork registers no `Hints.*` gates at all**, so it can never report danger and never
triggers a yield. Its avoidance comes purely from its own pathfinding during the moments we are not
moving the character — at holds and once arrived. It also cannot interrupt a route mid-path.

**Fork detection proves itself.** The installed-plugin list says what is installed and *enabled*,
not what actually came up: this machine's log shows `BossModReborn` with `loadPlugin: true` that
never reached "Finished loading", alongside a healthy original. Reborn is only accepted when a
Reborn-**only** gate actually answers, so a half-loaded install cannot make the plugin drive a
surface that is not there.

**Closing on the target: ours with Reborn, BossMod's with the original.** Reborn will *not* walk
the character into range in the configuration this plugin puts it in, and only its source says
so: `AIBehaviour` adds the follow-the-target goal zone inside `if (_followMaster)`, and
`_followMaster` is `master != player`. Solo — which is how a field-operation runner works — the
master *is* the player, so that branch never runs; the fallback below it needs a
`targeting.Target`, which is only populated when an AI preset is loaded, and this plugin
deliberately clears the preset so Reborn cannot press buttons alongside RSR. The result is a
Reborn that dodges and does nothing else, a melee job standing where travel left it, and RSR
falling back to its ranged filler — Enpi on Samurai. `CombatApproach` closes the gap over vnavmesh
instead: melee and tanks to 2y inside the hitbox, everything else to 15y, yielding completely
whenever Reborn is steering.

The original is the opposite case: its `FollowSlot` module has a fallback that adds a goal zone
around the player's hostile target whenever no other module has one (3y melee/tank, 25y
otherwise), and `NormalMovement` pathfinds to it *around the forbidden zones* — strictly better
than a vnavmesh approach that cannot see them. So with the original `CombatApproach` stands down
(`CombatDirector.AvoidanceOwnsApproach`); an approach path of ours would only make BossMod yield.
The flip side is that `FollowSlot` does not check combat or death, so a target left over from the
last fight would have the character walk to it during holds — `CombatDirector.Travel` clears a
hostile hard target for the original only, and RSR in Auto re-targets on `Engage`.

**Re-asserting combat state.** RSR and Reborn cannot be asked what state they are actually in —
RSR's `ChangeOperatingMode` is write-only with no getter, and Reborn exposes no AI-enabled gate —
and both drop the state on their own. Reborn's `AIManager.Update` calls `SwitchToIdle()` the moment
the party slot it follows stops being valid, which a Bozja alliance does routinely. A pure edge
trigger latches "already on" forever after that, so the on/off state is re-sent on an interval
(default 5s, Settings ▸ Combat, 0 to disable). The re-assert is withheld while Reborn is mid-dodge,
because `/bmrai on` runs `SwitchToFollow` and would tear down the behaviour it is dodging with.
The original's AI on/off *is* a config value (`AIConfig.Enabled`) and so is readable: its
heartbeat reads before it writes and touches nothing — no `Modified`, no config save — when
nothing drifted, which also makes it safe to run at any moment.

### Which BossMod

Both forks are supported and auto-detected by InternalName; **Reborn is preferred** when both are
loaded (which is a broken setup in its own right — they register identical `BossMod.*` gate
names, so whichever loaded last owns the IPC and unloading either strips it — and the
Dependencies tab says so). Nothing about the fork is user-configured.

| | **BossMod Reborn** (`FFXIV-CombatReborn/BossmodReborn`) | **Original** (`awgil/ffxiv_bossmod`) |
|---|---|---|
| InternalName / commands | `BossModReborn`, `/bmr` + `/bmrai` | `BossMod`, `/vbm` (`/vbmai` is a deprecated alias, not used) |
| The AI | its own subsystem (`AIManager`/`AIBehaviour`), independent of the autorotation preset | the "VBM Multibox" **autorotation preset**, added when `AIConfig.Enabled` and *not* force-disabled |
| Driven through | `/bmrai on|off`, `/bmrai forbidactions|forbidmovement`, `AI.SetPreset`, `Presets.SetForceDisabled` | `BossMod.Configuration` (`AIConfig Enabled/ForbidActions/ForbidMovement`, read *and* write), `Presets.ClearActive`; `/vbm ai` / `/vbm cfg` as chat fallbacks |
| Telemetry gates | `AI.IsNavigating`, `Hints.ForbiddenZonesCount`, `Hints.NextDamageIn`, `Hints.IsPositionSafe` | none — only `Presets.*`, `ObstacleMap.*`, `Configuration` |
| Dodges en route | yes — travel yields to `IsNavigating` | no — it yields to any running vnavmesh path and cannot say it wants to move; dodges at holds and once arrived |
| Closes on the target | no (solo) — `CombatApproach` does it | yes — `FollowSlot` fallback; `CombatApproach` stands down |
| Snapshot / restore | active preset, AI preset, `ForbidActions`, `ForbidMovement` | `Enabled`, `ForbidActions`, `ForbidMovement`, active-preset **list** (`GetActiveList`/`SetActiveList`), force-disable |

The `BossMod.Configuration` gate takes `<node> <field> [<value>]` — there is **no** `cfg` prefix;
`cfg` is the chat subcommand, not part of the argument. (Earlier builds passed it, so the read-back
had always returned "Config type not found" and `ForbidActions` was never restored.)

## Multibox

Modelled on AutoDuty's `MultiboxUtility`: one local **named pipe** (`BozjaBuddyRebornPipe`),
one host, N clients. Each game client is a separate process with its own Dalamud, so
in-process shared-data helpers cannot see across them.

**What it prevents:** without coordination each box independently picks "the best" engagement
and they scatter — one flies to a CE in the north while another takes a FATE in the south, and
neither group has the bodies. The host picks one objective and broadcasts it.

- **Objective broadcast** — the host decides; clients follow.
- **Arrival barrier** (optional) — the group holds until every box is on site, so nobody
  registers alone. The host releases anyway after `MultiboxBarrierTimeoutSeconds` so one stuck
  box cannot stall everyone.
- **Start/stop broadcast** — the host starts and stops the whole group.
- **Group duty-action hotbar** (`/bbr duty`) — every box's two Duty Action slots on one surface,
  with icons, charges and a live cooldown sweep, and **clicking a slot fires it**. Your own two
  slots always answer; a peer's answers only from the host box, the same rule the multiboxer panel
  states. The press itself is the game's own `RaptureHotbarModule.ExecuteDutyActionSlot`, so
  targeting behaves exactly as it does for a manual click. The instruction carries the *action id*
  the operator was looking at, not just the slot index — a peer's row is up to half a second old
  and any box can reload its slots from the holster, so a mismatch is refused by name rather than
  firing whatever happened to replace it. Turn it back into a readout under Settings → Lost
  Actions if you keep the window somewhere you will misclick it.

**Exactly one box must be ticked as host.** `MultiboxIsHost` defaults to *false* on every box,
so a fresh setup has no listener at all and every box searches forever. The main window now says
so explicitly after a few seconds of failed connects rather than showing a flat "Link down".

**Identity is the connection's, not the character's.** Everything that distinguishes one box from
another keys on the pipe connection id. Character names are display only — they are self-reported,
they arrive *after* the connection is already in the table, they are not unique, and until 1.0.12.0
they were the arrival barrier's key, which is what broke it: the name is computed on the first
framework frame, at the title screen, where there is no character yet, so **every box announced the
literal string `unknown`** and N clients collapsed into one entry. `Peers: 3 / Arrived: 1` meant
`arrived >= peers` was never true and every objective burned the full 45-second barrier timeout.
Toggling the multibox checkbox was the only thing that forced a fresh HELLO carrying a real name —
which is precisely why "spam it until they show up" appeared to work.

**Discovery is fast now.** The connect timeout (1s of *active* polling) is separate from the retry
backoff (0/250/500/1000/2000ms, reset on every success). Previously one 3-second constant served as
both, so toggling multibox on the *host* dropped every client into a 3-second blackout — impatient
re-toggling actively prevented discovery, while toggling on a *client* cancelled the sleep and
reconnected instantly. That asymmetry, with no feedback, is why it read as random.

**Deterministic fallback.** `TargetSelector` in multibox mode deliberately ignores distance —
distance differs per character, and a distance-ranked pick is exactly how two boxes end up in
two places. Ties break on engagement id, which every client agrees on. So if the pipe is down,
the boxes *still* converge on the same objective with no coordination at all.

Named pipes are machine-local, which covers the normal setup (several clients on one PC).
Boxes on separate machines would need a network transport; that is not implemented.

## Travel

### Arrival, and the vnavmesh contract

Two things about vnavmesh drive most of the travel code, and getting either wrong makes the runner
stand still while insisting it is travelling.

**vnavmesh stops around the point *it* was given, not the point you meant.** Critical Engagement
markers are routinely in mid-air or inside geometry, so every destination is snapped onto reachable
mesh first — and vnavmesh then comes to rest on a shell of radius `range` around the *snapped*
point. Measuring arrival against the *raw* marker (which is what the controller did until 1.0.12.0)
is therefore unsatisfiable for any snap that moved the point sideways, and *systematically*
unsatisfiable in exactly the case snapping exists for. The failure was silent and total: travel's
own early return measures against the snapped point, so it stopped issuing paths, while the
controller kept asking for more — and the repath counter, stall clock and widening re-snap all sit
*below* that early return, so nothing ever escalated and no warning was printed. `Movement` now owns
a persistent arrival basis and answers both questions from it. Where the snap had to move the target
further than the arrive range, the status says so — you may be standing outside the arena you aimed
for, and that is worth seeing.

**`Path.Stop` is a request, not a guarantee.** It clears the waypoint list but cannot cancel a
pathfind that is still computing, and that pathfind hands its result straight to the follower
whenever it lands — silently undoing the stop. Stops are therefore latched and re-issued until
vnavmesh reports it is neither computing nor following, pumped every frame from the plugin's update
rather than the controller tick, because the stops that matter most (stopping the run, leaving the
zone, unloading the plugin) are exactly the ones where the controller is no longer ticking.

Two more consequences of the same contract:

- **`PathfindAndMoveCloseTo` returns `false`** when a pathfind is already pending — it refuses, it
  does not queue. So the issue site is the commit point: nothing records "we are heading there"
  unless vnavmesh accepted, and travel will not even enter the issue block while a pathfind is in
  flight, because doing so burns a `Path.Stop` that tears down the leg currently walking in exchange
  for a request that gets discarded.
- **There is one `FollowPath` for the whole process**, so vnavmesh's "is a path running" gates carry
  no request identity. Travel and `CombatApproach` both asked that question meaning "is *my* path
  running", which let a leftover travel path convince the approach it was already closing. Path
  ownership (`NavClient`) is recorded at the single point every request of ours passes through.

A dodge **suspends** rather than stops: the path really does have to end (both BossMod forks refuse
to steer while `vnav.PathIsRunning` is set) but the destination, snap and timing survive it, so a
mechanic no longer defeats the repath throttle or resets the stuck detector.

**There is no teleport inside Bozja or Zadnor.** Neither territory has a single row in the
`Aetheryte` sheet — no aetherytes, no aethernet shards, no in-zone network. The only facility
markers on either map are clustered at the one base camp (Utya's Aegis / Camp Vrdelnis). So
mount travel *is* the fast travel, and there is nothing faster to reach for.

The runner therefore:

1. **Mounts** past 30y (Mount Roulette, `GeneralAction` 9) — without this the character jogs the
   entire map, which in Zadnor is minutes per objective.
2. **Flies only when actually airborne.** The flight flag handed to vnavmesh tracks
   `ConditionFlag.InFlight`, not "flight is allowed". Telling vnavmesh to fly a grounded,
   unmounted character hands it a 3D path it physically cannot follow, and it stalls partway —
   this is a correctness fix, not a speed one.
3. **Dismounts before fighting** (`GeneralAction` 23) and will not arm RSR until grounded. You
   cannot attack from a mount, so arming the rotation early just looks like "RSR is doing
   nothing".

Turn mounting off in Settings → Movement if you want the character to stay on foot.

### Enemy aggro avoidance

Running skirmish to skirmish drags the route straight through field mobs, and the heavier ones
in Bozja and Zadnor will delete a character that gets pulled into a pack. BossMod Reborn cannot
help here — by the time a mechanic is telegraphed you are already tagged — so this happens at the
**pathing** layer: don't enter the cone in the first place.

The model mirrors how FFXIV aggro actually works, in two parts:

- a forward **sight cone** (default 22y, 120°), and
- a smaller all-round **proximity ring** (default 10y) that fires from any angle, including from
  directly behind.

That split is the point — passing *behind* an enemy is safe at a distance that would pull it
head-on, so cone-aware routing gets through gaps a flat keep-out radius would refuse.

When the straight route would walk into a footprint, the runner routes via a perpendicular detour
placed on the opposite side to that enemy. The detour is snapped to the navmesh and then
**re-checked** — snapping can drag the point back into the cone it was meant to dodge, and
committing to that would just walk into the enemy by a longer route. Detours are single-step and
re-evaluated on arrival, which chains them around several enemies without multi-waypoint planning.

Skipped entirely while airborne (ground enemies can't reach you), for enemies within 25y of the
destination (those are the objective's own mobs — routing around them means never arriving), and
for anything already targeting you (self-defence owns that).

The level threshold defaults to **0 — avoid every hostile enemy**, which is what you want while
travelling. Settings → Movement lists nearby hostiles with the levels the game *actually* reports,
so you can raise the threshold from observation rather than from an assumption about what Bozja
mobs read as. All four geometry values are sliders.

### Idle staging

When the working zone has nothing up, the runner goes and waits at a configured staging point
for **the region it is actually working** rather than idling wherever the last fight ended. In
Zadnor the plateaus are far enough apart that starting the next run from the wrong one costs
most of the registration window.

Shipped default: **Zadnor Z3 (Northern Plateau) at map (16.1, 14.7)** → world (-268.7, -338.7).
Configure one per region in Settings → Movement; the **Here** button captures wherever you are
standing. A region with no staging point configured just holds position, so it is opt-in.

Map coordinates carry no altitude, so the point is dropped onto the navmesh from above.
(Seeding Y at 0 is the classic trap — it sits below the terrain, so every nearest-point query
rejects the floor and resolves nothing.)

## Zones — and why they decide the whole run

Both field zones are split into three named regions, and **the relic materials are
region-specific**. Farming the wrong third of the map yields nothing you need.

| | Z1 | Z2 | Z3 |
|---|---|---|---|
| **Bozjan Southern Front** | Southern Entrenchment (3536) | Old Bozja (3537) | The Alermuc Climb (3538) |
| **Zadnor** | The Southern Plateau (3668) | The Western Plateau (3669) | The Northern Plateau (3670) |

PlaceName ids from `PlaceName.csv`; the Z-numbering is confirmed by the map-marker north-south
ordering (Z3 sits at the lowest map Y, furthest north) and matches the relic wiki's own
"(Zone 1/2/3)" labels.

**Zadnor adds a second axis.** Within one plateau, skirmishes and Critical Engagements drop
*different* items — so "right zone, wrong activity" is also a wasted run:

| Region | Skirmish (1 per) | Critical Engagement (2 per) |
|---|---|---|
| Z1 Southern Plateau | Compact Axle | Compact Spring |
| Z2 Western Plateau | A Day in the Life: Battles for the Realm | A Day in the Life: Beyond the Rift |
| Z3 Northern Plateau | Bleak Memory of the Dying | Lurid Memory of the Dying |

Bozja's three augment memories are region-specific but not activity-specific: Tortured (Z1),
Sorrowful (Z2), Harrowing (Z3).

### Choosing where to work

Two ways, and they do not fight each other:

- **Work zone picker** (main window, next to Start) — restrict to Z1, Z2, Z3 or anywhere. The
  stored value is the *number*, so one choice carries across both Bozja and Zadnor; the labels
  shown are for whichever zone you are in.
- **Farm a material** (relic window) — pins the region *and* the activity from the drop table.
  This takes precedence and the zone picker shows as locked, because a material already answers
  the question and letting the two disagree would just produce an empty selection.

If you are in the wrong field zone entirely, it says so instead of silently idling.

### How a region is known

- **Where you are**: exact, from `TerritoryInfo.AreaPlaceNameId` / `SubAreaPlaceNameId`, which
  the game maintains as you cross map ranges. No geometry.
- **Where an objective is**: *learned*. Nothing ships a table of which Critical Engagement sits
  in which region, so the first time you stand at one, the region the game reports is recorded
  and persisted. Until then a positional estimate from the region label anchors keeps a fresh
  install useful, and the learned value permanently replaces it — so a bad estimate near a
  boundary self-corrects the first time that engagement is visited.

Materials farmed *outside* the field zones (Bitter, Loathsome, Haunting, Vexatious, Timeworn
Artifact, Raw Emotion) deliberately get no Farm button — this plugin cannot route to alliance
raids or deep dungeons, and offering one would be a lie.

## Lost Actions

`UseFromHolster(index, slot)` is one function with two behaviours, and `MYCTemporaryItem.Type` says
which one you get. **Type 2 (item)** — every Essence, the kits, Dynamis Dice, Lodestone, Light
Curtain, Resistance Elixir — is *consumed outright* by that call. **Type 1 (action)** — Lost Cure,
Lost Protect, the Banners — is only *loaded into a duty slot*; the charge is spent by pressing the
slot, which is `RaptureHotbarModule.ExecuteDutyActionSlot` via `DutyActions.Press`. The load is not
instant, so every press here is a two-step: load, then press on a later tick **only once the slot
reports the action id that was asked for**.

Nothing re-applies a buff that is already running. The status a row grants is derived two ways —
a small table for the Essences (which grant differently-named "Spirit of the …" statuses) and exact
unique `Action.Name` == `Status.Name` for everything else — and an entry whose status cannot be
named is simply never refused.

### Party support

A separate, **stoppable** task that keeps the party's buffs up and heals whoever is worst off.
Started and stopped from the main window, per-box in the multiboxer, or across the group with
`BoxVerb.PartySupport`.

| Sweep | Who | Order |
|---|---|---|
| 1 — unmet need | party members with **no** buff at all; and, for heals, anyone under the HP floor | the priority order you ticked; heals by **lowest current HP** |
| 2 — top up | party members under **20%** of the buff's total duration | most-expired first |

Both sweeps run over the **whole** configured list, not within each action — an unmet need anywhere
outranks a top-up anywhere, which is what "apply first to those who don't have it" means.

The total duration comes from `ActionTransient.Description` ("Duration: 600s" — Lost Bravery is ten
minutes), parsed from the **English** sheet because the text is localised, first match only because
13 rows carry more than one. Where a duration is not in the data — every Essence, every instant —
the second sweep is **skipped** rather than guessed at, and the settings list says so.

**It cannot target anything outside your party.** Candidates come from `PartyView`, built on
`IPartyList`, which covers the 8-man party only — the 48-player alliance is not in it and not
reachable from it, so there is no way to *name* a non-party target. Membership is re-derived live in
the instant before the cast.

**It never touches your selection.** Casts name their target through `DutyActions.PressAt`, which
calls `ActionManager.UseAction(ActionType.Action, id, targetId, …)` — byte-for-byte the call the
duty bar itself makes, differing only in the target. Aiming by *setting* the hard target instead
was wrong twice: a slot press takes `TargetSystem.GetTargetObjectId()`, which prefers the **soft**
target, so a controller user or another plugin holding one would silently receive the charge; and
the selection is shared with `CombatApproach` (which reads it to decide what to close on) and
`CombatDirector` (which clears a hostile one every travel tick).

In a multibox group each box starts its sweep at **its own position** in the party list and wraps.
Plain party order is deterministic, which sounds like a virtue and is the opposite one here — every
box would pick the same first unbuffed member and eight charges would buy one buff.

It **stops itself** when no configured action is loaded with a charge nor sitting in the holster.
Everyone being covered is deliberately *not* a stop — buffs expire, so it idles and says so.

## Relic tracking

Resistance Weapons (Shadowbringers/Bozja), all 8 stages, scoped to relic progression only — no
fragments, Lost Action inventory, or field notes, as requested.

Quest state comes from `QuestManager` (the authority), material counts from `InventoryManager`.
Nothing is persisted — a stale cached count is worse than no count. The window shows the stage
you are on plus an "outstanding across all stages" list, because the Bozja grind feeds several
stages at once.

Stages 5 (`The Resistance Remembers`) and 7 (`A Done Deal`) are **one-time account-wide** grinds;
the rest are repeatable per weapon. `RelicStage.OneTime` carries that distinction because it
changes what "done" means — a repeatable stage's completed quest only means the tier is unlocked.

## Where the data comes from

Everything the source research marked "unverified / verify against source" was verified against
real source in this build:

| Thing | Verified against |
|---|---|
| `PublicContentBozja`, `BozjaState`, `UseFromHolster` | FFXIVClientStructs `InstanceContent/PublicContentBozja.cs` |
| `DynamicEventContainer`, `DynamicEvent`, `DynamicEventState` | FFXIVClientStructs `InstanceContent/DynamicEventContainer.cs` |
| Resistance rank | `PlayerState.GetContentValue(5)` — documented in the struct |
| Territory ids 920 / 975 / 936 / 937 | `TerritoryType.csv` joined on the Bozja `PlaceName` rows |
| CE roster, duels, large-scale | `DynamicEvent.csv` + `DynamicEventEnemyType.csv` (row 3 = "Solo Engagement") |
| Lost Action names/weights/kind | `MYCTemporaryItem.csv` → `Action.csv`; `Type` 1 = action, 2 = item, cross-checked against `MYCTemporaryItemUICategory` row 7 |
| Lost Action → status effect | `Action.StatusGainSelf` is empty for 98 of 99 rows, so: Essence rows 41–55/56–70 → `Status` 2311–2325 and 73–78 → 2434–2439 (contiguous, same order in both sheets); everything else by exact unique `Action.Name` == `Status.Name` where the action is self- and not hostile-targetable |
| Lost Action buff **duration** | No sheet has a duration column — it is prose in `ActionTransient.Description` ("Duration: 600s" / "Duration: 30m"), parsed from the **English** sheet, first match only |
| Which Lost Actions can be aimed at a party member | `Action.CanTargetParty`; raises separated by `Action.DeadTargetBehaviour != 0` (nonzero for exactly Lost Arise, Lost Sacrifice, Resistance Phoenix) |
| vnavmesh gates | vnavmesh `IPCProvider.cs` |
| BossMod Reborn gates, AI commands, config keys | Reborn `Framework/IPCProvider.cs`, `AI/AIManager.cs`, `AI/AIConfig.cs` |
| Original BossMod gates, `/vbm ai` + `cfg` handlers, AI preset contents, force-disable removing the AI, `FollowSlot` target fallback | original `Framework/IPCProvider.cs`, `Services/TickService.cs`, `DefaultRotationPresets.json`, `Autorotation/RotationModuleManager.cs`, `Autorotation/MiscAI/FollowSlot.cs` |
| Both forks yield to a running vnavmesh path | each fork's `Framework/MovementOverride.cs` (`FollowPathActive`), vnavmesh `Movement/FollowPath.cs` (`vnav.PathIsRunning`) |
| Console gate argument shape (no `cfg`) | each fork's `Config/ConfigRoot.cs` `ConsoleCommand` |
| RSR gate + `StateCommandType` | RSR `IPC/IPCProvider.cs`, `Data/RSCommandType.cs` |
| Relic quest ids + material item ids | `Quest.csv`, `Item.csv` |
| Zone regions + Z-numbering | `PlaceName.csv`, `MapMarker.csv` (ranges 418/446), `Map.csv` |
| Material → region → activity | Consolidated Gamer Wiki `Resistance Weapons` snapshot |
| Live region readout | `TerritoryInfo.AreaPlaceNameId` / `SubAreaPlaceNameId` |
| No in-zone teleport | `Aetheryte.csv` has zero rows for territory 920 or 975 |
| Mount / dismount actions | `GeneralAction.csv` rows 9 and 23 |

### Corrections to the source research document

- **`BossMod.Presets.Create` takes a serialized preset, not a name.** The research doc's
  `Presets.Create("AutoDuty", true)` reading is wrong — the first argument is BossMod's own
  preset JSON. This plugin therefore never creates presets.
- **`StateCommandType` has 7 members, not 5.** `Henched = 5` and `PvP = 6` were added after the
  commonly-circulated listing. The numeric values are what cross the wire.
- **There is no "AI on/off" IPC.** The AI loop is a chat command (`/vbmai` or `/bmrai`); IPC only
  manages presets and exposes hints/telemetry.
- **Part B was not "partially mapped".** Every Bozja struct the doc flagged as needing
  verification is fully defined in current FFXIVClientStructs, including a `UseFromHolster`
  member function.
- **`UseFromHolster` does not fire an action-type Lost Action — and this file said it did.**
  Through 1.0.20.0 the line above finished "…a `UseFromHolster` member function that removes the
  need for any `ActionManager` duty-slot guesswork." That reads the ClientStructs doc comment
  backwards, and `HolsterDriver` was built on it. The comment is *"use lost action from holster
  **into** specified duty action slot (slot is ignored for items, which are used directly)"* — one
  call, two behaviours, and `MYCTemporaryItem.Type` says which one you get:

  | `Type` | Rows | What `UseFromHolster` does |
  |---|---|---|
  | `1` — action | 33: Lost Cure, Lost Manawall, Lost Font of Power, the Banners, … | **Loads** the duty slot and stops. Something still has to press it. |
  | `2` — item | 66: every Essence and Deep/Pure Essence, Dynamis Dice, Resistance Phoenix, Reraiser, the potion/ether/medi kits, Lodestone, Light Curtain, Resistance Elixir | **Consumes** it outright. No duty slot involved. |

  (The split is corroborated by `Category`: every `Type` 2 row and no `Type` 1 row sits in
  `MYCTemporaryItemUICategory` row 7, "Item-related".) So the duty-slot half is not guesswork that
  was removed — it is a second call that was missing. It is
  `RaptureHotbarModule.ExecuteDutyActionSlot`, added as `DutyActions.Press` in 1.0.20.0 and wired
  into `HolsterDriver` in 1.0.21.0 behind its own opt-in. `ActionManager` is still not used for the
  press, and for a better reason than this one: the game identifies a duty action by the `Action`
  sheet's `PrimaryCostType` (20 for slot 1, 21 for slot 2), so pressing the slot sidesteps a
  parameter an `ActionManager.UseAction` call would have to get right.

## Seams — needs in-game confirmation

Compilation and data are verified; these are behavioural and cannot be checked offline.

1. **CE registration.** The plugin travels into the engagement area during the `Register` phase
   and holds, on the model that standing inside is what joins you (LogMessage 9631, "you have
   been granted permission to engage"). If a given CE instead requires the Resistance
   Recruitment menu, that path is not implemented.
2. **`MapMarker.Position` as the CE destination.** It is the published marker centre; for a few
   arenas it may sit off the walkable mesh. `NavmeshIpc.SnapToMesh` is the mitigation
   (`NearestPointReachable` → `PointOnFloor` → raw), and the stall guard re-snaps on wedge.
3. **Zadnor elevation.** Known-bad terrain for ground pathing. `StallTimeoutSeconds` drives
   teardown/re-snap/re-path; tune it if runs wedge.
4. **`AI.SetPreset("")` as "clear the AI preset."** Reborn's gate looks the name up among its
   presets and calls `SetAIPreset(found)`, so an unmatched name resolves to `null`, which is
   exactly "no preset". That is read from source, not observed live.
5. **The `AIConfig` read-back** (`BossMod.Configuration ["AIConfig","ForbidActions"]`, and
   `Enabled`/`ForbidMovement`) matches both forks' `ConsoleCommand` parsers as read from source
   (node name first, no `cfg`), but was not round-tripped live. If it returns nothing, the value
   is simply not restored on stop — the failure is a setting left as we set it, never a crash.
9. **Original fork: `AIConfig.Modified` from the console gate is what applies `ForbidActions`.**
   The original's `AIWindow` subscribes to `AIConfig.Modified` and pushes ForbidActions /
   ForbidMovement into the AI preset's transient settings (`ToggleTarget`/`ToggleMove`); the gate
   is called with `save=true` precisely so `Modified` fires. Read from source, not observed.
10. **Original fork: restoring the active-preset list.** `Presets.SetActiveList` gives up
   wholesale if any saved name no longer resolves; the fallback restores one at a time through
   `SetActive`. The list is snapshotted with `""` (the force-disable sentinel) and `"VBM Multibox"`
   (the AI preset, which re-adds itself) filtered out.
11. **The dodge yield stops the travel path.** Both forks refuse to steer while
   `vnav.PathIsRunning` is set, so this is required for a mid-route dodge to happen at all with
   Reborn — but it means a dodge costs a re-pathfind. Dodges now *suspend* rather than stop
   (destination and timing survive), and resume is held 400ms after the signal drops because
   Reborn's `IsNavigating` flaps around a telegraph edge. That 400ms is a judgement call.
12. **How far the navmesh snap actually moves real CE markers** in Bozja and Zadnor. This decides
   how much the arrival fix matters and, more importantly, whether arriving at the snapped point
   still puts you inside the registration area. Watch for the status line
   `Arrived as close as the navmesh allows — Ny from the marker centre` and report any large N;
   if drift turns out to be routinely large, the right answer may be to treat those engagements
   as unreachable and skip them instead.
13. **The multibox fixes need three or more boxes to exercise.** Host plus a single client passes
   the arrival barrier cleanly even with the old name-collision bug, because `arrived = 1` and
   `peers = 1`. The barrier collapse only appears with two or more clients.
14. **The reconnect backoff schedule** (0/250/500/1000/2000ms) was chosen against named-pipe
   semantics, not measured with several game clients launching at once.
6. **Multibox pipe** across real clients — the protocol is straightforward but has only been
   reasoned through, not run with two live clients.
7. **Which of `AreaPlaceNameId` / `SubAreaPlaceNameId` carries the plateau.** Both are checked,
   area first, so either nesting works — but which one the game actually populates for Bozja and
   Zadnor was not observed live. If both read 0 in-game, the region shows "unknown zone" and the
   positional estimate carries the run until it can be learned.
8. **The region label anchor world positions** are an algebraic conversion of the map markers,
   not observed coordinates. They only affect pre-learning behaviour, and every objective's
   region is replaced by the learned value on first visit.
15. **`RaptureHotbarModule.ExecuteDutyActionSlot` as the duty-action press.** It is the game's own
   duty-bar button and it compiles against the pinned ClientStructs, but it appears in no plugin in
   the local corpus, so this is the first place that signature is exercised here. ClientStructs
   also documents it as *not* validating that the slot is executable and as returning `true`
   regardless — hence the plugin's own guards (framework thread, living character, slot loaded,
   charge up) and hence the fact that its return value is ignored. If a press does nothing at all
   while the bar plainly shows a charge up, that call is what to look at; the line under the bar
   reports every refusal the plugin *did* make, so a silent press means the guards passed.
16. **How long the game takes to populate a duty slot after `UseFromHolster`.** `HolsterDriver`
   loads, then presses on a later tick once the slot reports the action id it asked for, giving up
   after 2500ms — the delay itself is the thing that was never measured, only reasoned about. The
   symptom of it being wrong in either direction is visible: repeated *"duty slot 1 never came up
   as X — nothing fired"* means the timeout is short (or the load is being refused outright), and
   the press firing something other than what was ticked would mean the id check is not holding,
   which it is designed to make impossible. Also unmeasured: whether loading a slot the player is
   looking at re-triggers any client-side animation lockout.
17. **The asserted half of the Lost Action → status table.** Two parts of it are not name-matched
   and so are not self-checking. The Essence runs assume `MYCTemporaryItem` rows 41–55 / 56–70 /
   73–78 stay contiguous and in step with `Status` rows 2311–2325 / 2311–2325 / 2434–2439; a patch
   inserting a row into either sheet would shift a whole family, and the only guard is that a
   status id resolving to nothing drops that entry. The Reraise pair (`Resistance Reraiser` and
   `Lost Reraise` → status 2355) is inferred from the names rather than matched. Both fail
   visibly: the driver's line and the loadout's refusal each name the status they believe is up,
   so an Essence that reports the wrong buff — or one that never applies because something it does
   not grant reads as permanently running — points straight at the table.
18. **Plain vs Deep Essence cannot be told apart by status id.** They share `Status` rows
   2311–2325, so using a Deep Essence over a plain one of the same name is a genuine upgrade that
   the "already running" check refuses. Deliberately conservative — it declines to spend rather
   than spending twice — and stated in the editor. Whether the game distinguishes them by the
   status's `Param` (which would remove the false positive) has not been checked live.
19. **Whether the server accepts a Lost Action aimed at an object that was never the hard target.**
   `PressAt` passes the target id straight to `UseAction`. Strongly implied to work — MOAction and
   ReAction do exactly this for ordinary actions by rewriting `UseAction`'s `targetId`, and the
   action packet carries its own target — but not measured for a Lost Action specifically. If casts
   stop landing entirely, that is what failed, and the fallback is the older shape: set the hard
   target, press the slot, restore synchronously. Refusals have a voice either way, since
   `GetActionStatus` is consulted before anything is spent and its status code is printed.
   Also unmeasured: whether firing by action id deducts the duty slot's charge identically to
   pressing the slot — inferred from the two calls being identical.
20. **Whether `IPartyMember.Statuses` stays populated for an out-of-range member.** The struct read
   succeeds either way, so a zeroed list would read as *"they have no buff"* and burn a charge
   topping up someone who is fine. Members whose game object is unresolved are already excluded,
   which covers the common case but not a member who is loaded yet far away.
21. **Parsed durations come from display text.** If a balance patch changed a duration and left the
   tooltip stale, the 80% threshold would be computed against the wrong total. The failure is
   proportionate rather than dangerous — topping up slightly early or late — and the settings list
   shows each parsed duration next to its action, so a wrong one is visible.

The Dependencies tab has "Re-apply avoidance-only config" and "Restore BossMod …" buttons for
driving 4, 5, 9 and 10 by hand while checking them; the button tooltip lists exactly what is
sent for the fork that is loaded.
