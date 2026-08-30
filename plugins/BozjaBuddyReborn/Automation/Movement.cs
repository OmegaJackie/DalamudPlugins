using System;
using System.Collections.Generic;
using System.Numerics;
using BozjaBuddyReborn.External;
using ECommons.DalamudServices;

namespace BozjaBuddyReborn.Automation;

/// <summary>
/// Travel to a world position via vnavmesh, with the two guards Bozja actually needs:
/// destination snapping and stall recovery.
///
/// SNAPPING: a Critical Engagement's published map-marker centre is the middle of the arena,
/// which is frequently in mid-air or inside geometry. Pathing at the raw point strands the
/// character, so every destination is snapped onto a reachable navmesh point first.
///
/// STALLS: Zadnor is stacked, multi-level terrain and vnavmesh regularly routes onto the
/// wrong elevation and wedges. Rather than sitting there, this watches NET PROGRESS toward the
/// leg being walked - not raw displacement, which a wedged character produces plenty of - and
/// tears the path down for a fresh pathfind when the character stops closing the gap.
/// </summary>
public sealed class Movement(NavmeshIpc navmesh, Configuration config, AggroAvoidance avoidance)
{
    private readonly NavmeshIpc _navmesh = navmesh;
    private readonly Configuration _config = config;
    private readonly AggroAvoidance _avoidance = avoidance;

    private Vector3 _destination = Vector3.Zero;
    private Vector3 _snapped = Vector3.Zero;
    private long _lastProgressMs;
    private int _repathCount;

    /// <summary>
    /// The closest we have ever been to the current leg target, and the yardstick the stall
    /// detector actually measures against.
    ///
    /// THIS REPLACES RAW DISPLACEMENT, WHICH COULD NOT DETECT A STALL AT ALL. The old test asked
    /// whether the character had moved more than 0.75y since the last sample. That is satisfied
    /// by ANY motion - a dodge sidestep, being knocked back, BossMod repositioning, or the
    /// plugin's own re-path loop taking two steps at an unreachable point and stopping - none of
    /// which get us closer to anywhere. Worse, the sample anchor was only rewritten when the test
    /// passed, so a single twitch anywhere inside the timeout window reset the clock AND the
    /// repath counter. The escalation could therefore only fire for a character that was
    /// perfectly motionless for the whole timeout, which is the one wedged case that does not
    /// happen: a wedged character is usually still being walked into the wall.
    ///
    /// Net progress toward the goal is the honest question, and it is monotone: running in
    /// circles, being pushed around and re-pathing in place all leave it unchanged.
    /// </summary>
    private float _bestRemaining = float.MaxValue;

    /// <summary>
    /// The raw destination the current snap belongs to, and the point vnavmesh is ACTUALLY
    /// being driven to. These deliberately survive <see cref="Stop"/>, because arrival has to
    /// stay answerable after the path is torn down - see <see cref="HasArrived"/>.
    /// </summary>
    private Vector3 _basisRaw = Vector3.Zero;
    private Vector3 _basisSnapped = Vector3.Zero;

    /// <summary>An issued stop that vnavmesh has not finished honouring yet. See PumpStop.</summary>
    private bool _stopPending;

    /// <summary>Travel is paused for a dodge; the destination is remembered. See Suspend.</summary>
    private bool _suspended;
    private long _suspendedAtMs;

    /// <summary>
    /// Intermediate point being routed via to clear an enemy aggro cone, or zero when the route
    /// is direct. Re-evaluated on arrival, which is what lets a single-step detour chain around
    /// several enemies in sequence.
    /// </summary>
    private Vector3 _detour = Vector3.Zero;

    /// <summary>What vnavmesh was last told to walk to (detour or goal), so a leg change re-paths.</summary>
    private Vector3 _leg = Vector3.Zero;

