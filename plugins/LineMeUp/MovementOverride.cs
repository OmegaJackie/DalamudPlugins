using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Dalamud.Game.Config;
using Dalamud.Hooking;
using Dalamud.Utility.Signatures;
using FFXIVClientStructs.FFXIV.Client.Game.Control;

namespace LineMeUp;

[StructLayout(LayoutKind.Explicit, Size = 0x2B0)]
internal unsafe struct CameraEx
{
    /// <summary>0 is north, increases clockwise.</summary>
    [FieldOffset(0x140)] public float DirH;
}

[StructLayout(LayoutKind.Explicit, Size = 0x18)]
internal unsafe struct PlayerMoveControllerFlyInput
{
    [FieldOffset(0x0)] public float Forward;
    [FieldOffset(0x4)] public float Left;
    [FieldOffset(0x8)] public float Up;
    [FieldOffset(0xC)] public float Turn;
    [FieldOffset(0x10)] public float u10;
    [FieldOffset(0x14)] public byte DirMode;
    [FieldOffset(0x15)] public byte HaveBackwardOrStrafe;
}

/// <summary>
/// Feeds movement input into the game as if it came from your keyboard or stick, so the character
/// actually walks: normal speed, normal collision, normal server sync. Nothing about the character's
/// position is written directly.
///
/// This is the standard hook every movement plugin uses (vnavmesh, visland, Lifestream). It intercepts
/// the game reading its own movement input and, only when you aren't pressing anything yourself,
/// substitutes a direction toward <see cref="DesiredPosition"/>.
///
/// The input is a *magnitude*, not a switch — an analog stick pushed halfway walks at half speed. That
/// is what makes fine positioning possible: as the gap closes, the magnitude eases down, so the final
/// step is small enough to land essentially on the mark instead of stomping past it.
/// </summary>
internal sealed unsafe class MovementOverride : IDisposable
{
    // ---- tuning (driven from Configuration) --------------------------------

    /// <summary>Stop feeding input once the flat distance is under this, in yalms.</summary>
    public float Precision = 0.02f;

    /// <summary>Distance at which we start easing off full speed.</summary>
    public float SlowRadius = 1.2f;

    /// <summary>
    /// Slowest crawl, as a fraction of full input. Keep
    /// <c>MinMagnitude * runSpeed / fps</c> below <see cref="Precision"/> or the character will
    /// oscillate around the mark instead of settling on it.
    /// </summary>
    public float MinMagnitude = 0.08f;

    /// <summary>Optional yaw to turn toward using real turn input. Null disables turn injection.</summary>
    public float? DesiredFacing;

    public float FacingPrecision = 0.5f * (MathF.PI / 180f);

    /// <summary>Flip if turn input rotates the wrong way on your setup.</summary>
    public bool InvertTurn;

    // ---- state -------------------------------------------------------------

    /// <summary>False when the signatures didn't resolve; the plugin degrades instead of crashing.</summary>
    public bool Available { get; }

    public string UnavailableReason { get; } = string.Empty;

    public Vector3 DesiredPosition;

    /// <summary>True while the player is pressing movement keys themselves.</summary>
    public bool UserInput { get; private set; }

    private bool enabled;

    public bool Enabled
    {
        get => enabled;
        set
        {
            if (!Available || enabled == value)
            {
                enabled = Available && value;
                return;
            }

            enabled = value;

            if (value)
            {
                rmiWalkHook.Enable();
                rmiFlyHook.Enable();
            }
            else
            {
                UserInput = false;
                rmiWalkHook.Disable();
                rmiFlyHook.Disable();
            }
        }
    }

    // ---- hooks -------------------------------------------------------------

    private delegate bool RMIWalkIsInputEnabled(void* self);

    private delegate void RMIWalkDelegate(
        void* self, float* sumLeft, float* sumForward, float* sumTurnLeft,
        byte* haveBackwardOrStrafe, byte* a6, byte bAdditiveUnk);

    private delegate void RMIFlyDelegate(void* self, PlayerMoveControllerFlyInput* result);

    // Not readonly: SignatureHelper assigns these by reflection during InitializeFromAttributes.
    [Signature("E8 ?? ?? ?? ?? 80 7B 3E 00 48 8D 3D", DetourName = nameof(RMIWalkDetour))]
    private Hook<RMIWalkDelegate> rmiWalkHook = null!;

    [Signature("E8 ?? ?? ?? ?? 0F B6 0D ?? ?? ?? ?? B8", DetourName = nameof(RMIFlyDetour))]
    private Hook<RMIFlyDelegate> rmiFlyHook = null!;

    private readonly RMIWalkIsInputEnabled? isInputEnabled1;
    private readonly RMIWalkIsInputEnabled? isInputEnabled2;

    private bool legacyMode;

    public MovementOverride()
    {
        try
        {
            // ScanText follows the E8/E9 relative call automatically, so these patterns
            // resolve to the function bodies rather than the call sites.
            var addr1 = Svc.SigScanner.ScanText("E8 ?? ?? ?? ?? 84 C0 75 10 38 43 3C");
            var addr2 = Svc.SigScanner.ScanText("E8 ?? ?? ?? ?? 84 C0 75 03 88 47 3F");
            isInputEnabled1 = Marshal.GetDelegateForFunctionPointer<RMIWalkIsInputEnabled>(addr1);
            isInputEnabled2 = Marshal.GetDelegateForFunctionPointer<RMIWalkIsInputEnabled>(addr2);

            Svc.Interop.InitializeFromAttributes(this);

            Svc.GameConfig.UiControlChanged += OnConfigChanged;
            UpdateLegacyMode();

            Available = true;
        }
        catch (Exception ex)
        {
            Available = false;
            UnavailableReason =
                "Movement signatures didn't resolve — the game probably updated. "
              + "Walking is disabled until they're refreshed.";
            Svc.Log.Error(ex, "Line Me Up: failed to set up movement hooks.");
        }
    }

