using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace AutoFATE.CLib.ImGuiHelpers;

/// <summary>
/// A <see cref="Window"/> that can be minimised via a title bar button. When minimised, only minimal content is shown
/// and the window size is reduced to <see cref="MinimisedSize"/>; when restored, the previous size is applied.
/// </summary>
public abstract class MinimisableWindow : Window {
    private readonly TitleBarButton _minimiseBtn;
    private readonly ImGuiWindowFlags _expandedFlags;
    private Vector2? _savedSize;

    protected bool Minimised { get; private set; }

    /// <summary>Width is overridden by <see cref="MinimisedContentWidth"/> when that is set to a positive value</summary>
    protected virtual Vector2 MinimisedSize => new(400, 80);

    /// <summary>Set from <see cref="DrawContent"/> when minimised to the measured content width; the base uses it for the next frame's window width. Leave 0 to use <see cref="MinimisedSize"/> width.</summary>
    protected float MinimisedContentWidth { get; set; }

    protected MinimisableWindow(string title, ImGuiWindowFlags flags = ImGuiWindowFlags.None) : base(title, flags) {
        _expandedFlags = flags;
        _minimiseBtn = new TitleBarButton {
            Icon = FontAwesomeIcon.Minus,
            IconOffset = new Vector2(1.5f, 1),
            Priority = int.MinValue,
            Click = _ => {
                if (Minimised)
                    Expand();
                else {
                    Minimised = true;
                    _minimiseBtn!.Icon = FontAwesomeIcon.WindowMaximize;
                }
            },
            ShowTooltip = () => {
                using var _ = ImRaii.Tooltip();
                ImGui.Text(Minimised ? ExpandTooltipText : MinimiseTooltipText);
            },
            AvailableClickthrough = true,
        };
        TitleBarButtons.Add(_minimiseBtn);
    }

    protected virtual string MinimiseTooltipText => "Minimal View";
    protected virtual string ExpandTooltipText => "Expanded View";

    /// <summary>Restore the expanded view; no-op when already expanded.</summary>
    protected void Expand() {
        if (!Minimised)
            return;
        Minimised = false;
        _minimiseBtn.Icon = FontAwesomeIcon.Minus;
        if (_savedSize is { } saved) {
            Size = saved;
            SizeCondition = ImGuiCond.Always;
        }
    }

    protected abstract void DrawContent(bool minimised);

    public override void Draw() {
        // Window.Size is in unscaled units — Dalamud multiplies it by GlobalScale before applying.
        // Anything measured in actual pixels (GetWindowSize, the measured content width) has that
        // scale baked in already and must be divided out, or every assignment inflates the window
        // by the UI scale again (compounding on each minimise/restore cycle).
        if (Minimised) {
            var w = MinimisedContentWidth > 0 ? MinimisedContentWidth / Dalamud.Interface.Utility.ImGuiHelpers.GlobalScale : MinimisedSize.X;
            Size = new Vector2(w, MinimisedSize.Y);
            SizeCondition = ImGuiCond.Always;
            Flags = _expandedFlags | ImGuiWindowFlags.NoResize;
            DrawContent(minimised: true);
            return;
        }

        Flags = _expandedFlags;
        _savedSize = ImGui.GetWindowSize() / Dalamud.Interface.Utility.ImGuiHelpers.GlobalScale;
        SizeCondition = ImGuiCond.FirstUseEver;
        DrawContent(minimised: false);
    }
}
