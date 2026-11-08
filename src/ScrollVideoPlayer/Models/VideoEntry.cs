using System.IO;
namespace ScrollVideoPlayer.Models;

public sealed class VideoEntry
{
    public required string FullPath { get; init; }
    public required string FileName { get; init; }
    public string FolderName => Path.GetDirectoryName(FullPath) ?? string.Empty;
}
