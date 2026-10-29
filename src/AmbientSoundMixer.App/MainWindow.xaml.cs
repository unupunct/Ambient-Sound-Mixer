using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AmbientSoundMixer.App.Services;
using AmbientSoundMixer.App.ViewModels;
using AmbientSoundMixer.Core.Models;
using AmbientSoundMixer.Core.Session;

namespace AmbientSoundMixer.App;

public partial class MainWindow : Window
{
    private const double CompactWidth = 480;
    private readonly MainViewModel _vm;
    private readonly TrayService _tray;
    private bool _exiting;
    private double _normalWidth, _normalHeight;

    public MainWindow(MainViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
        DataContext = vm;

        var w = vm.AppSettings.Window;
        _normalWidth = w.Width;
        _normalHeight = w.Height;
        Width = w.Width;
        Height = w.Height;
        if (w.Left is { } l && w.Top is { } t && IsVisibleOnScreen(l, t, w.Width, w.Height))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = l; Top = t;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
        if (w.Maximized) WindowState = WindowState.Maximized;
        Topmost = vm.AppSettings.AlwaysOnTop;

        _tray = new TrayService(
            show: RestoreFromTray,
            playPause: () => Dispatcher.Invoke(vm.TogglePlayPause),
            stop: () => Dispatcher.Invoke(vm.StopPlayback),
            exit: ExitApp,
            isPlaying: () => vm.Session.State == TransportState.Playing);
        UpdateTrayVisibility();

        vm.WindowOptionsChanged += (_, _) => UpdateTrayVisibility();
        vm.AlwaysOnTopChangedEvent += (_, _) => Topmost = vm.AppSettings.AlwaysOnTop;
        vm.CompactChanged += (_, _) => ApplyCompact();
        if (vm.IsCompact) ApplyCompact();
    }

    private static bool IsVisibleOnScreen(double left, double top, double width, double height)
    {
        var win = new Rect(left, top, width, height);
        var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        win.Intersect(screen);
        return !win.IsEmpty && win.Width > 100 && win.Height > 60;
    }

    // ----- show / tray -----

    public void ShowOrTray(bool startMinimized)
    {
        if (startMinimized && (_vm.AppSettings.MinimizeToTray || _vm.AppSettings.CloseToTray))
        {
            Hide();
            _tray.Visible = true;
        }
        else
        {
            Show();
            if (startMinimized) WindowState = WindowState.Minimized;
        }
    }

    public void RestoreFromTray()
    {
        Dispatcher.Invoke(() =>
        {
            Show();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
            UpdateTrayVisibility();
        });
    }

    private void UpdateTrayVisibility() =>
        _tray.Visible = _vm.AppSettings.MinimizeToTray || _vm.AppSettings.CloseToTray;

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        // A maximized borderless window overhangs the screen by the resize border; compensate.
        Root.Margin = WindowState == WindowState.Maximized ? new Thickness(SystemParameters.WindowResizeBorderThickness.Left + 1) : new Thickness(0);
        if (WindowState == WindowState.Minimized && _vm.AppSettings.MinimizeToTray)
        {
            Hide();
            _tray.Visible = true;
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_exiting && _vm.AppSettings.CloseToTray)
        {
            e.Cancel = true;
            Hide();
            _tray.Visible = true;
            return;
        }
        SaveWindowState();
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _tray.Dispose();
        base.OnClosed(e);
        Application.Current.Shutdown();
    }

    private void ExitApp()
    {
        Dispatcher.Invoke(() =>
        {
            _exiting = true;
            Close();
        });
    }

    private void SaveWindowState()
    {
        var w = _vm.AppSettings.Window;
        bool compact = _vm.IsCompact;
        w.Maximized = WindowState == WindowState.Maximized;
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        w.Left = bounds.Left;
        w.Top = bounds.Top;
        // In compact mode keep the regular size so leaving compact (or relaunching) restores it.
        w.Width = compact ? _normalWidth : bounds.Width;
        w.Height = compact ? _normalHeight : bounds.Height;
    }

    // ----- compact mode -----

    private void ApplyCompact()
    {
        if (_vm.IsCompact)
        {
            if (WindowState == WindowState.Normal) { _normalWidth = Width; _normalHeight = Height; }
            WindowState = WindowState.Normal;
            MinWidth = 400;
            Width = CompactWidth;
            _vm.CurrentView = MainView.Mixer;
        }
        else
        {
            MinWidth = 860;
            Width = Math.Max(_normalWidth, 860);
            Height = _normalHeight;
        }
    }

    // ----- title bar -----

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    // ----- drag and drop import -----

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            _vm.CurrentView = MainView.Mixer;
            _vm.ImportPaths(files.Where(File.Exists));
        }
    }

    // ----- inline rename -----

    private void RenameBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is TextBox tb && tb.IsVisible)
        {
            tb.Dispatcher.BeginInvoke(() => { tb.Focus(); tb.SelectAll(); }, System.Windows.Threading.DispatcherPriority.Input);
        }
    }

    private void RenameBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ChannelViewModel vm }) vm.IsRenaming = false;
    }
}
