using Lumina.Data;
using Lumina.Excel;
using Lumina.Excel.Sheets;

namespace AutoFATE.CLib.Extensions;

public static class SatisfactionNpcExtensions {
    extension(SatisfactionNpc row) {
        // I don't like the name matching here but couldn't find another connection. Maybe the key prop is relevant but nothing obvious
        public RowRef<Achievement> Achievement
            => new(row.ExcelPage.Module,
                Achievement.FirstOrNull(r => r is { AchievementCategory.RowId: 22, AchievementTarget.RowId: 46, Data: [{ RowId: 150 }, ..] }
                    && r.WithLanguage(Language.English).Description.ContainsText(row.Npc.Value.WithLanguage(Language.English).Singular))?.RowId ?? new());
    }
}
