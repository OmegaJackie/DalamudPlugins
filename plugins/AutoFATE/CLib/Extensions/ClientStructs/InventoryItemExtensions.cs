using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel;
using Lumina.Excel.Sheets;

namespace AutoFATE.CLib.Extensions;

public static class InventoryItemExtensions {
    extension(ref InventoryItem item) {
        /// <summary>
        /// Use with <see cref="RowRef.TryGetValue{T}(out T)"/> since this will return either an Item or EventItem row
        /// </summary>
        public RowRef GameData => ItemUtil.GetBaseId(item.ItemId).Kind is ItemKind.EventItem ? RowRef.Create<EventItem>(IDataManager.Get().Excel, item.ItemId) : RowRef.Create<Item>(IDataManager.Get().Excel, item.GetBaseItemId());
    }
}
