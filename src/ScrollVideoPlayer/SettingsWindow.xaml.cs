using System.Windows;
using ScrollVideoPlayer.Models;

namespace ScrollVideoPlayer;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _workingSettings;

    public AppSettings UpdatedSettings { get; private set; }

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        _workingSettings = settings.Copy();
        UpdatedSettings = _workingSettings.Copy();

        IncludeSubfoldersCheck.IsChecked = _workingSettings.IncludeSubfolders;
        RememberFolderCheck.IsChecked = _workingSettings.RememberLastFolder;
        AutoPlayNextCheck.IsChecked = _workingSettings.AutoPlayNext;
        LoopPlaylistCheck.IsChecked = _workingSettings.LoopPlaylist;
        ShuffleCheck.IsChecked = _workingSettings.Shuffle;
        ConfirmDeleteCheck.IsChecked = _workingSettings.ConfirmBeforeDelete;
        DeleteModeBox.SelectedIndex = _workingSettings.DeletePermanently ? 1 : 0;
        SortModeBox.SelectedIndex = _workingSettings.SortMode switch
        {
            "Newest first" => 1,
            "Oldest first" => 2,
            _ => 0
        };
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        UpdatedSettings = _workingSettings.Copy();
        UpdatedSettings.IncludeSubfolders = IncludeSubfoldersCheck.IsChecked == true;
        UpdatedSettings.RememberLastFolder = RememberFolderCheck.IsChecked == true;
        UpdatedSettings.AutoPlayNext = AutoPlayNextCheck.IsChecked == true;
        UpdatedSettings.LoopPlaylist = LoopPlaylistCheck.IsChecked == true;
        UpdatedSettings.Shuffle = ShuffleCheck.IsChecked == true;
        UpdatedSettings.ConfirmBeforeDelete = ConfirmDeleteCheck.IsChecked == true;
        UpdatedSettings.DeletePermanently = DeleteModeBox.SelectedIndex == 1;
        UpdatedSettings.SortMode = SortModeBox.SelectedIndex switch
        {
            1 => "Newest first",
            2 => "Oldest first",
            _ => "Name"
        };

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
