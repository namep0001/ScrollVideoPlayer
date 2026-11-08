using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Input;
using System.Windows.Threading;
using LibVLCSharp.Shared;
using LibVLCSharp.Shared.Structures;
using Microsoft.VisualBasic.FileIO;
using Microsoft.Win32;
using ScrollVideoPlayer.Models;
using ScrollVideoPlayer.Services;

namespace ScrollVideoPlayer;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<VideoEntry> _videos = new();
    private readonly SettingsService _settingsService = new();
    private readonly VideoLibraryService _libraryService = new();
    private readonly DispatcherTimer _uiTimer;
    private AppSettings _settings;
    private LibVLC? _libVlc;
    private MediaPlayer? _player;
    private Media? _currentMedia;
    private int _currentIndex = -1;
    private bool _suppressSelection;
    private bool _updatingSeek;
    private bool _isSeeking;
    private bool _pointerDown;
    private Point _pointerStart;
    private bool _isFullscreen;
    private WindowStyle _previousWindowStyle;
    private WindowState _previousWindowState;
    private ResizeMode _previousResizeMode;

    public MainWindow()
    {
        InitializeComponent();
        _settings = _settingsService.Load();
        VideoList.ItemsSource = _videos;

        _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _uiTimer.Tick += UiTimer_Tick;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            Core.Initialize();
            _libVlc = new LibVLC();
            _player = new MediaPlayer(_libVlc);
            VideoSurface.MediaPlayer = _player;
            _player.EndReached += Player_EndReached;
            _player.EncounteredError += Player_EncounteredError;
            _player.Volume = Math.Clamp((int)VolumeSlider.Value, 0, 100);
            _uiTimer.Start();

            if (_settings.RememberLastFolder && Directory.Exists(_settings.LastFolder))
                LoadFolder(_settings.LastFolder, _settings.LastVideo);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                "The VLC playback engine could not be initialized.\n\n" + ex.Message +
                "\n\nRe-extract the complete application folder and try again.",
                "Scroll Video Player", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        Focus();
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choose a folder containing videos",
            Multiselect = false
        };

        if (Directory.Exists(_settings.LastFolder))
            dialog.InitialDirectory = _settings.LastFolder;

        if (dialog.ShowDialog(this) == true)
            LoadFolder(dialog.FolderName);
    }

    private void LoadFolder(string folder, string? preferredVideo = null)
    {
        if (!Directory.Exists(folder))
            return;

        try
        {
            var list = _libraryService.Scan(folder, _settings.IncludeSubfolders, _settings.SortMode);
            _videos.Clear();
            foreach (var item in list)
                _videos.Add(item);

            HeaderFolder.Text = folder;
            VideoCountText.Text = $"{_videos.Count} video{(_videos.Count == 1 ? string.Empty : "s")}";
            PlaylistFooter.Text = $"{_videos.Count} items  ·  Scroll ↑ next  ·  Swipe ← delete";

            _settings.LastFolder = folder;
            _settings.LastVideo = string.Empty;
            _settingsService.Save(_settings);

            if (_videos.Count == 0)
            {
                StopAndClearCurrentMedia();
                EmptyState.Visibility = Visibility.Visible;
                ShowStatus("No supported video files were found in this folder.");
                return;
            }

            var startIndex = preferredVideo is null
                ? -1
                : _videos.ToList().FindIndex(v => string.Equals(v.FullPath, preferredVideo, StringComparison.OrdinalIgnoreCase));

            PlayAt(startIndex >= 0 ? startIndex : 0);
            ShowStatus($"Loaded {_videos.Count} videos. Scroll up for next; swipe left to delete.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Could not read this folder.\n\n" + ex.Message,
                "Folder error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void PlayAt(int index)
    {
        if (_player is null || _libVlc is null || index < 0 || index >= _videos.Count)
            return;

        var item = _videos[index];
        try
        {
            _player.Stop();
            _currentMedia?.Dispose();
            _player.SetRate(1.0f);
            _player.AspectRatio = null;
            SpeedButton.Content = "1.0×";
            _currentMedia = new Media(_libVlc, new Uri(item.FullPath, UriKind.Absolute));
            _currentIndex = index;
            _player.Play(_currentMedia);

            EmptyState.Visibility = Visibility.Collapsed;
            _suppressSelection = true;
            VideoList.SelectedIndex = index;
            VideoList.ScrollIntoView(item);
            _suppressSelection = false;

            _settings.LastVideo = item.FullPath;
            _settingsService.Save(_settings);
            PlayPauseButton.Content = "Ⅱ  Pause";
            UpdateTitleAndCount();
            ShowStatus(item.FileName);
        }
        catch (Exception ex)
        {
            ShowStatus($"Could not play {item.FileName}: {ex.Message}");
            _suppressSelection = true;
            VideoList.SelectedIndex = -1;
            _suppressSelection = false;
        }
    }

    private void PlayNext()
    {
        if (_videos.Count == 0)
            return;

        int nextIndex;
        if (_settings.Shuffle && _videos.Count > 1)
        {
            do { nextIndex = Random.Shared.Next(_videos.Count); }
            while (nextIndex == _currentIndex);
        }
        else
        {
            nextIndex = _currentIndex + 1;
        }

        if (nextIndex >= _videos.Count)
        {
            if (_settings.LoopPlaylist)
                nextIndex = 0;
            else
            {
                _player?.Stop();
                PlayPauseButton.Content = "▶  Play";
                ShowStatus("End of folder. Enable Loop playlist in Settings to repeat.");
                return;
            }
        }

        PlayAt(nextIndex);
    }

    private void PlayPrevious()
    {
        if (_videos.Count == 0)
            return;

        if (_player is not null && _player.Time > 3500)
        {
            _player.Time = 0;
            return;
        }

        var previousIndex = _currentIndex - 1;
        if (previousIndex < 0)
            previousIndex = _settings.LoopPlaylist ? _videos.Count - 1 : 0;

        PlayAt(previousIndex);
    }

    private void StopAndClearCurrentMedia()
    {
        _player?.Stop();
        _currentMedia?.Dispose();
        _currentMedia = null;
        _currentIndex = -1;
        _suppressSelection = true;
        VideoList.SelectedIndex = -1;
        _suppressSelection = false;
        PlayPauseButton.Content = "▶  Play";
        ElapsedText.Text = "00:00";
        DurationText.Text = "00:00";
        SeekSlider.Value = 0;
        SeekSlider.Maximum = 1;
        UpdateTitleAndCount();
    }

    private void UpdateTitleAndCount()
    {
        if (_currentIndex >= 0 && _currentIndex < _videos.Count)
            Title = $"{_videos[_currentIndex].FileName} — Scroll Video Player";
        else
            Title = "Scroll Video Player";

        VideoCountText.Text = $"{_videos.Count} video{(_videos.Count == 1 ? string.Empty : "s")}";
    }

    private void UiTimer_Tick(object? sender, EventArgs e)
    {
        if (_player is null)
            return;

        var length = Math.Max(0, _player.Length);
        var time = Math.Max(0, _player.Time);

        _updatingSeek = true;
        SeekSlider.Maximum = Math.Max(1, length);
        if (!_isSeeking)
            SeekSlider.Value = Math.Min(time, SeekSlider.Maximum);
        _updatingSeek = false;

        ElapsedText.Text = FormatTime(time);
        DurationText.Text = FormatTime(length);

        PlayPauseButton.Content = _player.IsPlaying ? "Ⅱ  Pause" : "▶  Play";
        MuteButton.Content = _player.Mute ? "🔇" : "🔊";
    }

    private static string FormatTime(long milliseconds)
    {
        if (milliseconds <= 0)
            return "00:00";

        var span = TimeSpan.FromMilliseconds(milliseconds);
        return span.TotalHours >= 1
            ? $"{(int)span.TotalHours:00}:{span.Minutes:00}:{span.Seconds:00}"
            : $"{span.Minutes:00}:{span.Seconds:00}";
    }

    private void Player_EndReached(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_settings.AutoPlayNext || _settings.LoopPlaylist)
                PlayNext();
            else
            {
                PlayPauseButton.Content = "▶  Play";
                ShowStatus("Video finished.");
            }
        }));
    }

    private void Player_EncounteredError(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            ShowStatus("VLC could not decode this video. Use Next to continue.");
        }));
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (_player is null)
            return;

        if (_currentIndex < 0 && _videos.Count > 0)
        {
            PlayAt(0);
            return;
        }

        if (_player.IsPlaying)
            _player.Pause();
        else
            _player.Play();
    }

    private void Next_Click(object sender, RoutedEventArgs e) => PlayNext();
    private void Previous_Click(object sender, RoutedEventArgs e) => PlayPrevious();

    private void Delete_Click(object sender, RoutedEventArgs e) => DeleteCurrentVideo();

    private void DeleteCurrentVideo()
    {
        if (_currentIndex < 0 || _currentIndex >= _videos.Count)
            return;

        var item = _videos[_currentIndex];
        var operation = _settings.DeletePermanently ? "permanently delete" : "move to the Recycle Bin";
        if (_settings.ConfirmBeforeDelete)
        {
            var answer = MessageBox.Show(this,
                $"Do you want to {operation} this file?\n\n{item.FileName}",
                "Delete video", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes)
                return;
        }

        var oldIndex = _currentIndex;
        _player?.Stop();
        _currentMedia?.Dispose();
        _currentMedia = null;

        try
        {
            if (_settings.DeletePermanently)
                File.Delete(item.FullPath);
            else
                FileSystem.DeleteFile(item.FullPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);

            _suppressSelection = true;
            _videos.RemoveAt(oldIndex);
            _suppressSelection = false;
            if (_videos.Count == 0)
            {
                StopAndClearCurrentMedia();
                EmptyState.Visibility = Visibility.Visible;
                ShowStatus("The folder queue is now empty.");
            }
            else
            {
                var next = Math.Min(oldIndex, _videos.Count - 1);
                PlayAt(next);
                ShowStatus(_settings.DeletePermanently
                    ? $"Permanently deleted: {item.FileName}"
                    : $"Moved to Recycle Bin: {item.FileName}");
            }

            _settingsService.Save(_settings);
            UpdateTitleAndCount();
        }
        catch (Exception ex)
        {
            if (File.Exists(item.FullPath))
                PlayAt(oldIndex);
            MessageBox.Show(this, $"Could not delete this video.\n\n{ex.Message}",
                "Delete failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void TogglePlaylist_Click(object sender, RoutedEventArgs e)
    {
        PlaylistPanel.Visibility = PlaylistPanel.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void VideoList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelection || VideoList.SelectedIndex < 0 || VideoList.SelectedIndex == _currentIndex)
            return;

        PlayAt(VideoList.SelectedIndex);
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow(_settings.Copy()) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        var oldFolder = _settings.LastFolder;
        var currentVideo = _currentIndex >= 0 && _currentIndex < _videos.Count
            ? _videos[_currentIndex].FullPath
            : _settings.LastVideo;

        _settings = dialog.UpdatedSettings;
        _settingsService.Save(_settings);

        if (Directory.Exists(oldFolder))
            LoadFolder(oldFolder, currentVideo);
    }

    private void AudioTracks_Click(object sender, RoutedEventArgs e)
    {
        if (_player is null)
            return;

        var tracks = _player.AudioTrackDescription;
        ShowTrackMenu(sender as UIElement, tracks, _player.AudioTrack, id => _player.SetAudioTrack(id), "Audio tracks");
    }

    private void SubtitleTracks_Click(object sender, RoutedEventArgs e)
    {
        if (_player is null)
            return;

        var tracks = _player.SpuDescription;
        ShowTrackMenu(sender as UIElement, tracks, _player.Spu, id => _player.SetSpu(id), "Subtitle tracks");
    }

    private static void ShowTrackMenu(UIElement? target, TrackDescription[] tracks, int selectedId,
        Action<int> selectTrack, string title)
    {
        if (target is null)
            return;

        if (tracks.Length == 0)
        {
            MessageBox.Show($"{title} are not available for this video.",
                "Scroll Video Player", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var menu = new ContextMenu();
        foreach (var track in tracks)
        {
            var item = new MenuItem
            {
                Header = string.IsNullOrWhiteSpace(track.Name) ? $"Track {track.Id}" : track.Name,
                IsCheckable = true,
                IsChecked = track.Id == selectedId
            };
            var id = track.Id;
            item.Click += (_, _) => selectTrack(id);
            menu.Items.Add(item);
        }

        menu.PlacementTarget = target;
        menu.IsOpen = true;
    }

    private void VideoMode_Click(object sender, RoutedEventArgs e)
    {
        if (_player is null || sender is not UIElement target)
            return;

        var menu = new ContextMenu();
        AddAspectItem(menu, "Automatic", null);
        AddAspectItem(menu, "16:9", "16:9");
        AddAspectItem(menu, "4:3", "4:3");
        AddAspectItem(menu, "1:1", "1:1");
        AddAspectItem(menu, "21:9", "21:9");
        menu.PlacementTarget = target;
        menu.IsOpen = true;
    }

    private void AddAspectItem(ContextMenu menu, string label, string? ratio)
    {
        var item = new MenuItem { Header = label };
        item.Click += (_, _) =>
        {
            if (_player is not null)
            {
                _player.AspectRatio = ratio;
                ShowStatus($"Aspect ratio: {label}");
            }
        };
        menu.Items.Add(item);
    }

    private void Speed_Click(object sender, RoutedEventArgs e)
    {
        if (_player is null || sender is not UIElement target)
            return;

        var menu = new ContextMenu();
        foreach (var rate in new[] { 0.5f, 0.75f, 1.0f, 1.25f, 1.5f, 1.75f, 2.0f })
        {
            var item = new MenuItem
            {
                Header = $"{rate:0.##}×",
                IsCheckable = true,
                IsChecked = Math.Abs(_player.Rate - rate) < 0.01f
            };
            item.Click += (_, _) =>
            {
                _player.SetRate(rate);
                SpeedButton.Content = $"{rate:0.##}×";
            };
            menu.Items.Add(item);
        }

        menu.PlacementTarget = target;
        menu.IsOpen = true;
    }

    private void Snapshot_Click(object sender, RoutedEventArgs e)
    {
        if (_player is null || _currentIndex < 0)
            return;

        try
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "ScrollVideoPlayer");
            Directory.CreateDirectory(folder);
            var name = $"snapshot-{DateTime.Now:yyyyMMdd-HHmmss}.png";
            var path = Path.Combine(folder, name);
            if (_player.TakeSnapshot(0, path, 0, 0))
                ShowStatus($"Snapshot saved to Pictures\\ScrollVideoPlayer\\{name}");
            else
                ShowStatus("Snapshot could not be created.");
        }
        catch (Exception ex)
        {
            ShowStatus($"Snapshot failed: {ex.Message}");
        }
    }

    private void Mute_Click(object sender, RoutedEventArgs e)
    {
        if (_player is null)
            return;

        _player.Mute = !_player.Mute;
        MuteButton.Content = _player.Mute ? "🔇" : "🔊";
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_player is not null)
            _player.Volume = Math.Clamp((int)e.NewValue, 0, 100);
    }

    private void SeekSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_updatingSeek && _isSeeking && _player is not null)
            _player.Time = (long)e.NewValue;
    }

    private void SeekSlider_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) => _isSeeking = true;

    private void SeekSlider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isSeeking = false;
        if (_player is not null)
            _player.Time = (long)SeekSlider.Value;
    }

    private void FullScreen_Click(object sender, RoutedEventArgs e) => ToggleFullscreen();

    private void ToggleFullscreen()
    {
        if (!_isFullscreen)
        {
            _previousWindowStyle = WindowStyle;
            _previousWindowState = WindowState;
            _previousResizeMode = ResizeMode;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;
            _isFullscreen = true;
        }
        else
        {
            WindowStyle = _previousWindowStyle;
            ResizeMode = _previousResizeMode;
            WindowState = _previousWindowState;
            _isFullscreen = false;
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0 && e.Key == Key.O)
        {
            OpenFolder_Click(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case Key.Space:
                PlayPause_Click(this, new RoutedEventArgs());
                e.Handled = true;
                break;
            case Key.Up:
                PlayNext();
                e.Handled = true;
                break;
            case Key.Down:
                PlayPrevious();
                e.Handled = true;
                break;
            case Key.Right:
                if (_player is not null) _player.Time = Math.Max(0, _player.Time + 5000);
                e.Handled = true;
                break;
            case Key.Left:
                if (_player is not null) _player.Time = Math.Max(0, _player.Time - 5000);
                e.Handled = true;
                break;
            case Key.Delete:
                DeleteCurrentVideo();
                e.Handled = true;
                break;
            case Key.M:
                Mute_Click(this, new RoutedEventArgs());
                e.Handled = true;
                break;
            case Key.F:
            case Key.F11:
                ToggleFullscreen();
                e.Handled = true;
                break;
            case Key.Escape:
                if (_isFullscreen)
                {
                    ToggleFullscreen();
                    e.Handled = true;
                }
                break;
            case Key.P:
                TogglePlaylist_Click(this, new RoutedEventArgs());
                e.Handled = true;
                break;
        }
    }

    private void Overlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (IsInteractiveSource(e.OriginalSource))
        {
            _pointerDown = false;
            return;
        }

        _pointerStart = e.GetPosition(OverlayRoot);
        _pointerDown = true;
    }

    private void Overlay_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_pointerDown)
            return;

        _pointerDown = false;
        ProcessSwipe(e.GetPosition(OverlayRoot));
    }

    private void Overlay_TouchDown(object sender, TouchEventArgs e)
    {
        if (IsInteractiveSource(e.OriginalSource))
        {
            _pointerDown = false;
            return;
        }

        _pointerStart = e.GetTouchPoint(OverlayRoot).Position;
        _pointerDown = true;
        e.Handled = true;
    }

    private void Overlay_TouchMove(object sender, TouchEventArgs e)
    {
        if (_pointerDown)
            e.Handled = true;
    }

    private void Overlay_TouchUp(object sender, TouchEventArgs e)
    {
        if (!_pointerDown)
            return;

        _pointerDown = false;
        ProcessSwipe(e.GetTouchPoint(OverlayRoot).Position);
        e.Handled = true;
    }

    private static bool IsInteractiveSource(object? source)
    {
        if (source is not DependencyObject current)
            return false;

        while (current is not null)
        {
            if (current is ButtonBase or Slider or ListBoxItem or ListBox)
                return true;

            current = current is Visual or Visual3D
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }

        return false;
    }

    private void ProcessSwipe(Point end)
    {
        var deltaX = end.X - _pointerStart.X;
        var deltaY = end.Y - _pointerStart.Y;

        if (Math.Abs(deltaX) > Math.Abs(deltaY) * 1.25 && deltaX < -115)
            DeleteCurrentVideo();
        else if (Math.Abs(deltaY) > Math.Abs(deltaX) * 1.15 && deltaY < -85)
            PlayNext();
        else if (Math.Abs(deltaY) > Math.Abs(deltaX) * 1.15 && deltaY > 85)
            PlayPrevious();
    }

    private void Overlay_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (IsInteractiveSource(e.OriginalSource))
            return;

        if (e.Delta > 0)
            PlayNext();
        else if (e.Delta < 0)
            PlayPrevious();

        e.Handled = true;
    }

    private void ShowStatus(string message) => StatusText.Text = message;

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        _uiTimer.Stop();
        if (_settings.RememberLastFolder)
            _settingsService.Save(_settings);

        if (_player is not null)
        {
            _player.EndReached -= Player_EndReached;
            _player.EncounteredError -= Player_EncounteredError;
            _player.Stop();
            _player.Dispose();
        }

        _currentMedia?.Dispose();
        _libVlc?.Dispose();
    }
}
