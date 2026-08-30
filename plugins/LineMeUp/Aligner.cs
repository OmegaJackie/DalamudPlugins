using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using CSGameObject = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;

namespace LineMeUp;

/// <summary>
/// Orchestrates the align job: walk the character to the target's spot using real movement input,
/// then match their facing.
///
/// Nothing here writes the character's position. Position is reached only by feeding movement input
/// through <see cref="MovementOverride"/>, so the character walks there under its own power, at its
/// own speed, colliding with what it would normally collide with.
///
/// Coordinate conventions (FFXIV):
///   +X east, +Y up, +Z south. Rotation is yaw in radians on (-pi, pi], 0 == facing south (+Z).
///   forward(r) = (sin r, 0, cos r);  right(r) = (-cos r, 0, sin r)
/// Sanity check: r = 0 faces south, so "right" is west = (-1, 0, 0). The formula agrees.
/// </summary>
internal sealed unsafe class Aligner : IDisposable
{
    private static readonly VirtualKey[] MovementKeys =
    [
        VirtualKey.W, VirtualKey.A, VirtualKey.S, VirtualKey.D,
        VirtualKey.UP, VirtualKey.DOWN, VirtualKey.LEFT, VirtualKey.RIGHT,
        VirtualKey.SPACE,
    ];

    private const float StuckDistance = 0.02f;
    private const long StuckWindowMs = 2000;

    private readonly Configuration cfg;
    private readonly MovementOverride movement;
    private readonly HashSet<VirtualKey> validKeys;

    // job state
    private ulong lockedId;
    private string lockedName = string.Empty;
    private long deadlineMs;
    private long lostSinceMs;

    // What this particular job matches. Defaults come from config, but the /lineup pos and
    // /lineup rot subcommands override them for a single run.
    private bool jobPosition = true;
    private bool jobRotation = true;

    // stuck detection
    private Vector3 lastPosition;
    private long lastProgressMs;

    // hotkey edge detection
    private bool walkKeyWasDown;
    private bool followKeyWasDown;

    public Aligner(Configuration configuration, MovementOverride movementOverride)
    {
        cfg = configuration;
        movement = movementOverride;
        validKeys = Svc.Keys.GetValidVirtualKeys().ToHashSet();
        Svc.Framework.Update += OnUpdate;
    }

    public void Dispose()
    {
        Svc.Framework.Update -= OnUpdate;
        movement.Enabled = false;
    }

    /// <summary>A one-shot walk is in progress.</summary>
    public bool Walking { get; private set; }

    /// <summary>Continuously matching a target.</summary>
    public bool Following { get; private set; }

    public bool Busy => Walking || Following;

    public string LockedName => lockedName;

    public bool CanWalk => movement.Available;

    public string WalkUnavailableReason => movement.UnavailableReason;

    // ---------------------------------------------------------------- targets

    /// <summary>Reads the configured target slot. Returns null if empty or filtered out.</summary>
    public IGameObject? GetTarget()
    {
        var t = cfg.Slot switch
        {
            TargetSlot.Target => Svc.Targets.Target,
            TargetSlot.SoftTarget => Svc.Targets.SoftTarget,
            TargetSlot.FocusTarget => Svc.Targets.FocusTarget,
            TargetSlot.MouseOver => Svc.Targets.MouseOverTarget,
            TargetSlot.GPose => Svc.Targets.GPoseTarget,
            _ => Svc.Targets.Target,
        };

        if (t is null || !t.IsValid())
            return null;

        if (cfg.PlayersOnly && t.ObjectKind != ObjectKind.Pc)
            return null;

        return t;
    }

    // ------------------------------------------------------------------- math

    public static Vector3 Forward(float r) => new(MathF.Sin(r), 0f, MathF.Cos(r));

    public static Vector3 Right(float r) => new(-MathF.Cos(r), 0f, MathF.Sin(r));

    /// <summary>Normalises a yaw into (-pi, pi], matching the game's own range.</summary>
    public static float WrapPi(float r) => (float)Math.IEEERemainder(r, Math.PI * 2d);

    /// <summary>Horizontal distance only — the vertical gap isn't something walking can close.</summary>
    public static float FlatDistance(Vector3 a, Vector3 b)
        => new Vector2(a.X - b.X, a.Z - b.Z).Length();

    /// <summary>The world position we're walking to. With zero offsets this is the target's own spot.</summary>
    public Vector3 ResolvePosition(IGameObject target)
    {
        var p = target.Position;

        if (cfg.OffsetForward == 0f && cfg.OffsetRight == 0f && cfg.OffsetUp == 0f)
            return p;

        var r = target.Rotation;
        return p
             + (Forward(r) * cfg.OffsetForward)
             + (Right(r) * cfg.OffsetRight)
             + (Vector3.UnitY * cfg.OffsetUp);
    }

