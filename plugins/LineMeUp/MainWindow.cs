using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Interface.Windowing;

namespace LineMeUp;

internal sealed class MainWindow : Window, IDisposable
{
    private static readonly Vector4 Good = new(0.45f, 0.85f, 0.45f, 1f);
    private static readonly Vector4 Warn = new(0.95f, 0.75f, 0.30f, 1f);
    private static readonly Vector4 Bad = new(0.90f, 0.40f, 0.40f, 1f);
    private static readonly Vector4 Dim = new(0.65f, 0.65f, 0.65f, 1f);

    private readonly Configuration cfg;
    private readonly Aligner aligner;
    private readonly VirtualKey[] keyChoices;

    public MainWindow(Configuration configuration, Aligner alignerInstance)
        : base("Line Me Up###LineMeUpMain")
    {
        cfg = configuration;
        aligner = alignerInstance;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(420, 380),
            MaximumSize = new Vector2(900, 1200),
        };

        keyChoices = new[] { VirtualKey.NO_KEY }
            .Concat(Svc.Keys.GetValidVirtualKeys().Where(k => k != VirtualKey.NO_KEY))
            .ToArray();
    }

    public void Dispose() { }

    public override void Draw()
    {
        if (!aligner.CanWalk)
        {
            ImGui.TextColored(Bad, "Walking unavailable");
            ImGui.TextWrapped(aligner.WalkUnavailableReason);
            ImGui.Separator();
        }

        DrawActions();
        ImGui.Separator();
        DrawLiveReadout();
        ImGui.Separator();
        DrawWhatToMatch();
        ImGui.Separator();
        DrawOffsets();
        ImGui.Separator();
        DrawWalking();
        ImGui.Separator();
        DrawFacing();
        ImGui.Separator();
        DrawHotkeys();
        ImGui.Separator();
        DrawMisc();
    }

    // ---------------------------------------------------------------- actions

    private void DrawActions()
    {
        var buttonWidth = (ImGui.GetContentRegionAvail().X - (ImGui.GetStyle().ItemSpacing.X * 2)) / 3f;

        if (ImGui.Button("Walk there", new Vector2(buttonWidth, 0)))
            aligner.Align();
        Tooltip("Walk to the target's spot, then match their facing.");

        ImGui.SameLine();

        if (ImGui.Button(aligner.Following ? "Stop following" : "Follow", new Vector2(buttonWidth, 0)))
            aligner.ToggleFollow();
        Tooltip("Stay on their mark as they move, until you turn this off.");

        ImGui.SameLine();

        if (ImGui.Button("Stop", new Vector2(buttonWidth, 0)))
            aligner.StopAll();
        Tooltip("Stop right now and hand control back to you.");

        if (aligner.Following)
            ImGui.TextColored(Good, $"Following: {aligner.LockedName}");
        else if (aligner.Walking)
            ImGui.TextColored(Good, $"Walking to {aligner.LockedName}...");
        else
            ImGui.TextColored(Dim, "Idle.");
    }

    // ----------------------------------------------------------- live readout

    private void DrawLiveReadout()
    {
        ImGui.TextUnformatted("Live");

        var me = Svc.Objects.LocalPlayer;
        var target = aligner.GetTarget();

        if (me is null)
        {
            ImGui.TextColored(Warn, "Not logged in.");
            return;
        }

        if (target is null)
        {
            ImGui.TextColored(Dim, $"No valid {Aligner.SlotName(cfg.Slot)}.");
            return;
        }

        var wantPos = aligner.ResolvePosition(target);
        var wantRot = aligner.ResolveRotation(target);
        var flat = Aligner.FlatDistance(me.Position, wantPos);
        var vertical = wantPos.Y - me.Position.Y;
        var rotDelta = Aligner.WrapPi(wantRot - me.Rotation) * (180f / MathF.PI);

        ImGui.TextUnformatted($"Target: {target.Name.TextValue}");

        if (ImGui.BeginTable("##readout", 4, ImGuiTableFlags.SizingStretchProp))
        {
            Row("Their spot", wantPos, wantRot);
            Row("You", me.Position, me.Rotation);
            ImGui.EndTable();
        }

        ImGui.TextColored(
            flat <= cfg.ArrivalTolerance ? Good : Dim,
            $"Gap: {flat * 100f:F1}cm horizontal, {vertical * 100f:+0.0;-0.0;0.0}cm vertical, {rotDelta:+0.0;-0.0;0.0}° facing");

        if (MathF.Abs(vertical) > 0.5f)
            ImGui.TextColored(Warn, "They're on a different level — walking can't close a vertical gap.");

        static void Row(string label, Vector3 p, float r)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(label);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted($"X {p.X:F3}");
            ImGui.TableNextColumn();
            ImGui.TextUnformatted($"Y {p.Y:F3}");
            ImGui.TableNextColumn();
            ImGui.TextUnformatted($"Z {p.Z:F3}  |  {r * (180f / MathF.PI):F1}°");
        }
    }

    // ------------------------------------------------------------ what to match

    private void DrawWhatToMatch()
    {
        ImGui.TextUnformatted("What to match");

        if (ImGui.Checkbox("Position", ref cfg.MatchPosition))
            cfg.Save();
        ImGui.SameLine();
        if (ImGui.Checkbox("Facing", ref cfg.MatchRotation))
            cfg.Save();

        ImGui.SetNextItemWidth(200);
        if (EnumCombo("Read from", ref cfg.Slot))
            cfg.Save();
        Tooltip("Which of the game's target slots to copy from.");

        if (ImGui.Checkbox("Players only", ref cfg.PlayersOnly))
            cfg.Save();
        Tooltip("Ignore NPCs, minions, mounts and world objects.");
    }

    // ---------------------------------------------------------------- offsets

    private void DrawOffsets()
    {
        ImGui.TextUnformatted("Offset");
        ImGui.TextColored(Dim, "All zero = stand exactly where they stand.");

        var changed = false;
        ImGui.SetNextItemWidth(180);
        changed |= ImGui.DragFloat("Forward (yalms)", ref cfg.OffsetForward, 0.01f, -50f, 50f, "%.3f");
        Tooltip("Positive puts you in front of the target, along the way they face.");

        ImGui.SetNextItemWidth(180);
        changed |= ImGui.DragFloat("Right (yalms)", ref cfg.OffsetRight, 0.01f, -50f, 50f, "%.3f");
        Tooltip("Positive puts you to the target's right.");

        ImGui.SetNextItemWidth(180);
        changed |= ImGui.DragFloat("Up (yalms)", ref cfg.OffsetUp, 0.01f, -50f, 50f, "%.3f");
        Tooltip("Only meaningful while flying — walking can't change your altitude.");

        ImGui.SetNextItemWidth(180);
        changed |= ImGui.DragFloat("Facing offset (deg)", ref cfg.RotationOffsetDegrees, 0.5f, -180f, 180f, "%.1f");
        Tooltip("Added on top of their facing. 180 makes you face them instead of with them.");

        if (ImGui.Button("Reset offsets"))
        {
            cfg.OffsetForward = 0f;
            cfg.OffsetRight = 0f;
            cfg.OffsetUp = 0f;
            cfg.RotationOffsetDegrees = 0f;
            changed = true;
        }

        ImGui.SameLine();
        if (ImGui.Button("Face them"))
        {
            cfg.RotationOffsetDegrees = 180f;
            changed = true;
        }

        if (changed)
            cfg.Save();
    }

    // ---------------------------------------------------------------- walking

    private void DrawWalking()
    {
        ImGui.TextUnformatted("Walking");
        ImGui.TextColored(Dim, "Your character walks there under its own power, at its own speed.");

        var changed = false;

        ImGui.SetNextItemWidth(180);
        changed |= ImGui.DragFloat("Arrival tolerance (y)", ref cfg.ArrivalTolerance, 0.005f, 0.005f, 1f, "%.3f");
        Tooltip("How close counts as arrived. 0.05 is about 5cm.");

        ImGui.SetNextItemWidth(180);
        changed |= ImGui.DragFloat("Slow-down radius (y)", ref cfg.SlowRadius, 0.05f, 0.1f, 10f, "%.2f");
        Tooltip("Distance at which the character eases off full speed for the final approach.");

        ImGui.SetNextItemWidth(180);
        changed |= ImGui.DragFloat("Minimum crawl", ref cfg.MinMagnitude, 0.01f, 0.01f, 1f, "%.2f");
        Tooltip("The slowest the final approach gets, as a fraction of a fully pushed stick. "
              + "Lower lands more precisely but takes longer to settle. If your character jitters "
              + "on the spot instead of stopping, lower this or raise the arrival tolerance.");

        ImGui.SetNextItemWidth(180);
        changed |= ImGui.DragFloat("Give up after (sec)", ref cfg.WalkTimeoutSecs, 0.5f, 1f, 60f, "%.0f");

        ImGui.SetNextItemWidth(180);
        changed |= ImGui.DragFloat("Max distance (y)", ref cfg.MaxWalkDistance, 1f, 1f, 200f, "%.0f");
        Tooltip("Refuse to start beyond this. There's no pathfinding — the walk is a straight line.");

        changed |= ImGui.Checkbox("Give up when stuck", ref cfg.AbortWhenStuck);
        Tooltip("Stop if the character stops making progress, rather than grinding against a wall.");

        changed |= ImGui.Checkbox("Movement keys stop following", ref cfg.FollowBreaksOnInput);

        ImGui.SetNextItemWidth(180);
        changed |= ImGui.DragFloat("Follow grace (sec)", ref cfg.FollowGraceSecs, 0.1f, 0f, 30f, "%.1f");
        Tooltip("How long to keep a follow alive while the target is out of range or loading.");

        if (changed)
            cfg.Save();
    }

    // ----------------------------------------------------------------- facing

    private void DrawFacing()
    {
        ImGui.TextUnformatted("Facing");

        ImGui.SetNextItemWidth(200);
        if (EnumCombo("Mode", ref cfg.Rotation))
            cfg.Save();

        ImGui.TextColored(
            Dim,
            cfg.Rotation == RotationMode.Instant
                ? "Instant: turns you to their exact facing the moment you arrive."
                : "Turn: rotates you into place the way holding a turn key would. Slower, and it only\n"
                + "settles within about a degree.");

        if (cfg.Rotation == RotationMode.Turn)
        {
            if (ImGui.Checkbox("Invert turn direction", ref cfg.InvertTurn))
                cfg.Save();
            Tooltip("Tick this if the character turns away from the target instead of toward it.");
        }
    }

    // ---------------------------------------------------------------- hotkeys

    private void DrawHotkeys()
    {
        ImGui.TextUnformatted("Hotkeys");

        if (DrawHotkeyRow("Walk", ref cfg.WalkKey, ref cfg.WalkCtrl, ref cfg.WalkAlt, ref cfg.WalkShift))
            cfg.Save();

        if (DrawHotkeyRow("Follow", ref cfg.FollowKey, ref cfg.FollowCtrl, ref cfg.FollowAlt, ref cfg.FollowShift))
            cfg.Save();

        ImGui.TextColored(Dim, "Pressing the walk key again while moving cancels it.");
    }

    private bool DrawHotkeyRow(string label, ref VirtualKey key, ref bool ctrl, ref bool alt, ref bool shift)
    {
        var changed = false;
        ImGui.PushID(label);

        ImGui.TextUnformatted(label);
        ImGui.SameLine(70);

        changed |= ImGui.Checkbox("Ctrl", ref ctrl);
        ImGui.SameLine();
        changed |= ImGui.Checkbox("Alt", ref alt);
        ImGui.SameLine();
        changed |= ImGui.Checkbox("Shift", ref shift);
        ImGui.SameLine();

        ImGui.SetNextItemWidth(140);
        if (ImGui.BeginCombo("##key", KeyName(key)))
        {
            foreach (var choice in keyChoices)
            {
                if (ImGui.Selectable(KeyName(choice), choice == key))
                {
                    key = choice;
                    changed = true;
                }
            }

            ImGui.EndCombo();
        }

        ImGui.PopID();
        return changed;
    }

    private static string KeyName(VirtualKey key) => key == VirtualKey.NO_KEY ? "(none)" : key.ToString();

    // ------------------------------------------------------------------- misc

    private void DrawMisc()
    {
        if (ImGui.Checkbox("Print to chat", ref cfg.PrintToChat))
            cfg.Save();

        ImGui.TextColored(Dim, "Commands: /lineup, /lineup pos, /lineup rot, /lineup follow, /lineup stop");
    }

    // ------------------------------------------------------------------ utils

    private static void Tooltip(string text)
    {
        if (!ImGui.IsItemHovered())
            return;

        ImGui.BeginTooltip();
        ImGui.PushTextWrapPos(ImGui.GetFontSize() * 24f);
        ImGui.TextUnformatted(text);
        ImGui.PopTextWrapPos();
        ImGui.EndTooltip();
    }

    private static bool EnumCombo<T>(string label, ref T value) where T : struct, Enum
    {
        var changed = false;
        if (ImGui.BeginCombo(label, value.ToString()))
        {
            foreach (var option in Enum.GetValues<T>())
            {
                if (ImGui.Selectable(option.ToString(), option.Equals(value)))
                {
                    value = option;
                    changed = true;
                }
            }

            ImGui.EndCombo();
        }

        return changed;
    }
}
