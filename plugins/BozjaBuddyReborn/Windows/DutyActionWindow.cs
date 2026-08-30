using System;
using System.Numerics;
using BozjaBuddyReborn.Game;
using BozjaBuddyReborn.Multibox;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Windowing;
using ECommons.DalamudServices;

namespace BozjaBuddyReborn.Windows;

/// <summary>
/// A duty-action hotbar for the whole group.
///
/// One row per box, two slots each - the same two Duty Action slots the game gives you - showing
/// the icon, the charges and the recharge exactly as your own hotbar shows them, AND pressable.
/// The point is that a multibox operator can see at a glance which box still has an Essence or a
/// Lost Action up without cycling windows, and can then fire it without focusing that box's game
/// window - which is the same job the Phantom Job bar does in Occult Crescent.
///
/// Your own row is read live from DutyActionManager every frame; peer rows arrive over the
/// multibox pipe twice a second and have their cooldowns aged forward locally in between, so
/// every sweep moves smoothly rather than stepping.
///
/// NOTHING IS PRESSED FROM HERE. An ImGui click happens inside the draw callback, which is not the
/// framework thread and may not touch game memory - so a click only ever ENQUEUES a BoxCommand,
/// exactly the one a remote instruction would be, and the controller's per-frame pump carries it
/// out on a legal thread. Your own slot and a peer's slot therefore take one code path and cannot
/// drift in behaviour. See <see cref="DutyActions.Press"/> for what happens at the far end, and
/// <see cref="BoxVerb.DutyAction"/> for why the instruction names the action and not just the slot.
/// </summary>
public sealed class DutyActionWindow : Window
{
    private static readonly Vector4 Grey = new(0.70f, 0.70f, 0.70f, 1f);
    private static readonly Vector4 Green = new(0.40f, 0.85f, 0.40f, 1f);
    private static readonly Vector4 Yellow = new(0.95f, 0.80f, 0.30f, 1f);
    private static readonly Vector4 Dim = new(1f, 1f, 1f, 0.35f);

    /// <summary>Border of a slot the mouse is over and that would actually fire.</summary>
    private static readonly Vector4 Hot = new(1f, 0.92f, 0.55f, 1f);

    /// <summary>
    /// Cooldown wash drawn over a slot that is recharging.
    ///
    /// Deliberately a plain colour rather than a cached ImGui.GetColorU32 result: a static
    /// initialiser would run during the plugin constructor, which Dalamud invokes on a
    /// threadpool thread with no ImGui frame in scope. Converting inside Draw costs nothing and
    /// cannot take the plugin down on load, which is a trade this project has already paid for
    /// once.
    /// </summary>
    private static readonly Vector4 CooldownWash = new(0f, 0f, 0f, 0.62f);

    private const float IconSize = 44f;

    /// <summary>How long the outcome of a press stays on screen.</summary>
    private const long PressNoticeMs = 6000;

    private readonly Configuration _config;
    private readonly DutyActionSync _sync;
    private readonly MultiboxLink _link;

    /// <summary>The last instruction this box SENT to a peer, which is all it can know about it.</summary>
    private string _sent = string.Empty;
    private long _sentMs;

