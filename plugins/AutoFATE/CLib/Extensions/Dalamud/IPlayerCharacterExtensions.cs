using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.SubKinds;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Excel;
using Lumina.Excel.Sheets;

namespace AutoFATE.CLib.Extensions;

public static unsafe class IPlayerCharacterExtensions {
    extension(IPlayerCharacter? pc) {
        public bool Available => pc != null;
        public bool Interactable => pc?.IsTargetable ?? false;
        public bool IsMoving => get_Available(pc) && (AgentMap.Instance()->IsPlayerMoving || get_IsJumping(pc));
        public bool IsJumping => get_Available(pc) && (ICondition.Get()[ConditionFlag.Jumping] || ICondition.Get()[ConditionFlag.Jumping61] || pc?.Character->IsJumping());
        public bool IsAirDismountable {
            get {
                var ground = new FFXIVClientStructs.FFXIV.Common.Math.Vector3();
                return UIState.Instance()->GetIsAirDismountable(&ground);
            }
        }

        public bool IsBusy
            => ICondition.Get().IsUnavailable() ||
            !get_Interactable(pc) ||
            (pc?.IsCasting ?? false) ||
            get_IsMoving(pc) ||
            ActionManager.Instance()->AnimationLock > 0 ||
            ICondition.Get()[ConditionFlag.InCombat] ||
            !GameMain.IsTerritoryLoaded;

        public bool IsUiFading => RaptureAtkUnitManager.Instance() is not null and var mgr && mgr->IsUiFading;

        public RowRef<TerritoryType> Territory => TerritoryType.GetRowRef(IClientState.Get().TerritoryType);

        public bool CanMount => pc.Territory.Value.Mount && PlayerState.Instance()->NumOwnedMounts > 0;
        public bool Mounted => ICondition.Get()[ConditionFlag.Mounted];
        public bool InFlight => ICondition.Get()[ConditionFlag.InFlight];
        public float Rotation {
            get => pc?.Character->Rotation;
            set => pc?.Character->SetRotation(value);
        }
        /// <summary>
        /// Rotation packed into a ushort. Used in some <see cref="GameMain.ExecuteCommand"/> functions.
        /// </summary>
        public float PackedRotation => (ushort)(((IObjectTable.Get().LocalPlayer?.Rotation + Math.PI) / (2 * Math.PI) * 65536) ?? 0);

        public bool IsRevivable => (pc?.IsDead ?? false) && AgentRevive.Instance()->ReviveState != 0;
    }
}
