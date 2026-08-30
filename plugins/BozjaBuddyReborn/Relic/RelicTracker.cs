using System.Collections.Generic;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;

namespace BozjaBuddyReborn.Relic;

/// <summary>Live progress for one material of the current stage.</summary>
public readonly record struct MaterialProgress(
    uint ItemId,
    string Name,
    int Held,
    int Required,
    string Source)
{
    public bool Satisfied => Held >= Required;
    public int Remaining => Required - Held > 0 ? Required - Held : 0;
    public float Fraction => Required <= 0 ? 1f : (float)Held / Required;
}

/// <summary>Live progress for one relic stage.</summary>
public readonly record struct StageProgress(
    RelicStage Stage,
    bool QuestComplete,
    byte QuestSequence,
    bool QuestAccepted,
    IReadOnlyList<MaterialProgress> Materials)
{
    public bool MaterialsReady
    {
        get
        {
            foreach (var m in Materials)
                if (!m.Satisfied)
                    return false;
            return true;
        }
    }

    /// <summary>
    /// A one-time stage is finished for good once its quest is complete. A repeatable stage's
    /// quest completion only means "this tier has been reached at least once", so it is never
    /// treated as terminal - you can always forge another.
    /// </summary>
    public bool Finished => Stage.OneTime && QuestComplete;
}

/// <summary>
/// Tracks Resistance-relic progression: which stage the character is on, whether that
/// stage's quest is done, and how many of each material they are holding.
///
/// Progress is read from the game, never persisted - a stale cached count is worse than no
/// count. Quest state comes from QuestManager (the authority), item counts from
/// InventoryManager. Both are live-memory reads, so this must run on the Framework thread.
///
/// Scope is deliberately relic-only: no fragments, no Lost Action inventory, no field notes.
/// </summary>
public sealed unsafe class RelicTracker
{
    private readonly Dictionary<uint, string> _itemNames = [];

    /// <summary>Is a quest complete? Pass the full sheet id - the API masks it internally.</summary>
    public static bool QuestComplete(uint questId)
    {
        try { return QuestManager.IsQuestComplete(questId); }
        catch { return false; }
    }

    /// <summary>Current step of an accepted quest; 0 when not active.</summary>
    public static byte QuestSequence(uint questId)
    {
        try { return QuestManager.GetQuestSequence(questId); }
        catch { return 0; }
    }

    public static bool QuestAccepted(uint questId)
    {
        try
        {
            var qm = QuestManager.Instance();
            return qm != null && qm->IsQuestAccepted(questId);
        }
        catch { return false; }
    }

    /// <summary>How many of an item the character is holding (bags, including armoury).</summary>
    public static int ItemCount(uint itemId)
    {
        try
        {
            var im = InventoryManager.Instance();
            return im == null ? 0 : im->GetInventoryItemCount(itemId);
        }
        catch { return 0; }
    }

    /// <summary>True once the Bozjan Southern Front has been unlocked.</summary>
    public static bool BozjaUnlocked => QuestComplete(ResistanceRelic.HailToTheQueenQuest);

    /// <summary>True once Zadnor has been unlocked.</summary>
    public static bool ZadnorUnlocked => QuestComplete(ResistanceRelic.ANewPlayingFieldQuest);

    /// <summary>Localised item name, resolved lazily from the Item sheet.</summary>
    public string ItemName(uint itemId, string fallback)
    {
        if (_itemNames.TryGetValue(itemId, out var cached))
            return cached;

        try
        {
            var sheet = Svc.Data.GetExcelSheet<Item>();
            var row = sheet?.GetRowOrDefault(itemId);
            if (row != null)
            {
                var name = row.Value.Name.ExtractText();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    _itemNames[itemId] = name;
                    return name;
                }
            }
        }
        catch { /* fall through to the fallback */ }

        // Do NOT cache the fallback - the sheet may simply not be ready yet.
        return fallback;
    }

    /// <summary>Progress for every stage of the line, in order.</summary>
    public List<StageProgress> ReadAll()
    {
        var result = new List<StageProgress>(ResistanceRelic.Stages.Count);
        foreach (var stage in ResistanceRelic.Stages)
            result.Add(Read(stage));
        return result;
    }

    public StageProgress Read(RelicStage stage)
    {
        var materials = new List<MaterialProgress>(stage.Materials.Count);
        foreach (var m in stage.Materials)
        {
            materials.Add(new MaterialProgress(
                ItemId: m.ItemId,
                Name: ItemName(m.ItemId, m.Fallback),
                Held: ItemCount(m.ItemId),
                Required: m.Required,
                Source: m.Source));
        }

        return new StageProgress(
            Stage: stage,
            QuestComplete: QuestComplete(stage.QuestId),
            QuestSequence: QuestSequence(stage.QuestId),
            QuestAccepted: QuestAccepted(stage.QuestId),
            Materials: materials);
    }

    /// <summary>
    /// The stage the character should be working on: the first stage that is not finished.
    ///
    /// "Finished" only terminates on the two ONE-TIME stages - a repeatable stage's completed
    /// quest means the tier has been unlocked, not that there is nothing left to do, so the
    /// walk does not stop there. This mirrors how the line actually plays: once you are past
    /// the one-time gates, the current stage is whichever repeatable tier you are feeding.
    /// </summary>
    public StageProgress? CurrentStage()
    {
        StageProgress? firstUnfinished = null;
        foreach (var stage in ResistanceRelic.Stages)
        {
            var p = Read(stage);
            if (p.Finished)
                continue;

            // A repeatable stage whose quest is done AND whose successor one-time gate is also
            // done is behind us; keep walking. Otherwise this is where the work is.
            firstUnfinished ??= p;

            if (!p.QuestComplete)
                return p;
        }
        return firstUnfinished;
    }

    /// <summary>
    /// Which materials the character should be farming right now, across every stage that is
    /// still short. Useful because the Bozja grind feeds several stages at once - the memories
    /// for the augment, the artifacts for Law's Order, and the Zadnor one-time set all drop
    /// from the same engagements.
    /// </summary>
    public List<MaterialProgress> OutstandingMaterials()
    {
        var outstanding = new List<MaterialProgress>();
        foreach (var stage in ResistanceRelic.Stages)
        {
            var p = Read(stage);
            if (p.Finished)
                continue;

            foreach (var m in p.Materials)
                if (!m.Satisfied)
                    outstanding.Add(m);
        }
        return outstanding;
    }
}
