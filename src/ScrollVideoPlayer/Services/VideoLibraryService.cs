using System.Runtime.InteropServices;
using ScrollVideoPlayer.Models;

namespace ScrollVideoPlayer.Services;

public sealed class VideoLibraryService
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".3g2", ".3gp", ".asf", ".avi", ".divx", ".dv", ".f4v", ".flv",
        ".m1v", ".m2t", ".m2ts", ".m4v", ".mkv", ".mov", ".mp2", ".mp4",
        ".mpe", ".mpeg", ".mpg", ".mpv", ".mts", ".mxf", ".nsv", ".ogm",
        ".ogv", ".qt", ".rm", ".rmvb", ".tod", ".ts", ".vob", ".webm",
        ".wmv", ".yuv"
    };

    public IReadOnlyList<VideoEntry> Scan(string rootFolder, bool includeSubfolders, string sortMode)
    {
        if (!Directory.Exists(rootFolder))
            return Array.Empty<VideoEntry>();

        var files = EnumerateVideoFiles(rootFolder, includeSubfolders).ToList();

        IEnumerable<string> ordered = sortMode switch
        {
            "Newest first" => files.OrderByDescending(SafeLastWriteTime),
            "Oldest first" => files.OrderBy(SafeLastWriteTime),
            _ => files.OrderBy(path => Path.GetFileName(path), NaturalComparer.Instance)
        };

        return ordered
            .Select(path => new VideoEntry
            {
                FullPath = path,
                FileName = Path.GetFileName(path)
            })
            .ToList();
    }

    private static IEnumerable<string> EnumerateVideoFiles(string rootFolder, bool recursive)
    {
        var pending = new Stack<string>();
        pending.Push(rootFolder);

        while (pending.Count > 0)
        {
            var folder = pending.Pop();

            string[] files;
            try
            {
                files = Directory.GetFiles(folder);
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            catch (IOException)
            {
                continue;
            }

            foreach (var file in files)
            {
                if (SupportedExtensions.Contains(Path.GetExtension(file)))
                    yield return file;
            }

            if (!recursive)
                continue;

            string[] directories;
            try
            {
                directories = Directory.GetDirectories(folder);
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            catch (IOException)
            {
                continue;
            }

            foreach (var directory in directories)
                pending.Push(directory);
        }
    }

    private static DateTime SafeLastWriteTime(string path)
    {
        try { return File.GetLastWriteTimeUtc(path); }
        catch { return DateTime.MinValue; }
    }

    private sealed class NaturalComparer : IComparer<string>
    {
        public static readonly NaturalComparer Instance = new();

        public int Compare(string? x, string? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x is null) return -1;
            if (y is null) return 1;

            try { return StrCmpLogicalW(x, y); }
            catch { return StringComparer.OrdinalIgnoreCase.Compare(x, y); }
        }

        [DllImport("Shlwapi.dll", CharSet = CharSet.Unicode)]
        private static extern int StrCmpLogicalW(string first, string second);
    }
}
