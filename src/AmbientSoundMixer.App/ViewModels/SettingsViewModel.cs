using System.Reflection;
using System.Windows.Input;
using AmbientSoundMixer.App.Infrastructure;
using AmbientSoundMixer.App.Services;
using AmbientSoundMixer.Core.Audio;
using AmbientSoundMixer.Core.Models;

namespace AmbientSoundMixer.App.ViewModels;

public sealed record DeviceOption(string? Id, string Name)
{
    public override string ToString() => Name;
}

public sealed class SettingsViewModel : ObservableObject
{
    private readonly AppSettings _s;
    private readonly MainViewModel _main;

    public SettingsViewModel(AppSettings settings, MainViewModel main)
    {
        _s = settings;
        _main = main;
        ResetCommand = new RelayCommand(Reset);
        RefreshDevicesCommand = new RelayCommand(RefreshDevices);
        RefreshDevices();
    }

    public ICommand ResetCommand { get; }
    public ICommand RefreshDevicesCommand { get; }

    public AppTheme[] Themes { get; } = { AppTheme.Dark, AppTheme.Light };
    public int[] SampleRates { get; } = EngineFormat.SupportedSampleRates;
    public List<DeviceOption> Devices { get; private set; } = new();

    public string VersionText => "Version " + (typeof(App).Assembly.GetName().Version?.ToString(3) ?? "1.0.0");

    public AppTheme Theme
    {
        get => _s.Theme;
        set { _s.Theme = value; ThemeService.Apply(value); OnPropertyChanged(); _main.RequestSave(); }
    }

    public double DefaultMasterPercent
    {
        get => Math.Round(_s.DefaultMasterVolume * 100);
        set { _s.DefaultMasterVolume = value / 100; OnPropertyChanged(); _main.RequestSave(); }
    }

    public double CrossfadeSeconds
    {
        get => _s.DefaultCrossfadeSeconds;
        set { _s.DefaultCrossfadeSeconds = Math.Round(value * 2) / 2; _main.Session.Options.CrossfadeSeconds = _s.DefaultCrossfadeSeconds; OnPropertyChanged(); _main.RequestSave(); }
    }

    public double FadeInSeconds
    {
        get => _s.FadeInSeconds;
        set { _s.FadeInSeconds = Math.Round(value * 2) / 2; _main.Session.Options.FadeInSeconds = _s.FadeInSeconds; OnPropertyChanged(); _main.RequestSave(); }
    }

    public double FadeOutSeconds
    {
        get => _s.FadeOutSeconds;
        set { _s.FadeOutSeconds = Math.Round(value * 2) / 2; _main.Session.Options.FadeOutSeconds = _s.FadeOutSeconds; OnPropertyChanged(); _main.RequestSave(); }
    }

    public bool RememberLastSession
    {
        get => _s.RememberLastSession;
        set { _s.RememberLastSession = value; OnPropertyChanged(); _main.RequestSave(); }
    }

    public bool AutoPlayOnStart
    {
        get => _s.AutoPlayOnStart;
        set { _s.AutoPlayOnStart = value; OnPropertyChanged(); _main.RequestSave(); }
    }

    public bool StartWithWindows
    {
        get => _s.StartWithWindows;
        set { _s.StartWithWindows = value; StartupRegistration.Set(value); OnPropertyChanged(); _main.RequestSave(); }
    }

    public bool MinimizeToTray
    {
        get => _s.MinimizeToTray;
        set { _s.MinimizeToTray = value; OnPropertyChanged(); _main.RequestSave(); _main.RaiseWindowOptionsChanged(); }
    }

    public bool CloseToTray
    {
        get => _s.CloseToTray;
        set { _s.CloseToTray = value; OnPropertyChanged(); _main.RequestSave(); _main.RaiseWindowOptionsChanged(); }
    }

    public bool AlwaysOnTop
    {
        get => _s.AlwaysOnTop;
        set { _s.AlwaysOnTop = value; OnPropertyChanged(); _main.OnAlwaysOnTopChanged(); _main.RequestSave(); }
    }

