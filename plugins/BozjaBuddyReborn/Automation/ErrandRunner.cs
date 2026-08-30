using System;
using System.Numerics;
using BozjaBuddyReborn.External;
using BozjaBuddyReborn.Game;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;

namespace BozjaBuddyReborn.Automation;

/// <summary>
/// Runs a one-shot "go over there and interact with that" errand on this box.
///
/// WHY THIS IS SEPARATE FROM THE CONTROLLER. The orchestrator owns a long-running goal - pick an
/// objective, travel, fight - and an errand is the opposite: a short imperative the operator
/// issued by hand, which must be able to interrupt and then hand everything back exactly as it
/// was. Folding it into the controller state machine would mean every branch there growing an
/// "unless we are running an errand" clause.
///
/// It takes the same vnavmesh path the controller uses, through the same Movement instance, so
/// the two can never both be driving: the controller yields while an errand is live.
/// </summary>
public sealed class ErrandRunner(Movement movement, NavmeshIpc navmesh)
{
    private readonly Movement _movement = movement;
    private readonly NavmeshIpc _navmesh = navmesh;

    /// <summary>How close we need to be before the game will accept an interaction.</summary>
    private const float InteractRange = 3.5f;

    /// <summary>Give up rather than run at something unreachable forever.</summary>
    private const long TimeoutMs = 90_000;

    private uint _dataId;
    private long _startedMs;
    private long _lastInteractMs;

    /// <summary>True while an errand is running; the controller stands down.</summary>
    public bool Active { get; private set; }

    /// <summary>What the errand is doing, for the UI and for the operator's panel.</summary>
    public string Status { get; private set; } = string.Empty;

    /// <summary>Begin walking to the nearest object of this kind and interacting with it.</summary>
    public void Begin(uint dataId)
    {
        _dataId = dataId;
        _startedMs = Environment.TickCount64;
        _lastInteractMs = 0;
        Active = true;
        Status = $"Looking for the nearest {Interactables.Label(dataId)}.";
    }

    /// <summary>Abandon the errand and hand movement back.</summary>
    public void Cancel(string reason = "Errand cancelled.")
    {
        if (!Active)
            return;

        Active = false;
        _dataId = 0;
        Status = reason;
        _movement.Stop();
    }

    /// <summary>Drive one tick. Call from the controller tick, framework thread.</summary>
    public void Tick()
    {
        if (!Active)
            return;

        if (Environment.TickCount64 - _startedMs > TimeoutMs)
        {
            Cancel($"Gave up reaching the {Interactables.Label(_dataId)} after 90s.");
            return;
        }

        var me = Svc.Objects.LocalPlayer;
        if (me == null)
            return;

        var target = Interactables.Nearest(_dataId);
        if (target is not { } obj)
        {
            // Nothing streamed nearby. Worth saying plainly rather than silently walking nowhere:
            // these objects are fixed, so "not visible" means the box is in the wrong part of the
            // zone entirely and no amount of waiting will help.
            Cancel($"No {Interactables.Label(_dataId)} in range of this box.");
            return;
        }

        var distance = Movement.HorizontalDistance(me.Position, obj.Position);

        if (distance <= InteractRange)
        {
            _movement.Stop();

            // Throttled: the game ignores a second interact while the first is resolving, and
            // hammering it just spams the error sound.
            var now = Environment.TickCount64;
            if (now - _lastInteractMs < 1000)
                return;
            _lastInteractMs = now;

            if (Interactables.Interact(obj))
            {
                Active = false;
                Status = $"Interacted with the {Interactables.Label(_dataId)}.";
            }
            else
            {
                Status = $"At the {Interactables.Label(_dataId)} - the game refused the interaction, retrying.";
            }
            return;
        }

        if (!_navmesh.Available || !_navmesh.MeshReady)
        {
            Cancel("vnavmesh is not ready, so the errand cannot travel.");
            return;
        }

        _movement.TravelTo(obj.Position, InteractRange - 0.5f);
        Status = $"Walking to the {Interactables.Label(_dataId)} ({distance:F0}y).";
    }
}
