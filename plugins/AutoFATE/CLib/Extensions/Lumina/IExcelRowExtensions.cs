using Lumina.Excel;
using Lumina.Extensions;

namespace AutoFATE.CLib.Extensions;

public static class IExcelRowExtensions {
    extension<T>(IExcelRow<T> excelRow) where T : struct, IExcelRow<T> {
        public T WithLanguage(Dalamud.Game.ClientLanguage language)
            => IDataManager.Get().GetExcelSheet<T>(language: language).GetRow(excelRow.RowId);

        public T WithLanguage(Lumina.Data.Language language)
            => IDataManager.Get().GetExcelSheet<T>(language: (Dalamud.Game.ClientLanguage)language).GetRow(excelRow.RowId);

        public static IEnumerable<T> Rows => IDataManager.Get().GetExcelSheet<T>();

        public static RowRef<T> GetRowRef(uint id, Lumina.Data.Language? language = null)
            => new(IDataManager.Get().Excel, id, language);

        public static T GetRow(uint id, Dalamud.Game.ClientLanguage? language = null)
            => IDataManager.Get().GetExcelSheet<T>(language: language).GetRow(id);

        public static T? GetRowOrNull(uint id, Dalamud.Game.ClientLanguage? language = null)
            => IDataManager.Get().GetExcelSheet<T>(language: language).TryGetRow(id, out var row) ? row : null;

        public static bool TryGetRow(uint id, out T row, Dalamud.Game.ClientLanguage? language = null) {
            if (IDataManager.Get().GetExcelSheet<T>(language: language).TryGetRow(id, out var r)) {
                row = r;
                return true;

            }
            else {
                row = default;
                return false;
            }
        }

        public static bool Any(Func<T, bool> predicate)
            => IDataManager.Get().GetExcelSheet<T>().Any(r => predicate(r));

        public static int Count(Func<T, bool> predicate)
            => IDataManager.Get().GetExcelSheet<T>().Count(r => predicate(r));

        public static bool All(Func<T, bool> predicate)
            => IDataManager.Get().GetExcelSheet<T>().All(r => predicate(r));

        public static T[] Where(Func<T, bool> predicate)
            => [.. IDataManager.Get().GetExcelSheet<T>().Where(r => predicate(r))];

        public static TResult[] Select<TResult>(Func<T, TResult> selector)
            => [.. IDataManager.Get().GetExcelSheet<T>().Select(selector)];

        public static T? FirstOrNull()
            => IDataManager.Get().GetExcelSheet<T>().FirstOrNull();

        public static T? FirstOrNull(Func<T, bool> predicate)
            => IDataManager.Get().GetExcelSheet<T>().Where(r => predicate(r)).FirstOrNull();
    }
}

public static class IExcelSubrowExtensions {
    extension<T>(IExcelSubrow<T> row) where T : struct, IExcelSubrow<T> {
        public T? WithLanguage(ushort subRowId, Dalamud.Game.ClientLanguage language)
            => IDataManager.Get().GetSubrowSheet<T>(language: language).GetSubrowOrDefault(row.RowId, subRowId);

        public T? WithLanguage(ushort subRowId, Lumina.Data.Language language)
            => IDataManager.Get().GetSubrowSheet<T>(language: (Dalamud.Game.ClientLanguage)language).GetSubrowOrDefault(row.RowId, subRowId);

        public static IEnumerable<T> Rows => IDataManager.Get().GetSubrowSheet<T>().SelectMany(r => r);

        public static SubrowRef<T> GetSubrowRef(uint rowId, Lumina.Data.Language? language = null)
            => new(IDataManager.Get().Excel, rowId, language);

        public static T? GetSubrow(uint rowId, ushort subRowId, Dalamud.Game.ClientLanguage? language = null)
            => IDataManager.Get().GetSubrowSheet<T>(language: language).GetSubrowOrDefault(rowId, subRowId);

        public static bool TryGetSubrow(uint rowId, ushort subRowId, out T subrow, Dalamud.Game.ClientLanguage? language = null) {
            if (IDataManager.Get().GetSubrowSheet<T>(language: language).TryGetSubrow(rowId, subRowId, out var r)) {
                subrow = r;
                return true;
            }
            else {
                subrow = default;
                return false;
            }
        }

        public static bool TryGetSubrows(uint rowId, out SubrowCollection<T> subrows) {
            if (IDataManager.Get().TryGetSubrows<T>(rowId, out var r)) {
                subrows = r;
                return true;
            }
            else {
                subrows = [];
                return false;
            }
        }

        public static bool Any(Func<T, bool> predicate)
            => EnumerateSubrows<T>().Any(predicate);

        public static int Count(Func<T, bool> predicate)
            => EnumerateSubrows<T>().Count(predicate);

        public static bool All(Func<T, bool> predicate)
            => EnumerateSubrows<T>().All(predicate);

        public static T[] Where(Func<T, bool> predicate)
            => [.. EnumerateSubrows<T>().Where(predicate)];

        public static TResult[] Select<TResult>(Func<T, TResult> selector)
            => [.. EnumerateSubrows<T>().Select(selector)];

        public static T? FirstOrNull()
            => EnumerateSubrows<T>().FirstOrNull();

        public static T? FirstOrNull(Func<T, bool> predicate)
            => EnumerateSubrows<T>().Where(predicate).FirstOrNull();
    }

    private static IEnumerable<T> EnumerateSubrows<T>(Dalamud.Game.ClientLanguage? language = null) where T : struct, IExcelSubrow<T>
        => IDataManager.Get().GetSubrowSheet<T>(language: language).SelectMany(r => r);
}
