using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Group;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using IntendedUse = FFXIVClientStructs.FFXIV.Client.Enums.TerritoryIntendedUse;

namespace AutoFATE.CLib.Extensions;

public static class IPlayerStateExtensions {
    extension(IPlayerState ps) {
        public unsafe RowRef<TerritoryType> Territory
            => ps.IsLoaded && GameMain.Instance()->TerritoryLoadState is 2 ? TerritoryType.GetRowRef(GameMain.Instance()->CurrentTerritoryTypeId) : default;
        public unsafe IntendedUse TerritoryIntendedUse
            => ps.IsLoaded && GameMain.Instance()->TerritoryLoadState is 2 ? GameMain.Instance()->CurrentTerritoryIntendedUseId : unchecked((IntendedUse)(-1));
        public unsafe RowRef<ContentFinderCondition> ContentFinderCondition
            => ps.IsLoaded && GameMain.Instance()->CurrentContentFinderConditionId is not 0 and var cfc ? ContentFinderCondition.GetRowRef(cfc) : default;
        public unsafe RowRef<Aetheryte> HomeAetheryte
            => ps.IsLoaded && PlayerState.Instance()->HomeAetheryteId is not 0 and var aetheryte ? Aetheryte.GetRowRef(aetheryte) : default;
        public RowRef<OnlineStatus> OnlineStatus
            => ps.IsLoaded && IObjectTable.Get().LocalPlayer is { OnlineStatus: var status } ? status : default;
        public unsafe RowRef<Companion> Minion {
            get {
                if (!ps.IsLoaded) return default;
                if (IObjectTable.Get().LocalPlayer is not { } player) return default;
                if (player.Character->CompanionData.CompanionObject is not null and var minion)
                    return Companion.GetRowRef(minion->BaseId);
                else
                    return Companion.GetRowRef(player.Character->CompanionData.CompanionId);
            }
        }

        public unsafe FlagMapMarker MapFlag => ps.IsLoaded ? AgentMap.Instance()->FlagMapMarkers[0] : default;

        public bool IsInSoloDuty
            => ps.IsLoaded && get_Territory(ps).IsValid && get_Territory(ps).ValueNullable?.TerritoryIntendedUse.ValueNullable?.StructsEnum is IntendedUse.SoloDuty;
        public unsafe bool IsInDuty
            => ps.IsLoaded && GameMain.Instance()->CurrentContentFinderConditionId is not 0;
        public unsafe bool IsPenalised
            => ps.IsLoaded && FFXIVClientStructs.FFXIV.Client.Game.UI.InstanceContent.Instance()->GetPenaltyRemainingInMinutes(0) > 0;

        public unsafe bool IsBuddyInStable => ps.IsLoaded && PlayerState.Instance()->IsPlayerStateFlagSet(PlayerStateFlag.IsBuddyInStable);

        public unsafe bool IsPartyLeader
            => ps.IsLoaded && IObjectTable.Get().LocalPlayer is { EntityId: var id } && GroupManager.Instance()->GetGroup()->MemberCount > 0 && GroupManager.Instance()->GetGroup()->IsEntityIdPartyLeader(id);
    }
}