    /// <summary>
    /// "The path underneath us was torn down; re-issue immediately" - which is NOT the same
    /// statement as "we are now going somewhere else", and conflating the two was the single
    /// worst defect in this file.
    ///
    /// Resuming from a dodge used to zero <see cref="_leg"/> to force one re-issue. But _leg is
    /// also what <c>legChanged</c> is measured against, and a Bozja world coordinate is hundreds
    /// of units from the origin, so a zeroed leg made legChanged unconditionally true - which
    /// then ran the "new intent" branch at the bottom and reset the stall clock. The resume block
    /// above takes care to ADVANCE the clock past the pause rather than reset it, with a comment
    /// saying that resetting it is what used to make wedged travel unable to report itself; the
    /// zeroed leg quietly undid that twenty lines later, in the same call. Every dodge therefore
    /// re-armed the stall detector, and since Bozja telegraphs arrive far more often than the
    /// 8s timeout, it could never fire at all.
    ///
    /// So the two meanings are now separate fields: this one only bypasses the repath throttle.
    /// </summary>
    private bool _forceReissue;

    private long _lastAvoidCheckMs;
    private long _lastPathIssueMs;

    /// <summary>How close counts as having reached a detour waypoint.</summary>
    private const float DetourArriveRange = 4f;

    /// <summary>
    /// Avoidance is re-decided at most this often.
    ///
    /// Was 1000ms, which is longer than it takes a mounted character to cross an entire
    /// proximity ring: at mount speed a second is 10-12 yalms, so the plugin could identify a
    /// blocker, decline to route around it, and be inside its aggro radius before it looked
    /// again. The scan is a filtered walk of the object table costing microseconds, so there was
    /// never a reason for it to be this coarse.
    /// </summary>
    private const long AvoidCheckIntervalMs = 250;

    /// <summary>
    /// How soon to look again after a detour was identified but REFUSED.
    ///
    /// A refusal is not a decision - the blocker is still there and still on the route - so
    /// burning the full interval on it is how the character ends up walking straight at an enemy
    /// it correctly identified a moment earlier.
    /// </summary>
    private const long AvoidRetryIntervalMs = 100;

    /// <summary>
    /// Minimum gap between two path issues for the SAME leg.
    ///
    /// This is the fix for the character standing still and twitching. vnavmesh reports
    /// "not busy" for a tick or two between finishing a pathfind and starting to follow it, and
    /// permanently when the destination cannot be reached at all - and its own
    /// PathfindAndMoveCloseTo refuses outright while a pathfind is already pending. Re-issuing on
    /// every such tick meant Path.Stop plus a fresh pathfind five times a second, so the follower
    /// was torn down before it ever took a step. A new leg is real new intent and goes out
    /// immediately; a retry of the same leg waits this out.
    /// </summary>
    private const long RepathIntervalMs = 750;

    /// <summary>
    /// How far the navmesh snap may move a detour waypoint before the detour is refused.
    ///
    /// A sidestep computed in open space frequently lands inside a building or a cliff, and the
    /// nearest-reachable query then returns whatever mesh is closest - routinely the far side of
    /// the wall it landed in. Pathing there is how the character ends up running into walls, so a
    /// snap that had to move the point this far means the sidestep was never viable.
    /// </summary>
    private const float DetourSnapTolerance = 5f;

    /// <summary>
    /// How far the navmesh snap may move a detour waypoint VERTICALLY before it is refused.
    ///
    /// The horizontal tolerance above cannot see the failure this catches: a sidestep dropped
    /// onto the Zadnor tier below, or into a ravine, sits within a few yalms horizontally and is
    /// unreachable in practice.
    /// </summary>
    private const float DetourVerticalTolerance = 3f;

    /// <summary>The enemy currently being routed around, for the UI.</summary>
    public DangerZone? AvoidingEnemy { get; private set; }

    /// <summary>
    /// Detours that were identified as needed but could not be used - off-mesh, on another tier,
    /// or not actually clear.
    ///
    /// Surfaced because a refusal used to be completely silent and indistinguishable from "no
    /// danger found": the character walks straight through the enemy it just correctly
    /// identified, and every reading available to the user says avoidance is working.
    /// </summary>
    public int RefusedDetours { get; private set; }

    /// <summary>The snapped destination currently being travelled to.</summary>
    public Vector3 Destination => _snapped;

