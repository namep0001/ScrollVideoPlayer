namespace ScrollVideoPlayer.Models;

public sealed class AppSettings
{
    public string LastFolder { get; set; } = string.Empty;
    public string LastVideo { get; set; } = string.Empty;
    public bool IncludeSubfolders { get; set; } = true;
    public bool DeletePermanently { get; set; }
    public bool ConfirmBeforeDelete { get; set; }
    public bool AutoPlayNext { get; set; } = true;
    public bool LoopPlaylist { get; set; }
    public bool Shuffle { get; set; }
    public bool RememberLastFolder { get; set; } = true;
    public string SortMode { get; set; } = "Name";

    public AppSettings Copy() => new()
    {
        LastFolder = LastFolder,
        LastVideo = LastVideo,
        IncludeSubfolders = IncludeSubfolders,
        DeletePermanently = DeletePermanently,
        ConfirmBeforeDelete = ConfirmBeforeDelete,
        AutoPlayNext = AutoPlayNext,
        LoopPlaylist = LoopPlaylist,
        Shuffle = Shuffle,
        RememberLastFolder = RememberLastFolder,
        SortMode = SortMode
    };
}
