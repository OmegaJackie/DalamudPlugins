using System;
using System.Collections.Generic;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;

namespace BozjaBuddyReborn.Game;

/// <summary>
/// The party, as much of it as is actually actionable, in one snapshot per tick.
///
/// WHY A SNAPSHOT AND NOT LIVE READS. Every member below is a pointer into game memory that can
/// go away between two statements - a member zones, dies, or drops group - and the caller here is
/// choosing a target and then firing something finite at it. Reading once and deciding from that
/// makes the decision and the act agree, and makes the "is this still the person I chose" check at
/// the moment of firing a real check rather than a re-read of the same volatile thing.
///
/// WHAT IS DELIBERATELY NOT HERE. The 48-player ALLIANCE. IPartyList covers the 8-man party only,
/// and that is exactly the scope asked for: a Lost Action fired at an alliance member who is not
/// in the party is a farmed charge spent on a stranger. Nothing in this file can return one, which
/// is the strongest form the "never apply to non-party targets" rule can take - it is not a check
/// that can be forgotten at a call site, it is an absence of any way to name such a target.
///
/// SOLO IS A PARTY OF ONE. IPartyList.Length is 0 when not grouped, not 1, so the local player
/// would otherwise vanish from a list they are plainly a member of. <see cref="Snapshot"/> falls
/// back to the local player alone, so a solo box buffs itself and the callers need no special case.
/// </summary>
public static class PartyView
{
    /// <summary>
    /// One party member, resolved far enough to be a target and to be reasoned about.
    /// </summary>
    public readonly record struct Member(
        ulong Id,
        string Name,
        IBattleChara Chara,
        uint Hp,
        uint MaxHp,
        bool IsSelf)
    {
        /// <summary>0..1 of maximum. Dead members read 0.</summary>
        public float HpFraction => MaxHp == 0 ? 0f : Math.Clamp((float)Hp / MaxHp, 0f, 1f);

        /// <summary>A corpse takes a raise, not a buff, so the two callers want opposite answers.</summary>
        public bool IsDead => Hp == 0;
    }

    /// <summary>
    /// The party right now: everyone whose game object is actually resolved.
    ///
    /// A member in another zone, or simply too far to be streamed, has a null GameObject - there is
    /// nothing to target and nothing to read a status from, so they are left out rather than
    /// carried as an entry every caller would have to null-check.
    /// </summary>
    public static List<Member> Snapshot()
    {
        var list = new List<Member>(8);

        try
        {
            var me = Svc.Objects.LocalPlayer;
            var selfId = me?.GameObjectId ?? 0ul;

            var party = Svc.Party;
            if (party.Length == 0)
            {
                // Not grouped. The local player is still the whole party for this purpose.
                if (me != null)
                    list.Add(new Member(me.GameObjectId, me.Name.TextValue, me, me.CurrentHp, me.MaxHp, true));
                return list;
            }

            foreach (var member in party)
            {
                if (member.GameObject is not IBattleChara chara)
                    continue;

                list.Add(new Member(
                    Id: chara.GameObjectId,
                    Name: member.Name.TextValue,
                    Chara: chara,
                    Hp: chara.CurrentHp,
                    MaxHp: chara.MaxHp,
                    IsSelf: chara.GameObjectId == selfId));
            }
        }
        catch
        {
            return list;
        }

        return list;
    }

    /// <summary>
    /// Is this object id one of the current party's? The last gate before anything is fired.
    ///
    /// Deliberately re-derived from the live party list rather than trusted from a snapshot: the
    /// snapshot is what a decision was MADE from, and this is what it is CHECKED against at the
    /// moment of acting. A member who dropped group in between is exactly the case worth catching.
    /// </summary>
    public static bool IsInParty(ulong id)
    {
        if (id == 0)
            return false;

        try
        {
            var party = Svc.Party;
            if (party.Length == 0)
                return Svc.Objects.LocalPlayer?.GameObjectId == id;

            foreach (var member in party)
                if (member.GameObject is { } obj && obj.GameObjectId == id)
                    return true;
        }
        catch { return false; }

        return false;
    }

    /// <summary>
    /// Is <paramref name="statusId"/> on this character, and for how much longer?
    ///
    /// The party-member counterpart of LostActionStatuses.IsActive, which only ever asks about the
    /// local player. A permanent status reports a negative remaining time; that is normalised to 0
    /// and reported as up, so "no clock" never reads as "expired".
    /// </summary>
    public static bool HasStatus(IBattleChara chara, uint statusId, out float remaining)
    {
        remaining = 0f;

        if (statusId == 0)
            return false;

        try
        {
            foreach (var s in chara.StatusList)
            {
                if (s == null || s.StatusId != statusId)
                    continue;

                remaining = s.RemainingTime > 0f ? s.RemainingTime : 0f;
                return true;
            }
        }
        catch { return false; }

        return false;
    }
}
