using System;
using System.Text.RegularExpressions;
using Dalamud.Game;
using ECommons.DalamudServices;
using Lumina.Excel.Sheets;

namespace BozjaBuddyReborn.Game;

/// <summary>
/// How long a Lost Action's buff lasts, so "this one is nearly out" is a fact rather than a guess.
///
/// WHERE IT COMES FROM, after the obvious places turned out to be empty. The Status sheet has no
/// duration column at all - 37 raw columns, not one of them time-valued. Action's only time fields
/// are Cast100ms and Recast100ms, both 25 for Lost Bravery AND Lost Protect even though those buffs
/// run 600s and 1800s. PrimaryCostValue is the holster cost, not a duration (Lost Protect, the
/// LONGER of the two, has cost value 0). No sheet anywhere is keyed by status id and carries a
/// duration.
///
/// It is in ActionTransient.Description - the tooltip text - as a literal line:
///     Action 20713, Lost Bravery: "Increases damage dealt by an ally or self by 5%.\nDuration: 600s"
///     Action 20709, Lost Protect: "Applies a barrier ... physical damage taken by 10%.\nDuration: 30m"
/// which is parsed here. 600s is ten minutes, which is the number the request quoted for Bravery,
/// so the source agrees with the player-visible truth.
///
/// TWO HAZARDS, BOTH HANDLED. The text is LOCALISED - the same row reads "効果時間：600秒" on a
/// Japanese client - so the sheet is fetched with an explicit English language rather than the
/// client's, which Dalamud allows regardless of what the game is running in. And 13 of the 99 rows
/// carry more than one Duration line (Lost Swift has three: 10s, 60s, 30s), where the FIRST is the
/// action's own effect and the rest belong to secondary statuses - so only the first is taken.
///
/// PARSED, NOT TYPED IN, which is the point: a balance patch that changes a duration changes this
/// with it. The cost is a dependency on tooltip prose, so a row that fails to parse returns 0 and
/// every caller treats 0 as "no duration known" and declines to reason about how far through it is.
/// </summary>
public static class LostActionDurations
{
    /// <summary>
    /// "Duration: 600s" / "Duration: 30m" / "Duration: 3h".
    ///
    /// Anchored on the English wording deliberately - the sheet is read in English for exactly this
    /// reason. Matching a bare number-plus-unit instead would happily read "by 5%" or a potency
    /// figure out of the same sentence.
    /// </summary>
    private static readonly Regex DurationLine =
        new(@"Duration:\s*(\d+)\s*([smh])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Total seconds this action's effect runs for, or 0 when the description does not say.
    ///
    /// Zero is the common and correct answer for a great many rows: every Essence (which lasts
    /// until you die or leave), and every instant - Lost Cure, Lost Dispel, Lost Slash, Lost Arise,
    /// Dynamis Dice. None of those has a "how far through it is" to compute.
    /// </summary>
    public static float Seconds(uint actionId)
    {
        if (actionId == 0)
            return 0f;

        try
        {
            // English explicitly. The client may be running in any language and this parse is
            // anchored on an English word.
            var sheet = Svc.Data.GetExcelSheet<ActionTransient>(ClientLanguage.English);
            var row = sheet?.GetRowOrDefault(actionId);
            if (row == null)
                return 0f;

            var text = row.Value.Description.ExtractText();
            if (text.Length == 0)
                return 0f;

            var m = DurationLine.Match(text);
            if (!m.Success)
                return 0f;

            if (!int.TryParse(m.Groups[1].Value, out var value) || value <= 0)
                return 0f;

            return char.ToLowerInvariant(m.Groups[2].Value[0]) switch
            {
                'h' => value * 3600f,
                'm' => value * 60f,
                _ => value,
            };
        }
        catch { return 0f; }
    }
}
