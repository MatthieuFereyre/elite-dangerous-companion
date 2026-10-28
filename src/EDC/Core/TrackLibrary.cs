using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace EDC.Core;

public enum TrackMode { Calm, Combat, Off }

/// <summary>A track; <see cref="File"/> is relative to its folder, so two folders may each have a same name.</summary>
public sealed record Track(string File, string Section, TrackMode Mode, bool Like, string Folder = "")
{
    public string Title => Path.GetFileNameWithoutExtension(File);

    /// <summary>What tells it from every other track: its folder and its file.</summary>
    public string Key => KeyOf(Folder, File);

    internal static string KeyOf(string folder, string file) => folder.Length == 0 ? file : $"{folder}/{file}";
}

/// <summary>A folder of MP3s; switched off, its tracks stay sorted but are not played.</summary>
public sealed record MusicFolder(string Name, string Path, bool Enabled = true);

/// <summary>
/// The tracks of library.json. Every change is applied to a fresh read of the file and written back at
/// once, in the same layout, so a hand edit made meanwhile is kept rather than overwritten.
/// A track without a "folder" belongs to the first folder: the entries written when there was only
/// the one musicRoot stay valid as they are.
/// </summary>
public sealed class TrackLibrary : IDisposable
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        IndentSize = 2,
        NewLine = "\r\n",
        // Keeps « é », « Ç » and apostrophes as they are instead of \u escapes.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>What Media Foundation, which the decks read with, decodes.</summary>
    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase) { ".mp3", ".wav", ".flac", ".m4a", ".wma" };

    private readonly Lock _gate = new();
    private FileSystemWatcher? _watcher;
    private Timer? _reloadDebounce;
    private string _lastWritten = "";

    public TrackLibrary(string path)
    {
        FilePath = Path.GetFullPath(path);
        Load();
    }

    public string FilePath { get; }
    public IReadOnlyList<MusicFolder> Folders { get; private set; } = [];
    public IReadOnlyList<Track> Tracks { get; private set; } = [];

    /// <summary>Raised after any change, ours or a hand edit of the file. May come from a worker thread.</summary>
    public event Action? Changed;

    public Track? Find(string key) => Tracks.FirstOrDefault(t => t.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

    public string FullPath(Track track) => Path.Combine(FolderOf(track)?.Path ?? "", track.File);

    public IEnumerable<Track> Playable(TrackMode mode) =>
        Tracks.Where(t => t.Mode == mode && FolderOf(t) is { Enabled: true } && File.Exists(FullPath(t)));

    private MusicFolder? FolderOf(Track track) =>
        Folders.FirstOrDefault(f => f.Name.Equals(track.Folder, StringComparison.OrdinalIgnoreCase));

    public void SetLike(string key, bool like) => Update(key, entry =>
    {
        if (like)
            entry["like"] = true;
        else
            entry.Remove("like");
    });

    public void SetMode(string key, TrackMode mode) => Update(key, entry => entry["mode"] = ModeName(mode));

    /// <summary>
    /// Adds the folder and its music files, each in the mode of <paramref name="modes"/> (by relative path), calm
    /// otherwise; a folder already there only gets its new files. Named after the directory unless <paramref name="name"/> is given.
    /// </summary>
    public (MusicFolder Folder, int Added) AddFolder(string path, string? name = null, IReadOnlyDictionary<string, TrackMode>? modes = null)
    {
        path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var files = MusicFiles(path);

        MusicFolder folder = null!;
        var added = 0;
        Edit(root =>
        {
            var folders = FoldersOf(root);
            folder = ReadFolders(root).FirstOrDefault(f => f.Path.Equals(path, StringComparison.OrdinalIgnoreCase))!;
            if (folder is null)
            {
                folder = new MusicFolder(UniqueName(name ?? FolderName(path), ReadFolders(root)), path);
                folders.Add(new JsonObject { ["name"] = folder.Name, ["path"] = folder.Path });
            }

            var tracks = root["tracks"]!.AsArray();
            var known = Entries(root).Where(e => e.Folder.Equals(folder.Name, StringComparison.OrdinalIgnoreCase))
                .Select(e => e.File).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var file in files.Where(f => !known.Contains(f)))
            {
                tracks.Add(new JsonObject { ["folder"] = folder.Name, ["file"] = file, ["mode"] = ModeName(modes?.GetValueOrDefault(file) ?? TrackMode.Calm) });
                added++;
            }
        });

        return (folder, added);
    }

    /// <summary>The music files of a folder and its subfolders (MP3, WAV, FLAC, M4A, WMA), relative to it.</summary>
    public static IReadOnlyList<string> MusicFiles(string path) =>
        Directory.EnumerateFiles(path, "*", new EnumerationOptions { RecurseSubdirectories = true })
            .Where(f => AudioExtensions.Contains(Path.GetExtension(f)))
            .Select(f => Path.GetRelativePath(path, f))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>Takes the folder out, and its tracks with their likes and categories.</summary>
    public void RemoveFolder(string name) => Edit(root =>
    {
        var tracks = root["tracks"]!.AsArray();
        foreach (var (entry, _, _) in Entries(root).Where(e => e.Folder.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList())
            tracks.Remove(entry);

        var folders = FoldersOf(root);
        folders.Remove(folders.OfType<JsonObject>().First(f => string.Equals((string?)f["name"], name, StringComparison.OrdinalIgnoreCase)));
    });

    public void SetFolderEnabled(string name, bool enabled) => Edit(root =>
    {
        var folder = FoldersOf(root).OfType<JsonObject>().First(f => string.Equals((string?)f["name"], name, StringComparison.OrdinalIgnoreCase));
        if (enabled)
            folder.Remove("enabled");
        else
            folder["enabled"] = false;
    });

    /// <summary>Re-reads the file when someone else changes it, so a hand edit applies without a restart.</summary>
    public void WatchFile()
    {
        _reloadDebounce = new Timer(_ => ReloadIfChangedOnDisk());
        _watcher = new FileSystemWatcher(Path.GetDirectoryName(FilePath)!, Path.GetFileName(FilePath))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
        };
        FileSystemEventHandler onChange = (_, _) => _reloadDebounce.Change(500, Timeout.Infinite);
        _watcher.Changed += onChange;
        _watcher.Created += onChange;
        _watcher.Renamed += (_, _) => _reloadDebounce.Change(500, Timeout.Infinite);
        _watcher.EnableRaisingEvents = true;
    }

    private void ReloadIfChangedOnDisk()
    {
        try
        {
            lock (_gate)
            {
                if (ReadText() == _lastWritten)
                    return;
                Load();
            }

            AppLog.Write("bibliothèque modifiée à la main, relue");
            Changed?.Invoke();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            AppLog.Write($"bibliothèque illisible, ancienne version gardée : {ex.Message}");
        }
    }

    private void Update(string key, Action<JsonObject> change) => Edit(root =>
    {
        var entry = Entries(root).FirstOrDefault(e => Track.KeyOf(e.Folder, e.File).Equals(key, StringComparison.OrdinalIgnoreCase)).Entry
                    ?? throw new InvalidOperationException(string.Format(Resources.Strings.LibraryMissingTrack, key));
        change(entry);
    });

    private void Edit(Action<JsonObject> change)
    {
        lock (_gate)
        {
            var root = Parse(ReadText());
            change(root);
            Write(root);
            Apply(root);
        }

        Changed?.Invoke();
    }

    /// <summary>The tracks of the file, each with the folder it belongs to.</summary>
    private static IEnumerable<(JsonObject Entry, string Folder, string File)> Entries(JsonObject root)
    {
        var first = ReadFolders(root).FirstOrDefault()?.Name ?? "";
        return root["tracks"]!.AsArray()
            .OfType<JsonObject>()
            .Select(t => (t, (string?)t["folder"] ?? first, (string?)t["file"] ?? ""));
    }

    private static List<MusicFolder> ReadFolders(JsonObject root)
    {
        if (root["folders"] is JsonArray folders)
            return folders.OfType<JsonObject>()
                .Select(f => new MusicFolder((string?)f["name"] ?? "", (string?)f["path"] ?? "",
                    f["enabled"] is not JsonValue enabled || !enabled.TryGetValue(out bool on) || on))
                .Where(f => f.Name.Length > 0)
                .ToList();

        return (string?)root["musicRoot"] is { Length: > 0 } musicRoot ? [new MusicFolder(FolderName(musicRoot), musicRoot)] : [];
    }

    /// <summary>The "folders" array, made in place of the old musicRoot the first time a folder changes.</summary>
    private static JsonArray FoldersOf(JsonObject root)
    {
        if (root["folders"] is JsonArray folders)
            return folders;

        folders = new JsonArray(ReadFolders(root).Select(f => (JsonNode)new JsonObject { ["name"] = f.Name, ["path"] = f.Path }).ToArray());
        var at = root.IndexOf("musicRoot");
        if (at >= 0)
            root.RemoveAt(at);
        root.Insert(Math.Max(at, 0), "folders", folders);
        return folders;
    }

    /// <summary>"deep-space-soundtrack" reads "Deep Space Soundtrack"; a name with capitals stays as it is.</summary>
    public static string FolderName(string path)
    {
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(path)).Replace('-', ' ').Replace('_', ' ');
        return name.Any(char.IsUpper) ? name : System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name);
    }

    private static string UniqueName(string name, IReadOnlyList<MusicFolder> folders)
    {
        var unique = name;
        for (var n = 2; folders.Any(f => f.Name.Equals(unique, StringComparison.OrdinalIgnoreCase)); n++)
            unique = $"{name} ({n})";
        return unique;
    }

    private void Load()
    {
        var text = ReadText();
        Apply(Parse(text));
        _lastWritten = text;
    }

    private void Apply(JsonObject root)
    {
        Folders = ReadFolders(root);
        // Without a section of its own, a track is shown under its folder's name.
        Tracks = Entries(root)
            .Select(e => new Track(
                e.File,
                (string?)e.Entry["section"] ?? e.Folder,
                ParseMode((string?)e.Entry["mode"]),
                e.Entry["like"] is JsonValue like && like.TryGetValue(out bool liked) && liked,
                e.Folder))
            .Where(t => t.File.Length > 0)
            .ToList();
    }

    private void Write(JsonObject root)
    {
        var text = root.ToJsonString(WriteOptions) + "\r\n";
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temp, FilePath, overwrite: true);
        _lastWritten = text;
    }

    private string ReadText()
    {
        // The file may be mid-save by an editor: a couple of retries rather than a failure.
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                using var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                return reader.ReadToEnd();
            }
            catch (IOException) when (attempt < 5)
            {
                Thread.Sleep(100);
            }
        }
    }

    private static JsonObject Parse(string text) =>
        JsonNode.Parse(text) as JsonObject ?? throw new JsonException(Resources.Strings.LibraryNotJson);

    internal static TrackMode ParseMode(string? mode) => mode switch
    {
        "calm" => TrackMode.Calm,
        "combat" => TrackMode.Combat,
        _ => TrackMode.Off,
    };

    internal static string ModeName(TrackMode mode) => mode switch
    {
        TrackMode.Calm => "calm",
        TrackMode.Combat => "combat",
        _ => "off",
    };

    public void Dispose()
    {
        _watcher?.Dispose();
        _reloadDebounce?.Dispose();
    }
}
