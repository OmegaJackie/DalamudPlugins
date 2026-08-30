using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;

namespace BozjaBuddyReborn.Automation;

/// <summary>
/// "Is something actually hitting us right now?"
///
/// Deliberately NOT just ConditionFlag.InCombat. That flag lingers after the last mob dies and
/// is set by things we are not obliged to answer, so keying self-defence on it makes the runner
/// stop for phantom fights. Counting hostiles that have us as their target is the honest
/// question, and it goes false the moment they die or lose aggro.
/// </summary>
public static class Threat
{
    /// <summary>Attackers further out than this are outrunnable and not worth stopping for.</summary>
    public const float DefaultRange = 30f;

    /// <summary>How many living hostiles within range are currently targeting the player.</summary>
    public static int CountAttackers(float range = DefaultRange)
    {
        var me = Svc.Objects.LocalPlayer;
        if (me == null)
            return 0;

        var myId = me.GameObjectId;
        var count = 0;

        try
        {
            foreach (var obj in Svc.Objects)
            {
                if (obj is not IBattleNpc npc)
                    continue;

                if (npc.CurrentHp == 0)
                    continue;

                // HOSTILE COMBATANTS ONLY. IBattleNpc also covers pets, companion chocobos and
                // weak-spot parts, and "targeting the player" is not the same question for those
                // - a companion following its owner targets the owner. Without this filter a box
                // with a chocobo out reads as permanently under attack, which stops the run to
                // fight nothing, forever. AggroAvoidance.Scan has always applied both of these;
                // this counter did not, and it is the one that decides whether to stand and fight.
                if (npc.BattleNpcKind != BattleNpcSubKind.Combatant)
                    continue;

                if (!npc.StatusFlags.HasFlag(StatusFlags.Hostile))
                    continue;

                if (npc.TargetObjectId != myId)
                    continue;

                if (Movement.HorizontalDistance(npc.Position, me.Position) > range)
                    continue;

                count++;
            }
        }
        catch
        {
            return 0;
        }

        return count;
    }

    /// <summary>True when at least one hostile is attacking the player.</summary>
    public static bool UnderAttack(float range = DefaultRange) => CountAttackers(range) > 0;
}
