using EDC.Core;
using Microsoft.Win32;

namespace EDC.Services;

/// <summary>
/// The per-user "Run" key, which starts the player at log on. Ticked from the tray menu of a player
/// started by hand: one started from the Claude desktop app writes to a private copy of the registry.
/// </summary>
public static class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "EDC";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
        return key?.GetValue(ValueName) is string { Length: > 0 };
    }

    public static void SetEnabled(bool enabled)
    {
        if (Environment.ProcessPath is not { } exe)
            return;

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (enabled)
                key.SetValue(ValueName, $"\"{exe}\"");
            else
                key.DeleteValue(ValueName, throwOnMissingValue: false);

            AppLog.Write(enabled ? $"lancement à l'ouverture de session : {exe}" : "lancement à l'ouverture de session retiré");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            AppLog.Write($"lancement à l'ouverture de session non modifié : {ex.Message}");
        }
    }
}
