using System.Collections.Generic;

namespace BozjaBuddyReborn.Relic;

/// <summary>One relic material and how many the current stage needs.</summary>
public readonly record struct RelicMaterial(uint ItemId, string Fallback, int Required, string Source);

/// <summary>One upgrade stage of the Resistance (Bozja) relic line.</summary>
public readonly record struct RelicStage(
    int Order,
    string Name,
    uint QuestId,
    ushort QuestIdMasked,
    string QuestName,
    string ItemLevel,
    bool OneTime,
    IReadOnlyList<RelicMaterial> Materials,
    string Note);

/// <summary>
/// The Resistance Weapons (Shadowbringers / Bozja) relic line.
///
/// Quest ids are full Quest-sheet row ids (they include the 0x10000 base); the masked ushort
/// the runtime uses is stored alongside. QuestManager.IsQuestComplete(uint) masks for you,
/// but anything indexing QuestWork directly wants the masked value - both are kept so
/// neither call site has to guess.
///
/// Every id here was cross-checked against Quest.csv by exact name match, and every item id
/// against Item.csv. Deliberately scoped to RELIC progression only - no fragment, Lost
/// Action, or field-note tracking, which the player asked to leave out.
///
/// Stages 5 and 7 are ONE-TIME account-wide grinds, not per-weapon; the rest are repeatable
/// quests that must be redone for every additional relic. <see cref="RelicStage.OneTime"/>
/// carries that distinction because it changes what "done" means.
/// </summary>
public static class ResistanceRelic
{
    // --- Material item ids (verified against Item.csv) ---
    public const uint ThavnairianScalepowder = 30273;
    public const uint TorturedMemory = 31573;
    public const uint SorrowfulMemory = 31574;
    public const uint HarrowingMemory = 31575;
    public const uint BitterMemory = 31576;
    public const uint LoathsomeMemory = 32956;
    public const uint HauntingMemory = 32957;
    public const uint VexatiousMemory = 32958;
    public const uint TimewornArtifact = 32959;
    public const uint CompactAxle = 33757;
    public const uint CompactSpring = 33758;
    public const uint BattlesForTheRealm = 33759;
    public const uint BeyondTheRift = 33760;
    public const uint BleakMemory = 33763;
    public const uint LuridMemory = 33764;
    public const uint RawEmotion = 33767;

    /// <summary>Quest that unlocks the Bozjan Southern Front field operation.</summary>
    public const uint HailToTheQueenQuest = 69370;

    /// <summary>Quest that unlocks the Zadnor field operation.</summary>
    public const uint ANewPlayingFieldQuest = 69620;

