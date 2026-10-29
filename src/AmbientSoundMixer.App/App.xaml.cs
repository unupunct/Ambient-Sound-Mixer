using System.Diagnostics;
using System.IO;
using System.Windows;
using AmbientSoundMixer.App.Services;
using AmbientSoundMixer.App.ViewModels;
using AmbientSoundMixer.Core.Audio;
using AmbientSoundMixer.Core.Persistence;
using AmbientSoundMixer.Core.Session;

namespace AmbientSoundMixer.App;

public partial class App : Application
{
    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;
    private MainWindow? _window;
    private MixerSession? _session;
    private MainViewModel? _vm;

    protected override void OnStartup(StartupEventArgs e)
    {
        try
        {
            StartApp(e);
        }
        catch (Exception ex)
        {
            LogCrash(ex);
            MessageBox.Show("Ambient Sound Mixer could not start:" + Environment.NewLine + Environment.NewLine + ex.Message, "Ambient Sound Mixer", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private static void LogCrash(Exception ex)
    {
        try
        {
            var dir = new AppPaths().Root;
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "error.log"), $"{DateTime.Now:u}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch { /* logging must never throw */ }
    }

    private void StartApp(StartupEventArgs e)
    {
        base.OnStartup(e);

        _mutex = new Mutex(true, @"Local\AmbientSoundMixer.SingleInstance", out bool first);
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\AmbientSoundMixer.Show");
        if (!first)
        {
            _showEvent.Set(); // ask the running instance to come to the front
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            LogCrash(args.Exception);
            MessageBox.Show("An unexpected error occurred:\n\n" + args.Exception.Message, "Ambient Sound Mixer", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        var paths = new AppPaths();
        var settingsStore = new SettingsStore(paths);
        var settings = settingsStore.Load(out var settingsWarning);
        var presetStore = new PresetStore(paths);
        try { presetStore.EnsureDefaults(); } catch (Exception ex) { Debug.WriteLine(ex); }

        ThemeService.Apply(settings.Theme);

        var options = new SessionOptions
        {
            OutputDeviceId = settings.OutputDeviceId,
            LatencyMs = settings.LatencyMs,
            SampleRate = settings.SampleRate,
            FadeInSeconds = settings.FadeInSeconds,
            FadeOutSeconds = settings.FadeOutSeconds,
            CrossfadeSeconds = settings.DefaultCrossfadeSeconds,
        };
        _session = new MixerSession(new WasapiOutputFactory(), options);
        _vm = new MainViewModel(_session, settings, settingsStore, presetStore);
        if (settingsWarning != null) _vm.ShowMessage(settingsWarning, true);

        bool minimized = e.Args.Contains("--minimized", StringComparer.OrdinalIgnoreCase);
        _window = new MainWindow(_vm);
        _vm.RestoreStartupState(minimized);
        MainWindow = _window;
        _window.ShowOrTray(minimized);

        var t = new Thread(() =>
        {
            while (_showEvent.WaitOne())
                Dispatcher.BeginInvoke(() => _window?.RestoreFromTray());
        }) { IsBackground = true };
        t.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _vm?.SaveNow(); } catch { }
        _vm?.Dispose();
        _session?.Dispose(); // closes the audio device
        try { _mutex?.ReleaseMutex(); } catch { }
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