    /// <summary>
    /// How far the navmesh snap had to move the destination being travelled to.
    ///
    /// This is the number that decides whether "arrived" means what the caller thinks: the
    /// character comes to rest around the SNAPPED point, so a large drift means arriving at the
    /// closest reachable spot rather than at the marker itself.
    /// </summary>
    public float SnapDrift => _basisSnapped == Vector3.Zero
        ? 0f
        : HorizontalDistance(_basisRaw, _basisSnapped);

    /// <summary>Path requests vnavmesh refused because it was already computing one.</summary>
    public int RejectedIssues { get; private set; }

    /// <summary>True while detouring around an enemy rather than heading straight at the goal.</summary>
    public bool IsDetouring => _detour != Vector3.Zero;

    /// <summary>How many times the current travel has had to recover from a stall.</summary>
    public int RepathCount => _repathCount;

    /// <summary>
    /// Seconds since the character last got CLOSER to where it is going, while a path was
    /// supposed to be running. Climbing past the stall timeout means vnavmesh cannot get us
    /// there from here. See <see cref="_bestRemaining"/> for why this is net progress rather
    /// than displacement.
    /// </summary>
    public float SecondsWithoutProgress =>
        _destination == Vector3.Zero ? 0f : (Environment.TickCount64 - _lastProgressMs) / 1000f;

    /// <summary>
    /// True when repeated re-snaps and re-paths have all failed to move the character. Surfaced
    /// so a genuinely unreachable objective reads as that rather than as a silent hang.
    /// </summary>
    public bool Stuck => _repathCount >= 3 && SecondsWithoutProgress > _config.StallTimeoutSeconds;

    public bool NavmeshAvailable => _navmesh.Available;
    public bool MeshReady => _navmesh.MeshReady;
    public bool Busy => _navmesh.Busy;

    /// <summary>Distance from the local player to a world position, ignoring height.</summary>
    public static float HorizontalDistance(Vector3 a, Vector3 b)
    {
        var dx = a.X - b.X;
        var dz = a.Z - b.Z;
        return MathF.Sqrt(dx * dx + dz * dz);
    }

    /// <summary>Horizontal distance from the local player to a world position, or float.MaxValue.</summary>
    public static float DistanceToPlayer(Vector3 world)
    {
        var me = Svc.Objects.LocalPlayer;
        return me == null ? float.MaxValue : HorizontalDistance(me.Position, world);
    }