    /// <summary>The exact yaw we want to face.</summary>
    public float ResolveRotation(IGameObject target)
    {
        if (cfg.RotationOffsetDegrees == 0f)
            return target.Rotation;

        return WrapPi(target.Rotation + (cfg.RotationOffsetDegrees * (MathF.PI / 180f)));
    }

    // ------------------------------------------------------------------ gates

    private static bool CanAct(out string why)
    {
        if (Svc.Objects.LocalPlayer is null)
        {
            why = "Not logged in.";
            return false;
        }

        if (Svc.Condition[ConditionFlag.BetweenAreas] || Svc.Condition[ConditionFlag.BetweenAreas51])
        {
            why = "Zoning.";
            return false;
        }

        if (Svc.Condition[ConditionFlag.OccupiedInCutSceneEvent]
            || Svc.Condition[ConditionFlag.WatchingCutscene]
            || Svc.Condition[ConditionFlag.WatchingCutscene78])
        {
            why = "In a cutscene.";
            return false;
        }

        if (Svc.Condition[ConditionFlag.Unconscious])
        {
            why = "You're dead.";
            return false;
        }

        why = string.Empty;
        return true;
    }

    // ------------------------------------------------------------------ start

    /// <summary>Begin a one-shot align: walk there, then face the same way.</summary>
    public bool Align(bool? position = null, bool? rotation = null)
    {
        var wantPosition = position ?? cfg.MatchPosition;
        var wantRotation = rotation ?? cfg.MatchRotation;

        if (!wantPosition && !wantRotation)
        {
            Report("Nothing to match — both position and facing are off.", true);
            return false;
        }

        if (!CanAct(out var why))
        {
            Report(why, true);
            return false;
        }

        var target = GetTarget();
        if (target is null)
        {
            Report($"No valid {SlotName(cfg.Slot)}.", true);
            return false;
        }

        var me = Svc.Objects.LocalPlayer!;

        // Facing-only needs no walking at all — unless we're turning with real input, which still
        // rides on the movement hook.
        if (!wantPosition)
        {
            if (cfg.Rotation == RotationMode.Turn && !movement.Available)
            {
                Report(movement.UnavailableReason, true);
                return false;
            }

            lockedId = target.GameObjectId;
            lockedName = target.Name.TextValue;
            BeginJob(wantPosition, wantRotation);
            Walking = true;
            return true;
        }

        if (!movement.Available)
        {
            Report(movement.UnavailableReason, true);
            return false;
        }

        var destination = ResolvePosition(target);
        var distance = FlatDistance(me.Position, destination);

        if (distance > cfg.MaxWalkDistance)
        {
            Report(
                $"{lockedNameOr(target)} is {distance:F1}y away — further than the {cfg.MaxWalkDistance:F0}y walk limit. "
              + "There's no pathfinding, so move closer first.",
                true);
            return false;
        }

        lockedId = target.GameObjectId;
        lockedName = target.Name.TextValue;
        BeginJob(wantPosition, wantRotation);
        Walking = true;

        Report($"Walking over to {lockedName} — {distance:F1}y away.", false);
        return true;

        static string lockedNameOr(IGameObject t)
            => string.IsNullOrEmpty(t.Name.TextValue) ? "Target" : t.Name.TextValue;
    }

    public void ToggleFollow()
    {
        if (Following)
            StopFollow("Stopped following.");
        else
            StartFollow();
    }

    public void StartFollow()
    {
        if (!CanAct(out var why))
        {
            Report(why, true);
            return;
        }

        if (cfg.MatchPosition && !movement.Available)
        {
            Report(movement.UnavailableReason, true);
            return;
        }

        var target = GetTarget();
        if (target is null)
        {
            Report($"No valid {SlotName(cfg.Slot)} to follow.", true);
            return;
        }

        lockedId = target.GameObjectId;
        lockedName = target.Name.TextValue;
        BeginJob(cfg.MatchPosition, cfg.MatchRotation);
        Walking = false;
        Following = true;

        Report($"Following {lockedName} — staying on their mark until you say otherwise.", false);
    }

