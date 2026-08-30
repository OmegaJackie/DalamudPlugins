using AutoFATE.Core;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using ECommons.ImGuiMethods;

namespace AutoFATE.UI;

/// <summary>
/// Header chips shared by every window that can start a run. Multibox state used to live only in
/// MainWindow, so starting a run from anywhere else left no indication of role or sync at all.
/// </summary>
internal static class StatusChips {
    internal static void Chip(string text, EzColor background, EzColor textColor) {
        using var chipColor = ImRaii.PushColor(ImGuiCol.Button, (uint)background)
            .Push(ImGuiCol.ButtonHovered, (uint)background)
            .Push(ImGuiCol.ButtonActive, (uint)background)
            .Push(ImGuiCol.Text, (uint)textColor);
        ImGui.Button(text);
    }

    /// <summary>Draws nothing when this character has no multibox role, so callers can place it
    /// unconditionally. Owns its own SameLine for the same reason.</summary>
    internal static void Multibox(FateModule module, bool sameLine = true) {
        if (module.Multibox.Role == MultiboxRole.Off)
            return;

        string label;
        bool healthy;
        if (module.Multibox.Role == MultiboxRole.Host) {
            var clients = module.Multibox.ConnectedClients;
            var synced = clients.Count(c => c.Responding && c.Following);
            label = $"Host: {synced}/{clients.Count} synced";
            healthy = clients.Count > 0 && synced == clients.Count;
        }
        else {
            healthy = module.Multibox.IsFollowingHost;
            label = healthy
                ? $"Client: synced to {module.Multibox.HostName ?? "host"}"
                : $"Client: {module.Multibox.StatusText}";
        }

        if (sameLine)
            ImGui.SameLine();
        Chip(label, healthy ? Colors.ChipGold : Colors.ChipMuted, Colors.Grey2);
        ImGui.TooltipOnHover("Multibox sync status. Set this character's role with '/af role <host|client|off>'.");
    }
}
