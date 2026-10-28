using System.IO;

namespace EDC.Core;

/// <summary>
/// The player's files, in the user's profile: <c>Saved Games\Elite Dangerous Companion</c>, next to
/// the game's journals, or the folder given by <c>--data-dir</c>. Not in AppData: a program started
/// from a Microsoft Store app (Claude Desktop among them) writes there into a private copy.
/// </summary>
public sealed class AppPaths(string folder)
{
    public static string DefaultFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Saved Games", "Elite Dangerous Companion");

    public string Folder { get; } = Path.GetFullPath(folder);
    public string SettingsFile => Path.Combine(Folder, "settings.json");
    public string LibraryFile => Path.Combine(Folder, "library.json");
    public string LogFile => Path.Combine(Folder, "edc.log");

    public static AppPaths Resolve(string? dataDir) => new(string.IsNullOrWhiteSpace(dataDir) ? DefaultFolder : dataDir);

    /// <summary>Makes the folder, and an empty library on a first start; a library already there is never touched.</summary>
    public void Prepare()
    {
        Directory.CreateDirectory(Folder);
        if (!File.Exists(LibraryFile))
            File.WriteAllText(LibraryFile, "{\r\n  \"folders\": [],\r\n  \"tracks\": []\r\n}\r\n");
    }
}