    private void BeginJob(bool position, bool rotation)
    {
        jobPosition = position;
        jobRotation = rotation;

        var me = Svc.Objects.LocalPlayer;
        lastPosition = me?.Position ?? Vector3.Zero;
        lastProgressMs = Environment.TickCount64;
        lostSinceMs = 0;
        deadlineMs = Environment.TickCount64 + (long)(cfg.WalkTimeoutSecs * 1000f);

        movement.Precision = MathF.Max(cfg.ArrivalTolerance * 0.5f, 0.005f);
        movement.SlowRadius = cfg.SlowRadius;
        movement.MinMagnitude = cfg.MinMagnitude;
        movement.InvertTurn = cfg.InvertTurn;
    }

    // ------------------------------------------------------------------- stop

    public void StopFollow(string? message = null)
    {
        if (!Following)
            return;

        Following = false;
        Release();

        if (message is not null)
            Report(message, false);
    }

    public void StopAll()
    {
        Walking = false;
        Following = false;
        Release();
    }

    private void Release()
    {
        movement.Enabled = false;
        movement.DesiredFacing = null;
        lockedId = 0;
        lostSinceMs = 0;
    }

    // ------------------------------------------------------------------- tick

    private void OnUpdate(IFramework framework)
    {
        try
        {
            PollHotkeys();

            if (!Busy)
                return;

            if (!CanAct(out _))
            {
                Abort("Stopped — you can't move right now.");
                return;
            }

            if (BreaksOnInput() && movement.UserInput)
            {
                Abort(Following ? "Stopped following — you took over." : "Walk cancelled — you took over.");
                return;
            }

            var target = Svc.Objects.SearchById(lockedId);
            if (target is null || !target.IsValid())
            {
                HandleMissingTarget();
                return;
            }

            lostSinceMs = 0;
            lockedName = target.Name.TextValue;

            if (Following)
                TickFollow(target);
            else
                TickWalk(target);
        }
        catch (Exception ex)
        {
            Svc.Log.Error(ex, "Line Me Up update failed; stopping.");
            StopAll();
        }
    }

    private bool BreaksOnInput() => Following ? cfg.FollowBreaksOnInput : true;

    private void HandleMissingTarget()
    {
        var now = Environment.TickCount64;

        if (!Following)
        {
            Abort($"Stopped — lost track of {lockedName}.");
            return;
        }

        // Zoning or a streaming-range drop shouldn't silently end a follow.
        if (lostSinceMs == 0)
        {
            lostSinceMs = now;
            movement.Enabled = false;
        }
        else if (now - lostSinceMs > (long)(cfg.FollowGraceSecs * 1000f))
        {
            StopFollow($"Stopped following — {lockedName} is gone.");
        }
    }

    /// <summary>One-shot: walk in, face, then report and release.</summary>
    private void TickWalk(IGameObject target)
    {
        var me = Svc.Objects.LocalPlayer!;
        var destination = ResolvePosition(target);
        var wantFacing = ResolveRotation(target);

        var positionDone = !jobPosition
                        || FlatDistance(me.Position, destination) <= cfg.ArrivalTolerance;

        if (!positionDone)
        {
            Drive(destination, null);

            if (Environment.TickCount64 > deadlineMs)
            {
                Abort($"Gave up after {cfg.WalkTimeoutSecs:F0}s — still {FlatDistance(me.Position, destination):F1}y short.");
                return;
            }

            if (cfg.AbortWhenStuck && IsStuck(me.Position))
            {
                Abort($"Can't get any closer than {FlatDistance(me.Position, destination):F1}y — something's in the way.");
                return;
            }

            return;
        }

        // Arrived. Now the facing.
        if (!jobRotation)
        {
            Finish(me, destination, null);
            return;
        }

        if (cfg.Rotation == RotationMode.Instant)
        {
            SetFacing(wantFacing);
            Finish(me, destination, wantFacing);
            return;
        }

        // Turn mode: keep the hook running and let real turn input close the gap.
        // Position is already settled, so stop feeding walk input.
        Drive(null, wantFacing);

        if (MathF.Abs(WrapPi(wantFacing - me.Rotation)) <= movement.FacingPrecision)
        {
            Finish(me, destination, wantFacing);
            return;
        }

        if (Environment.TickCount64 > deadlineMs)
            Abort("Gave up turning — your facing never settled.");
    }

    /// <summary>Continuous: stay on their mark for as long as follow is on.</summary>
    private void TickFollow(IGameObject target)
    {
        var me = Svc.Objects.LocalPlayer!;
        var destination = ResolvePosition(target);
        var wantFacing = ResolveRotation(target);

        var atSpot = !jobPosition
                  || FlatDistance(me.Position, destination) <= cfg.ArrivalTolerance;

        // Facing is only worth matching once we're standing still — while walking, the character
        // faces the way it's travelling and any instant write would just be fought by the game.
        float? facing = jobRotation && cfg.Rotation == RotationMode.Turn && atSpot
            ? wantFacing
            : null;

        Drive(jobPosition ? destination : null, facing);

        if (atSpot && jobRotation && cfg.Rotation == RotationMode.Instant
            && MathF.Abs(WrapPi(wantFacing - me.Rotation)) > movement.FacingPrecision)
        {
            SetFacing(wantFacing);
        }
    }

