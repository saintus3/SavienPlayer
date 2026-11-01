using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using SavienPlayer.Core;
using SavienPlayer.Infrastructure;

namespace SavienPlayer.App;

public partial class MainWindow : Window
{
    private static readonly Brush HitTestTransparentBrush = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
    private readonly IMediaPlayerService _playerService;
    private readonly ISubtitleService _subtitleService;
    private PlaylistWindow? _playlistWindow;
    private bool _isFullscreen;
    private WindowState _previousWindowState = WindowState.Normal;

    public MainWindow(IMediaPlayerService playerService)
    {
        if (playerService is not VlcMediaPlayerService vlc)
        {
            throw new InvalidOperationException("현재 구현은 VlcMediaPlayerService를 필요로 합니다.");
        }

        _playerService = playerService;
        _subtitleService = vlc;

        InitializeComponent();

        CustomTitleBar.Height = Math.Max(14, SystemParameters.WindowCaptionHeight / 2d);

        _playerService.PositionChanged += PlayerServiceOnPositionChanged;
        _playerService.PlaybackStateChanged += PlayerServiceOnPlaybackStateChanged;
        VideoView.MediaPlayer = vlc.NativePlayer;

        _playerService.SetVolume((int)VolumeBar.Value);
        UpdateVideoSurfaceAppearance(_playerService.State);
    }

