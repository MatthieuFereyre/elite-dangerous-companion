using System.IO;

namespace EDC.Core;

/// <summary>
/// The player's journal: each track, each switch and its reason, in the user's data folder (<see cref="AppPaths"/>).
/// </summary>
public static class AppLog
{
    private static readonly Lock Gate = new();

    public static string? FilePath { get; set; }

    /// <summary>Also sends each line to this sink, for the replay mode and tests.</summary>
    public static Action<string>? Echo { get; set; }

    public static void Write(string text)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {text}";
        Echo?.Invoke(line);
        if (FilePath is null)
            return;

        try
        {
            lock (Gate)
                File.AppendAllText(FilePath, line + "\r\n");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The log must never be what stops the music.
        }
    }
}
