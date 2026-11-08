using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Threading;
using LibVLCSharp.Shared;
using MediaPlayer = LibVLCSharp.Shared.MediaPlayer;
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
    private bool _touchGestureDown;
    private Point _touchGestureStart;
    private bool _mouseGestureDown;
    private Point _mouseGestureStart;
    private IntPtr _mouseHook = IntPtr.Zero;
    private readonly LowLevelMouseProc _mouseHookCallback;
    private bool _isFullscreen;
    private WindowStyle _previousWindowStyle;
    private WindowState _previousWindowState;
    private ResizeMode _previousResizeMode;

    public MainWindow()
    {
        _mouseHookCallback = MouseHookCallback;
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
            var rememberedVolume = Math.Clamp(_settings.LastVolume, 0, 100);
            VolumeSlider.Value = rememberedVolume;
            _player.Volume = rememberedVolume;
            _player.Mute = _settings.IsMuted;
            MuteButton.Content = _settings.IsMuted ? "🔇" : "🔊";

            InstallMouseHook();
            _uiTimer.Start();

            if (_settings.RememberLastFolder &&
                Directory.Exists(_settings.LastFolder) &&
                File.Exists(_settings.LastVideo))
            {
                var choice = MessageBox.Show(
                    this,
                    $"Would you like to resume the last video?\n\n{Path.GetFileName(_settings.LastVideo)}\n\nYes: resume this video\nNo: choose a different folder\nCancel: open the player without loading videos",
                    "Welcome back",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                if (choice == MessageBoxResult.Yes)
                    LoadFolder(_settings.LastFolder, _settings.LastVideo);
                else if (choice == MessageBoxResult.No)
                    BrowseForFolder(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos));
            }
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
        BrowseForFolder(_settings.LastFolder);
    }

    private void BrowseForFolder(string? initialFolder)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choose a folder containing videos",
            Multiselect = false
        };

        if (!Directory.Exists(initialFolder))
            initialFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        if (Directory.Exists(initialFolder))
            dialog.InitialDirectory = initialFolder;

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
            _player.Media = null;
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
        if (_player is not null)
            _player.Media = null;
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
        if (_player is not null)
            _player.Media = null;
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
        _settings.IsMuted = _player.Mute;
        MuteButton.Content = _player.Mute ? "🔇" : "🔊";
        _settingsService.Save(_settings);
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_player is not null)
        {
            var volume = Math.Clamp((int)e.NewValue, 0, 100);
            _player.Volume = volume;
            _settings.LastVolume = volume;
            _settingsService.Save(_settings);
        }
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

    // VLC's WPF VideoView renders through a native child HWND. Mouse input over
    // that surface can bypass ordinary WPF routed events. A low-level mouse hook
    // lets this window recognise feed gestures over the video and its overlays.
    private const int WhMouseLl = 14;
    private const int WmLButtonDown = 0x0201;
    private const int WmLButtonUp = 0x0202;
    private const int WmMouseWheel = 0x020A;
    private const uint GaRootOwner = 3;

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LowLevelMouseData
    {
        public NativePoint Point;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc callback, IntPtr moduleHandle, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr handle, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(NativePoint point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr handle, uint flags);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    private void InstallMouseHook()
    {
        if (_mouseHook != IntPtr.Zero)
            return;

        _mouseHook = SetWindowsHookEx(WhMouseLl, _mouseHookCallback, GetModuleHandle(null), 0);
        if (_mouseHook == IntPtr.Zero)
            ShowStatus("Mouse gesture hook could not be installed. Keyboard shortcuts remain available.");
    }

    private IntPtr MouseHookCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && !Dispatcher.HasShutdownStarted)
        {
            var data = Marshal.PtrToStructure<LowLevelMouseData>(lParam);
            var message = wParam.ToInt32();
            var screenPoint = new Point(data.Point.X, data.Point.Y);
            var insideWindow = IsInsideMainWindow(screenPoint);

            if (insideWindow || (message == WmLButtonUp && _mouseGestureDown))
            {
                var mouseData = data.MouseData;
                Dispatcher.BeginInvoke(new Action(() =>
                    HandleGlobalMouseInput(message, screenPoint, mouseData, insideWindow)));
            }
        }

        return CallNextHookEx(_mouseHook, code, wParam, lParam);
    }

    private bool IsInsideMainWindow(Point screenPoint)
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero || !GetWindowRect(handle, out var rect))
            return false;

        if (screenPoint.X < rect.Left || screenPoint.X >= rect.Right ||
            screenPoint.Y < rect.Top || screenPoint.Y >= rect.Bottom)
            return false;

        // Ignore input when another application or a modal dialog is in front.
        var overlay = Window.GetWindow(OverlayRoot);
        var overlayHandle = overlay is null ? IntPtr.Zero : new WindowInteropHelper(overlay).Handle;
        var foreground = GetForegroundWindow();
        if (foreground != handle && foreground != overlayHandle)
            return false;

        var pointedWindow = WindowFromPoint(new NativePoint
        {
            X = (int)screenPoint.X,
            Y = (int)screenPoint.Y
        });

        return pointedWindow != IntPtr.Zero &&
               GetAncestor(pointedWindow, GaRootOwner) == GetAncestor(handle, GaRootOwner);
    }

    private bool IsInteractiveAtScreenPoint(Point screenPoint)
    {
        try
        {
            var localPoint = OverlayRoot.PointFromScreen(screenPoint);

            // The root overlay does not cover the native window's title-bar area.
            // Treat points outside the overlay as non-gesture input.
            if (localPoint.X < 0 || localPoint.Y < 0 ||
                localPoint.X >= OverlayRoot.ActualWidth ||
                localPoint.Y >= OverlayRoot.ActualHeight)
                return true;

            var hit = OverlayRoot.InputHitTest(localPoint);
            return IsInteractiveSource(hit);
        }
        catch
        {
            return false;
        }
    }

    private void HandleGlobalMouseInput(int message, Point screenPoint, uint mouseData, bool insideWindow)
    {
        switch (message)
        {
            case WmMouseWheel:
                if (!insideWindow || IsInteractiveAtScreenPoint(screenPoint))
                    return;

                var wheelDelta = unchecked((short)(mouseData >> 16));
                if (wheelDelta > 0)
                    PlayNext();
                else if (wheelDelta < 0)
                    PlayPrevious();
                break;

            case WmLButtonDown:
                _mouseGestureDown = insideWindow && !IsInteractiveAtScreenPoint(screenPoint);
                if (_mouseGestureDown)
                    _mouseGestureStart = screenPoint;
                break;

            case WmLButtonUp:
                if (!_mouseGestureDown)
                    return;

                _mouseGestureDown = false;
                if (insideWindow)
                    ProcessSwipe(_mouseGestureStart, screenPoint);
                break;
        }
    }

    private void Overlay_TouchDown(object sender, TouchEventArgs e)
    {
        if (IsInteractiveSource(e.OriginalSource))
        {
            _touchGestureDown = false;
            return;
        }

        _touchGestureStart = e.GetTouchPoint(OverlayRoot).Position;
        _touchGestureDown = true;
        e.Handled = true;
    }

    private void Overlay_TouchMove(object sender, TouchEventArgs e)
    {
        if (_touchGestureDown)
            e.Handled = true;
    }

    private void Overlay_TouchUp(object sender, TouchEventArgs e)
    {
        if (!_touchGestureDown)
            return;

        _touchGestureDown = false;
        if (!IsInteractiveSource(e.OriginalSource))
            ProcessSwipe(_touchGestureStart, e.GetTouchPoint(OverlayRoot).Position);
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

    private void ProcessSwipe(Point start, Point end)
    {
        var deltaX = end.X - start.X;
        var deltaY = end.Y - start.Y;

        if (Math.Abs(deltaX) > Math.Abs(deltaY) * 1.25 && deltaX < -115)
            DeleteCurrentVideo();
        else if (Math.Abs(deltaY) > Math.Abs(deltaX) * 1.15 && deltaY < -85)
            PlayNext();
        else if (Math.Abs(deltaY) > Math.Abs(deltaX) * 1.15 && deltaY > 85)
            PlayPrevious();
    }

    private void ShowStatus(string message) => StatusText.Text = message;

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        _uiTimer.Stop();
        if (_mouseHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }

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
