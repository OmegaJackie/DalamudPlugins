using System.Collections.Generic;
using ECommons.DalamudServices;
using LuminaDynamicEvent = Lumina.Excel.Sheets.DynamicEvent;

namespace BozjaBuddyReborn.Game;

/// <summary>
/// Static metadata for every Critical Engagement, read from the Lumina DynamicEvent sheet.
///
/// Columns used (EXDSchema latest):
///   Name             - display name
///   EnemyType        - link to DynamicEventEnemyType; row 3 is "Solo Engagement" (a duel)
///   MaxParticipants  - 1 for duels, 24/48 for group engagements
///   Duration         - in minutes
///
/// Resolution is lazy and retried: the sheet can be unavailable for the first frames after
/// load, and latching an empty catalogue would leave every engagement permanently unnamed.
/// </summary>
public sealed class CeCatalog
{
    /// <summary>DynamicEventEnemyType row for "Solo Engagement" - the duel marker.</summary>
    private const uint SoloEngagementEnemyType = 3;

    /// <summary>
    /// DynamicEventType row 4 marks the large-scale battles: The Battle of Castrum Lacus
    /// Litore (row 16) and The Dalriada (row 32). Ordinary Critical Engagements are types 1-3.
    /// </summary>
    private const uint LargeScaleEventType = 4;

    public readonly record struct Entry(
        ushort EventId,
        string Name,
        string Description,
        byte MaxParticipants,
        byte DurationMinutes,
        bool IsDuel,
        bool IsLargeScale,
        uint Zone);

    private readonly Dictionary<ushort, Entry> _byId = [];
    private bool _resolved;

    /// <summary>Every catalogued engagement, ordered by row id (Bozja first, then Zadnor).</summary>
    public IEnumerable<Entry> All
    {
        get
        {
            Ensure();
            foreach (var id in BozjaZones.AllCatalogue())
                if (_byId.TryGetValue(id, out var e))
                    yield return e;
        }
    }

    /// <summary>Catalogued engagements for one field zone.</summary>
    public IEnumerable<Entry> ForZone(uint territory)
    {
        Ensure();
        foreach (var id in BozjaZones.CatalogueFor(territory))
            if (_byId.TryGetValue(id, out var e))
                yield return e;
    }

    public bool TryGet(ushort eventId, out Entry entry)
    {
        Ensure();
        return _byId.TryGetValue(eventId, out entry);
    }

    /// <summary>Display name, falling back to the raw id when the sheet has not resolved yet.</summary>
    public string Name(ushort eventId)
        => TryGet(eventId, out var e) && e.Name.Length > 0 ? e.Name : $"Engagement #{eventId}";

    /// <summary>
    /// True for the six 1v1 duels (Aces High, Beast of Man, And the Flames Went Higher,
    /// The Broken Blade, Head of the Snake, Taking the Lyon's Share). They are gated on
    /// notoriety and only one player is chosen, so the controller skips them by default.
    /// </summary>
    public bool IsDuel(ushort eventId) => TryGet(eventId, out var e) && e.IsDuel;

    /// <summary>
    /// True for the two large-scale battles (Castrum Lacus Litore, The Dalriada). They run on
    /// their own long schedule and are normally organised runs, so they are opt-in.
    /// </summary>
    public bool IsLargeScale(ushort eventId) => TryGet(eventId, out var e) && e.IsLargeScale;

    /// <summary>Drop the cache so the next read re-reads the sheet (used on logout/zone reload).</summary>
    public void Invalidate()
    {
        _byId.Clear();
        _resolved = false;
    }

    private void Ensure()
    {
        if (_resolved)
            return;

        try
        {
            var sheet = Svc.Data.GetExcelSheet<LuminaDynamicEvent>();
            if (sheet == null)
                return;

            foreach (var id in BozjaZones.AllCatalogue())
            {
                var row = sheet.GetRowOrDefault(id);
                if (row == null)
                    continue;

                var r = row.Value;
                var name = r.Name.ExtractText();
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                var enemyType = r.EnemyType.RowId;
                var isDuel = enemyType == SoloEngagementEnemyType || r.MaxParticipants == 1;
                var isLargeScale = r.EventType.RowId == LargeScaleEventType;

                _byId[id] = new Entry(
                    EventId: id,
                    Name: name,
                    Description: r.Description.ExtractText(),
                    MaxParticipants: r.MaxParticipants,
                    DurationMinutes: r.Duration,
                    IsDuel: isDuel,
                    IsLargeScale: isLargeScale,
                    Zone: BozjaZones.ZoneOfEvent(id));
            }
        }
        catch
        {
            // Leave unresolved so the next call retries rather than latching an empty map.
            return;
        }

        // Only latch once rows actually landed.
        if (_byId.Count > 0)
            _resolved = true;
    }
}
