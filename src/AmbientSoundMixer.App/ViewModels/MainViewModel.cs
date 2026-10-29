using System.IO;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using AmbientSoundMixer.App.Infrastructure;
using AmbientSoundMixer.Core.Audio;
using AmbientSoundMixer.Core.Models;
using AmbientSoundMixer.Core.Persistence;
using AmbientSoundMixer.Core.Scenarios;
using AmbientSoundMixer.Core.Session;
using Microsoft.Win32;

namespace AmbientSoundMixer.App.ViewModels;

public enum MainView { Mixer, Presets, Settings }

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly AppSettings _settings;
    private readonly SettingsStore _settingsStore;
    private readonly PresetStore _presetStore;
    private readonly DispatcherTimer _uiTimer;
    private readonly DispatcherTimer _saveTimer;
    private MainView _view = MainView.Mixer;
    private string? _message;
    private bool _messageIsWarning;
    private double _peakPercent;
    private bool _nearClipping;
    private string _sleepText = "";
    private int _selectedSleepMinutes;
    private Preset? _selectedPreset;
    private string _newPresetName = "";
    private TransportState _lastState = TransportState.Stopped;

    public MainViewModel(MixerSession session, AppSettings settings, SettingsStore settingsStore, PresetStore presetStore)
    {
        Session = session;
        _settings = settings;
        _settingsStore = settingsStore;
        _presetStore = presetStore;
        _selectedSleepMinutes = settings.SleepTimerMinutes;

        _uiTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(100) };
        _uiTimer.Tick += (_, _) => UiTick();
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); SaveNow(); };

        Scenarios = new ObservableCollection<ScenarioViewModel>(BuiltInScenarios.All.Select(s => new ScenarioViewModel(s, this)));
        Settings = new SettingsViewModel(settings, this);

        PlayPauseCommand = new RelayCommand(TogglePlayPause);
        StopCommand = new RelayCommand(StopPlayback);
        ShowMixerCommand = new RelayCommand(() => CurrentView = MainView.Mixer);
        ShowPresetsCommand = new RelayCommand(() => { CurrentView = CurrentView == MainView.Presets ? MainView.Mixer : MainView.Presets; });
        ShowSettingsCommand = new RelayCommand(() => { CurrentView = CurrentView == MainView.Settings ? MainView.Mixer : MainView.Settings; });
        ToggleThemeCommand = new RelayCommand(() => Settings.Theme = _settings.Theme == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark);
        ToggleCompactCommand = new RelayCommand(() => IsCompact = !IsCompact);
        ToggleAlwaysOnTopCommand = new RelayCommand(() => Settings.AlwaysOnTop = !_settings.AlwaysOnTop);
        AddNoiseCommand = new RelayCommand(() => Session.AddNoise(NoiseType.White, 0.5));
        AddBinauralCommand = new RelayCommand(() => Session.AddBinaural());
        ImportCommand = new RelayCommand(ImportWithPicker);
        ToggleSleepCommand = new RelayCommand(ToggleSleep);
        DismissMessageCommand = new RelayCommand(() => { Message = null; });

        SavePresetCommand = new RelayCommand(SavePreset, () => !string.IsNullOrWhiteSpace(NewPresetName));
        LoadPresetCommand = new RelayCommand(p => LoadPreset(p as Preset ?? SelectedPreset), p => (p as Preset ?? SelectedPreset) != null);
        RenamePresetCommand = new RelayCommand(RenamePreset, () => SelectedPreset != null);
        DuplicatePresetCommand = new RelayCommand(DuplicatePreset, () => SelectedPreset != null);
        DeletePresetCommand = new RelayCommand(DeletePreset, () => SelectedPreset != null);
        RestoreDefaultPresetsCommand = new RelayCommand(RestoreDefaultPresets);

        Session.ChannelsChanged += (_, _) => Dispatch(RebuildChannels);
        Session.StateChanged += (_, _) => Dispatch(OnStateChanged);
        Session.MessageRaised += (_, m) => Dispatch(() => ShowMessage(m, warning: true));
        Session.SleepTimerChanged += (_, _) => Dispatch(UpdateSleepText);

        RebuildChannels();
        ReloadPresets();

        _uiTimer.Start();
    }

    public MixerSession Session { get; }
    public SettingsViewModel Settings { get; }
    public ObservableCollection<ScenarioViewModel> Scenarios { get; }
    public ObservableCollection<ChannelViewModel> Channels { get; } = new();
    public ObservableCollection<Preset> Presets { get; } = new();
    public int[] SleepOptions { get; } = { 5, 10, 15, 30, 45, 60, 90, 120, 180 };

    public ICommand PlayPauseCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand ShowMixerCommand { get; }
    public ICommand ShowPresetsCommand { get; }
    public ICommand ShowSettingsCommand { get; }
    public ICommand ToggleThemeCommand { get; }
    public ICommand ToggleCompactCommand { get; }
    public ICommand ToggleAlwaysOnTopCommand { get; }
    public ICommand AddNoiseCommand { get; }
    public ICommand AddBinauralCommand { get; }
    public ICommand ImportCommand { get; }
    public ICommand ToggleSleepCommand { get; }
    public ICommand DismissMessageCommand { get; }
    public ICommand SavePresetCommand { get; }
    public ICommand LoadPresetCommand { get; }
    public ICommand RenamePresetCommand { get; }
    public ICommand DuplicatePresetCommand { get; }
    public ICommand DeletePresetCommand { get; }
    public ICommand RestoreDefaultPresetsCommand { get; }

    public event EventHandler? WindowOptionsChanged;
    public event EventHandler? AlwaysOnTopChangedEvent;
    public event EventHandler? CompactChanged;

    public AppSettings AppSettings => _settings;

    // ----- state -----

    public MainView CurrentView
    {
        get => _view;
        set
        {
            if (!SetProperty(ref _view, value)) return;
            OnPropertyChanged(nameof(IsMixerView));
            OnPropertyChanged(nameof(IsPresetsView));
            OnPropertyChanged(nameof(IsSettingsView));
        }
    }

    public bool IsMixerView => _view == MainView.Mixer;
    public bool IsPresetsView => _view == MainView.Presets;
    public bool IsSettingsView => _view == MainView.Settings;

    public bool IsPlaying => Session.State == TransportState.Playing;
    public string PlayPauseGlyph => IsPlaying ? "" : "";
    public string PlayPauseText => IsPlaying ? "Pause" : Session.State == TransportState.Paused ? "Resume" : "Play";
    public string StateText => Session.State switch
    {
        TransportState.Playing => "Playing",
        TransportState.Paused => "Paused",
        _ => "Stopped",
    };

    public double MasterPercent
    {
        get => Math.Round(Session.MasterVolume * 100);
        set { Session.MasterVolume = (float)(value / 100.0); OnPropertyChanged(); RequestSave(); }
    }

    public double PeakPercent { get => _peakPercent; private set => SetProperty(ref _peakPercent, value); }
    public bool IsNearClipping { get => _nearClipping; private set { if (SetProperty(ref _nearClipping, value)) OnPropertyChanged(nameof(ClipText)); } }
    public string ClipText => IsNearClipping ? "Near clipping: lower the master or a channel" : "Level OK";

    public bool HasBinaural => Channels.Any(c => c.IsBinaural);
    public bool IsEmpty => Channels.Count == 0;

    public string? Message { get => _message; private set { if (SetProperty(ref _message, value)) OnPropertyChanged(nameof(HasMessage)); } }
    public bool HasMessage => !string.IsNullOrEmpty(_message);
    public bool MessageIsWarning { get => _messageIsWarning; private set => SetProperty(ref _messageIsWarning, value); }

    public bool IsCompact
    {
        get => _settings.CompactMode;
        set { _settings.CompactMode = value; OnPropertyChanged(); CompactChanged?.Invoke(this, EventArgs.Empty); RequestSave(); }
    }

    public bool IsAlwaysOnTop => _settings.AlwaysOnTop;
    public bool IsLightTheme => _settings.Theme == AppTheme.Light;

    // ----- sleep timer -----

    public int SelectedSleepMinutes
    {
        get => _selectedSleepMinutes;
        set { if (SetProperty(ref _selectedSleepMinutes, value)) OnPropertyChanged(nameof(SleepButtonText)); }
    }

    public bool IsSleepActive => Session.SleepTimerRemaining != null;
    public string SleepText { get => _sleepText; private set => SetProperty(ref _sleepText, value); }
    public string SleepButtonText => IsSleepActive ? "Cancel timer" : "Start timer";

    private void ToggleSleep()
    {
        if (Session.SleepTimerRemaining != null)
            Session.CancelSleepTimer();
        else
        {
            Session.StartSleepTimer(TimeSpan.FromMinutes(SelectedSleepMinutes), _settings.SleepTimerFadeOut, _settings.SleepTimerFadeSeconds);
            if (Session.State != TransportState.Playing) Session.Play();
        }
        UpdateSleepText();
    }

    private void UpdateSleepText()
    {
        var left = Session.SleepTimerRemaining;
        SleepText = left is { } t ? $"Stops in {(int)t.TotalHours:0}:{t.Minutes:00}:{t.Seconds:00}" : "";
        OnPropertyChanged(nameof(IsSleepActive));
        OnPropertyChanged(nameof(SleepButtonText));
    }

    // ----- transport -----

    public void TogglePlayPause()
    {
        if (Session.State == TransportState.Playing) Session.Pause();
        else Session.Play();
    }

    public void StopPlayback() => Session.Stop();

    public void PlayScenario(Scenario s)
    {
        if (Session.CurrentScenarioId != s.Id) Session.LoadScenario(s.Id);
        Session.Play();
    }

    public void SelectScenario(Scenario s)
    {
        if (Session.CurrentScenarioId != s.Id) Session.LoadScenario(s.Id);
        CurrentView = MainView.Mixer;
        RequestSave();
    }

    private void OnStateChanged()
    {
        OnPropertyChanged(nameof(IsPlaying));
        OnPropertyChanged(nameof(PlayPauseGlyph));
        OnPropertyChanged(nameof(PlayPauseText));
        OnPropertyChanged(nameof(StateText));
        RefreshScenarioFlags();
        foreach (var c in Channels) c.Refresh();
    }

    private void RefreshScenarioFlags()
    {
        foreach (var s in Scenarios)
        {
            s.IsActive = Session.CurrentScenarioId == s.Id;
            s.IsPlaying = s.IsActive && IsPlaying;
        }
    }

    private void UiTick()
    {
        var e = Session.Engine;
        PeakPercent = Math.Min(1f, e.PeakLevel) * 100;
        IsNearClipping = e.IsNearClipping;
        foreach (var c in Channels) c.Refresh();
        if (Session.SleepTimerRemaining != null) UpdateSleepText();
        else if (SleepText.Length > 0) UpdateSleepText();
        if (_lastState != Session.State) { _lastState = Session.State; OnStateChanged(); }
    }

    // ----- channels -----

    private void RebuildChannels()
    {
        var existing = Channels.ToDictionary(c => c.Channel.Id);
        var ordered = Session.Channels.Select(c => existing.TryGetValue(c.Id, out var vm) ? vm : new ChannelViewModel(c, this)).ToList();
        Channels.Clear();
        foreach (var vm in ordered) Channels.Add(vm);
        OnPropertyChanged(nameof(HasBinaural));
        OnPropertyChanged(nameof(IsEmpty));
        RefreshScenarioFlags();
        RequestSave();
    }

    public void RemoveChannel(ChannelViewModel vm) => Session.RemoveChannel(vm.Channel.Id);

    public void ImportPaths(IEnumerable<string> paths)
    {
        var skipped = new List<string>();
        foreach (var path in paths)
        {
            if (!AudioFileDecoder.IsSupported(path)) { skipped.Add(Path.GetFileName(path)); continue; }
            Session.AddFile(path);
        }
        if (skipped.Count > 0)
            ShowMessage("Unsupported file type: " + string.Join(", ", skipped) + ". Supported: " + string.Join(", ", AudioFileDecoder.SupportedExtensions), true);
    }

    private void ImportWithPicker()
    {
        var exts = string.Join(";", AudioFileDecoder.SupportedExtensions.Select(e => "*" + e));
        var dlg = new OpenFileDialog { Title = "Import audio files", Multiselect = true, Filter = $"Audio files ({exts})|{exts}|All files|*.*" };
        if (dlg.ShowDialog() == true) ImportPaths(dlg.FileNames);
    }

    // ----- presets -----

    public Preset? SelectedPreset
    {
        get => _selectedPreset;
        set => SetProperty(ref _selectedPreset, value);
    }

    public string NewPresetName { get => _newPresetName; set => SetProperty(ref _newPresetName, value); }

    public void ReloadPresets()
    {
        var all = _presetStore.LoadAll(out var warnings);
        var selId = SelectedPreset?.Id;
        Presets.Clear();
        foreach (var p in all) Presets.Add(p);
        SelectedPreset = Presets.FirstOrDefault(p => p.Id == selId);
        if (warnings.Count > 0) ShowMessage(string.Join(" ", warnings), true);
    }

    private void SavePreset()
    {
        string name = NewPresetName.Trim();
        var existing = Presets.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.CurrentCultureIgnoreCase));
        if (existing != null &&
            MessageBox.Show($"A preset named '{existing.Name}' already exists. Replace it?", "Save preset", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        var preset = Session.CaptureState(name);
        preset.FadeSeconds = null;
        if (existing != null) preset.Id = existing.Id;
        try
        {
            _presetStore.Save(preset);
            NewPresetName = "";
            ReloadPresets();
            SelectedPreset = Presets.FirstOrDefault(p => p.Id == preset.Id);
            ShowMessage($"Saved preset '{preset.Name}'.", false);
        }
        catch (Exception ex) { ShowMessage("Could not save preset: " + ex.Message, true); }
    }

    public void LoadPreset(Preset? preset)
    {
        if (preset == null) return;
        try
        {
            var warnings = Session.ApplyPreset(preset);
            MasterChanged();
            CurrentView = MainView.Mixer;
            if (warnings.Count > 0) ShowMessage(string.Join(" ", warnings), true);
            else ShowMessage($"Loaded '{preset.Name}'.", false);
            if (Session.State != TransportState.Playing) Session.Play();
        }
        catch (Exception ex) { ShowMessage("Could not load preset: " + ex.Message, true); }
    }

    private void RenamePreset()
    {
        if (SelectedPreset == null) return;
        var name = PromptWindow.Ask(Application.Current.MainWindow, "Rename preset", SelectedPreset.Name);
        if (name == null) return;
        try
        {
            _presetStore.Rename(SelectedPreset, name);
            var id = SelectedPreset.Id;
            ReloadPresets();
            SelectedPreset = Presets.FirstOrDefault(p => p.Id == id);
        }
        catch (Exception ex) { ShowMessage("Could not rename preset: " + ex.Message, true); }
    }

    private void DuplicatePreset()
    {
        if (SelectedPreset == null) return;
        try
        {
            var copy = _presetStore.Duplicate(SelectedPreset);
            ReloadPresets();
            SelectedPreset = Presets.FirstOrDefault(p => p.Id == copy.Id);
        }
        catch (Exception ex) { ShowMessage("Could not duplicate preset: " + ex.Message, true); }
    }

    private void DeletePreset()
    {
        if (SelectedPreset == null) return;
        if (MessageBox.Show($"Delete preset '{SelectedPreset.Name}'? Your audio files are not touched.", "Delete preset",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            _presetStore.Delete(SelectedPreset.Id);
            SelectedPreset = null;
            ReloadPresets();
        }
        catch (Exception ex) { ShowMessage("Could not delete preset: " + ex.Message, true); }
    }

    private void RestoreDefaultPresets()
    {
        try
        {
            _presetStore.RestoreDefaults();
            ReloadPresets();
            ShowMessage("Default presets restored. Presets you edited or renamed were left as they are.", false);
        }
        catch (Exception ex) { ShowMessage("Could not restore presets: " + ex.Message, true); }
    }

    // ----- session restore / persistence -----

    public void RestoreStartupState(bool startMinimized)
    {
        Session.MasterVolume = (float)_settings.DefaultMasterVolume;
        bool restored = false;
        if (_settings.RememberLastSession && _settings.LastSession is { } last && last.Channels.Count > 0)
        {
            try
            {
                var warnings = Session.ApplyPreset(last);
                if (warnings.Count > 0) ShowMessage(string.Join(" ", warnings), true);
                restored = true;
            }
            catch (Exception ex) { ShowMessage("Could not restore the last session: " + ex.Message, true); }
        }
        if (!restored) Session.LoadScenario("ocean", 0);
        MasterChanged();
        if (_settings.AutoPlayOnStart) Session.Play();
    }

    private void MasterChanged() => OnPropertyChanged(nameof(MasterPercent));

    public void RequestSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    public void SaveNow()
    {
        try
        {
            _settings.LastSession = _settings.RememberLastSession ? Session.CaptureState("Last session") : null;
            _settingsStore.Save(_settings);
        }
        catch (Exception ex)
        {
            ShowMessage("Could not save settings: " + ex.Message, true);
        }
    }

    public void RaiseWindowOptionsChanged()
    {
        OnPropertyChanged(nameof(IsLightTheme));
        WindowOptionsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void OnAlwaysOnTopChanged()
    {
        OnPropertyChanged(nameof(IsAlwaysOnTop));
        AlwaysOnTopChangedEvent?.Invoke(this, EventArgs.Empty);
    }

    public void NotifyThemeChanged() => OnPropertyChanged(nameof(IsLightTheme));

    public void ShowMessage(string text, bool warning)
    {
        MessageIsWarning = warning;
        Message = text;
    }

    private static void Dispatch(Action a)
    {
        var d = Application.Current?.Dispatcher;
        if (d == null) return;
        if (d.CheckAccess()) a(); else d.BeginInvoke(a);
    }

    public void Dispose()
    {
        _uiTimer.Stop();
        _saveTimer.Stop();
    }
}
