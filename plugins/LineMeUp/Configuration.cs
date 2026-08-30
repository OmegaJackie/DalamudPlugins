using System;
using Dalamud.Configuration;
using Dalamud.Game.ClientState.Keys;

namespace LineMeUp;

/// <summary>Which of the game's target slots to read from.</summary>
public enum TargetSlot
{
    Target,
    SoftTarget,
    FocusTarget,
    MouseOver,
    GPose,
}

/// <summary>How to match the target's facing once you've arrived.</summary>
public enum RotationMode
{
    /// <summary>Set the facing directly. Exact, instant, one float.</summary>
    Instant,

    /// <summary>Feed real turn input and rotate into place. Nothing is written, but it only converges to about a degree.</summary>
    Turn,
}

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 2;

    // ---- what gets matched ------------------------------------------------
    public bool MatchPosition = true;
    public bool MatchRotation = true;

    /// <summary>Which target slot to read. Hard target by default.</summary>
    public TargetSlot Slot = TargetSlot.Target;

    /// <summary>When set, only players can be aligned to (not NPCs, mounts, objects).</summary>
    public bool PlayersOnly = false;

    // ---- offsets ----------------------------------------------------------
    // Expressed in the TARGET's local frame, in yalms. All zero => stand where they stand.
    public float OffsetForward;
    public float OffsetRight;
    public float OffsetUp;

    /// <summary>Extra facing offset in degrees, applied on top of the target's rotation.</summary>
    public float RotationOffsetDegrees;

    // ---- walking ----------------------------------------------------------

    /// <summary>Close enough to call it arrived, in yalms. Flat (XZ) distance.</summary>
    public float ArrivalTolerance = 0.05f;

    /// <summary>Distance at which the character starts easing off full speed.</summary>
    public float SlowRadius = 1.2f;

    /// <summary>
    /// Slowest crawl as a fraction of full input. Lower lands more precisely but takes longer to
    /// settle. Keep it low enough that one frame of movement is smaller than the tolerance.
    /// </summary>
    public float MinMagnitude = 0.08f;

    /// <summary>Give up on a walk after this long.</summary>
    public float WalkTimeoutSecs = 12f;

    /// <summary>
    /// Refuse to start a walk further than this, in yalms. There's no pathfinding — the character
    /// walks in a straight line, so a long walk just means a long time spent against a wall.
    /// </summary>
    public float MaxWalkDistance = 30f;

    /// <summary>Abort if the character stops making progress (walked into something).</summary>
    public bool AbortWhenStuck = true;

    // ---- facing -----------------------------------------------------------
    public RotationMode Rotation = RotationMode.Instant;

    /// <summary>Flip if turn input rotates the wrong way on your setup.</summary>
    public bool InvertTurn;

    // ---- follow -----------------------------------------------------------
    /// <summary>Stop following when the user takes manual control.</summary>
    public bool FollowBreaksOnInput = true;

    /// <summary>Drop follow if the locked target leaves the object table for longer than this.</summary>
    public float FollowGraceSecs = 3.0f;

    // ---- hotkeys ----------------------------------------------------------
    public VirtualKey WalkKey = VirtualKey.NO_KEY;
    public bool WalkCtrl, WalkAlt, WalkShift;

    public VirtualKey FollowKey = VirtualKey.NO_KEY;
    public bool FollowCtrl, FollowAlt, FollowShift;

    // ---- misc -------------------------------------------------------------
    public bool PrintToChat = true;

    public void Save() => Svc.PluginInterface.SavePluginConfig(this);
}
