using System;
using System.Collections.Generic;
using Dalamud.Configuration;

namespace BozjaBuddyReborn;

/// <summary>How the runner answers something that aggroes onto it mid-route.</summary>
public enum TravelAggroResponse : byte
{
    /// <summary>
    /// Keep running to the objective. The rotation is never armed en route, so nothing gets
    /// attacked - field mobs leash and drop off once they are outrun.
    /// </summary>
    KeepRunning = 0,

    /// <summary>Stop, clear whatever is on us, then resume travel.</summary>
    FightBack = 1,
}

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    // --- combat roles -------------------------------------------------------
    // The split is the design: BossMod dodges, RSR presses buttons. Both default on.

    /// <summary>Let BossMod steer the character out of telegraphed AoEs.</summary>
    public bool UseBossModAvoidance = true;

    /// <summary>Let Rotation Solver Reborn run the rotation.</summary>
    public bool UseRotationSolver = true;

    /// <summary>
    /// Re-apply BossMod's avoidance-only configuration every time combat is engaged rather
    /// than once per session. Slower, but survives the user changing BossMod settings mid-run.
    /// </summary>
    public bool ReapplyAvoidanceConfigEachFight;

    /// <summary>
    /// Re-send the on/off state to BossMod Reborn and RSR this often, in seconds, even when it
    /// has not changed. 0 turns the heartbeat off and leaves pure edge-triggering.
    ///
    /// Neither plugin can be asked what state it is actually in, and both drop it on their own -
    /// Reborn idles its AI when the party slot it follows goes invalid, and RSR has no getter at
    /// all - so without this the runner can travel and fight with nothing armed and no way to
    /// find out.
    /// </summary>
    public float CombatStateReassertSeconds = 5f;

    /// <summary>
    /// Log every UI callback the GAME fires, to /xllog, at Debug level.
    ///
    /// THIS EXISTS TO SETTLE ONE SPECIFIC QUESTION. Nothing anywhere - not this machine's
    /// FFXIVClientStructs, not several hundred cloned plugins - drives the Resistance Recruitment
    /// window, so how a Register or a Commence press is expressed on the wire has never been
    /// written down. With this on, clicking those buttons BY HAND prints the addon's real runtime
    /// name and the exact argument list the game sent, which is the whole of what is missing.
    ///
    /// Off by default and deliberately noisy: it hooks every addon callback in the client, not
    /// just ours.
    /// </summary>
    public bool LogUiCallbacks;

    /// <summary>
    /// Walk into range of the current target during a fight and stay there.
    ///
    /// BossMod Reborn will not do this in the avoidance-only configuration this plugin puts it
    /// in, so without it a melee job stands where travel left it and the rotation falls back to
    /// its ranged filler. See <see cref="Automation.CombatApproach"/> for the proof.
    /// </summary>
    public bool CloseToTarget = true;

    // --- engagement selection ----------------------------------------------

    /// <summary>Join Critical Engagements as they open for registration.</summary>
    public bool DoCriticalEngagements = true;

    /// <summary>Farm skirmish FATEs while no Critical Engagement is available.</summary>
    public bool DoFates = true;

    /// <summary>
    /// What to do about enemies that aggro onto the character WHILE TRAVELLING.
    ///
    /// This governs the route only. Standing at the objective is not "en route" - there is
    /// nowhere further to run, and a Critical Engagement's registration window has to be waited
    /// out where we stand - so attackers are always answered once we have arrived.
    /// </summary>
    public TravelAggroResponse AggroResponse = TravelAggroResponse.KeepRunning;

    /// <summary>
    /// Once committed to an objective, stay on it while it is still worth doing rather than
    /// re-ranking every tick. Re-picking each tick makes the character abandon a fight the
    /// instant another objective ranks higher.
    /// </summary>
    public bool StickyObjective = true;

    /// <summary>
    /// Enter the 1v1 duels (Aces High, Beast of Man, ...). Off by default: only one player is
    /// chosen, entry costs notoriety, and losing wastes the window.
    /// </summary>
    public bool EngageDuels;

    /// <summary>
    /// Enter the large-scale engagements (Castrum Lacus Litore, The Dalriada). Off by default -
    /// they are long, scheduled, and usually run as an organised group.
    /// </summary>
    public bool EngageLargeScale;

    /// <summary>
    /// Do not try to register with less than this many seconds left in the registration
    /// window. The game refuses registration under 10 seconds, so the default leaves margin
    /// for the travel time to actually land.
    /// </summary>
    public int MinRegisterSecondsLeft = 15;

    /// <summary>DynamicEvent row ids the user never wants to be sent to.</summary>
    public HashSet<uint> BlockedEngagements = [];

    /// <summary>Preferred order; engagements earlier in this list win ties.</summary>
    public List<uint> PriorityEngagements = [];

    // --- zone-targeted farming ---------------------------------------------
    // Relic materials are region-specific, and in Zadnor skirmishes and Critical Engagements
    // within the same plateau drop different items. Setting a farm target restricts selection
    // to objectives that actually drop it.

    /// <summary>Item id of the relic material to farm, or 0 for "anything".</summary>
    public uint FarmMaterialItemId;

    /// <summary>
    /// Restrict work to one third of the zone: 0 = anywhere, 1/2/3 = Z1/Z2/Z3. Ignored while a
    /// farm material is selected, since that already pins the region (and the activity).
    /// </summary>
    public byte PreferredRegion;

    /// <summary>
    /// Skip objectives whose region is not yet known. Off by default: visiting an unknown
    /// objective is how its region gets learned, and a first run would otherwise sit idle.
    /// </summary>
    public bool SkipUnknownRegions;

    /// <summary>
    /// Learned objective-to-region map, keyed "territory:kind:id" (kind c = Critical
    /// Engagement, f = skirmish FATE). Populated by observing TerritoryInfo while standing at
    /// an objective, so it is exact once learned and survives restarts.
    /// </summary>
    public Dictionary<string, byte> LearnedRegions = [];

    // --- idle staging -------------------------------------------------------

    /// <summary>
    /// Travel to a staging point and hold there when the working zone has nothing up, instead
    /// of stopping wherever the last objective happened to leave the character.
    /// </summary>
    public bool UseIdleSpot = true;

    /// <summary>
    /// Staging point per region, keyed "territory:region", value [mapX, mapY] in the
    /// two-decimal map coordinates the game shows.
    ///
    /// Seeded with the Zadnor Northern Plateau (Z3) spot at (16.1, 14.7), which resolves to
    /// world (-268.7, -338.7) - comfortably inside Z3.
    /// </summary>
    public Dictionary<string, float[]> IdleSpots = new()
    {
        ["975:3"] = [16.1f, 14.7f],
    };

    /// <summary>How close to the staging point counts as "waiting there".</summary>
    public float IdleArriveRange = 6f;

    // --- movement -----------------------------------------------------------

    /// <summary>Allow vnavmesh to fly. Bozja and Zadnor both permit flight.</summary>
    public bool AllowFlight = true;

    /// <summary>
    /// Summon a mount for long hauls. Neither field zone has an aetheryte, so mount travel is
    /// the only fast travel available - with this off, the character jogs the whole map.
    /// </summary>
    public bool UseMount = true;

    /// <summary>How close to the engagement centre to stand before considering ourselves there.</summary>
    public float ArriveRange = 12f;

    /// <summary>
    /// Seconds of no measurable movement while a path is supposedly running before the path is
    /// torn down and recomputed. Zadnor's stacked terrain makes this a routine occurrence.
    /// </summary>
    public float StallTimeoutSeconds = 8f;

    // --- enemy aggro avoidance ---------------------------------------------
    // Running between objectives drags the route through field mobs, and the heavier ones in
    // Bozja and Zadnor will delete a character that gets pulled into a pack. Combat avoidance
    // does not help here - by the time anything is telegraphed you are already tagged - so this
    // happens at the pathing layer: route around the cone rather than walk into it.

    /// <summary>Route around enemy aggro footprints while travelling.</summary>
    public bool AvoidDangerousEnemies = true;

    /// <summary>
    /// Only avoid enemies at or above this level. 0 avoids every hostile field enemy, which is
    /// the safe default - you generally want to aggro nothing at all while travelling. Raise it
    /// if you only care about the heavy hitters; the main window lists nearby enemies with their
    /// actual levels so this can be set from what the game really reports.
    /// </summary>
    public byte DangerousEnemyMinLevel;

    /// <summary>How far an enemy notices you within its facing cone.</summary>
    public float DangerSightRadius = 22f;

    /// <summary>Full width of the sight cone in degrees.</summary>
    public float DangerConeDegrees = 120f;

    /// <summary>
    /// Radius at which an enemy aggroes from ANY direction, including directly behind. This is
    /// what makes passing behind something safe only up to a point.
    /// </summary>
    public float DangerProximityRadius = 10f;

    /// <summary>Extra margin added when routing around a danger zone.</summary>
    public float DangerClearance = 6f;

    /// <summary>
    /// Enemies within this distance of the destination are ignored - they are almost certainly
    /// the objective's own mobs, and routing around those would mean never arriving.
    ///
    /// MUST STAY BELOW <see cref="DangerSightRadius"/>. At the old default of 25 it exceeded both
    /// the sight radius (22) and the proximity ring (10), so the exemption was a bubble larger
    /// than the footprint it exists to overlook - the last 25y of every approach was unguarded,
    /// including approaches to open-field staging points that have no objective mobs at all.
    /// </summary>
    public float DangerIgnoreNearObjective = 10f;

    // --- lost actions -------------------------------------------------------

    /// <summary>Use configured Lost Actions from the holster during engagements.</summary>
    public bool AutoUseLostActions;

    /// <summary>
    /// Also PRESS the duty slot after loading an action-type Lost Action into it.
    ///
    /// Separate from <see cref="AutoUseLostActions"/>, and default off, because the two cost
    /// different things. UseFromHolster consumes item-type entries (the Essences and kits) on its
    /// own, but for an action-type entry it only loads a duty slot - so until 1.0.21.0 the switch
    /// above spent no action-type charge at all, no matter how long it was left on. Completing it
    /// silently would have changed the price of a box already ticked. This is that decision, taken
    /// on its own terms; with it off, action-type entries are skipped rather than loaded, which is
    /// exactly what the switch above already effectively did, minus the pointless slot churn.
    /// </summary>
    public bool AutoFireLostActions;

    /// <summary>MYCTemporaryItem row ids to auto-use, in priority order.</summary>
    public List<byte> AutoLostActions = [];

    /// <summary>Minimum gap between two auto-fired Lost Actions.</summary>
    public int LostActionCooldownMs = 8000;

    /// <summary>
    /// Clicking a slot on the duty-action hotbar fires it.
    ///
    /// On by default - a hotbar you cannot press is a display, and the window exists to be one
    /// you can press. The switch is here because a Lost Action is farmed time, and a window you
    /// keep open beside a rotation is a window you will eventually click by accident; turning
    /// this off puts the hotbar back to read-only without closing it.
    /// </summary>
    public bool DutyActionClickToUse = true;

    /// <summary>
    /// Draw the duty-action window with no background, so it reads as a bar over the game rather
    /// than a panel in front of it.
    ///
    /// On by default. The window exists to be glanced at mid-fight beside the real hotbars, and a
    /// solid ImGui panel is the one thing that makes it look like a separate application. The
    /// title bar deliberately stays - it is the only thing left to drag the window by, and a bar
    /// you cannot move is worse than one with a strip of chrome on it.
    /// </summary>
    public bool DutyActionTransparent = true;

    // --- party support ------------------------------------------------------
    // A stoppable task that keeps the party's Lost Action buffs up and heals whoever is worst off.
    // Everything here spends farmed charges on OTHER people, so nothing runs without being started.

    /// <summary>MYCTemporaryItem row ids the party-support task maintains, in priority order.</summary>
    public List<byte> PartySupportActions = [];

    /// <summary>
    /// Re-apply a buff once the remaining time falls below this fraction of its total.
    ///
    /// 0.20 by default, which is the request stated as a number: Lost Bravery runs 600s, so it is
    /// topped up with under two minutes left. The total comes from the game's own tooltip text via
    /// LostActionDurations; an action whose duration is not in the data is never topped up at all,
    /// only applied to people who have nothing.
    /// </summary>
    public float PartyBuffRefreshFraction = 0.20f;

    /// <summary>
    /// Only heal a party member below this fraction of maximum HP.
    ///
    /// Without a floor, "target the lowest HP member" means firing a Lost Cure at whoever is at 99%
    /// - there is always a lowest. This is what makes "nothing left to do" a real state.
    /// </summary>
    public float PartyHealBelowFraction = 0.65f;

    /// <summary>Minimum gap between two party-support casts.</summary>
    public int PartySupportGapMs = 1500;

    /// <summary>
    /// Which duty slot the party-support task loads into: 0 or 1.
    ///
    /// Defaults to the SECOND slot, because the auto-use driver defaults to the first and two
    /// drivers reloading one slot underneath each other would spend the pair of them fighting.
    /// An action already sitting in either slot is used where it is, so this only matters when
    /// something has to be loaded.
    /// </summary>
    public int PartySupportSlot = 1;

    // --- multibox -----------------------------------------------------------

    /// <summary>Coordinate with other game clients on this machine over a named pipe.</summary>
    public bool MultiboxEnabled;

    /// <summary>
    /// This client picks the objective and broadcasts it. Exactly one box in the group must be
    /// the host; the rest follow.
    /// </summary>
    public bool MultiboxIsHost;

    /// <summary>
    /// Hold the group at the objective until every box has arrived before registering. Keeps
    /// the group together at the cost of moving at the speed of the slowest box.
    /// </summary>
    public bool MultiboxArrivalBarrier = true;

    /// <summary>Seconds the host will wait for stragglers before releasing the group anyway.</summary>
    public int MultiboxBarrierTimeoutSeconds = 45;

    /// <summary>
    /// Named Lost Action loadouts the operator can push to any box.
    ///
    /// Stored on the OPERATOR's box only. A loadout is an instruction, not shared state, so a
    /// client never needs its own copy - it is told which two actions to load and does that.
    /// </summary>
    public List<Automation.Loadout> Loadouts = [];

    // --- relic --------------------------------------------------------------

    /// <summary>Show the relic-progress window.</summary>
    public bool ShowRelicWindow = true;

    // --- diagnostics --------------------------------------------------------

    public bool VerboseLog;
}
