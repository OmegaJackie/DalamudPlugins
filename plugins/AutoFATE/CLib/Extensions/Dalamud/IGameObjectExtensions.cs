using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Common.Component.BGCollision;
using Lumina.Excel.Sheets;

namespace AutoFATE.CLib.Extensions;

public static class IGameObjectExtensions {
    extension(IGameObject obj) {
        public unsafe BattleChara* BattleChara => (BattleChara*)obj.Address;
        public unsafe Character* Character => (Character*)obj.Address;

        public int HuntRank => NotoriousMonster.FirstOrNull(r => r.BNpcBase.RowId == obj.BaseId) is { Rank: var rank } ? rank : 0;
        public unsafe uint NameplateIconId => ((GameObject*)obj.Address)->NamePlateIconId;

        public float DistanceTo() => Vector3.Distance(obj.Position, IObjectTable.Get().LocalPlayer?.Position ?? obj.Position);
        public float DistanceTo(IGameObject other) => Vector3.Distance(obj.Position, other.Position);
        public float DistanceTo(Vector3 position) => Vector3.Distance(obj.Position, position);

        public bool IsInLineOfSight() => IObjectTable.Get().LocalPlayer is { Position: var pos } && IsInLineOfSight(obj, pos);
        public bool IsInLineOfSight(Vector3 point) {
            var adjustedOrigin = obj.Position.AddY(2);
            var adjustedTarget = point.AddY(2);
            return !BGCollisionModule.RaycastMaterialFilter(adjustedOrigin, Vector3.Normalize(adjustedTarget - adjustedOrigin), out _, Vector3.Distance(adjustedOrigin, adjustedTarget));
        }
    }

    public static float FlatDistanceTo(this IGameObject? obj, Vector3 position) {
        if (obj is null) return 0f;
        var dx = obj.Position.X - position.X;
        var dz = obj.Position.Z - position.Z;
        return MathF.Sqrt(dx * dx + dz * dz);
    }
    public static bool WithinRange(this IGameObject? obj, Vector3 position, float range) => obj is not null && Vector3.Distance(obj.Position, position) < range;
    public static unsafe bool IsTargetingPlayer(this IGameObject obj) => obj.TargetObjectId == GameObjectManager.Instance()->Objects.IndexSorted[0].Value->GetGameObjectId().ObjectId;
    public static unsafe EventHandlerInfo? EventInfo(this IGameObject obj) {
        if (obj == null) return null;
        var cs = (GameObject*)obj.Address;
        return cs == null || cs->EventHandler == null ? null : cs->EventHandler->Info;
    }
    public static unsafe bool IsInInteractRange(this IGameObject obj) => EventFramework.Instance()->CheckInteractRange((GameObject*)IObjectTable.Get().LocalPlayer!.Address, (GameObject*)obj.Address, 1, false);

    public static unsafe bool CanRidePillion(this IGameObject? obj) {
        if (obj == null) return false;
        var cont = obj.Character->Mount;
        return cont.MountedEntityIds[1..].ToArray().Count(x => x != 0) < (Mount.GetRowRef(cont.MountId).ValueNullable?.ExtraSeats ?? 0);
    }
}
