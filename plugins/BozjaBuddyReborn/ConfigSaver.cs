using System;
using ECommons.DalamudServices;

namespace BozjaBuddyReborn;

/// <summary>
/// Saves the plugin configuration without ever letting the save take the plugin down.
///
/// WHY THIS EXISTS, AND WHY IT MATTERS MOST TO MULTIBOXERS. Dalamud does not write plugin configs
/// straight to disk - DalamudPluginInterface.SavePluginConfig goes through ReliableFileStorage,
/// which is backed by a SQLite database inside the XIVLauncher folder. Every game client running
/// off the same XIVLauncher installation shares that one database. So with several boxes running,
/// two clients saving at the same moment is routine, and the loser gets
/// "SQLite.SQLiteException: database is locked".
///
/// Uncaught, that is not a lost setting - it is an UNLOAD ERROR. Dispose() saved the config, the
/// exception escaped Dispose, and Dalamud reported "Error while unloading BozjaBuddyReborn"
/// followed by its own warning that the plugin may be in an inconsistent state and the game should
/// be restarted. A contended write during teardown must never cost more than the write itself.
///
/// A short retry is worth it because the lock is held for milliseconds - another client finishing
/// its own transaction - so the second attempt almost always lands. After that the save is
/// abandoned deliberately: settings are re-saved on the next change, and a config write is never
/// worth blocking a frame or failing an unload for.
/// </summary>
public static class ConfigSaver
{
    /// <summary>How many times to retry a contended write before giving up.</summary>
    private const int Attempts = 3;

    /// <summary>
    /// Persist the configuration. Never throws.
    /// </summary>
    /// <returns>True when the write landed.</returns>
    public static bool Save(Configuration config)
    {
        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            try
            {
                Svc.PluginInterface.SavePluginConfig(config);
                return true;
            }
            catch (Exception ex)
            {
                // Last go: say so once, at a level the user can actually see, and move on.
                if (attempt == Attempts - 1)
                {
                    Svc.Log.Warning(
                        $"[BozjaBuddyReborn] Could not save settings: {ex.Message}. " +
                        "This is usually another game client writing the shared Dalamud config " +
                        "database at the same moment; the setting will be saved on the next change.");
                    return false;
                }

                // Deliberately a blocking sleep rather than an async wait: this is called from the
                // framework tick, from ImGui draw, and from Dispose, and only the last of those
                // could await anything at all. Two of these at 20ms is imperceptible.
                try { System.Threading.Thread.Sleep(20); }
                catch { /* nothing sensible to do */ }
            }
        }

        return false;
    }
}