    public DutyActionWindow(Configuration config, DutyActionSync sync, MultiboxLink link)
        : base("Duty Actions###BozjaBuddyRebornDutyActions")
    {
        _config = config;
        _sync = sync;
        _link = link;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(300, 120),
            MaximumSize = new Vector2(900, 900),
        };
    }

    /// <summary>
    /// Whether a click on a peer's slot has anywhere to go.
    ///
    /// The same rule the multiboxer panel already states in words: only the host may instruct the
    /// group, a client may still drive itself. So a client's peer rows are drawn but not
    /// pressable, and the tooltip says which box to tick as host rather than leaving a button that
    /// looks live and does nothing.
    /// </summary>
    private bool CanCommandPeers => _config.MultiboxEnabled && _config.MultiboxIsHost;

    /// <summary>
    /// Apply the transparency setting before the window is begun.
    ///
    /// It has to happen here rather than in Draw: BgAlpha and Flags are read by the window system
    /// on the way INTO ImGui.Begin, so a value set inside Draw would land a frame late and flicker
    /// every time the setting changed. NoBackground suppresses the panel and its border; the title
    /// bar is left alone on purpose, since with the background gone it is the only handle left.
    /// </summary>
    public override void PreDraw()
    {
        if (_config.DutyActionTransparent)
        {
            Flags |= ImGuiWindowFlags.NoBackground;
            BgAlpha = 0f;
        }
        else
        {
            Flags &= ~ImGuiWindowFlags.NoBackground;
            BgAlpha = null;
        }
    }

    public override void Draw()
    {
        var rows = _sync.Snapshot();

        if (!_sync.Available)
        {
            ImGui.TextColored(Yellow, "No duty actions right now.");
            ImGui.TextColored(Grey,
                "The two Duty Action slots only exist inside a field operation. Your own row fills\n" +
                "in as soon as you are in Bozja or Zadnor with actions loaded.");

            // Name the source that answered and what it saw. An empty bar with actions plainly
            // on screen is a read problem, not a game state, and this is what makes the
            // difference visible rather than guessable.
            ImGui.TextColored(Grey, $"Read: {_sync.Diagnostic}");
            ImGui.Separator();
        }

        if (_config.MultiboxEnabled && !_link.Connected && rows.Count == 1)
        {
            ImGui.TextColored(Grey,
                _link.IsHost
                    ? "No other box has connected yet - showing your own slots only."
                    : "Link down - showing your own slots only.");
            ImGui.Separator();
        }
        else if (!_config.MultiboxEnabled)
        {
            ImGui.TextColored(Grey, "Multibox is off - showing your own slots only.");
            ImGui.Separator();
        }

        foreach (var row in rows)
            DrawRow(row);

        DrawPressNotice();
    }

    /// <summary>
    /// Echo the last thing a click here caused.
    ///
    /// A refused press - the slot recharged between the frame you looked and the frame you
    /// clicked, the box swapped its loadout, the character is dead - is otherwise completely
    /// silent, and a silent hotbar is indistinguishable from a broken one. The refusal is the
    /// message worth having, so it gets the space; it fades so the window does not accumulate
    /// history.
    ///
    /// A press aimed at a PEER can only ever be reported as SENT. Whether it fired is decided on
    /// that box, a pipe away, and comes back on its own screen and in its multiboxer panel - so
    /// claiming anything stronger here would be this window inventing an outcome it does not have.
    /// The two notices share one line and the more recent one wins.
    /// </summary>
    private void DrawPressNotice()
    {
        var now = Environment.TickCount64;

        var local = DutyActions.LastPress.Length > 0 && now - DutyActions.LastPressMs <= PressNoticeMs;
        var sent = _sent.Length > 0 && now - _sentMs <= PressNoticeMs;

        if (!local && !sent)
            return;

        var message = !local ? _sent
            : !sent ? DutyActions.LastPress
            : _sentMs > DutyActions.LastPressMs ? _sent : DutyActions.LastPress;

        ImGui.Separator();
        ImGui.TextColored(Grey, message);
    }

    private void DrawRow(PeerDuty peer)
    {
        using var _ = ImRaiiId(peer.Name);

        ImGui.TextColored(peer.IsSelf ? Green : Grey, peer.IsSelf ? $"{peer.Name} (you)" : peer.Name);

        for (var i = 0; i < DutyActions.SlotCount; i++)
        {
            if (i > 0)
                ImGui.SameLine();
            DrawSlot(peer, i);
        }

        ImGui.Spacing();
    }

    private void DrawSlot(PeerDuty peer, int index)
    {
        var slot = peer.Slot(index);
        var start = ImGui.GetCursorScreenPos();
        var size = new Vector2(IconSize, IconSize);

        if (!slot.IsSet)
        {
            // An empty slot still occupies its place, so rows stay aligned and it reads as
            // "nothing loaded" rather than as a missing widget. Left as a Dummy rather than made
            // into a button like the loaded slots below: there is nothing to press, and an id-less
            // item is one ImGui will let you drag the window by - which is worth keeping when the
            // rest of the bar deliberately will not.
            ImGui.Dummy(size);
            var dl0 = ImGui.GetWindowDrawList();
            dl0.AddRect(start, start + size, ImGui.GetColorU32(Dim), 4f);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip($"Duty Action {index + 1}: empty");
            return;
        }

        var (name, iconId) = DutyActions.Describe(slot.ActionId);
        var (pressable, refusal) = CanPress(peer, slot);

        // THE SLOT IS A REAL BUTTON WITH THE ICON PAINTED INTO IT, and that is not decoration.
        // ImGui.Image and ImGui.Dummy submit their item with id ZERO, which never claims
        // g.HoveredId - and a frame in which the mouse is clicked while ActiveId and HoveredId are
        // both zero is precisely what ImGui reads as "clicked the window background", so it starts
        // moving the window. Testing IsItemHovered plus IsMouseClicked over an Image therefore
        // fires the action AND begins a drag on the same press, and the hotbar walks across the
        // screen over a session of pressing it. An InvisibleButton takes the id, so the drag never
        // starts; it reserves exactly the same rect the Image did, so the layout is unchanged; and
        // it presses on release, so sliding off a slot still cancels the way a button should.
        var clicked = ImGui.InvisibleButton($"slot{index}", size);
        var hovered = ImGui.IsItemHovered();

        if (clicked && pressable)
            Press(peer, index, slot);

        var dl = ImGui.GetWindowDrawList();

        if (iconId != 0)
        {
            try
            {
                var tex = Svc.Texture.GetFromGameIcon(new GameIconLookup(iconId)).GetWrapOrDefault();
                if (tex != null)
                {
                    // Dim the whole icon when nothing is pressable, so "has charges" is readable
                    // from across the screen without reading the number.
                    dl.AddImage(tex.Handle, start, start + size, Vector2.Zero, Vector2.One,
                        ImGui.GetColorU32(slot.Ready ? Vector4.One : Dim));
                }
            }
            catch { /* the box, the charges and the tooltip still say what is loaded */ }
        }

        // Cooldown wash: a bottom-up fill, the way the game draws a recharging action.
        if (slot.CooldownRemaining > 0f)
        {
            var fill = 1f - slot.ChargeProgress;
            var top = start + new Vector2(0, size.Y * (1f - fill));
            dl.AddRectFilled(top, start + size, ImGui.GetColorU32(CooldownWash));

            var secs = slot.CooldownRemaining >= 10f
                ? $"{slot.CooldownRemaining:F0}"
                : $"{slot.CooldownRemaining:F1}";
            var textSize = ImGui.CalcTextSize(secs);
            dl.AddText(start + (size - textSize) * 0.5f, ImGui.GetColorU32(Vector4.One), secs);
        }

        // Charges, bottom-right, only when the action actually has more than one.
        if (slot.MaxCharges > 1)
        {
            var label = $"{slot.CurCharges}";
            var ts = ImGui.CalcTextSize(label);
            var at = start + size - ts - new Vector2(3, 2);
            dl.AddText(at + Vector2.One, ImGui.GetColorU32(new Vector4(0, 0, 0, 0.9f)), label);
            dl.AddText(at, ImGui.GetColorU32(slot.Ready ? Vector4.One : Dim), label);
        }

        // The border carries two things at once: green for "a charge is up", and a bright ring
        // while the mouse is over a slot that would genuinely fire. A slot this box may not press
        // never lights up, so the affordance cannot lie about what a click will do.
        var live = hovered && pressable;
        var border = live ? Hot : slot.Ready ? Green : Dim;
        dl.AddRect(start, start + size, ImGui.GetColorU32(border), 4f, ImDrawFlags.None, live ? 2f : 1f);

        if (hovered)
        {
            ImGui.SetTooltip(
                $"Duty Action {index + 1}: {name}\n" +
                (slot.MaxCharges > 1 ? $"Charges: {slot.CurCharges}/{slot.MaxCharges}\n" : "") +
                (slot.CooldownRemaining > 0f
                    ? $"Next charge in {slot.CooldownRemaining:F1}s\n"
                    : "Ready\n") +
                (pressable
                    ? peer.IsSelf ? "Click to use it." : $"Click to have {peer.Name} use it."
                    : refusal));
        }
    }

    /// <summary>
    /// Whether clicking this slot would go anywhere, and - when it would not - the one line that
    /// says why, which goes straight into the tooltip.
    ///
    /// CHARGES ARE DELIBERATELY NOT PART OF THIS. A slot showing a cooldown is still worth
    /// clicking at: the refusal comes back from the real read on the framework thread a frame
    /// later and is accurate, whereas refusing here would mean this window adjudicating a peer's
    /// row that is up to half a second old and locally aged. The window's job is to deliver the
    /// intent; deciding whether it can be honoured belongs to the box that will honour it.
    /// </summary>
    private (bool Pressable, string Why) CanPress(PeerDuty peer, DutySlot slot)
    {
        if (!slot.IsSet)
            return (false, "Nothing loaded in this slot.");

        if (!_config.DutyActionClickToUse)
            return (false, "Click-to-use is off - turn it back on under Settings, Lost Actions.");

        if (peer.IsSelf)
            return (true, string.Empty);

        if (!_config.MultiboxEnabled)
            return (false, "Multibox is off - only your own slots can be pressed.");

        if (!CanCommandPeers)
            return (false, "This box is a client. Only the host can press another box's slot.");

        return (true, string.Empty);
    }

    /// <summary>
    /// Hand the press to the instruction queue.
    ///
    /// The action id travels with the slot index so the far end can refuse a press aimed at an
    /// action that is no longer there - see <see cref="DutyActions.Press"/>. Locally that is a
    /// formality; for a peer, whose row was drawn from a roster up to half a second old, it is the
    /// difference between firing the Essence the operator chose and firing whatever replaced it.
    /// </summary>
    private void Press(PeerDuty peer, int index, DutySlot slot)
    {
        var arg = BoxCommand.EncodeDutyAction(index, slot.ActionId);

        if (peer.IsSelf)
        {
            _link.SendCommandLocal(new BoxCommand(_sync.SelfName, BoxVerb.DutyAction, arg));
            return;
        }

        _link.SendCommand(new BoxCommand(peer.Name, BoxVerb.DutyAction, arg));
        _sent = $"Told {peer.Name} to use {DutyActions.Describe(slot.ActionId).Name}.";
        _sentMs = Environment.TickCount64;
    }

    /// <summary>ImGui id scope without taking a dependency on ImRaii's exact shape.</summary>
    private static IdScope ImRaiiId(string id)
    {
        ImGui.PushID(id);
        return new IdScope();
    }

    private readonly struct IdScope : System.IDisposable
    {
        public void Dispose() => ImGui.PopID();
    }
}
