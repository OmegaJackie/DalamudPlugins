using Dalamud.Game.Inventory;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel;
using Lumina.Excel.Sheets;

namespace AutoFATE.CLib.Extensions;

public static class GameInventoryItemExtensions {
    extension(GameInventoryItem item) {
        public RowRef<Item> GameData => Item.GetRowRef(item.BaseItemId);
        public ItemHandle Handle => (ItemHandle)item;
    }

    public static unsafe InventoryItem* Struct(this GameInventoryItem item) => (InventoryItem*)item.Address;
}
