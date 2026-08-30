using System;
using System.Numerics;
using BozjaBuddyReborn.Automation;
using BozjaBuddyReborn.Game;
using BozjaBuddyReborn.Relic;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using ECommons.DalamudServices;

namespace BozjaBuddyReborn.Windows;

public sealed class ConfigWindow : Window
{
    private static readonly Vector4 Grey = new(0.70f, 0.70f, 0.70f, 1f);
    private static readonly Vector4 Yellow = new(0.95f, 0.80f, 0.30f, 1f);

    private readonly Configuration _config;
    private readonly LostActionCatalog _lostActions;
    private readonly RegionResolver _regions;
    private readonly AggroAvoidance _avoidance;

    public ConfigWindow(
        Configuration config,
        LostActionCatalog lostActions,
        RegionResolver regions,
        AggroAvoidance avoidance)
        : base("Bozja Buddy Reborn - Settings###BozjaBuddyRebornConfig")
    {
        _config = config;
        _lostActions = lostActions;
        _regions = regions;
        _avoidance = avoidance;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(520, 420),
            MaximumSize = new Vector2(1200, 1200),
        };
    }

    private void Save() => ConfigSaver.Save(_config);

    public override void Draw()
    {
        if (!ImGui.BeginTabBar("##bbr_cfg"))
            return;

        if (ImGui.BeginTabItem("Combat"))
        {
            DrawCombat();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Engagements"))
        {
            DrawEngagements();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Movement"))
        {
            DrawMovement();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Zones"))
        {
            DrawZones();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Lost Actions"))
        {
            DrawLostActions();
            ImGui.EndTabItem();
        }

        ImGui.EndTabBar();
    }

    private void DrawCombat()
    {
        ImGui.TextColored(Grey,
            "The two plugins run in separate roles so they cannot fight each other:\n" +
            "BossMod dodges, RSR presses buttons.");
        ImGui.Separator();

        var avoid = _config.UseBossModAvoidance;
        if (ImGui.Checkbox("BossMod: AoE avoidance", ref avoid))
        {
            _config.UseBossModAvoidance = avoid;
            Save();
        }
        ImGui.TextColored(Grey,
            "Works with BossMod Reborn (preferred) or the original awgil BossMod - whichever is loaded.\n" +
            "Reborn: its AI is turned on with ForbidActions, its AI preset cleared and the global\n" +
            "autorotation force-disabled, so it dodges and never queues an action.\n" +
            "Original: AI mode is turned on with auto-target off and any active rotation preset cleared -\n" +
            "its AI preset is only auto-target + follow + movement, so it has nothing to press anyway.\n" +
            "Without these guards BossMod and RSR stall each other's action queue.\n" +
            "Everything changed is restored when you stop.");
        ImGui.TextColored(Grey,
            "The original cannot report when it is dodging and only moves while we are not pathing,\n" +
            "so it dodges at holds and once arrived; Reborn dodges en route as well.");

        ImGui.Spacing();

        var rsr = _config.UseRotationSolver;
        if (ImGui.Checkbox("Rotation Solver Reborn: rotation", ref rsr))
        {
            _config.UseRotationSolver = rsr;
            Save();
        }
        ImGui.TextColored(Grey, "RSR runs in Auto mode - it picks and attacks targets itself.");

        if (!rsr)
            ImGui.TextColored(Yellow, "With RSR off nothing will attack. You will have to fight manually.");

        ImGui.Spacing();

        var reapply = _config.ReapplyAvoidanceConfigEachFight;
        if (ImGui.Checkbox("Re-apply the avoidance config before every fight", ref reapply))
        {
            _config.ReapplyAvoidanceConfigEachFight = reapply;
            Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Slower, but survives you changing BossMod's settings mid-session.");

        ImGui.Separator();

        var close = _config.CloseToTarget;
        if (ImGui.Checkbox("Walk into range of the target while fighting", ref close))
        {
            _config.CloseToTarget = close;
            Save();
        }
        ImGui.TextColored(Grey,
            "BossMod Reborn only closes on a target when it is following a party master, and solo\n" +
            "the master is you - so in the avoidance-only setup this plugin uses, it dodges and\n" +
            "nothing else. Without this a melee job stands where travel left it and the rotation\n" +
            "falls back to its ranged filler (Enpi on Samurai, and so on).\n" +
            "Melee and tanks are pulled to 2y inside the hitbox; everything else to 15y.\n" +
            "With the ORIGINAL BossMod this is bypassed: its FollowSlot module walks to the target\n" +
            "by itself, around the AoEs, and an approach path of ours would only make it yield.");

        ImGui.Spacing();

        var reassert = _config.CombatStateReassertSeconds;
        ImGui.SetNextItemWidth(200);
        if (ImGui.SliderFloat("Re-assert combat state (s)", ref reassert, 0f, 30f, "%.0f"))
        {
            _config.CombatStateReassertSeconds = reassert;
            Save();
        }
        ImGui.TextColored(Grey,
            "Re-sends the on/off state to both plugins on this interval even when it has not\n" +
            "changed. RSR and Reborn cannot be asked what state they are in, and both drop it on\n" +
            "their own - Reborn idles its AI whenever the party slot it follows goes invalid, which\n" +
            "a Bozja alliance does constantly - so without this the run can continue with nothing\n" +
            "armed. The original BossMod's AI state IS readable, so its heartbeat only writes when\n" +
            "something drifted (it force-disables itself on death, which would drop the dodging).\n" +
            "0 turns it off.");
        if (reassert > 0f)
        {
            ImGui.TextColored(Yellow,
                "If RSR's \"show toggled setting in chat\" is on, each re-assert prints a line. Turn\n" +
                "that off in RSR, or raise this interval, if the chat noise bothers you.");
        }
    }

    private void DrawEngagements()
    {
        var ces = _config.DoCriticalEngagements;
        if (ImGui.Checkbox("Join Critical Engagements", ref ces))
        {
            _config.DoCriticalEngagements = ces;
            Save();
        }

        var fates = _config.DoFates;
        if (ImGui.Checkbox("Farm skirmish FATEs when no engagement is open", ref fates))
        {
            _config.DoFates = fates;
            Save();
        }

        ImGui.Separator();

        ImGui.TextUnformatted("When something aggroes onto you while travelling:");

        var keepRunning = _config.AggroResponse == TravelAggroResponse.KeepRunning;

        if (ImGui.RadioButton("Keep running (never attack)", keepRunning))
        {
            _config.AggroResponse = TravelAggroResponse.KeepRunning;
            Save();
        }
        ImGui.SameLine();
        if (ImGui.RadioButton("Stop and fight back", !keepRunning))
        {
            _config.AggroResponse = TravelAggroResponse.FightBack;
            Save();
        }

        ImGui.TextColored(Grey,
            "Keep running holds the rotation OFF for the whole route, so nothing is attacked on the\n" +
            "way - field mobs leash and drop off once you outrun them. Stopping for every puller\n" +
            "instead turns one run into a string of fights that earn nothing and burn the\n" +
            "registration window.\n" +
            "Either way, attackers ARE answered once you have arrived: there is nowhere left to run,\n" +
            "and a Critical Engagement's registration window has to be waited out where you stand.\n" +
            "Keyed on hostiles actually targeting you, not the in-combat flag, which lingers after\n" +
            "the last mob dies.");
        ImGui.TextColored(Yellow,
            "You cannot mount in combat, so while something is chasing you the run is on foot until\n" +
            "it leashes.");

        var sticky = _config.StickyObjective;
        if (ImGui.Checkbox("Stay on the current objective until it is done", ref sticky))
        {
            _config.StickyObjective = sticky;
            Save();
        }
        ImGui.TextColored(Grey,
            "Objectives are re-ranked every tick. Without this, a skirmish that spawns and ranks\n" +
            "higher pulls the character off the fight it is already in.");

        ImGui.Separator();

        var duels = _config.EngageDuels;
        if (ImGui.Checkbox("Enter duels (1v1)", ref duels))
        {
            _config.EngageDuels = duels;
            Save();
        }
        ImGui.TextColored(Grey,
            "Aces High, Beast of Man, And the Flames Went Higher, The Broken Blade, Head of the Snake,\n" +
            "Taking the Lyon's Share. Only one player is chosen and entry costs notoriety.");

        var large = _config.EngageLargeScale;
        if (ImGui.Checkbox("Enter large-scale battles", ref large))
        {
            _config.EngageLargeScale = large;
            Save();
        }
        ImGui.TextColored(Grey, "Castrum Lacus Litore and The Dalriada. Long, scheduled, usually organised runs.");

        ImGui.Separator();

        var minSeconds = _config.MinRegisterSecondsLeft;
        ImGui.SetNextItemWidth(200);
        if (ImGui.SliderInt("Minimum registration window (s)", ref minSeconds, 10, 60))
        {
            _config.MinRegisterSecondsLeft = minSeconds;
            Save();
        }
        ImGui.TextColored(Grey,
            "The game refuses registration under 10 seconds, so leave enough margin to actually get there.");
    }

    private void DrawMovement()
    {
        ImGui.TextColored(Grey,
            "Neither Bozja nor Zadnor has a single aetheryte - there is no in-zone teleport of any\n" +
            "kind. Mount travel is the fast travel, so leaving mounting off means jogging the map.");
        ImGui.Separator();

        var mount = _config.UseMount;
        if (ImGui.Checkbox("Summon a mount for long travel", ref mount))
        {
            _config.UseMount = mount;
            Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Uses Mount Roulette past 30y, and dismounts on arrival so you can fight.");

        var fly = _config.AllowFlight;
        if (ImGui.Checkbox("Allow flight", ref fly))
        {
            _config.AllowFlight = fly;
            Save();
        }
        ImGui.TextColored(Grey,
            "The flight path is only used once actually airborne - handing vnavmesh a flight path\n" +
            "while grounded gives it a route the character cannot follow, which stalls the run.");

        var arrive = _config.ArriveRange;
        ImGui.SetNextItemWidth(200);
        if (ImGui.SliderFloat("Arrival range (y)", ref arrive, 3f, 40f, "%.0f"))
        {
            _config.ArriveRange = arrive;
            Save();
        }

        var stall = _config.StallTimeoutSeconds;
        ImGui.SetNextItemWidth(200);
        if (ImGui.SliderFloat("Stall timeout (s)", ref stall, 3f, 30f, "%.0f"))
        {
            _config.StallTimeoutSeconds = stall;
            Save();
        }
        ImGui.TextColored(Grey,
            "Zadnor's stacked terrain wedges ground paths regularly. When no real movement happens for\n" +
            "this long, the path is torn down, the destination re-snapped to the navmesh, and a fresh\n" +
            "path issued.");

        ImGui.Separator();
        DrawAggroAvoidance();

        ImGui.Separator();
        DrawIdleSpots();
    }

    /// <summary>Routing around enemy aggro while travelling.</summary>
    private void DrawAggroAvoidance()
    {
        var avoid = _config.AvoidDangerousEnemies;
        if (ImGui.Checkbox("Route around enemy aggro while travelling", ref avoid))
        {
            _config.AvoidDangerousEnemies = avoid;
            Save();
        }
        ImGui.TextColored(Grey,
            "Models FFXIV aggro as a forward SIGHT cone plus a smaller all-round PROXIMITY ring, so\n" +
            "passing behind an enemy is allowed at a distance that would pull it head-on. Skipped\n" +
            "entirely while actually flying - ground enemies cannot reach you up there.");

        if (!avoid)
            return;

        var minLevel = (int)_config.DangerousEnemyMinLevel;
        ImGui.SetNextItemWidth(200);
        if (ImGui.SliderInt("Only avoid level >=", ref minLevel, 0, 100))
        {
            _config.DangerousEnemyMinLevel = (byte)minLevel;
            Save();
        }
        ImGui.TextColored(Grey, "0 avoids every hostile enemy. The list below shows what levels are really nearby.");

        var sight = _config.DangerSightRadius;
        ImGui.SetNextItemWidth(200);
        if (ImGui.SliderFloat("Sight radius (y)", ref sight, 5f, 50f, "%.0f"))
        {
            _config.DangerSightRadius = sight;
            Save();
        }

        var cone = _config.DangerConeDegrees;
        ImGui.SetNextItemWidth(200);
        if (ImGui.SliderFloat("Sight cone (deg)", ref cone, 30f, 360f, "%.0f"))
        {
            _config.DangerConeDegrees = cone;
            Save();
        }

        var proximity = _config.DangerProximityRadius;
        ImGui.SetNextItemWidth(200);
        if (ImGui.SliderFloat("Proximity radius (y)", ref proximity, 2f, 30f, "%.0f"))
        {
            _config.DangerProximityRadius = proximity;
            Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Aggro range that ignores facing - it fires from directly behind too.");

        var clearance = _config.DangerClearance;
        ImGui.SetNextItemWidth(200);
        // Floor at 2, not 0. The detour offset is (sight radius + this), and the sidestep has to
        // clear the enemy by enough for the route to it to be accepted - at 0 there is no offset
        // to speak of and no detour can ever be used, which silently switches the whole feature
        // off while every other setting still says it is on.
        if (ImGui.SliderFloat("Detour clearance (y)", ref clearance, 2f, 25f, "%.0f"))
        {
            _config.DangerClearance = clearance;
            Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(
                "How far to the side a detour steps past an enemy.\n" +
                "HIGHER IS SAFER HERE. Raising the sight radius makes detours HARDER to\n" +
                "accept (there is more to clear); raising this makes them easier.");

        var ignore = _config.DangerIgnoreNearObjective;
        ImGui.SetNextItemWidth(200);
        if (ImGui.SliderFloat("Ignore within of objective (y)", ref ignore, 0f, 80f, "%.0f"))
        {
            _config.DangerIgnoreNearObjective = ignore;
            Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(
                "Enemies this close to the destination are the objective's own mobs.\n" +
                "Routing around those would mean never arriving.");

        ImGui.Spacing();
        ImGui.TextColored(Grey, "Hostile enemies nearby (use this to set the level threshold):");

        var zones = _avoidance.Scan();
        var census = _avoidance.LastCensus;

        // WHY THE LIST IS THE SHAPE IT IS. "Avoidance does nothing" has several causes that look
        // identical from outside, and one of them - the game not setting the hostile flag on an
        // idle field mob - would empty this list completely while every setting still reads as
        // correct. Showing what was dropped and why turns that into a glance.
        ImGui.TextColored(Grey,
            $"  scanned {census.Combatants} combatant(s): {census.Accepted} tracked, " +
            $"{census.NotHostile} not flagged hostile, {census.BelowLevel} below the level " +
            $"threshold, {census.AlreadyOnUs} already on us, {census.OtherFloor} on another " +
            $"floor, {census.Suppressed} suppressed, {census.OutOfRange} out of range.");

        if (census.Combatants > 0 && census.Accepted == 0 && census.NotHostile == census.Combatants)
            ImGui.TextColored(Yellow,
                "  Every nearby combatant was dropped for not carrying the hostile flag.\n" +
                "  That is the one reading that would silently disable avoidance entirely -\n" +
                "  if these are ordinary field mobs, report it.");

        if (zones.Count == 0)
        {
            ImGui.TextColored(Grey, "  none tracked");
            return;
        }

        if (!ImGui.BeginChild("##bbr_danger", new Vector2(0, 140), true))
        {
            ImGui.EndChild();
            return;
        }

        foreach (var z in zones)
            ImGui.TextUnformatted($"Lv{z.Level,-3} {z.Name}   {Movement.DistanceToPlayer(z.Position):F0}y");

        ImGui.EndChild();
    }

    /// <summary>Staging points to wait at when the working zone has nothing up.</summary>
    private void DrawIdleSpots()
    {
        var idle = _config.UseIdleSpot;
        if (ImGui.Checkbox("Wait at a staging point when nothing is up", ref idle))
        {
            _config.UseIdleSpot = idle;
            Save();
        }
        ImGui.TextColored(Grey,
            "Stages inside the zone you are working, so the next thing to spawn is close. Zadnor's\n" +
            "plateaus are far enough apart that starting from the wrong one costs most of the\n" +
            "registration window.");

        if (!idle)
            return;

        var territory = Svc.ClientState.TerritoryType;
        var editable = BozjaZones.IsFieldZone(territory) ? territory : BozjaZones.Zadnor;

        ImGui.Spacing();
        ImGui.TextColored(Grey, $"Staging points for {BozjaZones.Name(editable)} (map coordinates):");

        foreach (var region in FieldRegions.All)
        {
            var key = $"{editable}:{(byte)region}";
            var has = _config.IdleSpots.TryGetValue(key, out var value) && value.Length >= 2;
            var coords = has ? new Vector2(value![0], value[1]) : Vector2.Zero;

            ImGui.SetNextItemWidth(160);
            if (ImGui.InputFloat2($"{FieldRegions.Label(editable, region)}##idle{key}", ref coords))
            {
                if (coords is { X: > 0, Y: > 0 })
                    _config.IdleSpots[key] = [coords.X, coords.Y];
                else
                    _config.IdleSpots.Remove(key);
                Save();
                OnIdleSpotsChanged?.Invoke();
            }

            ImGui.SameLine();
            if (ImGui.SmallButton($"Here##idle{key}"))
            {
                if (MapCoords.PlayerMapPosition() is { } here && BozjaZones.IsFieldZone(territory))
                {
                    _config.IdleSpots[key] = [here.X, here.Y];
                    Save();
                    OnIdleSpotsChanged?.Invoke();
                }
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Set this staging point to where you are standing right now.");

            if (has)
            {
                ImGui.SameLine();
                if (ImGui.SmallButton($"Clear##idle{key}"))
                {
                    _config.IdleSpots.Remove(key);
                    Save();
                    OnIdleSpotsChanged?.Invoke();
                }
            }
        }
    }

    /// <summary>
    /// Raised when a staging point is edited, so the controller drops its resolved-position
    /// cache and re-snaps the new coordinates to the navmesh.
    /// </summary>
    public Action? OnIdleSpotsChanged { get; set; }

    private void DrawZones()
    {
        ImGui.TextColored(Grey,
            "Both field zones are split into three regions, and the relic materials are\n" +
            "region-specific. In Zadnor, skirmishes and Critical Engagements inside the SAME\n" +
            "plateau drop different items - so farming the wrong third, or the wrong activity\n" +
            "in the right third, yields nothing you need.");
        ImGui.Separator();

        DrawDropTable("Bozjan Southern Front", BozjaZones.BozjanSouthernFront);
        ImGui.Spacing();
        DrawDropTable("Zadnor", BozjaZones.Zadnor);

        ImGui.Separator();
        ImGui.TextColored(Grey, "Learned engagement regions");
        ImGui.TextColored(Grey,
            "There is no shipped table saying which region each Critical Engagement sits in, so\n" +
            "it is recorded the first time you stand at one. Until then an estimate from the map\n" +
            "region labels is used, which the learned value permanently replaces.");
        ImGui.TextUnformatted($"Learned so far: {_regions.LearnedCount}");
        if (ImGui.Button("Forget learned regions"))
            _regions.Forget();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Use after a patch that moves engagements around.");

        ImGui.Spacing();
        var skipUnknown = _config.SkipUnknownRegions;
        if (ImGui.Checkbox("Skip objectives whose region is not yet known", ref skipUnknown))
        {
            _config.SkipUnknownRegions = skipUnknown;
            Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(
                "Off by default: visiting an unknown objective is how its region gets learned,\n" +
                "and a fresh install would otherwise have nothing to do while farming a material.");

        if (skipUnknown && _regions.LearnedCount == 0)
            ImGui.TextColored(Yellow,
                "Nothing has been learned yet, so with a farm target set this will skip everything.");

        ImGui.Separator();
        ImGui.TextColored(Grey, "Diagnostics");

        var logCallbacks = _config.LogUiCallbacks;
        if (ImGui.Checkbox("Log every UI callback to /xllog (debug)", ref logCallbacks))
        {
            _config.LogUiCallbacks = logCallbacks;
            Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(
                "For settling how the Resistance Recruitment window works.\n" +
                "Nothing anywhere documents how a Register or Commence press is sent, so with\n" +
                "this on, press those buttons BY HAND on a live engagement and /xllog will show\n" +
                "the addon's real name and the exact arguments the game used.\n" +
                "Very noisy - it logs every addon in the client, not just this plugin's.");

        if (logCallbacks)
            ImGui.TextColored(Yellow, "  Logging every UI callback. Turn this off when you are done.");

        return;

        static void DrawDropTable(string title, uint territory)
        {
            ImGui.TextColored(Grey, title);
            if (!ImGui.BeginTable($"##drops{territory}", 3,
                    ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
                return;

            ImGui.TableSetupColumn("Region", ImGuiTableColumnFlags.WidthFixed, 190);
            ImGui.TableSetupColumn("Activity", ImGuiTableColumnFlags.WidthFixed, 150);
            ImGui.TableSetupColumn("Material", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableHeadersRow();

            foreach (var region in FieldRegions.All)
            {
                foreach (var drop in ZoneDrops.ForTerritory(territory))
                {
                    if (drop.Region != region)
                        continue;

                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(FieldRegions.Label(territory, region));
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(drop.Activity switch
                    {
                        DropActivity.Skirmish => "Skirmish",
                        DropActivity.CriticalEngagement => "Critical Engagement",
                        _ => "Any",
                    });
                    ImGui.TableNextColumn();
                    var name = Svc.Data.GetExcelSheet<Lumina.Excel.Sheets.Item>()?
                        .GetRowOrDefault(drop.ItemId)?.Name.ExtractText();
                    ImGui.TextUnformatted(string.IsNullOrEmpty(name) ? $"item {drop.ItemId}" : name);
                }
            }

            ImGui.EndTable();
        }
    }

    private void DrawLostActions()
    {
        var click = _config.DutyActionClickToUse;
        if (ImGui.Checkbox("Clicking the duty-action hotbar fires the action", ref click))
        {
            _config.DutyActionClickToUse = click;
            Save();
        }
        ImGui.TextColored(Grey,
            "On by default. Your own two slots always answer; a peer's slot answers only from the\n" +
            "host box, because only the host may instruct the group. Turn this off to put the\n" +
            "window back to read-only.");

        ImGui.Spacing();

        var clear = _config.DutyActionTransparent;
        if (ImGui.Checkbox("Duty-action window has no background", ref clear))
        {
            _config.DutyActionTransparent = clear;
            Save();
        }
        ImGui.TextColored(Grey,
            "On by default, so the bar reads as an overlay on the game rather than a panel in front\n" +
            "of it. The title bar stays either way - with the background gone it is the only thing\n" +
            "left to drag the window by.");

        ImGui.Separator();

        var auto = _config.AutoUseLostActions;
        if (ImGui.Checkbox("Automatically use Lost Actions in combat", ref auto))
        {
            _config.AutoUseLostActions = auto;
            Save();
        }
        ImGui.TextColored(Grey,
            "Off by default. Lost Actions are a farmed resource - burning them on trash is worse than\n" +
            "not using them. One entry per cooldown window, in the order listed below.\n" +
            "Anything whose buff is already running is skipped and the window goes to the next entry,\n" +
            "so an Essence is not re-drunk over itself every few seconds.");

        if (!auto)
            return;

        ImGui.Spacing();

        var fire = _config.AutoFireLostActions;
        if (ImGui.Checkbox("...including pressing them, which SPENDS charges", ref fire))
        {
            _config.AutoFireLostActions = fire;
            Save();
        }
        ImGui.TextColored(Yellow,
            "Off by default, and worth reading before you turn it on.\n" +
            "\n" +
            "The two kinds of holster entry do not cost the same. ITEMS - every Essence, the potion,\n" +
            "ether and medi kits, Dynamis Dice, Reraiser, Lodestone, Light Curtain, Resistance Elixir\n" +
            "- are consumed the moment the box above uses them, and always have been. ACTIONS - Lost\n" +
            "Cure, Lost Font of Power, the Banners, and so on - are only LOADED into a duty slot by\n" +
            "that same call, and until this build nothing pressed the slot, so ticking the box above\n" +
            "has never spent an action charge however long it was left on.\n" +
            "\n" +
            "This switch is the missing press. Turning it on means every action you tick below is\n" +
            "actually fired, roughly once per cooldown window, for as long as you are in combat in an\n" +
            "engagement - so the toggle above starts costing farmed charges it did not cost before.\n" +
            "Leaving it off keeps that exactly as it was: items are used, actions are left alone.\n" +
            "\n" +
            "It drives duty slot 1 when it has to load, so a loadout parked there can be replaced. An\n" +
            "action already sitting in either slot is pressed where it stands and nothing is moved.");

        ImGui.Spacing();

        var cooldown = _config.LostActionCooldownMs;
        ImGui.SetNextItemWidth(200);
        if (ImGui.SliderInt("Minimum gap (ms)", ref cooldown, 2000, 60000))
        {
            _config.LostActionCooldownMs = cooldown;
            Save();
        }

        ImGui.Separator();
        ImGui.TextColored(Grey,
            "Tick what to auto-use. \"item\" marks the entries the game consumes straight out of the\n" +
            "holster - those are used whether or not the press switch above is on.");

        if (!ImGui.BeginChild("##bbr_lostactions", new Vector2(0, 300), true))
        {
            ImGui.EndChild();
            return;
        }

        foreach (var entry in _lostActions.All)
        {
            var selected = _config.AutoLostActions.Contains(entry.RowId);
            var kind = entry.IsItem ? ", item" : string.Empty;
            var label = $"{entry.Name}  (weight {entry.Weight}{kind})##la{entry.RowId}";
            if (ImGui.Checkbox(label, ref selected))
            {
                if (selected)
                    _config.AutoLostActions.Add(entry.RowId);
                else
                    _config.AutoLostActions.Remove(entry.RowId);
                Save();
            }

            // An action ticked while the press switch is off does nothing at all, which is a
            // state the user should be able to see rather than infer from the absence of an
            // effect - the whole failure this build exists to fix.
            if (selected && !entry.IsItem && !fire)
            {
                ImGui.SameLine();
                ImGui.TextColored(Yellow, "(not pressed)");
            }
        }

        ImGui.EndChild();

        DrawPartySupport();
    }

    /// <summary>
    /// The party-support task's settings. The task itself is started and stopped from the main
    /// window - this is only what it does once it is running.
    /// </summary>
    private void DrawPartySupport()
    {
        ImGui.Separator();
        ImGui.TextUnformatted("Party support");
        ImGui.TextColored(Grey,
            "A separate, stoppable task that keeps the party's Lost Action buffs up and heals whoever\n" +
            "is worst off. Start and stop it from the main window. It only ever aims at your own\n" +
            "party - never an alliance member or a passer-by - and it stops itself when it runs out.");

        ImGui.Spacing();

        var refresh = _config.PartyBuffRefreshFraction * 100f;
        ImGui.SetNextItemWidth(200);
        if (ImGui.SliderFloat("Top up below (% of duration)", ref refresh, 5f, 50f, "%.0f%%"))
        {
            _config.PartyBuffRefreshFraction = Math.Clamp(refresh / 100f, 0.05f, 0.5f);
            Save();
        }
        ImGui.TextColored(Grey,
            "Someone with no buff at all is always served first. This is the second pass: top up the\n" +
            "most-expired member once they drop below this much of the total. Lost Bravery runs 600s,\n" +
            "so 20% is the last two minutes. The totals are read out of the game's own tooltip text -\n" +
            "an action whose duration is not in the data is never topped up, only given to people who\n" +
            "have nothing.");

        ImGui.Spacing();

        var heal = _config.PartyHealBelowFraction * 100f;
        ImGui.SetNextItemWidth(200);
        if (ImGui.SliderFloat("Heal below (% HP)", ref heal, 20f, 95f, "%.0f%%"))
        {
            _config.PartyHealBelowFraction = Math.Clamp(heal / 100f, 0.2f, 0.95f);
            Save();
        }
        ImGui.TextColored(Grey,
            "Healing goes to the LOWEST-HP member first, re-decided before every cast. This floor is\n" +
            "what stops \"lowest\" meaning \"whoever is at 99%\" - there is always a lowest.");

        ImGui.Spacing();

        var gap = _config.PartySupportGapMs;
        ImGui.SetNextItemWidth(200);
        if (ImGui.SliderInt("Gap between casts (ms)", ref gap, 500, 10000))
        {
            _config.PartySupportGapMs = gap;
            Save();
        }

        var slot = _config.PartySupportSlot + 1;
        ImGui.SetNextItemWidth(200);
        if (ImGui.SliderInt("Duty slot to load into", ref slot, 1, 2))
        {
            _config.PartySupportSlot = Math.Clamp(slot - 1, 0, 1);
            Save();
        }
        ImGui.TextColored(Grey,
            "Slot 2 by default, because auto-use drives slot 1 and two things reloading one slot\n" +
            "underneath each other would spend the whole fight fighting. An action already sitting in\n" +
            "either slot is used where it is, so this only matters when something has to be loaded.");

        ImGui.Separator();
        ImGui.TextColored(Grey,
            "Tick what to maintain, in priority order. Only actions that can be aimed at a party\n" +
            "member are listed - raises are excluded, and so are the two whose status cannot be\n" +
            "identified in the game data, since \"do not re-apply what is already up\" is the point.");

        if (!ImGui.BeginChild("##bbr_partysupport", new Vector2(0, 200), true))
        {
            ImGui.EndChild();
            return;
        }

        foreach (var entry in _lostActions.PartySupport)
        {
            var selected = _config.PartySupportActions.Contains(entry.RowId);
            var kind = entry.IsPartyHeal
                ? "heal"
                : entry.HasDuration ? $"{Describe(entry.DurationSeconds)} buff" : "buff";

            if (ImGui.Checkbox($"{entry.Name}  ({kind})##ps{entry.RowId}", ref selected))
            {
                if (selected)
                    _config.PartySupportActions.Add(entry.RowId);
                else
                    _config.PartySupportActions.Remove(entry.RowId);
                Save();
            }

            // A buff with no duration can only ever be given to someone who has nothing - saying so
            // here is cheaper than the operator wondering why nobody is being topped up.
            if (selected && !entry.IsPartyHeal && !entry.HasDuration)
            {
                ImGui.SameLine();
                ImGui.TextColored(Yellow, "(no duration in the data - never topped up)");
            }
        }

        ImGui.EndChild();
    }

    private static string Describe(float seconds)
    {
        var total = (int)seconds;
        if (total >= 3600) return $"{total / 3600}h";
        return total >= 60 ? $"{total / 60}m" : $"{total}s";
    }
}
