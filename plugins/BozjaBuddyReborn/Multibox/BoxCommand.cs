using System;
using System.Globalization;

namespace BozjaBuddyReborn.Multibox;

/// <summary>What the operator is telling a box (or every box) to do.</summary>
public enum BoxVerb : byte
{
    None = 0,

    /// <summary>Start the orchestrator.</summary>
    Start = 1,

    /// <summary>Stop the orchestrator.</summary>
    Stop = 2,

    /// <summary>Load a saved Lost Action loadout into the two duty slots. Arg = "a0,a1".</summary>
    Loadout = 3,

    /// <summary>Walk to and interact with the nearest object of a data id. Arg = data id.</summary>
    Interact = 4,

    /// <summary>Abandon whatever remote errand is running and hand control back.</summary>
    Cancel = 5,

    /// <summary>Sign up for the current Critical Engagement via the Bozja recruitment window.</summary>
    SignUp = 6,

    /// <summary>
    /// Press one of the two duty-action slots. Arg = "slot,expectedActionId".
    ///
    /// The expected action id rides along because the roster a peer's slot was drawn from is up
    /// to half a second old - see DutyActions.Press for why a stale press is refused rather than
    /// resolved.
    /// </summary>
    DutyAction = 7,

    /// <summary>
    /// Start or stop the party-support task. Arg = "1" to start, "0" to stop.
    ///
    /// Worth being a group instruction rather than a local button, because the case it exists for
    /// is the one where you are running several boxes and want the healer box supporting everyone
    /// - and because a task that spends farmed charges on a timer is one you want to be able to
    /// stop on every box at once without focusing four game windows.
    /// </summary>
    PartySupport = 8,
}

/// <summary>
/// One instruction from the operator's box to one or all of the others.
///
/// THE WHOLE POINT of the multibox link so far has been that boxes agree on an objective. This is
/// the other half: acting on a box without focusing its game window. It is deliberately a
/// separate channel from the objective broadcast, because these are one-shot imperatives with a
/// target, not shared state - re-delivering the current objective to a reconnecting box is right,
/// re-delivering "interact with the cache" is not.
///
/// TARGETING IS BY NAME, and that is a considered exception to the rule that identity belongs to
/// the transport. The operator picks a row in a window that says "Kallen Vibritannia", so the
/// instruction has to carry something the operator can see; connection ids are invisible and
/// change on every reconnect. <see cref="All"/> addresses everyone. A name that matches nobody is
/// simply ignored by every box, which is the safe failure.
/// </summary>
public readonly record struct BoxCommand(string Target, BoxVerb Verb, string Arg)
{
    /// <summary>Target value meaning "every box, including the sender".</summary>
    public const string All = "*";

    public bool IsForEveryone => Target == All;

    /// <summary>Does this instruction apply to a box with the given name?</summary>
    public bool AppliesTo(string selfName) =>
        IsForEveryone || string.Equals(Target, selfName, StringComparison.Ordinal);

    public string Encode() => string.Join('|',
        DutyRoster.SanitiseName(Target) is var t && Target == All ? All : t,
        ((byte)Verb).ToString(CultureInfo.InvariantCulture),
        Arg ?? string.Empty);

    public static bool TryDecode(string[] parts, int from, out BoxCommand cmd)
    {
        cmd = default;
        if (parts.Length < from + 2)
            return false;

        if (!byte.TryParse(parts[from + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var verb))
            return false;

        if (verb == 0 || verb > (byte)BoxVerb.PartySupport)
            return false;

        cmd = new BoxCommand(parts[from], (BoxVerb)verb, parts.Length > from + 2 ? parts[from + 2] : string.Empty);
        return true;
    }

    public override string ToString() => Verb switch
    {
        BoxVerb.Start => "start",
        BoxVerb.Stop => "stop",
        BoxVerb.Loadout => $"apply loadout ({Arg})",
        BoxVerb.Interact => $"interact with {Arg}",
        BoxVerb.Cancel => "cancel errand",
        BoxVerb.SignUp => "sign up for the engagement",
        BoxVerb.DutyAction => TryDecodeDutyAction(Arg, out var slot, out _)
            ? $"press duty action {slot + 1}"
            : "press a duty action",
        BoxVerb.PartySupport => Arg == "1" ? "start party support" : "stop party support",
        _ => "nothing",
    };

    // ------------------------------------------------- duty action arguments

    /// <summary>
    /// Build the argument for <see cref="BoxVerb.DutyAction"/>.
    ///
    /// Comma separated, because '|' is the pipe's own field separator and an argument may never
    /// contain one - the same constraint DutyRoster works under.
    /// </summary>
    public static string EncodeDutyAction(int slot, uint expectedActionId) => string.Concat(
        slot.ToString(CultureInfo.InvariantCulture),
        ",",
        expectedActionId.ToString(CultureInfo.InvariantCulture));

    /// <summary>
    /// Read a <see cref="BoxVerb.DutyAction"/> argument.
    ///
    /// The action id is optional: a missing or unreadable one decodes as 0, which
    /// DutyActions.Press treats as "press whatever is loaded". Only the slot is required.
    /// </summary>
    public static bool TryDecodeDutyAction(string arg, out int slot, out uint expectedActionId)
    {
        slot = 0;
        expectedActionId = 0;

        if (string.IsNullOrEmpty(arg))
            return false;

        var p = arg.Split(',');
        if (!int.TryParse(p[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out slot))
            return false;

        if (p.Length > 1)
            uint.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out expectedActionId);

        return true;
    }
}
