using System;
using System.Collections.Generic;
using System.Numerics;
using BozjaBuddyReborn.Automation;
using BozjaBuddyReborn.Game;
using BozjaBuddyReborn.Multibox;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using ECommons.DalamudServices;

namespace BozjaBuddyReborn.Windows;

/// <summary>
/// The operator's console: every box in the group on one surface, with the actions you would
/// otherwise have to focus each game window to perform.
///
/// The design rule is that nothing here needs the target box to be running the orchestrator.
/// Applying a loadout or sending a box to the cache is exactly the sort of thing you want to do
/// to a box that is parked, and having to start a farm run first would defeat the purpose.
///
/// Instructions are one-shot and addressed (see <see cref="BoxCommand"/>) - they are deliberately
/// NOT shared state, so nothing here is replayed to a box that reconnects later.
/// </summary>
public sealed class MultiboxerWindow : Window
{
    private static readonly Vector4 Grey = new(0.70f, 0.70f, 0.70f, 1f);
    private static readonly Vector4 Green = new(0.40f, 0.85f, 0.40f, 1f);
    private static readonly Vector4 Yellow = new(0.95f, 0.80f, 0.30f, 1f);
    private static readonly Vector4 Red = new(0.95f, 0.45f, 0.45f, 1f);

    private readonly Configuration _config;
    private readonly MultiboxLink _link;
    private readonly DutyActionSync _sync;
    private readonly BozjaController _controller;
    private readonly LostActionCatalog _catalog;
    private readonly ErrandRunner _errands;
    private readonly SignUpRunner _signUps;

    private int _editing = -1;
    private string _newName = string.Empty;

    /// <summary>
    /// Search text for the three loadout pickers.
    ///
    /// Plain fields rather than a dictionary keyed by picker, because only one loadout's editor is
    /// open at a time - _editing is a single index - so there are only ever these three live. They
    /// are cleared whenever the editor changes, so opening a different loadout does not inherit
    /// the last one's search.
    /// </summary>
    private string _findSlot0 = string.Empty;
    private string _findSlot1 = string.Empty;
    private string _findEssence = string.Empty;