    public static readonly IReadOnlyList<RelicStage> Stages =
    [
        new RelicStage(
            Order: 1,
            Name: "Resistance Weapon (base)",
            QuestId: 69380, QuestIdMasked: 3844, QuestName: "Fire in the Forge",
            ItemLevel: "485",
            OneTime: false,
            Materials:
            [
                new RelicMaterial(ThavnairianScalepowder, "Thavnairian Scalepowder", 4,
                    "1,000 Poetics - only for EXTRA weapons via Resistance Is (Not) Futile"),
            ],
            Note: "First weapon is free on completing Fire in the Forge. Unlock chain from Zlatan in " +
                  "Gangos: Hail to the Queen -> Path to the Past -> The Bozja Incident -> Fire in the Forge. " +
                  "Requires a Lv80 combat job and the ShB MSQ."),

        new RelicStage(
            Order: 2,
            Name: "Augmented Resistance Weapon",
            QuestId: 69506, QuestIdMasked: 3970, QuestName: "For Want of a Memory",
            ItemLevel: "500",
            OneTime: false,
            Materials:
            [
                new RelicMaterial(TorturedMemory, "Tortured Memory of the Dying", 20, "Bozja skirmishes / CEs"),
                new RelicMaterial(SorrowfulMemory, "Sorrowful Memory of the Dying", 20, "Bozja skirmishes / CEs"),
                new RelicMaterial(HarrowingMemory, "Harrowing Memory of the Dying", 20, "Bozja skirmishes / CEs"),
            ],
            Note: "Repeatable at Zlatan. Memories also drop from gold-rated Heavensward FATEs."),

        new RelicStage(
            Order: 3,
            Name: "Recollection Weapon",
            QuestId: 69507, QuestIdMasked: 3971, QuestName: "The Will to Resist",
            ItemLevel: "500",
            OneTime: false,
            Materials:
            [
                new RelicMaterial(BitterMemory, "Bitter Memory of the Dying", 6,
                    "Lv60 HW dungeons synced (1), Leveling roulette (1/day), Bozja CEs"),
            ],
            Note: "Repeatable at Zlatan. Also unlocks the Replica Resistance Weapons vendor (Regana)."),

        new RelicStage(
            Order: 4,
            Name: "Law's Order Weapon",
            QuestId: 69574, QuestIdMasked: 4038, QuestName: "Change of Arms",
            ItemLevel: "510",
            OneTime: false,
            Materials:
            [
                new RelicMaterial(LoathsomeMemory, "Loathsome Memory of the Dying", 15,
                    "Castrum Lacus Litore (5), Crystal Tower raids (1), Bozja CEs (1)"),
            ],
            Note: "Gated behind Resistance Rank 10, clearing Castrum Lacus Litore, then the Save the " +
                  "Queen story quests ending in In the Queen's Image (needs a Delubrum Reginae clear)."),

        new RelicStage(
            Order: 5,
            Name: "The Resistance Remembers (one-time)",
            QuestId: 69575, QuestIdMasked: 4039, QuestName: "The Resistance Remembers",
            ItemLevel: "",
            OneTime: true,
            Materials:
            [
                new RelicMaterial(HauntingMemory, "Haunting Memory of the Dying", 18,
                    "Shadow of Mhach raids (3 each), Gyr Abania FATEs"),
                new RelicMaterial(VexatiousMemory, "Vexatious Memory of the Dying", 18,
                    "Return to Ivalice raids (3 each), Othard FATEs"),
            ],
            Note: "Done ONCE account-wide, not per weapon. Unlocks augmenting Law's Order weapons."),

        new RelicStage(
            Order: 6,
            Name: "Augmented Law's Order Weapon",
            QuestId: 69576, QuestIdMasked: 4040, QuestName: "A New Path of Resistance",
            ItemLevel: "515",
            OneTime: false,
            Materials:
            [
                new RelicMaterial(TimewornArtifact, "Timeworn Artifact", 15,
                    "Delubrum Reginae (3 per clear), Palace of the Dead drops"),
            ],
            Note: "Requires The Resistance Remembers first. You allocate substats on turn-in; " +
                  "reallocation later costs Aetherial Sealant x4 (400 Poetics)."),

        new RelicStage(
            Order: 7,
            Name: "A Done Deal (one-time, unlocks Blade's)",
            QuestId: 69636, QuestIdMasked: 4100, QuestName: "A Done Deal",
            ItemLevel: "",
            OneTime: true,
            Materials:
            [
                new RelicMaterial(CompactAxle, "Compact Axle", 30, "Zadnor skirmishes / CEs (Spare Parts)"),
                new RelicMaterial(CompactSpring, "Compact Spring", 30, "Zadnor skirmishes / CEs (Spare Parts)"),
                new RelicMaterial(BattlesForTheRealm, "A Day in the Life: Battles for the Realm", 30,
                    "Zadnor skirmishes / CEs (Tell Me a Story)"),
                new RelicMaterial(BeyondTheRift, "A Day in the Life: Beyond the Rift", 30,
                    "Zadnor skirmishes / CEs (Tell Me a Story)"),
                new RelicMaterial(BleakMemory, "Bleak Memory of the Dying", 30,
                    "Zadnor skirmishes / CEs (A Fond Memory)"),
                new RelicMaterial(LuridMemory, "Lurid Memory of the Dying", 30,
                    "Zadnor skirmishes / CEs (A Fond Memory)"),
            ],
            Note: "Done ONCE account-wide. Chain: A New Playing Field (unlocks Zadnor) -> What Dreams " +
                  "Are Made Of -> Spare Parts + Tell Me a Story + A Fond Memory (all three at once) -> A Done Deal."),

        new RelicStage(
            Order: 8,
            Name: "Blade's Weapon (final)",
            QuestId: 69637, QuestIdMasked: 4101, QuestName: "Irresistible",
            ItemLevel: "535",
            OneTime: false,
            Materials:
            [
                new RelicMaterial(RawEmotion, "Raw Emotion", 15,
                    "The Dalriada (3), Delubrum Reginae (2), Lv70 SB dungeons synced (1), Heaven-on-High"),
            ],
            Note: "Requires A Done Deal first. Final model and glow; you allocate substats on turn-in."),
    ];
}
