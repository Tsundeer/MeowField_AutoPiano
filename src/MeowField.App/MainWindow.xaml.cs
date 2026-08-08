using System.Windows;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using MahApps.Metro.IconPacks;
using MeowField.Application;

namespace MeowField.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly IGlobalHotkeyService _hotkeys;

    public MainWindow(MainViewModel viewModel, IGlobalHotkeyService hotkeys)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _hotkeys = hotkeys;
        DataContext = viewModel;
        Loaded += (_, _) =>
        {
            LocalizationService.Apply(this, _viewModel.IsEnglish);
            WireTitleBarSettingsButton(this);
        };
        SourceInitialized += OnSourceInitialized;
        Closed += OnClosed;
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

    private void OnMinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        handled = _hotkeys.HandleWindowMessage(message, wParam);
        return IntPtr.Zero;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _hotkeys.PlayPauseRequested -= OnPlayPauseRequested;
        _hotkeys.StopRequested -= OnStopRequested;
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