    public MultiboxerWindow(
        Configuration config,
        MultiboxLink link,
        DutyActionSync sync,
        BozjaController controller,
        LostActionCatalog catalog,
        ErrandRunner errands,
        SignUpRunner signUps)
        : base("Bozja Multiboxer###BozjaBuddyRebornMultiboxer")
    {
        _config = config;
        _link = link;
        _sync = sync;
        _controller = controller;
        _catalog = catalog;
        _errands = errands;
        _signUps = signUps;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(560, 300),
            MaximumSize = new Vector2(1100, 1000),
        };
    }

    private void Save() => ConfigSaver.Save(_config);

    /// <summary>Only the host can address the group; a client can still drive itself.</summary>
    private bool CanCommand => _config.MultiboxEnabled && _config.MultiboxIsHost;

    public override void Draw()
    {
        DrawHeader();
        ImGui.Separator();

        if (ImGui.BeginTabBar("###bbrmboxtabs"))
        {
            if (ImGui.BeginTabItem("Boxes"))
            {
                DrawBoxes();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Loadouts"))
            {
                DrawLoadouts();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Errands"))
            {
                DrawErrands();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }
    }

    private void DrawHeader()
    {
        if (!_config.MultiboxEnabled)
        {
            ImGui.TextColored(Yellow, "Multibox is off - this panel drives only this box.");
            ImGui.TextColored(Grey, "Turn it on in the main window to control the whole group.");
            return;
        }

        if (!_config.MultiboxIsHost)
        {
            ImGui.TextColored(Yellow, "This box is a client.");
            ImGui.TextColored(Grey,
                "Only the host can send instructions to the group - tick \"This client is the host\"\n" +
                "on whichever box you actually sit at. Buttons here still drive this box.");
            return;
        }

        var peers = _link.PeerCount;
        ImGui.TextColored(peers > 0 ? Green : Grey,
            peers > 0 ? $"Host - {peers} box{(peers == 1 ? "" : "es")} connected" : "Host - nobody connected yet");
    }

    // -------------------------------------------------------------------- boxes

    private void DrawBoxes()
    {
        if (CanCommand)
        {
            ImGui.TextColored(Grey, "Everything below, for every box at once:");

            // The one the operator reaches for under time pressure - a registration window is
            // short, so it gets its own row and the widest button.
            if (ImGui.Button("Sign up ALL for the engagement", new Vector2(280, 0)))
                _link.SendCommand(new BoxCommand(BoxCommand.All, BoxVerb.SignUp, ""));
            ImGui.SameLine();
            if (ImGui.Button("Sign up this box"))
                _link.SendCommandLocal(new BoxCommand(_sync.SelfName, BoxVerb.SignUp, ""));

            if (_signUps.Status.Length > 0)
                ImGui.TextColored(_signUps.Active ? Yellow : Grey, $"   {_signUps.Status}");

            // The phase and the window's real button labels, because "it did nothing" needs to be
            // separable into "no button" / "wrong label" / "clicked and ignored". Joining is a
            // TWO-step flow - Register, then Commence once the lottery picks you - so a box
            // sitting in AwaitingSelection is working correctly, not stuck.
            if (_signUps.Active)
            {
                ImGui.TextColored(Grey, $"   phase: {_signUps.Phase}");
                ImGui.TextColored(Grey,
                    _signUps.LastButtons.Count == 0
                        ? "   window buttons: none found"
                        : $"   window buttons: {string.Join(", ", _signUps.LastButtons)}");
            }

            ImGui.Spacing();

            if (ImGui.Button("Party support ON, all boxes", new Vector2(280, 0)))
                _link.SendCommand(new BoxCommand(BoxCommand.All, BoxVerb.PartySupport, "1"));
            ImGui.SameLine();
            if (ImGui.Button("OFF, all boxes"))
                _link.SendCommand(new BoxCommand(BoxCommand.All, BoxVerb.PartySupport, "0"));

            ImGui.TextColored(Grey,
                "   Buffs and heals the party from every box at once. It spends farmed charges for as\n" +
                "   long as it runs, which is why the OFF button sits next to the ON one rather than\n" +
                "   in a menu, and why each box also stops itself the moment it runs out.");

            ImGui.Spacing();

            if (ImGui.Button("Start all")) _link.SendCommand(new BoxCommand(BoxCommand.All, BoxVerb.Start, ""));
            ImGui.SameLine();
            if (ImGui.Button("Stop all")) _link.SendCommand(new BoxCommand(BoxCommand.All, BoxVerb.Stop, ""));
            ImGui.SameLine();
            if (ImGui.Button("Cancel errands")) _link.SendCommand(new BoxCommand(BoxCommand.All, BoxVerb.Cancel, ""));
            ImGui.Separator();
        }

        var rows = _sync.Snapshot();
        foreach (var box in rows)
        {
            ImGui.PushID(box.Name);

            ImGui.TextColored(box.IsSelf ? Green : Grey, box.IsSelf ? $"{box.Name} (you)" : box.Name);

            // Slot summary in words - the icons live in the hotbar window; here the useful thing
            // is whether the box is actually loaded for what you are about to do.
            ImGui.SameLine();
            ImGui.TextColored(Grey, $"   {Describe(box.Slot0)}  |  {Describe(box.Slot1)}");

            if (box.IsSelf)
            {
                ImGui.TextColored(Grey, $"   {_controller.Status}");
                if (_errands.Active)
                    ImGui.TextColored(Yellow, $"   Errand: {_errands.Status}");

                var support = _controller.PartySupport;
                if (support.Active || support.Status.Length > 0)
                    ImGui.TextColored(support.Active ? Green : Grey, $"   Party support: {support.Status}");

                if (ImGui.SmallButton(support.Active ? "Stop party support" : "Start party support"))
                    support.Toggle();
            }
            else if (CanCommand)
            {
                if (ImGui.SmallButton("Support on"))
                    _link.SendCommand(new BoxCommand(box.Name, BoxVerb.PartySupport, "1"));
                ImGui.SameLine();
                if (ImGui.SmallButton("Support off"))
                    _link.SendCommand(new BoxCommand(box.Name, BoxVerb.PartySupport, "0"));
                ImGui.SameLine();
                if (ImGui.SmallButton("Start")) _link.SendCommand(new BoxCommand(box.Name, BoxVerb.Start, ""));
                ImGui.SameLine();
                if (ImGui.SmallButton("Stop")) _link.SendCommand(new BoxCommand(box.Name, BoxVerb.Stop, ""));
                ImGui.SameLine();
                if (ImGui.SmallButton("Cancel")) _link.SendCommand(new BoxCommand(box.Name, BoxVerb.Cancel, ""));
                ImGui.SameLine();
                if (ImGui.SmallButton("Sign up")) _link.SendCommand(new BoxCommand(box.Name, BoxVerb.SignUp, ""));
            }

            ImGui.Separator();
            ImGui.PopID();
        }

        if (rows.Count <= 1 && _config.MultiboxEnabled)
        {
            ImGui.TextColored(Grey,
                "Only this box is listed. Peers appear once they connect - see the main window's\n" +
                "Multibox tab if the link is not coming up.");
        }

        if (_controller.LastCommandResult.Length > 0)
        {
            ImGui.Separator();
            ImGui.TextColored(Grey, $"Last instruction here: {_controller.LastCommandResult}");
        }
    }

    private string Describe(DutySlot s)
    {
        if (!s.IsSet)
            return "(empty)";
        var (name, _) = DutyActions.Describe(s.ActionId);
        return s.MaxCharges > 1 ? $"{name} {s.CurCharges}/{s.MaxCharges}" : name;
    }

    // ----------------------------------------------------------------- loadouts

    private void DrawLoadouts()
    {
        ImGui.TextColored(Grey,
            "A loadout is the two Lost Actions to keep in the duty slots, plus optionally the Essence\n" +
            "to be running, plus which box it is for. Applying one sets those slots on that box - it\n" +
            "does NOT buy or transfer anything, so a box that does not hold the action reports that\n" +
            "instead of silently looking configured.");
        ImGui.TextColored(Yellow,
            "The two duty slots only move an icon onto the bar. The Essence is an ITEM: applying it\n" +
            "SPENDS a copy. It is skipped when that Essence's buff is already running, so re-applying\n" +
            "a loadout - or pushing one to a group where some boxes are already buffed - costs\n" +
            "nothing on the boxes that do not need it.");
        ImGui.Separator();

        for (var i = 0; i < _config.Loadouts.Count; i++)
        {
            var lo = _config.Loadouts[i];
            ImGui.PushID(i);

            ImGui.TextColored(Green, lo.Name);
            ImGui.SameLine();
            ImGui.TextColored(Grey, lo.Target.Length == 0
                ? "-> this box"
                : lo.Target == Loadout.AllBoxes ? "-> all boxes" : $"-> {lo.Target}");
            ImGui.TextColored(Grey, $"   1: {_catalog.Name(lo.Slot0)}    2: {_catalog.Name(lo.Slot1)}");
            if (lo.Essence != 0)
                ImGui.TextColored(Grey, $"   Essence: {_catalog.Name(lo.Essence)}{EssenceNote(lo.Essence)}");

            DrawApplyButton(lo);

            ImGui.SameLine();
            if (ImGui.SmallButton(_editing == i ? "Done" : "Edit"))
            {
                _editing = _editing == i ? -1 : i;
                _findSlot0 = _findSlot1 = _findEssence = string.Empty;
            }

            ImGui.SameLine();
            if (ImGui.SmallButton("Delete"))
            {
                _config.Loadouts.RemoveAt(i);
                Save();
                ImGui.PopID();
                break;
            }

            if (_editing == i)
                DrawEditor(lo);

            ImGui.Separator();
            ImGui.PopID();
        }

        ImGui.SetNextItemWidth(200);
        ImGui.InputTextWithHint("###newloadout", "new loadout name", ref _newName, 64);
        ImGui.SameLine();
        if (ImGui.Button("Add") && _newName.Trim().Length > 0)
        {
            _config.Loadouts.Add(new Loadout { Name = _newName.Trim() });
            _newName = string.Empty;
            Save();
        }
    }

    private void DrawEditor(Loadout lo)
    {
        ImGui.Indent();

        DrawTargetPicker(lo);
        ImGui.Spacing();

        // Duty slots take action-type entries only; the Essence picker takes the 36 Essence rows.
        // The two lists are disjoint by construction - see LostActionCatalog - so neither picker
        // can produce a loadout that applies cleanly and then does nothing.
        if (DrawPicker("Duty slot 1", lo.Slot0, _catalog.DutyActions,
                "(leave this slot alone)", ref _findSlot0, out var newSlot0))
        {
            lo.Slot0 = newSlot0;
            Save();
        }

        if (DrawPicker("Duty slot 2", lo.Slot1, _catalog.DutyActions,
                "(leave this slot alone)", ref _findSlot1, out var newSlot1))
        {
            lo.Slot1 = newSlot1;
            Save();
        }

        if (DrawPicker("Essence", lo.Essence, _catalog.Essences,
                "(leave my Essence alone)", ref _findEssence, out var newEssence))
        {
            lo.Essence = newEssence;
            Save();
        }

        if (lo.Essence != 0)
        {
            ImGui.TextColored(Yellow,
                "   Spends a copy when applied, unless that Essence's buff is already running.");
            if (LostActionStatuses.SharesStatusWithDeeper(lo.Essence))
            {
                ImGui.TextColored(Grey,
                    "   A plain and a Deep Essence of the same name share one status effect, so this\n" +
                    "   cannot tell an upgrade from a repeat and will not spend one over the other.");
            }
        }

        ImGui.Unindent();
    }

    /// <summary>
    /// A combo with a search box at the top of it.
    ///
    /// The list is 33 duty actions or 36 Essences long and in row order, which is release order -
    /// so scrolling it to find "Lost Font of Power" is the slow part of building a loadout. The
    /// filter takes keyboard focus the moment the combo opens, making the interaction
    /// click-then-type rather than click-then-aim, and Enter commits the first remaining match,
    /// which is the whole point once two or three characters have narrowed it.
    /// </summary>
    /// <returns>True when the user picked something new, with the row id in <paramref name="picked"/>.</returns>
    private bool DrawPicker(
        string label,
        byte current,
        IEnumerable<LostActionCatalog.Entry> options,
        string clearLabel,
        ref string filter,
        out byte picked)
    {
        picked = current;

        ImGui.SetNextItemWidth(260);
        if (!ImGui.BeginCombo(label, current == 0 ? clearLabel : _catalog.Name(current)))
            return false;

        // Focus the search box on the frame the popup appears, and ONLY that frame - taking focus
        // every frame would leave the list itself unclickable.
        if (ImGui.IsWindowAppearing())
        {
            filter = string.Empty;
            ImGui.SetKeyboardFocusHere();
        }

        ImGui.SetNextItemWidth(-1);
        var enter = ImGui.InputTextWithHint("##find", "search", ref filter, 64,
            ImGuiInputTextFlags.EnterReturnsTrue);

        ImGui.Separator();

        var needle = filter.Trim();
        var changed = false;

        // The clear row is hidden while searching: it matches nothing typed, and leaving it at the
        // top would put it under an Enter meant for the first result.
        if (needle.Length == 0 && ImGui.Selectable(clearLabel, current == 0))
        {
            picked = 0;
            changed = true;
            ImGui.CloseCurrentPopup();
        }

        if (!changed)
        {
            var first = true;
            foreach (var e in options)
            {
                if (needle.Length > 0 &&
                    e.Name.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                // Enter commits the top match only when something was actually typed - on an empty
                // box it would silently pick whatever happens to be first in row order.
                var take = ImGui.Selectable(e.Name, current == e.RowId)
                           || (enter && first && needle.Length > 0);
                first = false;

                if (!take)
                    continue;

                picked = e.RowId;
                changed = true;
                ImGui.CloseCurrentPopup();
                break;
            }
        }

        // Enter with nothing left to match closes rather than leaving a popup that ignores the key.
        if (enter && !changed)
            ImGui.CloseCurrentPopup();

        ImGui.EndCombo();
        return changed && picked != current;
    }

    /// <summary>Live "you already have this" note beside a loadout's Essence, when it is running.</summary>
    private string EssenceNote(byte row)
    {
        if (!_catalog.TryGet(row, out var e) || !e.HasStatus)
            return string.Empty;

        if (!LostActionStatuses.IsActive(e.StatusId, out var remaining))
            return string.Empty;

        return remaining > 0f ? $"  (running, {FormatRemaining(remaining)} left)" : "  (running)";
    }

    private static string FormatRemaining(float seconds)
    {
        var total = (int)seconds;
        return total >= 60 ? $"{total / 60}m{total % 60:00}s" : $"{total}s";
    }

    /// <summary>
    /// One button, labelled with where the loadout actually goes.
    ///
    /// It replaced a fixed "Apply here" / "Apply to all" pair once loadouts learned to name a box.
    /// Two buttons that ignore the loadout's own target would be two ways to do the wrong thing,
    /// and a button that says "Apply" without saying to whom is the one thing an operator driving
    /// four clients cannot afford to guess at.
    /// </summary>
    private void DrawApplyButton(Loadout lo)
    {
        var label = lo.Target.Length == 0
            ? "Apply here"
            : lo.Target == Loadout.AllBoxes ? "Apply to all boxes" : $"Apply to {lo.Target}";

        // A peer is only reachable from the host, and only while it is actually connected. Both
        // failures are silent at the wire - a BoxCommand addressed to nobody is simply ignored by
        // everyone - so they are caught here and named instead.
        var blocked = string.Empty;
        if (lo.Target == Loadout.AllBoxes && !CanCommand)
            blocked = "only the host can address the group";
        else if (lo.TargetsPeer && !CanCommand)
            blocked = "only the host can drive another box";
        else if (lo.TargetsPeer && !IsConnected(lo.Target))
            blocked = $"{lo.Target} is not connected";

        if (blocked.Length > 0)
        {
            ImGui.BeginDisabled();
            ImGui.SmallButton(label);
            ImGui.EndDisabled();
            ImGui.SameLine();
            ImGui.TextColored(Yellow, $"({blocked})");
            return;
        }

        if (!ImGui.SmallButton(label))
            return;

        if (lo.Target.Length == 0)
            ApplyLocally(lo);
        else
            _link.SendCommand(new BoxCommand(lo.Target, BoxVerb.Loadout, lo.Encode()));
    }

    private bool IsConnected(string name)
    {
        foreach (var box in _sync.Snapshot())
            if (string.Equals(box.Name, name, StringComparison.Ordinal))
                return true;
        return false;
    }

    /// <summary>
    /// Pick which box a loadout is for.
    ///
    /// Listed live from the roster rather than typed, so the names can only ever be ones that
    /// exist - and a saved name whose box has since gone offline still shows, marked, instead of
    /// vanishing and silently retargeting the loadout at this box.
    /// </summary>
    private void DrawTargetPicker(Loadout lo)
    {
        var preview = lo.Target.Length == 0
            ? "This box"
            : lo.Target == Loadout.AllBoxes ? "All boxes" : lo.Target;

        ImGui.SetNextItemWidth(260);
        if (!ImGui.BeginCombo("Apply to", preview))
            return;

        if (ImGui.Selectable("This box", lo.Target.Length == 0))
        {
            lo.Target = string.Empty;
            Save();
        }

        if (ImGui.Selectable("All boxes", lo.Target == Loadout.AllBoxes))
        {
            lo.Target = Loadout.AllBoxes;
            Save();
        }

        ImGui.Separator();

        var sawSaved = false;
        foreach (var box in _sync.Snapshot())
        {
            if (box.IsSelf)
                continue;

            if (string.Equals(box.Name, lo.Target, StringComparison.Ordinal))
                sawSaved = true;

            if (ImGui.Selectable(box.Name, lo.Target == box.Name))
            {
                lo.Target = box.Name;
                Save();
            }
        }

        // Keep a saved-but-offline box in the list, marked. Dropping it would leave the combo
        // showing a name it does not offer, and re-picking it would be impossible until that box
        // happened to be up.
        if (lo.TargetsPeer && !sawSaved)
            ImGui.TextColored(Yellow, $"{lo.Target}  (not connected)");

        ImGui.EndCombo();
    }

    private void ApplyLocally(Loadout lo) =>
        _link.SendCommandLocal(new BoxCommand(_sync.SelfName, BoxVerb.Loadout, lo.Encode()));

    // ------------------------------------------------------------------ errands

    private void DrawErrands()
    {
        ImGui.TextColored(Grey,
            "Send a box to the nearest object of a kind and interact with it. The box walks there\n" +
            "with vnavmesh, so it must be able to path to it - errands do not teleport.");

        ImGui.TextColored(Grey,
            "Bozja and Zadnor have no Teleport-style fast travel (no Aetheryte rows, no teleport\n" +
            "coordinates), but a \"Bozjan aetheryte\" is a real interactable object in the world, so\n" +
            "walking to one and using it is exactly what this does.");
        ImGui.Separator();

        foreach (var t in Interactables.Known)
        {
            ImGui.PushID((int)t.DataId);

            ImGui.TextColored(Green, t.Label);
            ImGui.SameLine();
            ImGui.TextColored(Grey, $"- {t.Note}");

            var near = Interactables.Nearest(t.DataId);
            ImGui.TextColored(Grey, near is { } n
                ? $"   nearest to this box: {Movement.DistanceToPlayer(n.Position):F0}y"
                : "   none visible from this box");

            if (ImGui.SmallButton("Send this box"))
                _link.SendCommandLocal(new BoxCommand(_sync.SelfName, BoxVerb.Interact, t.DataId.ToString()));

            if (CanCommand)
            {
                ImGui.SameLine();
                if (ImGui.SmallButton("Send all boxes"))
                    _link.SendCommand(new BoxCommand(BoxCommand.All, BoxVerb.Interact, t.DataId.ToString()));
            }

            ImGui.Separator();
            ImGui.PopID();
        }

        if (_errands.Active)
        {
            ImGui.TextColored(Yellow, $"This box: {_errands.Status}");
            if (ImGui.Button("Cancel this box's errand"))
                _errands.Cancel("Cancelled from the panel.");
        }
        else if (_errands.Status.Length > 0)
        {
            ImGui.TextColored(Grey, $"This box: {_errands.Status}");
        }
    }
}