    /// <summary>
    /// Begin (or continue) travelling to a destination. Safe to call every tick with the same
    /// destination - it only issues a new path when the destination changes or a stall is
    /// detected.
    /// </summary>
    /// <returns>
    /// False when vnavmesh cannot move us right now - absent, mesh still building, or it refused
    /// the request outright. The refusal case used to return true, which made the caller's
    /// "could not start a path" diagnostic unreachable and a plugin that could not path
    /// indistinguishable from one travelling normally.
    /// </returns>
    public bool TravelTo(Vector3 destination, float range)
    {
        if (!_navmesh.Available || !_navmesh.MeshReady)
            return false;

        var me = Svc.Objects.LocalPlayer;
        if (me == null)
            return false;

        var now = Environment.TickCount64;
        var resumed = false;

        // Coming back from a dodge. The pause is not a stall and not a repath, so the clocks are
        // advanced past it rather than reset - resetting them is what used to pin RepathCount at
        // zero forever, so genuinely wedged travel could never report itself as stuck.
        if (_suspended)
        {
            var paused = now - _suspendedAtMs;
            _lastProgressMs += paused;
            _lastPathIssueMs += paused;
            _suspended = false;
            resumed = true;

            // The path really was torn down, so one re-issue is required - but that is ALL this
            // means. It must not travel through _leg, because _leg is the yardstick legChanged
            // is measured against and a zeroed leg reads as brand-new intent, which resets the
            // very clock the two lines above just took care to advance. See _forceReissue.
            _forceReissue = true;
        }

        // New destination: snap it and drop any detour from the previous goal.
        //
        // Compared by DISTANCE, not by exact inequality. The controller re-reads the objective's
        // live position every tick, and a FATE ring or a republished engagement marker that
        // shifts by a millimetre would otherwise count as a brand-new destination five times a
        // second - re-snapping, dropping the detour and restarting the stall clock every time, so
        // no path ever survives long enough to be followed.
        if (_snapped == Vector3.Zero || HorizontalDistance(destination, _destination) > 1f)
        {
            _destination = destination;
            _snapped = _navmesh.SnapToMesh(destination);

            // Remember what this snap was FOR. HasArrived reads these, and they must outlive
            // Stop() - the controller stops the path the moment it declares arrival, and if the
            // basis died with it the next tick would fall back to measuring against the raw
            // marker, flip "arrived" back to false and restart travel, forever.
            _basisRaw = destination;
            _basisSnapped = _snapped;

            ClearDetour();
            _leg = Vector3.Zero;
            _repathCount = 0;
            _bestRemaining = float.MaxValue;
            _lastProgressMs = now;

            // New intent goes out on this tick, not after the throttle: the previous
            // destination's issue time must not delay the first path to this one.
            _lastPathIssueMs = 0;
        }

        // Arrived at the real goal?
        var remaining = HorizontalDistance(me.Position, _snapped);
        if (remaining <= range)
        {
            ClearDetour();
            return true;
        }

        // Reached the detour waypoint: drop it so the route is re-evaluated from here. This is
        // what lets one-step detours chain around several enemies in sequence.
        if (IsDetouring && HorizontalDistance(me.Position, _detour) <= DetourArriveRange)
            ClearDetour();

        EvaluateAvoidance(me.Position, now);

        // Long haul still ahead: get mounted. Bozja and Zadnor have no aetherytes, so this is
        // the only fast travel there is.
        Mount.EnsureMounted(_config, remaining);

        var legTarget = IsDetouring ? _detour : _snapped;
        var legRange = IsDetouring ? DetourArriveRange : range;

        // Track progress TOWARD THE LEG, not raw displacement - see _bestRemaining. Getting
        // closer than we have ever been clears the escalation, because we are plainly not stuck;
        // moving without closing the gap is exactly what being stuck looks like.
        const float ProgressEpsilon = 0.75f;
        var legRemaining = HorizontalDistance(me.Position, legTarget);
        if (legRemaining < _bestRemaining - ProgressEpsilon)
        {
            _bestRemaining = legRemaining;
            _lastProgressMs = now;
            _repathCount = 0;
        }

        var stalled = now - _lastProgressMs > (long)(_config.StallTimeoutSeconds * 1000);

        // Not moving and not computing: the path finished short, or never started.
        //
        // Asks whether OUR path is running, not whether ANY path is. The global Busy flag is
        // satisfied by the combat approach's path, or by another plugin's entirely, which would
        // make travel sit and wait on movement that is not taking it anywhere.
        var idle = !_navmesh.OwnedBy(NavClient.Travel);

        // The leg changed (a detour was raised or cleared), so the old path is now wrong. This
        // is genuinely NEW INTENT, and it is the only thing that may reset the stall clock below.
        var legChanged = _leg != Vector3.Zero && HorizontalDistance(legTarget, _leg) > 1f;

        // First issue for this destination is new intent too, but there is no previous leg to
        // compare against - _leg is zero, and comparing a world coordinate to the origin would
        // make every re-issue look like a leg change (which is precisely the bug _forceReissue
        // exists to undo).
        var firstIssue = _leg == Vector3.Zero;

        if (!legChanged && !firstIssue && !_forceReissue && !stalled && !idle)
            return true;

        // Same leg, issued a moment ago: let vnavmesh finish computing and actually start
        // walking. See RepathIntervalMs - hammering here is what wedged the character.
        //
        // THE TWO GATES ASK DIFFERENT QUESTIONS. The one above decides whether there is any
        // reason to act at all; this one decides how OFTEN. So the exemptions differ: a genuine
        // leg change and a dodge resume go out immediately (the first is new intent, the second
        // has nothing left running to protect), but a stall recovery and a retry after a refusal
        // are throttled - they are repeats of a request that has already failed once, and
        // letting them through every tick is a 5Hz loop that achieves nothing. A genuine first
        // issue is immediate because the destination change zeroed _lastPathIssueMs.
        if (!legChanged && !resumed && now - _lastPathIssueMs < RepathIntervalMs)
            return true;

        // NOTHING BELOW MAY RUN WHILE A PATHFIND IS IN FLIGHT. vnavmesh refuses any request that
        // lands on a pending one (AsyncMoveRequest.MoveTo returns false and logs an error), and
        // it cannot be cancelled - it will hand its result to the follower whenever it finishes.
        // Entering the block anyway would issue a Path.Stop that tears down whatever leg is
        // currently walking, then have the replacement request thrown away: the character keeps
        // moving, but toward the destination we were trying to abandon.
        if (_navmesh.PathfindInProgress)
            return true;

        // Only tear down a path that is OURS. Path.Stop is process-global and carries no request
        // identity, so an unconditional stop here reaches into the combat approach's path - and
        // into any other plugin's. That mattered because a refused issue used to leave this
        // block reachable on every single tick, which turned one refusal into a 5Hz global stop
        // storm that nothing else on the machine could path through.
        if (_navmesh.OwnedBy(NavClient.Travel))
            _navmesh.Stop(NavClient.Travel);

        if (stalled)
        {
            if (IsDetouring)
            {
                // A wedged DETOUR is the detour's own fault, and re-pathing to the same sidestep
                // just wedges again. Drop it, stop considering that enemy for a while so it is
                // not immediately re-raised, and go direct - walking past a mob beats standing in
                // a wall. (Previously a stall while detouring re-snapped nothing and retried the
                // same point forever.)
                if (AvoidingEnemy is { } wedged)
                    _avoidance.Suppress(wedged.ObjectId);

                ClearDetour();
                legTarget = _snapped;
                legRange = range;
            }
            else
            {
                // Re-snap, widening the search each time it fails: the first snap may have picked
                // an unreachable ledge, and Zadnor's stacked terrain routinely needs a taller
                // query than the default to find the level the character is actually on.
                //
                // WIDER IS NOT FREE. The query returns the nearest mesh within the extent, so an
                // 80y box can hand back a point on a different Zadnor tier or across a ravine -
                // and _basisSnapped is what HasArrived measures against, so accepting it silently
                // redefines "arrived" to mean somewhere else entirely. Twenty lines below, a
                // detour snap that moved more than 5y is refused for exactly this reason. Accept
                // the widened result only while it still describes the place we were sent to.
                var extent = 20f + 20f * MathF.Min(_repathCount, 3);
                var resnapped = _navmesh.SnapToMesh(_destination, extent, extent);

                if (HorizontalDistance(resnapped, _destination) <= extent)
                {
                    _snapped = resnapped;
                    _basisSnapped = _snapped; // arrival is measured against where we are now driven
                }

                legTarget = _snapped;
            }

            // THE STALL CLOCK IS DELIBERATELY NOT RESET HERE, and resetting it was the same
            // mistake as the dodge-resume one. A recovery ATTEMPT is not progress: leaving the
            // clock running is what lets SecondsWithoutProgress keep climbing while the
            // escalation widens, so `Stuck` (which needs the counter AND the clock together) can
            // actually latch and be read by the caller. Resetting it capped the clock at one
            // stall timeout, so the two halves of Stuck were never true at the same moment and
            // the caller's report was unreachable no matter how many recoveries had failed.
            //
            // _leg is deliberately not committed here either - the issue site below is the only
            // commit point, so a refused request cannot leave us believing we walk somewhere we
            // do not.
            //
            // Keeping the best-so-far means a re-snap that does not actually help still counts
            // toward the next escalation instead of quietly resetting it.
            _bestRemaining = MathF.Min(_bestRemaining, HorizontalDistance(me.Position, legTarget));
        }

        // The flight flag must track the ACTUAL airborne state. Telling vnavmesh to fly a
        // grounded character hands it a path it cannot follow, which is itself a stall.
        //
        // THE ISSUE SITE IS THE COMMIT POINT. Everything that records "we are now walking to
        // legTarget" happens below this call and only when it succeeded. Committing first - which
        // is what the old code did - meant a refused request still overwrote _leg, so legChanged
        // went false, the throttle engaged, and the stale path walked the character to the
        // destination we had just abandoned with every recovery condition suppressed.
        if (!_navmesh.MoveCloseTo(legTarget, legRange, Mount.ShouldFly(_config.AllowFlight), NavClient.Travel))
        {
            // A REFUSAL IS A FAILURE, and it used to be recorded as the opposite. Zeroing _leg
            // made legChanged read as new intent forever after, which permanently disabled the
            // repath throttle, permanently skipped the _repathCount++ below, and left the block
            // above reachable every tick - so a plugin that could not path presented as one
            // travelling normally, at 5Hz, with no escalation and nothing in the status line.
            RejectedIssues++;
            _repathCount++;

            // Retry, but through the throttle - a request vnavmesh just refused will be refused
            // again on the next frame, and the old code's unthrottled retry is what turned one
            // refusal into a permanent 5Hz loop. _leg stays intact so the stall clock survives
            // and the escalation above can still widen.
            _forceReissue = true;
            _lastPathIssueMs = now;
            return false;
        }

        // A fresh path supersedes any stop we were still chasing; leaving the latch armed would
        // have the next frame's pump immediately tear this path down again.
        _stopPending = false;
        _forceReissue = false;

        _lastPathIssueMs = now;

        if (legChanged || firstIssue)
        {
            // New intent rather than a failure: the previous leg's history must not make a fresh
            // one look stalled the moment it starts, and the best-so-far belonged to the OLD leg.
            _lastProgressMs = now;
            _bestRemaining = HorizontalDistance(me.Position, legTarget);
        }
        else if (!resumed)
        {
            // Re-issuing the same leg because it stopped making progress IS a failure repath.
            // Re-issuing it because a dodge tore the path down is not - the escalation must not
            // be driven by how many mechanics happened to go off en route. (The stall CLOCK is
            // deliberately left alone in both cases; only new intent may reset that.)
            _repathCount++;
        }

        _leg = legTarget;
        return true;
    }

