using System.IO;

namespace EDC.Core;

/// <summary>
/// A route link given with --route to a second launch, for the first one to load: written in the data
/// folder, then the first one woken, which takes it once.
/// </summary>
public static class RouteRequest
{
    private static string FileOf(AppPaths paths) => Path.Combine(paths.Folder, "route-request.txt");

    public static void Put(AppPaths paths, string link) => File.WriteAllText(FileOf(paths), link);

    public static string? Take(AppPaths paths)
    {
        var file = FileOf(paths);
        if (!File.Exists(file))
            return null;
        var link = File.ReadAllText(file).Trim();
        File.Delete(file);
        return link.Length > 0 ? link : null;
    }
}