    public DeviceOption? SelectedDevice
    {
        get => Devices.FirstOrDefault(d => d.Id == _s.OutputDeviceId) ?? Devices.FirstOrDefault();
        set
        {
            if (value == null) return;
            _s.OutputDeviceId = value.Id;
            _main.Session.Options.OutputDeviceId = value.Id;
            _main.Session.ApplyOutputOptions();
            OnPropertyChanged();
            _main.RequestSave();
        }
    }

    public double LatencyMs
    {
        get => _s.LatencyMs;
        set
        {
            _s.LatencyMs = (int)Math.Round(value / 10) * 10;
            _main.Session.Options.LatencyMs = _s.LatencyMs;
            OnPropertyChanged();
            _main.RequestSave();
        }
    }

    public ICommand ApplyLatencyCommand => new RelayCommand(() => _main.Session.ApplyOutputOptions());

    public int SampleRate
    {
        get => _s.SampleRate;
        set { _s.SampleRate = value; OnPropertyChanged(); OnPropertyChanged(nameof(SampleRateNote)); _main.RequestSave(); }
    }

    public string SampleRateNote => _s.SampleRate != _main.Session.Options.SampleRate
        ? $"Will switch to {_s.SampleRate} Hz after you restart the app."
        : "Windows converts to the device format if needed.";

    public double SleepTimerMinutes
    {
        get => _s.SleepTimerMinutes;
        set { _s.SleepTimerMinutes = (int)Math.Round(value); OnPropertyChanged(); _main.SelectedSleepMinutes = _s.SleepTimerMinutes; _main.RequestSave(); }
    }

    public bool SleepTimerFadeOut
    {
        get => _s.SleepTimerFadeOut;
        set { _s.SleepTimerFadeOut = value; OnPropertyChanged(); _main.RequestSave(); }
    }

    public double SleepTimerFadeSeconds
    {
        get => _s.SleepTimerFadeSeconds;
        set { _s.SleepTimerFadeSeconds = Math.Round(value / 5) * 5; OnPropertyChanged(); _main.RequestSave(); }
    }

    public void RefreshDevices()
    {
        var list = new List<DeviceOption> { new(null, "System default device") };
        list.AddRange(AudioDevices.GetOutputDevices().Select(d => new DeviceOption(d.Id, d.Name)));
        // Keep a saved device visible even if currently unplugged, so the choice is not silently lost.
        if (_s.OutputDeviceId != null && list.All(d => d.Id != _s.OutputDeviceId))
            list.Add(new DeviceOption(_s.OutputDeviceId, "Saved device (not connected)"));
        Devices = list;
        OnPropertyChanged(nameof(Devices));
        OnPropertyChanged(nameof(SelectedDevice));
    }

    private void Reset()
    {
        if (System.Windows.MessageBox.Show("Reset all settings to their defaults? Your presets are not affected.", "Reset settings",
                System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question) != System.Windows.MessageBoxResult.Yes) return;

        var defaults = new AppSettings();
        foreach (var p in typeof(AppSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (p.Name is nameof(AppSettings.Window) or nameof(AppSettings.LastSession) or nameof(AppSettings.SchemaVersion)) continue;
            p.SetValue(_s, p.GetValue(defaults));
        }
        ThemeService.Apply(_s.Theme);
        StartupRegistration.Set(_s.StartWithWindows);
        var o = _main.Session.Options;
        o.CrossfadeSeconds = _s.DefaultCrossfadeSeconds; o.FadeInSeconds = _s.FadeInSeconds; o.FadeOutSeconds = _s.FadeOutSeconds;
        o.OutputDeviceId = _s.OutputDeviceId; o.LatencyMs = _s.LatencyMs;
        _main.Session.ApplyOutputOptions();
        _main.OnAlwaysOnTopChanged();
        _main.RaiseWindowOptionsChanged();
        OnPropertyChanged(string.Empty);
        _main.RequestSave();
    }
}
