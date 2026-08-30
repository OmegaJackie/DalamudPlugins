using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Memory;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace AutoFATE.Core;

/// <summary>
/// Answers the party teleport offer ("Accept teleport to ...?") while a grind is running: the host's
/// offer is accepted, everyone else's is refused.
///
/// Teleporting inside a party extends a free teleport offer to every party member in the same zone.
/// The server generates that offer from the teleport itself, so there is no flag we can clear to
/// teleport quietly — the only place to intervene is the prompt on the receiving client. Multibox
/// helpers that blanket-confirm it (BardToolbox's "Auto Accept Teleport Invite" is the one this
/// exists for) drag the whole fleet to whichever character teleported first, throwing away the
/// aetheryte every other character just picked for its own fate. The host is the exception: clients
/// mirror its zone anyway, so riding along is both correct and free.
///
/// The prompt names the destination, never the offerer, so who sent it is settled over multibox
/// instead — the host announces its destination before teleporting (<see cref="TaskBase.Teleporting"/>)
/// and a client accepts only an offer whose destination matches.
///
/// The dialog is answered from <see cref="AddonEvent.PostSetup"/>, which runs in the frame the addon
/// opens. BardToolbox polls SelectYesno from Framework.Update, which cannot observe the addon before
/// the frame after setup, so it never gets to answer a prompt we have already handled.
/// </summary>
internal sealed unsafe class TeleportOfferGuard : IDisposable {
    /// <summary>Addon sheet row holding the party teleport offer prompt.</summary>
    private const uint TeleportOfferAddonRow = 1800;

    private readonly FateModule _module;

    /// <summary>
    /// Literal text either side of the destination placeholder, read from the sheet in the client's
    /// own language so this keeps matching on non-English clients.
    /// </summary>
    private readonly string[] _promptFragments;

    internal TeleportOfferGuard(FateModule module) {
        _module = module;
        _promptFragments = [.. SeString.Parse(Sheets.Addon.GetRow(TeleportOfferAddonRow).Text)
            .Payloads.OfType<TextPayload>()
            .Select(payload => payload.Text?.Trim() ?? string.Empty)
            .Where(text => text.Length > 0)];

        if (_promptFragments.Length == 0)
            Svc.Log.PrintWarning($"Addon#{TeleportOfferAddonRow} yielded no text to match — party teleport offers will not be declined.");

        Svc.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "SelectYesno", OnSelectYesnoPostSetup);
    }

    public void Dispose() => Svc.AddonLifecycle.UnregisterListener(OnSelectYesnoPostSetup);

    private void OnSelectYesnoPostSetup(AddonEvent type, AddonArgs args) {
        if (!_module.Running || !_module.Config.DeclinePartyTeleportOffers || _promptFragments.Length == 0)
            return;

        if (ReadPrompt(args.GetAddon<AddonSelectYesno>()) is not { } prompt)
            return;

        // Same test BardToolbox uses to decide it should accept: every literal fragment of the sheet
        // row has to appear in the prompt, which the destination name in the middle does not disturb.
        if (!_promptFragments.All(fragment => prompt.Contains(fragment, StringComparison.Ordinal)))
            return;

        if (IsHostOffer(prompt)) {
            Svc.Log.Print($"Accepting the host's party teleport offer: {prompt}");
            AddonSelectYesno.Yes();
            return;
        }

        Svc.Log.Print($"Declining party teleport offer while running: {prompt}");
        AddonSelectYesno.No();
    }

    /// <summary>
    /// True when the offer on screen is the one the host just announced. Matched on destination
    /// because that is all the prompt carries; a host offer and another client's are only confusable
    /// when both lead to the same place, which makes accepting the right answer either way.
    /// </summary>
    private bool IsHostOffer(string prompt) {
        if (_module.Multibox.HostTeleportAetheryteId is not { } aetheryteId)
            return false;
        if (!Sheets.Aetheryte.TryGetRow(aetheryteId, out var aetheryte))
            return false;

        // Aetheryte name first; the zone name is a fallback in case a prompt ever names the territory
        // instead. Both identify the host's destination, so neither can widen this to someone else's.
        return Matches(aetheryte.PlaceName.ValueNullable?.Name.ToString())
            || Matches(aetheryte.Territory.ValueNullable?.PlaceName.ValueNullable?.Name.ToString());

        bool Matches(string? name)
            => name is { Length: > 0 } && prompt.Contains(name, StringComparison.Ordinal);
    }

    /// <summary>SelectYesno keeps its prompt in the first AtkValue.</summary>
    private static string? ReadPrompt(AddonSelectYesno* addon) {
        if (addon is null || addon->AtkValues is null || addon->AtkValuesCount == 0)
            return null;

        var value = addon->AtkValues[0];
        if (value.Type is not (AtkValueType.String or AtkValueType.Managed) || !value.String.HasValue)
            return null;

        return MemoryHelper.ReadSeStringNullTerminated((nint)value.String.Value).TextValue;
    }
}