    /// <summary>
    /// Decide whether the straight line to the goal walks into an enemy's aggro footprint and,
    /// if so, raise a detour around it.
    ///
    /// Throttled rather than run every tick: it walks the object table and samples the route, and
    /// re-deciding five times a second would also make the character waver between two routes.
    /// </summary>
    private void EvaluateAvoidance(Vector3 from, long now)
    {
        if (_avoidance.Disabled)
        {
            ClearDetour();
            return;
        }

        if (now - _lastAvoidCheckMs < AvoidCheckIntervalMs)
            return;
        _lastAvoidCheckMs = now;

        var zones = _avoidance.Scan();

        // A DETOUR LEG IS STILL A ROUTE, and nothing used to check it. The old guard returned
        // here the moment a detour existed, so from the instant one was raised until the
        // character reached it - which for a blocker far down the route can be a hundred yalms
        // and ten-plus seconds - the enemy picture was a frozen snapshot. Field mobs patrol and
        // turn, and rotation IS the cone, so a sidestep that was clear when it was chosen is
        // routinely not clear by the time it is walked. Re-check it against live zones and drop
        // it the moment it stops being safe, rather than committing blind.
        if (IsDetouring)
        {
            if (zones.Count == 0)
                return;

            if (_avoidance.FirstBlocking(from, _detour, zones, 0f) != null || Occupied(_detour, zones))
                ClearDetour();

            return;
        }

        if (zones.Count == 0)
            return;

        var blocking = _avoidance.FirstBlocking(from, _snapped, zones, _config.DangerIgnoreNearObjective);
        if (blocking is not { } enemy)
            return;

        // A rejection below is NOT a decision - the blocker is still there and still on the route
        // - so look again shortly rather than burning the whole interval on it. Every rejection
        // path used to cost the full interval, during which a mounted character closes far enough
        // to be inside the footprint it had just correctly identified, with nothing said in the
        // status line and no fallback attempted. The state after a rejection was byte-identical
        // to the state after "no danger found".
        _lastAvoidCheckMs = now - AvoidCheckIntervalMs + AvoidRetryIntervalMs;
        RefusedDetours++;

        var wanted = _avoidance.ComputeDetour(from, _snapped, enemy);
        var candidate = _navmesh.SnapToMesh(wanted);

        // The sidestep is computed in open space, so it lands inside a building or a cliff face
        // often enough that this guard matters more than the rest of the avoidance put together.
        // A snap that had to drag the point a long way did not "fix" it - it returned the nearest
        // unrelated mesh, routinely on the far side of the wall the point landed in. Pathing
        // there is exactly the "runs into a wall" failure, so refuse and travel direct instead.
        if (HorizontalDistance(candidate, wanted) > DetourSnapTolerance)
            return;

        // HORIZONTAL TOLERANCE IS NOT ENOUGH ON STACKED TERRAIN. ComputeDetour has no altitude to
        // work with so it copies ours, and SnapToMesh then drops that point onto whatever floor
        // is nearest - which in Zadnor is frequently the tier below, or the bottom of a ravine.
        // Such a point passes the horizontal test comfortably and is unreachable in practice,
        // which is the other half of the "runs into a wall" failure.
        if (MathF.Abs(candidate.Y - wanted.Y) > DetourVerticalTolerance)
            return;

        // And it has to be standable, reachable mesh in its own right - snapping always returns
        // something, so this is the question that can actually say no.
        if (!_navmesh.IsPointOnMesh(candidate))
            return;

        // Only accept a detour that is itself clear. Snapping to the navmesh can drag the point
        // back into the cone it was meant to dodge, and committing to that would just walk into
        // the enemy by a longer route.
        //
        // NO EXEMPTION HERE, and that is the whole point of passing 0. The primary call above
        // passes DangerIgnoreNearObjective so the objective's own mobs cannot make it
        // unreachable - but that argument is measured against the far END of the line, and here
        // the far end is the DETOUR, not the objective. Passing it excused every enemy within
        // 25y of the exact spot we were about to commit to walking to: a bubble wider than the
        // sight radius the detour exists to stay out of. Nothing near a sidestep deserves an
        // exemption.
        if (_avoidance.FirstBlocking(from, candidate, zones, 0f) != null)
            return;

        // Standing ON the waypoint has to be safe too, not just the walk to it. FirstBlocking
        // samples the segment every 3y, so a footprint the character ends up inside without the
        // sampled points landing in it is otherwise missed - which is exactly the near-tangent
        // geometry that occurs at the aggro threshold.
        if (Occupied(candidate, zones))
            return;

        RefusedDetours--; // provisionally counted above; this one was accepted
        _detour = candidate;
        AvoidingEnemy = enemy;
    }