    private void OpenButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Video Files|*.mp4;*.mkv;*.avi;*.mov;*.wmv;*.flv;*.ts;*.m4v|All Files|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            _playerService.Open(dialog.FileName);
            _playerService.Play();
        }
    }

    private async void OpenUrlButton_OnClick(object sender, RoutedEventArgs e)
    {
        var url = PromptForUrl();
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        try
        {
            Cursor = Cursors.Wait;
            await _playerService.OpenUrlAsync(url);
            _playerService.Play();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"URL 재생에 실패했습니다.\n{ex.Message}", "SavienPlayer", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            Cursor = null;
        }
    }

    private string? PromptForUrl()
    {
        var initialUrl = string.Empty;
        if (Clipboard.ContainsText())
        {
            var clipboardText = Clipboard.GetText().Trim();
            if (Uri.TryCreate(clipboardText, UriKind.Absolute, out var clipboardUri) &&
                (clipboardUri.Scheme == Uri.UriSchemeHttp || clipboardUri.Scheme == Uri.UriSchemeHttps))
            {
                initialUrl = clipboardText;
            }
        }

        var dialog = new Window
        {
            Title = "유튜브 / URL 열기",
            Width = 460,
            Height = 145,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.ToolWindow,
            Background = new SolidColorBrush(Color.FromRgb(0x0B, 0x16, 0x22)),
            Foreground = Brushes.White
        };

        var root = new Grid { Margin = new Thickness(12) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var label = new TextBlock
        {
            Text = "유튜브 주소 또는 스트림 URL을 입력하세요:",
            Margin = new Thickness(0, 0, 0, 6)
        };

        var urlBox = new TextBox
        {
            Text = initialUrl,
            Height = 26,
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Color.FromRgb(0x12, 0x30, 0x47)),
            Foreground = Brushes.White,
            CaretBrush = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x1E, 0x3A, 0x52)),
            Padding = new Thickness(6, 0, 6, 0)
        };
        Grid.SetRow(urlBox, 1);

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0)
        };
        Grid.SetRow(buttonPanel, 2);

        var okButton = new Button
        {
            Content = "재생",
            Width = 70,
            Height = 26,
            IsDefault = true,
            Margin = new Thickness(0, 0, 8, 0)
        };
        okButton.Click += (_, _) => dialog.DialogResult = true;

        var cancelButton = new Button
        {
            Content = "취소",
            Width = 70,
            Height = 26,
            IsCancel = true
        };

        buttonPanel.Children.Add(okButton);
        buttonPanel.Children.Add(cancelButton);

        root.Children.Add(label);
        root.Children.Add(urlBox);
        root.Children.Add(buttonPanel);
        dialog.Content = root;

        dialog.Loaded += (_, _) =>
        {
            urlBox.Focus();
            urlBox.SelectAll();
        };

        return dialog.ShowDialog() == true ? urlBox.Text.Trim() : null;
    }

    private void PlayButton_OnClick(object sender, RoutedEventArgs e) => _playerService.Play();
    private void PauseButton_OnClick(object sender, RoutedEventArgs e) => _playerService.Pause();
    private void StopButton_OnClick(object sender, RoutedEventArgs e) => _playerService.Stop();

    private void SeekBar_OnPreviewMouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _playerService.Seek(SeekBar.Value);
    }

    private void VolumeBar_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _playerService?.SetVolume((int)e.NewValue);
    }

    private void VideoOverlay_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is DependencyObject element)
        {
            var overlayWindow = GetWindow(element);
            if (overlayWindow is not null && !ReferenceEquals(overlayWindow, this))
            {
                overlayWindow.PreviewKeyDown -= Window_OnPreviewKeyDown;
                overlayWindow.PreviewKeyDown += Window_OnPreviewKeyDown;
                overlayWindow.PreviewMouseLeftButtonDown -= VideoSurface_OnPreviewMouseLeftButtonDown;
                overlayWindow.PreviewMouseLeftButtonDown += VideoSurface_OnPreviewMouseLeftButtonDown;
                overlayWindow.PreviewMouseRightButtonUp -= VideoSurface_OnPreviewMouseRightButtonUp;
                overlayWindow.PreviewMouseRightButtonUp += VideoSurface_OnPreviewMouseRightButtonUp;
            }
        }
    }

    private void VideoSurface_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is IInputElement inputElement)
        {
            inputElement.Focus();
        }

        if (e.LeftButton == MouseButtonState.Pressed)
        {
            e.Handled = true;
            DragMove();
        }
    }

    private void VideoSurface_OnPreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (FindResource("VideoContextMenu") is ContextMenu contextMenu)
        {
            contextMenu.PlacementTarget = sender as UIElement ?? VideoBorder;
            contextMenu.IsOpen = true;
            e.Handled = true;
        }
    }

    private void Window_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.OriginalSource is TextBox)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.U when !e.IsRepeat && Keyboard.Modifiers == ModifierKeys.Control:
                e.Handled = true;
                OpenUrlButton_OnClick(this, new RoutedEventArgs());
                break;

            case Key.Enter when !e.IsRepeat:
                e.Handled = true;
                ToggleFullscreen();
                break;

            case Key.Space when !e.IsRepeat:
                e.Handled = true;
                _playerService.Pause();
                break;

            case Key.Right:
                e.Handled = true;
                _playerService.SeekRelative(TimeSpan.FromSeconds(10));
                break;

            case Key.Left:
                e.Handled = true;
                _playerService.SeekRelative(TimeSpan.FromSeconds(-10));
                break;

            case Key.Up:
                e.Handled = true;
                VolumeBar.Value = Math.Clamp(VolumeBar.Value + 5, VolumeBar.Minimum, VolumeBar.Maximum);
                break;

            case Key.Down:
                e.Handled = true;
                VolumeBar.Value = Math.Clamp(VolumeBar.Value - 5, VolumeBar.Minimum, VolumeBar.Maximum);
                break;
        }
    }

    private void FullscreenButton_OnClick(object sender, RoutedEventArgs e) => ToggleFullscreen();

    private void CustomTitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source)
        {
            for (var current = source; current is not null; current = System.Windows.Media.VisualTreeHelper.GetParent(current))
            {
                if (current is Button)
                {
                    return;
                }
            }
        }

        if (e.ClickCount == 2)
        {
            ToggleMaximizeRestore();
            return;
        }

        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void MinimizeWindowButton_OnClick(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeRestoreWindowButton_OnClick(object sender, RoutedEventArgs e)
    {
        ToggleMaximizeRestore();
    }

    private void CloseWindowButton_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ToggleMaximizeRestore()
    {
        if (_isFullscreen)
        {
            ToggleFullscreen();
            return;
        }

        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void ToggleFullscreen()
    {
        _isFullscreen = !_isFullscreen;

        if (_isFullscreen)
        {
            _previousWindowState = WindowState;
            CustomTitleBar.Visibility = Visibility.Collapsed;
            ControlsPanel.Visibility = Visibility.Collapsed;
            RootGrid.Margin = new Thickness(0);
            VideoBorder.CornerRadius = new CornerRadius(0);
            VideoBorder.BorderThickness = new Thickness(0);
            MainWindowChrome.ResizeBorderThickness = new Thickness(0);

            if (WindowState == WindowState.Maximized)
            {
                WindowState = WindowState.Normal;
            }

            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;
        }
        else
        {
            CustomTitleBar.Visibility = Visibility.Visible;
            ControlsPanel.Visibility = Visibility.Visible;
            RootGrid.Margin = new Thickness(2);
            VideoBorder.CornerRadius = new CornerRadius(4);
            VideoBorder.BorderThickness = new Thickness(2);
            MainWindowChrome.ResizeBorderThickness = new Thickness(6);

            ResizeMode = ResizeMode.CanResize;
            WindowState = _previousWindowState;
        }
    }

    private void OpenSubtitleButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Subtitle Files|*.srt;*.ass;*.ssa;*.vtt|All Files|*.*"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        if (_subtitleService.TryLoad(dialog.FileName, out var error))
        {
            return;
        }

        MessageBox.Show(error ?? "자막 로드에 실패했습니다.", "SavienPlayer", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void ClearSubtitleButton_OnClick(object sender, RoutedEventArgs e)
    {
        _subtitleService.Clear();
    }

    private void PlaylistButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_playlistWindow is null)
        {
            _playlistWindow = new PlaylistWindow(_playerService, PlayPlaylistItem)
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = Left + Width + 8,
                Top = Top
            };
            _playlistWindow.Closed += (_, _) => _playlistWindow = null;
        }

        if (_playlistWindow.IsVisible)
        {
            _playlistWindow.Hide();
        }
        else
        {
            _playlistWindow.Show();
            _playlistWindow.Activate();
        }
    }

    private void PlayPlaylistItem(PlaylistItem item)
    {
        _playerService.Open(item.FilePath);
        _playerService.Play();
    }

    private void PlayerServiceOnPositionChanged(object? sender, double position)
    {
        Dispatcher.Invoke(() => SeekBar.Value = position);
    }

    private void PlayerServiceOnPlaybackStateChanged(object? sender, PlaybackState state)
    {
        Dispatcher.Invoke(() => UpdateVideoSurfaceAppearance(state));
    }

    private void UpdateVideoSurfaceAppearance(PlaybackState state)
    {
        var isVideoActive = _playerService.IsLoaded && state is PlaybackState.Opening or PlaybackState.Buffering or PlaybackState.Playing or PlaybackState.Paused;
        PlaceholderGrid.Visibility = isVideoActive ? Visibility.Collapsed : Visibility.Visible;
        VideoBorder.Background = isVideoActive ? Brushes.Black : Brushes.LightGray;
        VideoOverlayGrid.Background = isVideoActive ? HitTestTransparentBrush : Brushes.LightGray;
    }
}

