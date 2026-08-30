using System.Numerics;
using BozjaBuddyReborn.Game;
using BozjaBuddyReborn.Relic;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using ECommons.DalamudServices;

namespace BozjaBuddyReborn.Windows;

/// <summary>
/// Resistance-relic progress: which stage you are on, whether its quest is done, and how many
/// of each material you are holding.
///
/// Scoped to relic progression only - no fragments, no Lost Action inventory, no field notes.
/// </summary>
public sealed class RelicWindow : Window
{
    private static readonly Vector4 Green = new(0.40f, 0.85f, 0.40f, 1f);
    private static readonly Vector4 Yellow = new(0.95f, 0.80f, 0.30f, 1f);
    private static readonly Vector4 Grey = new(0.70f, 0.70f, 0.70f, 1f);
    private static readonly Vector4 Blue = new(0.50f, 0.75f, 1.00f, 1f);

    private readonly RelicTracker _tracker;
    private readonly Configuration _config;
    private bool _showAllStages;

    public RelicWindow(RelicTracker tracker, Configuration config)
        : base("Resistance Relic###BozjaBuddyRebornRelic")
    {
        _tracker = tracker;
        _config = config;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(520, 320),
            MaximumSize = new Vector2(1400, 1200),
        };
    }

    public override void Draw()
    {
        DrawUnlocks();
        ImGui.Separator();

        ImGui.Checkbox("Show every stage", ref _showAllStages);
        ImGui.SameLine();
        ImGui.TextColored(Grey, "otherwise only the stage you are on plus what is still outstanding");
        ImGui.Separator();

        if (_showAllStages)
            DrawAllStages();
        else
            DrawCurrent();
    }

    private void DrawUnlocks()
    {
        var bozja = RelicTracker.BozjaUnlocked;
        var zadnor = RelicTracker.ZadnorUnlocked;

        ImGui.TextColored(bozja ? Green : Yellow,
            bozja ? "Bozjan Southern Front unlocked" : "Bozjan Southern Front LOCKED (Hail to the Queen)");
        ImGui.TextColored(zadnor ? Green : Yellow,
            zadnor ? "Zadnor unlocked" : "Zadnor LOCKED (A New Playing Field)");
    }

    private void DrawCurrent()
    {
        var current = _tracker.CurrentStage();
        if (current is not { } stage)
        {
            ImGui.TextColored(Green, "Every tracked stage is complete.");
            return;
        }

        DrawStage(stage, expanded: true);

        ImGui.Separator();
        ImGui.TextColored(Blue, "Still outstanding across all stages");
        ImGui.TextColored(Grey, "The Bozja grind feeds several stages at once, so this is the real shopping list.");

        var outstanding = _tracker.OutstandingMaterials();
        if (outstanding.Count == 0)
        {
            ImGui.TextColored(Green, "Nothing outstanding.");
            return;
        }

        if (!ImGui.BeginTable("##bbr_outstanding", 4,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
            return;

        ImGui.TableSetupColumn("Material", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Held", ImGuiTableColumnFlags.WidthFixed, 100);
        ImGui.TableSetupColumn("Farm", ImGuiTableColumnFlags.WidthFixed, 70);
        ImGui.TableSetupColumn("Where", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableHeadersRow();

        foreach (var m in outstanding)
        {
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(m.Name);

            ImGui.TableNextColumn();
            ImGui.TextColored(Yellow, $"{m.Held} / {m.Required}");

            // The zone-targeted farm button. Only offered for materials this plugin can
            // actually route to - alliance-raid and deep-dungeon drops get no button, because
            // pretending we could farm them would be a lie.
            ImGui.TableNextColumn();
            var drop = ZoneDrops.For(m.ItemId);
            if (drop is { } d)
            {
                var active = _config.FarmMaterialItemId == m.ItemId;
                if (active)
                {
                    if (ImGui.Button($"Stop##farm{m.ItemId}"))
                    {
                        _config.FarmMaterialItemId = 0;
                        ConfigSaver.Save(_config);
                    }
                }
                else if (ImGui.Button($"Farm##farm{m.ItemId}"))
                {
                    _config.FarmMaterialItemId = m.ItemId;
                    ConfigSaver.Save(_config);
                }

                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(
                        $"Restrict the runner to {d.Describe()}.\n" +
                        "Objectives elsewhere in the zone will be skipped.");
            }
            else
            {
                ImGui.TextColored(Grey, "-");
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Not farmed in the field zones - this plugin cannot route to it.");
            }

            ImGui.TableNextColumn();
            // Prefer the precise region/activity line over the stage's prose blurb.
            ImGui.TextColored(Grey, drop is { } dd ? dd.Describe() : m.Source);
        }

        ImGui.EndTable();

        if (_config.FarmMaterialItemId != 0 && ZoneDrops.For(_config.FarmMaterialItemId) is { } active2)
        {
            ImGui.Separator();
            ImGui.TextColored(Blue, $"Farming: {active2.Describe()}");
            ImGui.SameLine();
            if (ImGui.SmallButton("Clear##farmclear"))
            {
                _config.FarmMaterialItemId = 0;
                ConfigSaver.Save(_config);
            }
        }
    }

    private void DrawAllStages()
    {
        foreach (var stage in _tracker.ReadAll())
            DrawStage(stage, expanded: false);
    }

    private static void DrawStage(StageProgress p, bool expanded)
    {
        var stage = p.Stage;

        var statusColour = p.QuestComplete ? Green : p.QuestAccepted ? Yellow : Grey;
        var statusText = p.QuestComplete
            ? (stage.OneTime ? "done" : "tier unlocked")
            : p.QuestAccepted ? $"in progress (step {p.QuestSequence})" : "not started";

        var header = $"{stage.Order}. {stage.Name}";
        if (stage.ItemLevel.Length > 0)
            header += $"  (i{stage.ItemLevel})";
        if (stage.OneTime)
            header += "  [one-time]";

        ImGui.SetNextItemOpen(expanded, ImGuiCond.FirstUseEver);
        if (!ImGui.CollapsingHeader($"{header}###stage{stage.Order}"))
        {
            ImGui.SameLine();
            ImGui.TextColored(statusColour, statusText);
            return;
        }

        ImGui.TextColored(statusColour, $"{stage.QuestName} - {statusText}");
        ImGui.TextColored(Grey, stage.Note);

        if (stage.Materials.Count > 0)
        {
            if (ImGui.BeginTable($"##mats{stage.Order}", 3,
                    ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
            {
                ImGui.TableSetupColumn("Material", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Held", ImGuiTableColumnFlags.WidthFixed, 130);
                ImGui.TableSetupColumn("Where", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableHeadersRow();

                foreach (var m in p.Materials)
                {
                    ImGui.TableNextRow();

                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(m.Name);

                    ImGui.TableNextColumn();
                    ImGui.TextColored(m.Satisfied ? Green : Yellow, $"{m.Held} / {m.Required}");
                    ImGui.SameLine();
                    ImGui.ProgressBar(m.Fraction, new Vector2(60, 0), "");

                    ImGui.TableNextColumn();
                    ImGui.TextColored(Grey, m.Source);
                }

                ImGui.EndTable();
            }

            ImGui.TextColored(p.MaterialsReady ? Green : Grey,
                p.MaterialsReady ? "Materials ready - go turn it in." : "Materials still short.");
        }

        ImGui.Spacing();
    }
}
