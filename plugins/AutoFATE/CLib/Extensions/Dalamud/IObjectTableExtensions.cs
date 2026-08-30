using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;

namespace AutoFATE.CLib.Extensions;

public static class IObjectTableExtensions {
    extension(IObjectTable objs) {
        public IOrderedEnumerable<IGameObject> ByDistance() => objs.OrderBy(o => o.DistanceTo(IObjectTable.Get().LocalPlayer?.Position ?? Vector3.Zero));
        public IGameObject? Nearest() => objs.ByDistance().FirstOrDefault();

        public bool TryGetByGameObjectId(ulong gameObjectId, out IGameObject obj) {
            if (objs.FirstOrDefault(o => o.GameObjectId == gameObjectId) is { } match) {
                obj = match;
                return true;
            }
            obj = default!;
            return false;
        }

        public unsafe bool TryGetPlayerByContentId(ulong contentId, out IGameObject obj) {
            if (objs.OfType<IPlayerCharacter>().FirstOrDefault(o => o.Character->ContentId == contentId) is { } match) {
                obj = match;
                return true;
            }
            obj = default!;
            return false;
        }
    }

    extension(IEnumerable<IGameObject> objs) {
        public IOrderedEnumerable<IGameObject> ByDistance() => objs.OrderBy(o => o.DistanceTo(IObjectTable.Get().LocalPlayer?.Position ?? Vector3.Zero));
        public IGameObject? Nearest() => objs.ByDistance().FirstOrDefault();
    }
}
