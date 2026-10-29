using System.Diagnostics;
using System.Windows;
using Microsoft.Win32;
using AmbientSoundMixer.Core.Models;

namespace AmbientSoundMixer.App.Services;

public static class ThemeService
{
    private static ResourceDictionary? _current;

    public static void Apply(AppTheme mode)
    {
        var uri = new Uri($"pack://application:,,,/AmbientSoundMixer;component/Themes/{(mode == AppTheme.Light ? "Light" : "Dark")}.xaml");
        var dict = new ResourceDictionary { Source = uri };
        var merged = Application.Current.Resources.MergedDictionaries;
        if (_current != null) merged.Remove(_current);
        merged.Insert(0, dict);
        _current = dict;
    }
}

public static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "AmbientSoundMixer";

    public static void Set(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true) ?? Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled)
            {
                string? exe = Environment.ProcessPath;
                if (exe != null) key.SetValue(ValueName, $"\"{exe}\" --minimized");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Startup registration failed: " + ex.Message);
        }
    }

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) != null;
        }
        catch { return false; }
    }
}

/// <summary>System tray icon with Show, Play/Pause, Stop and Exit.</summary>
public sealed class TrayService : IDisposable
{
    private readonly System.Windows.Forms.NotifyIcon _icon;
    private readonly System.Windows.Forms.ToolStripMenuItem _playPause;

    public TrayService(Action show, Action playPause, Action stop, Action exit, Func<bool> isPlaying)
    {
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Show", null, (_, _) => show());
        _playPause = new System.Windows.Forms.ToolStripMenuItem("Play", null, (_, _) => playPause());
        menu.Items.Add(_playPause);
        menu.Items.Add("Stop", null, (_, _) => stop());
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => exit());
        menu.Opening += (_, _) => _playPause.Text = isPlaying() ? "Pause" : "Play";

        System.Drawing.Icon icon;
        var stream = Application.GetResourceStream(new Uri("pack://application:,,,/AmbientSoundMixer;component/Assets/app.ico"))?.Stream;
        icon = stream != null ? new System.Drawing.Icon(stream) : System.Drawing.SystemIcons.Application;

        _icon = new System.Windows.Forms.NotifyIcon
        {
            Icon = icon,
            Text = "Ambient Sound Mixer",
            ContextMenuStrip = menu,
            Visible = false,
        };
        _icon.MouseClick += (_, e) => { if (e.Button == System.Windows.Forms.MouseButtons.Left) show(); };
    }

    public bool Visible { get => _icon.Visible; set => _icon.Visible = value; }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
