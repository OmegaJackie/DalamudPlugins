using System;
using System.Collections.Generic;
using BozjaBuddyReborn.Game;
using ECommons.DalamudServices;

namespace BozjaBuddyReborn.Multibox;

/// <summary>
/// Keeps the shared duty-action hotbar fed: reads this box's two duty slots every frame, pushes
/// them over the multibox link on an interval, and presents everyone's slots back as one list.
///
/// WHY THE LOCAL READ IS EVERY FRAME BUT THE SEND IS NOT. Reading DutyActionManager is two
/// pointer dereferences, so your own row can be perfectly smooth for free. The wire is the
/// expensive part, and a cooldown sweep does not need 60 updates a second to read correctly - so
/// peers are sent on an interval and their cooldowns are DECAYED locally in between, which is
/// what keeps a remote row ticking down smoothly instead of stepping once a second.
///
/// Must be ticked from the framework thread; it reads live game memory.
/// </summary>
public sealed class DutyActionSync(Configuration config, MultiboxLink link)
{
    private readonly Configuration _config = config;
    private readonly MultiboxLink _link = link;

    /// <summary>How often this box's loadout goes on the wire.</summary>
    private const long SendIntervalMs = 500;

    private long _lastSendMs;
    private long _rosterStampMs;
    private IReadOnlyList<PeerDuty> _lastRoster = [];

    /// <summary>This box's own two slots, read fresh every tick.</summary>
    public DutySlot Own0 { get; private set; } = DutySlot.Empty;
    public DutySlot Own1 { get; private set; } = DutySlot.Empty;

    /// <summary>True when the game is exposing duty actions at all (i.e. we are in Bozja).</summary>
    public bool Available { get; private set; }

    /// <summary>Why the local read produced what it did, surfaced when the bar looks empty.</summary>
    public string Diagnostic { get; private set; } = "not read yet";

    /// <summary>This box's character name as it goes on the wire.</summary>
    public string SelfName { get; private set; } = "unknown";

    public void Tick()
    {
        if (!Svc.Framework.IsInFrameworkUpdateThread)
            return;

        // ALWAYS read, then report. This used to gate the read on DutyActions.Available - which
        // is only ever SET by that read - so it was circular: nothing was read, the flag stayed
        // false, and every slot on every box showed empty while the duty bar plainly had actions.
        var (s0, s1) = DutyActions.ReadBoth();
        Own0 = s0;
        Own1 = s1;
        Available = DutyActions.Available;
        Diagnostic = DutyActions.Diagnostic;

        SelfName = DutyRoster.SanitiseName(Svc.Objects.LocalPlayer?.Name.TextValue);

        if (!_config.MultiboxEnabled)
            return;

        var now = Environment.TickCount64;
        if (now - _lastSendMs < SendIntervalMs)
            return;
        _lastSendMs = now;

        var encoded = DutyRoster.Encode(SelfName, s0, s1);

        if (_config.MultiboxIsHost)
        {
            // The host folds its own row in and pushes the combined roster out, so every box -
            // itself included - is reading one agreed list rather than each assembling its own.
            _link.BroadcastRoster(encoded);
        }
        else
        {
            _link.ReportDutyActions(encoded);
        }

        // Stamp whenever the roster object actually changes identity, so decay measures from the
        // moment the data arrived rather than from the moment we happened to look at it.
        if (!ReferenceEquals(_lastRoster, _link.Roster))
        {
            _lastRoster = _link.Roster;
            _rosterStampMs = now;
        }
    }

    /// <summary>
    /// Everyone's slots for display: this box first, then every peer, with peer cooldowns aged
    /// forward to now so a remote sweep runs smoothly between updates.
    ///
    /// Falls back to just this box when the link is down - running alone should still show your
    /// own hotbar rather than an empty window.
    /// </summary>
    public List<PeerDuty> Snapshot()
    {
        var self = new PeerDuty(SelfName, Own0, Own1, true);

        var roster = _link.Roster;
        if (roster.Count == 0)
            return [self];

        var age = Math.Max(0f, (Environment.TickCount64 - _rosterStampMs) / 1000f);

        var result = new List<PeerDuty> { self };
        foreach (var p in roster)
        {
            // Our own row is always the locally-read one; the copy that came back around the
            // loop is the same box seen a moment later and would only ever be staler.
            if (p.IsSelf || p.Name == SelfName)
                continue;

            result.Add(p with
            {
                Slot0 = Decay(p.Slot0, age),
                Slot1 = Decay(p.Slot1, age),
            });
        }

        return result;
    }

    /// <summary>
    /// Age a slot forward. A charge that finishes recharging in the meantime is credited, which
    /// keeps a peer's row from showing "0 charges, 0s left" while we wait for the next update.
    /// </summary>
    private static DutySlot Decay(DutySlot s, float seconds)
    {
        if (!s.IsSet || s.CooldownRemaining <= 0f || seconds <= 0f)
            return s;

        var remaining = s.CooldownRemaining - seconds;
        if (remaining > 0f)
            return s with { CooldownRemaining = remaining };

        var gained = s.CooldownTotal > 0f ? 1 + (int)(-remaining / s.CooldownTotal) : 1;
        var charges = (byte)Math.Min(s.MaxCharges, s.CurCharges + gained);

        return s with
        {
            CurCharges = charges,
            CooldownRemaining = charges >= s.MaxCharges || s.CooldownTotal <= 0f
                ? 0f
                : s.CooldownTotal - (-remaining % s.CooldownTotal),
        };
    }
}
