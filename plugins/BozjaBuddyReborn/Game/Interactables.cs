using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game.Control;

namespace BozjaBuddyReborn.Game;

/// <summary>A kind of world object the operator can send a box to interact with.</summary>
public readonly record struct InteractTarget(uint DataId, string Label, string Note);

/// <summary>
/// Finding and interacting with the fixed world objects Bozja cares about.
///
/// THE AETHERYTE CORRECTION. Bozja and Zadnor have zero rows in the Aetheryte sheet and no
/// teleport coordinates, which is true and is why there is no Teleport-style fast travel there -
/// but it is NOT the same as "there is nothing called an aetheryte". EObjName 2011160 is
/// literally "Bozjan aetheryte": the in-zone network is built as ordinary interactable
/// EventObjects rather than through the game's Aetheryte system, which is exactly how Occult
/// Crescent's aetherytes (EObjName 2014664) work too. So walking up to one and interacting is
/// perfectly possible; it is only the sheet-driven teleport that does not exist.
///
/// Ids are EObjName/EObj row ids, which is what a world object's BaseId carries, so matching is
/// exact and needs no name comparison or localisation.
/// </summary>
public static unsafe class Interactables
{
    /// <summary>Bozjan aetheryte - the in-zone network node (EObjName 2011160).</summary>
    public const uint BozjanAetheryte = 2011160;

    /// <summary>Lost finds cache - where Lost Actions are bought and stored (EObjName 2011127).</summary>
    public const uint LostFindsCache = 2011127;

    /// <summary>Lost belonging - the field pickup (EObjName 2014189).</summary>
    public const uint LostBelonging = 2014189;

    /// <summary>What the control panel offers to send boxes at.</summary>
    public static readonly IReadOnlyList<InteractTarget> Known =
    [
        new(BozjanAetheryte, "Bozjan aetheryte", "the in-zone network node"),
        new(LostFindsCache, "Lost finds cache", "buy and store Lost Actions"),
        new(LostBelonging, "Lost belonging", "field pickup"),
    ];

    public static string Label(uint dataId)
    {
        foreach (var t in Known)
            if (t.DataId == dataId)
                return t.Label;
        return $"object {dataId}";
    }

    /// <summary>
    /// Nearest matching world object, or null. Considers only objects the game is currently
    /// streaming, so "nothing found" legitimately means "not near enough to see".
    /// </summary>
    public static IGameObject? Nearest(uint dataId, float maxDistance = 200f)
    {
        var me = Svc.Objects.LocalPlayer;
        if (me == null)
            return null;

        IGameObject? best = null;
        var bestSq = maxDistance * maxDistance;

        try
        {
            foreach (var o in Svc.Objects)
            {
                if (o.BaseId != dataId)
                    continue;
                if (o.ObjectKind is not (ObjectKind.EventObj or ObjectKind.EventNpc))
                    continue;

                var d = Vector3.DistanceSquared(o.Position, me.Position);
                if (d < bestSq)
                {
                    bestSq = d;
                    best = o;
                }
            }
        }
        catch
        {
            // Object table is framework-thread only; a caller off-tick gets nothing rather than
            // an exception escaping into the pipe reader.
            return null;
        }

        return best;
    }

    /// <summary>
    /// Interact with a world object, the same call the game makes when you press the action.
    /// </summary>
    /// <returns>False when the object is gone or the game refused.</returns>
    public static bool Interact(IGameObject target)
    {
        try
        {
            var ts = TargetSystem.Instance();
            if (ts == null)
                return false;

            var raw = (FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)target.Address;
            if (raw == null)
                return false;

            // Line-of-sight checked, because skipping it is how you get a silent no-op against
            // something on the other side of a wall that we merely pathed close to.
            return ts->InteractWithObject(raw) != 0;
        }
        catch (Exception ex)
        {
            Svc.Log.Debug($"[BozjaBuddyReborn] Interact failed: {ex.Message}");
            return false;
        }
    }
}