    public void Dispose()
    {
        if (!Available)
            return;

        Svc.GameConfig.UiControlChanged -= OnConfigChanged;
        rmiWalkHook.Dispose();
        rmiFlyHook.Dispose();
    }

    // ---- detours -----------------------------------------------------------

    private void RMIWalkDetour(
        void* self, float* sumLeft, float* sumForward, float* sumTurnLeft,
        byte* haveBackwardOrStrafe, byte* a6, byte bAdditiveUnk)
    {
        rmiWalkHook.Original(self, sumLeft, sumForward, sumTurnLeft, haveBackwardOrStrafe, a6, bAdditiveUnk);

        try
        {
            UserInput = *sumLeft != 0 || *sumForward != 0;

            if (!enabled)
                return;

            // Respect the states where the game itself refuses input (casting, bound, cutscene...).
            var movementAllowed = bAdditiveUnk == 0
                               && isInputEnabled1 is not null && isInputEnabled1(self)
                               && isInputEnabled2 is not null && isInputEnabled2(self);

            if (!movementAllowed)
                return;

            // Never fight the player for the stick.
            if (UserInput)
                return;

            if (ComputeWalkInput() is { } walk)
            {
                *sumLeft = walk.X;
                *sumForward = walk.Y;
            }

            if (*sumTurnLeft == 0 && ComputeTurnInput() is { } turn)
                *sumTurnLeft = turn;
        }
        catch (Exception ex)
        {
            Svc.Log.Error(ex, "Line Me Up: walk detour failed; disabling.");
            enabled = false;
        }
    }

    private void RMIFlyDetour(void* self, PlayerMoveControllerFlyInput* result)
    {
        rmiFlyHook.Original(self, result);

        try
        {
            UserInput = result->Forward != 0 || result->Left != 0 || result->Up != 0;

            if (!enabled || UserInput)
                return;

            if (ComputeWalkInput() is { } walk)
            {
                result->Left = walk.X;
                result->Forward = walk.Y;
            }

            // Vertical: close the altitude gap directly while airborne.
            var me = Svc.Objects.LocalPlayer;
            if (me is not null)
            {
                var dy = DesiredPosition.Y - me.Position.Y;
                if (MathF.Abs(dy) > Precision)
                    result->Up = Math.Clamp(dy, -1f, 1f);
            }
        }
        catch (Exception ex)
        {
            Svc.Log.Error(ex, "Line Me Up: fly detour failed; disabling.");
            enabled = false;
        }
    }

    // ---- input maths -------------------------------------------------------

    /// <summary>
    /// Direction toward the destination, expressed relative to whatever the game treats as "forward"
    /// for input, and scaled down as we close in.
    /// </summary>
    private Vector2? ComputeWalkInput()
    {
        var me = Svc.Objects.LocalPlayer;
        if (me is null)
            return null;

        var delta = DesiredPosition - me.Position;
        var flat = new Vector2(delta.X, delta.Z);
        var dist = flat.Length();

        if (dist <= Precision)
            return null;

        // World heading toward the destination, in the game's yaw convention
        // (forward(r) == (sin r, cos r) in the XZ plane).
        var heading = MathF.Atan2(flat.X, flat.Y);

        // Standard movement is relative to the character; Legacy is relative to the camera.
        var reference = legacyMode
            ? ((CameraEx*)CameraManager.Instance()->GetActiveCamera())->DirH + MathF.PI
            : me.Rotation;

        var rel = heading - reference;

        // Ease off near the target so the final step is smaller than our tolerance.
        var magnitude = Math.Clamp(dist / MathF.Max(SlowRadius, 1e-4f), MinMagnitude, 1f);

        return new Vector2(MathF.Sin(rel), MathF.Cos(rel)) * magnitude;
    }

    /// <summary>Turn input to close the facing gap, when turn-based facing is in use.</summary>
    private float? ComputeTurnInput()
    {
        if (DesiredFacing is not { } want)
            return null;

        var me = Svc.Objects.LocalPlayer;
        if (me is null)
            return null;

        var diff = (float)Math.IEEERemainder(want - me.Rotation, Math.PI * 2d);
        if (MathF.Abs(diff) <= FacingPrecision)
            return null;

        // Rotation increases counter-clockwise (r = 0 faces south, r = pi/2 faces east, which is a
        // left turn from south), so a positive difference is a left turn.
        var turn = Math.Clamp(diff, -1f, 1f);
        return InvertTurn ? -turn : turn;
    }

    // ---- config ------------------------------------------------------------

    private void OnConfigChanged(object? sender, ConfigChangeEvent evt) => UpdateLegacyMode();

    private void UpdateLegacyMode()
    {
        legacyMode = Svc.GameConfig.UiControl.TryGetUInt("MoveMode", out var mode) && mode == 1;
        Svc.Log.Debug($"Line Me Up: legacy movement is {(legacyMode ? "on" : "off")}.");
    }
}