    /// <summary>Would standing at this point put us inside any live enemy footprint?</summary>
    private static bool Occupied(Vector3 point, IReadOnlyList<DangerZone> zones)
    {
        for (var i = 0; i < zones.Count; i++)
            if (zones[i].Contains(point))
                return true;

        return false;
    }

    private void ClearDetour()
    {
        _detour = Vector3.Zero;
        AvoidingEnemy = null;
    }

    /// <summary>
    /// True once the character is within <paramref name="range"/> of the point we are ACTUALLY
    /// being driven to.
    ///
    /// THIS WAS THE BIGGEST PATHING DEFECT IN 1.0.x, and it made the runner stand still
    /// indefinitely with a status line that said it was travelling.
    ///
    /// vnavmesh comes to rest on a shell of radius `range` around the point IT was given - the
    /// SNAPPED point - because the exact requested destination is appended as the final waypoint
    /// and the path is cleared once the character is within tolerance of it. The controller,
    /// meanwhile, was asking whether we were within `range` of the RAW marker. Let d be the
    /// horizontal distance the snap moved the point: the character stops up to `range + d` from
    /// the raw marker, so any sideways snap at all can make the controller's test unsatisfiable
    /// - and it is systematically unsatisfiable in exactly the case snapping exists for, where
    /// the marker is inside geometry and the reachable point is on the near side of it.
    ///
    /// The result was a silent deadlock: TravelTo returned "arrived" and stopped issuing paths
    /// (its own early return measures against _snapped), while the controller saw "not arrived"
    /// and called TravelTo again every tick. Nothing escalated, because the repath counter, the
    /// stall clock and the widening re-snap all live BELOW that early return - so RepathCount
    /// stayed 0, Stuck could never latch, and no warning was ever printed.
    ///
    /// Measuring against the basis makes the two agree by construction. It falls back to the raw
    /// destination when asked about somewhere we are not currently travelling to.
    /// </summary>
    public bool HasArrived(Vector3 destination, float range)
    {
        var me = Svc.Objects.LocalPlayer;
        if (me == null)
            return false;

        var target = _basisSnapped != Vector3.Zero && HorizontalDistance(destination, _basisRaw) <= 1f
            ? _basisSnapped
            : destination;

        return HorizontalDistance(me.Position, target) <= range;
    }

