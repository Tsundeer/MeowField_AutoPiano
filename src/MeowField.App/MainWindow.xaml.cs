using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MahApps.Metro.IconPacks;
using MeowField.Application;
using MeowField.App.Views;

namespace MeowField.App;

public partial class MainWindow : Window
{
    private const double DrawerWidth = 336;
    private const double DrawerSlideRoom = 96;
    private const double DrawerShadowRoom = 8;

    private readonly MainViewModel _viewModel;
    private readonly IGlobalHotkeyService _hotkeys;
    private Window? _drawer;
    private PlaylistView? _drawerView;
    private TranslateTransform? _drawerTransform;

    public MainWindow(MainViewModel viewModel, IGlobalHotkeyService hotkeys)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _hotkeys = hotkeys;
        DataContext = viewModel;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        StateChanged += OnWindowStateChanged;
        LocationChanged += (_, _) => UpdateDrawerPlacement();
        SizeChanged += (_, _) => UpdateDrawerPlacement();
        Loaded += (_, _) =>
        {
            LocalizationService.Apply(this, _viewModel.IsEnglish);
            WireTitleBarSettingsButton(this);
        };
        SourceInitialized += OnSourceInitialized;
        Closed += OnClosed;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsQueueOpen))
        {
            AnimateQueueDrawer(_viewModel.IsQueueOpen);
        }
        else if (e.PropertyName == nameof(MainViewModel.IsEnglish) && _drawer is { IsVisible: true })
        {
            LocalizationService.Apply(_drawerView, _viewModel.IsEnglish);
        }
    }

    private void AnimateQueueDrawer(bool open)
    {
        if (open)
        {
            EnsureDrawerWindow();
            LocalizationService.Apply(_drawerView!, _viewModel.IsEnglish);
            UpdateDrawerPlacement(force: true);
            _drawer!.Show();
            var slideIn = new DoubleAnimation(DrawerSlideRoom, 0, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            };
            _drawerTransform!.BeginAnimation(TranslateTransform.XProperty, slideIn);
        }
        else
        {
            if (_drawer is null || !_drawer.IsVisible) return;
            var slideOut = new DoubleAnimation(_drawerTransform!.X, DrawerSlideRoom, TimeSpan.FromMilliseconds(150))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
            };
            slideOut.Completed += (_, _) => _drawer!.Hide();
            _drawerTransform.BeginAnimation(TranslateTransform.XProperty, slideOut);
        }
    }

    private void EnsureDrawerWindow()
    {
        if (_drawer is not null) return;
        _drawerView = new PlaylistView
        {
            Width = DrawerWidth,
            HorizontalAlignment = HorizontalAlignment.Left,
            DataContext = DataContext,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 20,
                ShadowDepth = 4,
                Direction = 180,
                Opacity = 0.22,
            },
        };
        _drawerTransform = new TranslateTransform();
        var host = new Grid { Margin = new Thickness(DrawerShadowRoom, 0, 0, 0) };
        host.RenderTransform = _drawerTransform;
        host.Children.Add(_drawerView);
        _drawer = new Window
        {
            Title = "MeowField 播放队列",
            Content = host,
            Width = DrawerShadowRoom + DrawerWidth + DrawerSlideRoom,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = System.Windows.Media.Brushes.Transparent,
            ShowInTaskbar = false,
            ShowActivated = false,
            ResizeMode = ResizeMode.NoResize,
            Owner = this,
        };
    }

    private void UpdateDrawerPlacement(bool force = false)
    {
        if (_drawer is null || WindowState == WindowState.Minimized) return;
        if (!force && !_drawer.IsVisible) return;
        var fitsOutside = Left + ActualWidth + DrawerWidth <= SystemParameters.WorkArea.Right;
        _drawer.Top = Top;
        _drawer.Height = ActualHeight;
        _drawer.Left = fitsOutside
            ? Left + ActualWidth - DrawerShadowRoom
            : Math.Max(SystemParameters.WorkArea.Left, Left + ActualWidth - DrawerWidth - DrawerShadowRoom);
    }

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        ShellGrid.Margin = WindowState == WindowState.Maximized
            ? new Thickness(8)
            : new Thickness(16, 14, 16, 20);

        if (_drawer is null) return;
        if (WindowState == WindowState.Minimized)
        {
            _drawer.Hide();
        }
        else if (_viewModel.IsQueueOpen)
        {
            UpdateDrawerPlacement(force: true);
            _drawer.Show();
        }
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        _hotkeys.TryRegister(handle);
        _hotkeys.PlayPauseRequested += OnPlayPauseRequested;
        _hotkeys.StopRequested += OnStopRequested;
        HwndSource.FromHwnd(handle)?.AddHook(WndProc);
    }

    private void OnPlayPauseRequested(object? sender, EventArgs e) => _viewModel.TogglePlayPause();
    private void OnStopRequested(object? sender, EventArgs e) => _viewModel.StopPlayback();

    private void WireTitleBarSettingsButton(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is System.Windows.Controls.Button { Content: PackIconLucide { Kind: PackIconLucideKind.Settings } } button)
            {
                button.Click += (_, _) => _viewModel.OpenSettings();
                return;
            }

            WireTitleBarSettingsButton(child);
        }
    }

    private void OnTitleBarMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            if (e.ClickCount == 2) OnMaximizeClick(sender, new RoutedEventArgs());
            else if (WindowState != WindowState.Maximized) DragMove();
        }
    }

    private void OnRootMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, RootGrid) || ReferenceEquals(e.OriginalSource, ShellGrid))
        {
            OnTitleBarMouseDown(sender, e);
        }
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnTimelineSeekStarted(object sender, MouseButtonEventArgs e) => _viewModel.BeginTimelineSeek();

    private void OnTimelineSeekReleased(object sender, MouseButtonEventArgs e)
    {
        if (sender is Slider slider)
        {
            _viewModel.SeekFromTimeline(slider.Value);
        }
        _viewModel.EndTimelineSeek();
    }

    private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmGetMinMaxInfo)
        {
            ClampMaximizedToWorkArea(hwnd, lParam);
            return IntPtr.Zero;
        }

        handled = _hotkeys.HandleWindowMessage(message, wParam);
        return IntPtr.Zero;
    }

    private const int WmGetMinMaxInfo = 0x0024;
    private const uint MonitorDefaultToNearest = 0x00000002;

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NativePoint { public int X; public int Y; }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NativeRect { public int Left; public int Top; public int Right; public int Bottom; }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public NativePoint Reserved;
        public NativePoint MaxSize;
        public NativePoint MaxPosition;
        public NativePoint MinTrackSize;
        public NativePoint MaxTrackSize;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    private static void ClampMaximizedToWorkArea(IntPtr hwnd, IntPtr lParam)
    {
        var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero) return;
        var info = new MonitorInfo { Size = System.Runtime.InteropServices.Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info)) return;

        var minMax = System.Runtime.InteropServices.Marshal.PtrToStructure<MinMaxInfo>(lParam);
        minMax.MaxPosition.X = info.Work.Left - info.Monitor.Left;
        minMax.MaxPosition.Y = info.Work.Top - info.Monitor.Top;
        minMax.MaxSize.X = info.Work.Right - info.Work.Left;
        minMax.MaxSize.Y = info.Work.Bottom - info.Work.Top;
        System.Runtime.InteropServices.Marshal.StructureToPtr(minMax, lParam, false);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _hotkeys.PlayPauseRequested -= OnPlayPauseRequested;
        _hotkeys.StopRequested -= OnStopRequested;
        _hotkeys.Unregister();
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } files)
        {
            return;
        }

        var path = files[0];
        if (path.EndsWith(".mid", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".midi", StringComparison.OrdinalIgnoreCase))
        {
            await _viewModel.LoadMidiAsync(path);
        }
    }
}
