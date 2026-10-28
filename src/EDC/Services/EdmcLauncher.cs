using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security;
using EDC.Core;
using Microsoft.Win32;

namespace EDC.Services;

/// <summary>
/// EDMarketConnector, which sends the journal to EDDN (and so to Spansh), EDSM and Inara: started with
/// the game when asked. Without it installed, nothing happens and nothing is shown.
/// </summary>
public static class EdmcLauncher
{
    private const string ProcessName = "EDMarketConnector";
    private const string Exe = "EDMarketConnector.exe";
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";

    /// <summary>The executable, or null when EDMarketConnector is not installed.</summary>
    public static string? Find()
    {
        try
        {
            return Pick(Candidates(), File.Exists);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or ArgumentException)
        {
            AppLog.Write($"EDMarketConnector introuvable : {ex.Message}");
            return null;
        }
    }

    internal static string? Pick(IEnumerable<string> candidates, Func<string, bool> exists) => candidates.FirstOrDefault(exists);

    internal static bool ShouldLaunch(bool asked, string? exe, bool running) => asked && exe is not null && !running;

    /// <summary>Starts EDMarketConnector if asked, installed and not running yet; a failure only goes to the log.</summary>
    public static void LaunchWithGame(bool asked)
    {
        if (!asked)
            return;

        var exe = Find();
        if (!ShouldLaunch(asked, exe, IsRunning()))
            return;

        try
        {
            Process.Start(new ProcessStartInfo(exe!) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe!)! });
            AppLog.Write($"EDMarketConnector lancé avec le jeu : {exe}");
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException)
        {
            AppLog.Write($"EDMarketConnector non lancé : {ex.Message}");
        }
    }

    private static bool IsRunning()
    {
        var processes = Process.GetProcessesByName(ProcessName);
        foreach (var process in processes)
            process.Dispose();
        return processes.Length > 0;
    }

    // Where its installer puts it, then wherever its uninstall entry says it went.
    private static IEnumerable<string> Candidates()
    {
        foreach (var folder in new[] { Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.ProgramFiles })
            yield return Path.Combine(Environment.GetFolderPath(folder), "EDMarketConnector", Exe);

        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
        foreach (var location in InstallLocations(hive, view))
            yield return Path.Combine(location, Exe);
    }

    private static List<string> InstallLocations(RegistryHive hive, RegistryView view)
    {
        var found = new List<string>();
        try
        {
            using var root = RegistryKey.OpenBaseKey(hive, view);
            using var uninstall = root.OpenSubKey(UninstallKey);
            if (uninstall is null)
                return found;

            foreach (var name in uninstall.GetSubKeyNames())
            {
                using var entry = uninstall.OpenSubKey(name);
                if (entry?.GetValue("DisplayName") is string display && display.Contains("Market Connector", StringComparison.OrdinalIgnoreCase)
                    && entry.GetValue("InstallLocation") is string { Length: > 0 } location)
                    found.Add(location);
            }
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            // An entry we may not read is one fewer place to look.
        }

        return found;
    }
}