    /// <summary>
    /// Stop moving and forget the current destination.
    ///
    /// A single stop is a REQUEST, not a guarantee: vnavmesh's Path.Stop clears the waypoint
    /// list but cannot cancel a pathfind that is still computing, and that pathfind hands its
    /// result straight to the follower when it lands - silently undoing the stop and walking the
    /// character off again. So the stop is latched and re-issued by <see cref="PumpStop"/> until
    /// vnavmesh reports it is neither computing nor following.
    ///
    /// The old early-return here ("skip if we have no destination") made every stop after the
    /// first a no-op, which meant the plugin had no second brake at exactly the moment it needed
    /// one. Its real purpose - not spamming the gate while standing still - is now served by the
    /// latch clearing itself against vnavmesh's ACTUAL state instead of our own zeroed fields.
    ///
    /// The arrival basis deliberately survives this; see <see cref="HasArrived"/>.
    /// </summary>
    public void Stop()
    {
        // Cheap when there is genuinely nothing to stop - the controller calls this every tick
        // while holding at an objective. This is the ORIGINAL early-return's purpose, kept, but
        // it now tests whether we ever had a path rather than testing fields we are about to
        // zero: the old form made the second and every later Stop a no-op, which is exactly when
        // a second stop was needed.
        var hadPath = _snapped != Vector3.Zero || _leg != Vector3.Zero || _stopPending;

        _destination = Vector3.Zero;
        _snapped = Vector3.Zero;
        _leg = Vector3.Zero;
        _repathCount = 0;
        _bestRemaining = float.MaxValue;
        _suspended = false;
        _forceReissue = false;
        ClearDetour();

        if (!hadPath)
            return;

        _stopPending = true;
        PumpStop();
    }