    /// <summary>
    /// Hand the controls to the movement hook. A null destination means "don't walk" — used by
    /// facing-only jobs, which still need the hook running to inject turn input but must not move.
    /// </summary>
    private void Drive(Vector3? destination, float? facing)
    {
        var me = Svc.Objects.LocalPlayer;
        movement.DesiredPosition = destination ?? me?.Position ?? Vector3.Zero;
        movement.DesiredFacing = facing;
        movement.Enabled = true;
    }

    private void Finish(IGameObject me, Vector3 destination, float? facing)
    {
        Walking = false;
        Release();

        var residual = FlatDistance(me.Position, destination);
        var vertical = me.Position.Y - destination.Y;

        var message = jobPosition
            ? $"Lined up on {lockedName} — {residual * 100f:F1}cm off their mark"
            : $"Now facing the same way as {lockedName}";

        if (jobPosition && MathF.Abs(vertical) > cfg.ArrivalTolerance)
            message += $", and {MathF.Abs(vertical):F2}y {(vertical > 0f ? "above" : "below")} them";

        if (facing is { } f)
            message += $", facing {f * (180f / MathF.PI):F1}°";

        Report(message + ".", false);
    }

    private void Abort(string message)
    {
        Walking = false;
        Following = false;
        Release();
        Report(message, true);
    }

    private bool IsStuck(Vector3 current)
    {
        var now = Environment.TickCount64;

        if (FlatDistance(current, lastPosition) > StuckDistance)
        {
            lastPosition = current;
            lastProgressMs = now;
            return false;
        }

        return now - lastProgressMs > StuckWindowMs;
    }

    /// <summary>
    /// The one place that writes to the character, and it writes a single float: the facing.
    /// This does not move you — it's the same native setter the game uses when it turns you itself.
    /// </summary>
    private static void SetFacing(float yaw)
    {
        var me = Svc.Objects.LocalPlayer;
        if (me is null || me.Address == nint.Zero)
            return;

        ((CSGameObject*)me.Address)->SetRotation(yaw);
    }

    // ---------------------------------------------------------------- hotkeys

    private void PollHotkeys()
    {
        var walkDown = IsComboDown(cfg.WalkKey, cfg.WalkCtrl, cfg.WalkAlt, cfg.WalkShift);
        if (walkDown && !walkKeyWasDown)
        {
            if (Busy)
                StopAll();
            else
                Align();
        }

        walkKeyWasDown = walkDown;

        var followDown = IsComboDown(cfg.FollowKey, cfg.FollowCtrl, cfg.FollowAlt, cfg.FollowShift);
        if (followDown && !followKeyWasDown)
            ToggleFollow();
        followKeyWasDown = followDown;
    }

    private bool IsComboDown(VirtualKey key, bool ctrl, bool alt, bool shift)
    {
        if (key == VirtualKey.NO_KEY)
            return false;

        if (!Down(key))
            return false;

        return Down(VirtualKey.CONTROL) == ctrl
            && Down(VirtualKey.MENU) == alt
            && Down(VirtualKey.SHIFT) == shift;
    }

    /// <summary>IKeyState throws on keys the game doesn't track, so every read is filtered first.</summary>
    private bool Down(VirtualKey key) => validKeys.Contains(key) && Svc.Keys[key];

    public bool AnyMovementKeyDown()
    {
        foreach (var k in MovementKeys)
        {
            if (Down(k))
                return true;
        }

        return false;
    }

    // ------------------------------------------------------------------ chat

    public static string SlotName(TargetSlot slot) => slot switch
    {
        TargetSlot.Target => "target",
        TargetSlot.SoftTarget => "soft target",
        TargetSlot.FocusTarget => "focus target",
        TargetSlot.MouseOver => "mouseover target",
        TargetSlot.GPose => "GPose target",
        _ => "target",
    };

    private void Report(string message, bool isError)
    {
        Svc.Log.Debug(message);

        if (!cfg.PrintToChat)
            return;

        if (isError)
            Svc.Chat.PrintError($"[Line Me Up] {message}");
        else
            Svc.Chat.Print($"[Line Me Up] {message}");
    }
}
