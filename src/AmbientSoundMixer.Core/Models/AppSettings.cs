using AmbientSoundMixer.Core.Audio;

namespace AmbientSoundMixer.Core.Models;

public enum AppTheme { Dark, Light }

public sealed class WindowSettings
{
    public double? Left { get; set; }
    public double? Top { get; set; }
    public double Width { get; set; } = 1120;
    public double Height { get; set; } = 760;
    public bool Maximized { get; set; }
}

public sealed class AppSettings
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public AppTheme Theme { get; set; } = AppTheme.Dark;
    public double DefaultMasterVolume { get; set; } = 0.8;
    public double DefaultCrossfadeSeconds { get; set; } = 2.5;
    public double FadeInSeconds { get; set; } = 1.0;
    public double FadeOutSeconds { get; set; } = 1.5;

    public bool RememberLastSession { get; set; } = true;
    public bool AutoPlayOnStart { get; set; }
    public bool StartWithWindows { get; set; }
    public bool MinimizeToTray { get; set; }
    public bool CloseToTray { get; set; }
    public bool AlwaysOnTop { get; set; }
    public bool CompactMode { get; set; }

    /// <summary>Null = follow the Windows default output device.</summary>
    public string? OutputDeviceId { get; set; }
    /// <summary>Engine sample rate; applied at next launch.</summary>
    public int SampleRate { get; set; } = EngineFormat.DefaultSampleRate;
    public int LatencyMs { get; set; } = 100;

    public int SleepTimerMinutes { get; set; } = 30;
    public bool SleepTimerFadeOut { get; set; } = true;
    public double SleepTimerFadeSeconds { get; set; } = 30;

    public WindowSettings Window { get; set; } = new();
    public Preset? LastSession { get; set; }

    /// <summary>Clamps every value into its supported range. Call after loading or editing.</summary>
    public void Normalize()
    {
        if (!Enum.IsDefined(Theme)) Theme = AppTheme.Dark;
        DefaultMasterVolume = Clamp(DefaultMasterVolume, 0, 1, 0.8);
        DefaultCrossfadeSeconds = Clamp(DefaultCrossfadeSeconds, 0, 15, 2.5);
        FadeInSeconds = Clamp(FadeInSeconds, 0, 15, 1);
        FadeOutSeconds = Clamp(FadeOutSeconds, 0, 15, 1.5);
        if (!EngineFormat.SupportedSampleRates.Contains(SampleRate)) SampleRate = EngineFormat.DefaultSampleRate;
        LatencyMs = Math.Clamp(LatencyMs, 30, 500);
        SleepTimerMinutes = Math.Clamp(SleepTimerMinutes, 1, 600);
        SleepTimerFadeSeconds = Clamp(SleepTimerFadeSeconds, 0, 300, 30);
        if (string.IsNullOrWhiteSpace(OutputDeviceId)) OutputDeviceId = null;
        Window ??= new WindowSettings();
        Window.Width = Clamp(Window.Width, 640, 10000, 1120);
        Window.Height = Clamp(Window.Height, 420, 10000, 760);
        if (Window.Left is { } l && !double.IsFinite(l)) Window.Left = null;
        if (Window.Top is { } t && !double.IsFinite(t)) Window.Top = null;
    }

    private static double Clamp(double v, double min, double max, double fallback) =>
        double.IsFinite(v) ? Math.Clamp(v, min, max) : fallback;
}