    /// <summary>
    /// Hand vnavmesh back WITHOUT forgetting where we were going - what a dodge needs.
    ///
    /// Both BossMod forks refuse to steer while vnavmesh's shared "vnav.PathIsRunning" flag is
    /// set, so the path genuinely has to stop for a dodge to happen at all. But a dodge is a
    /// pause, not a cancellation: using Stop() for it threw away the snap, zeroed the leg and
    /// reset the stall clock and repath counter, so every mechanic silently defeated the repath
    /// throttle and pinned RepathCount at 0.
    /// </summary>
    public void Suspend()
    {
        if (!_suspended)
        {
            _suspended = true;
            _suspendedAtMs = Environment.TickCount64;
        }

        _stopPending = true;
        PumpStop();
    }

    /// <summary>
    /// Drive an outstanding stop to completion. Cheap no-op once vnavmesh is idle.
    ///
    /// Called every frame from the plugin's update rather than from the controller tick, because
    /// the stops that matter most - stopping the run, leaving the zone, unloading the plugin -
    /// are the ones where the controller is no longer ticking at all.
    /// </summary>
    public void PumpStop()
    {
        if (!_stopPending)
            return;

        // The approach has legitimately taken the path since our stop was issued. Tearing that
        // down would strand the melee close-in, and it would fight the approach's own pump
        // forever - each would keep stopping the other's path. Ours is finished with either way.
        if (_navmesh.Owner == NavClient.Approach)
        {
            _stopPending = false;
            return;
        }

        _navmesh.Stop(NavClient.Travel);

        if (!_navmesh.PathfindInProgress && !_navmesh.PathRunning)
            _stopPending = false;
    }
}
