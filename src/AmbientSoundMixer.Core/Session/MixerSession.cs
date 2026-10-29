using AmbientSoundMixer.Core.Audio;
using AmbientSoundMixer.Core.Models;
using AmbientSoundMixer.Core.Persistence;
using AmbientSoundMixer.Core.Scenarios;

namespace AmbientSoundMixer.Core.Session;

public enum TransportState { Stopped, Playing, Paused }

public sealed class SessionOptions
{
    public string? OutputDeviceId { get; set; }
    public int LatencyMs { get; set; } = 100;
    public int SampleRate { get; set; } = EngineFormat.DefaultSampleRate;
    public double FadeInSeconds { get; set; } = 1.0;
    public double FadeOutSeconds { get; set; } = 1.5;
    public double CrossfadeSeconds { get; set; } = 2.5;
}

/// <summary>
/// Owns the engine, the output device and the list of channels, and keeps playback state consistent.
/// The audio device is only open while sound is audible, so an idle app costs no CPU.
/// </summary>
public sealed class MixerSession : IDisposable
{
    private enum Pending { None, Pause, Stop }

    private readonly object _gate = new();
    private readonly IAudioOutputFactory _factory;
    private readonly TimeProvider _time;
    private readonly Timer? _timer;
    private readonly Dictionary<string, Task> _loads = new();
    private IAudioOutput? _output;
    private Pending _pending = Pending.None;
    private TransportState _state = TransportState.Stopped;
    private volatile bool _restartRequested;
    private string? _restartReason;
    private bool _disposed;
    private DateTimeOffset? _sleepEnd;
    private bool _sleepFade;
    private double _sleepFadeSeconds;

    public MixerSession(IAudioOutputFactory factory, SessionOptions? options = null, TimeProvider? time = null, bool autoTick = true)
    {
        _factory = factory;
        Options = options ?? new SessionOptions();
        _time = time ?? TimeProvider.System;
        Engine = new MixerEngine(Options.SampleRate);
        if (autoTick) _timer = new Timer(_ => Tick(), null, 200, 200);
    }

    public SessionOptions Options { get; }
    public MixerEngine Engine { get; }
    public string? CurrentScenarioId { get; private set; }

    public event EventHandler? StateChanged;
    public event EventHandler? ChannelsChanged;
    public event EventHandler<string>? MessageRaised;
    public event EventHandler? SleepTimerChanged;

    public TransportState State { get { lock (_gate) return _state; } }
    public bool IsOutputOpen { get { lock (_gate) return _output != null; } }

    /// <summary>Channels shown in the mixer (excludes channels that are only finishing a fade-out).</summary>
    public IReadOnlyList<MixerChannel> Channels => Engine.Channels.Where(c => !c.IsRemoving).ToList();

    public float MasterVolume { get => Engine.MasterVolume; set => Engine.MasterVolume = value; }

    public TimeSpan? SleepTimerRemaining
    {
        get
        {
            lock (_gate)
            {
                if (_sleepEnd is not { } end) return null;
                var left = end - _time.GetUtcNow();
                return left > TimeSpan.Zero ? left : TimeSpan.Zero;
            }
        }
    }

    // ----- transport -----

    public void Play()
    {
        var ev = new List<Action>();
        lock (_gate)
        {
            if (_disposed) return;
            if (_state == TransportState.Playing && _pending == Pending.None) return;
            if (!TryEnsureOutput(ev)) { SetState(TransportState.Stopped, ev); Flush(ev); return; }
            _pending = Pending.None;
            Engine.SetTransport(1f, Options.FadeInSeconds);
            SetState(TransportState.Playing, ev);
        }
        Flush(ev);
    }

    public void Pause()
    {
        var ev = new List<Action>();
        lock (_gate)
        {
            if (_state != TransportState.Playing) return;
            Engine.SetTransport(0f, Options.FadeOutSeconds);
            _pending = Pending.Pause;
            SetState(TransportState.Paused, ev);
        }
        Flush(ev);
    }

    public void Resume() => Play();

    public void Stop(double? fadeSeconds = null)
    {
        var ev = new List<Action>();
        lock (_gate) StopLocked(fadeSeconds ?? Options.FadeOutSeconds, ev);
        Flush(ev);
    }

    private void StopLocked(double fade, List<Action> ev)
    {
        if (_state == TransportState.Stopped && _pending == Pending.None) return;
        if (_output != null)
        {
            Engine.SetTransport(0f, fade);
            _pending = Pending.Stop;
        }
        SetState(TransportState.Stopped, ev);
    }

    // ----- sleep timer -----

    public void StartSleepTimer(TimeSpan duration, bool fadeOut, double fadeSeconds)
    {
        lock (_gate)
        {
            _sleepEnd = _time.GetUtcNow() + duration;
            _sleepFade = fadeOut;
            _sleepFadeSeconds = Math.Min(Math.Max(fadeSeconds, 0), duration.TotalSeconds);
        }
        SleepTimerChanged?.Invoke(this, EventArgs.Empty);
    }

    public void CancelSleepTimer()
    {
        lock (_gate) _sleepEnd = null;
        SleepTimerChanged?.Invoke(this, EventArgs.Empty);
    }

    // ----- housekeeping -----

    /// <summary>Called by the internal timer; public so tests can drive time deterministically.</summary>
    public void Tick()
    {
        var ev = new List<Action>();
        lock (_gate)
        {
            if (_disposed) return;

            if (_restartRequested)
            {
                _restartRequested = false;
                string reason = _restartReason ?? "Audio device changed.";
                bool wanted = _output != null && (_state == TransportState.Playing || _pending != Pending.None);
                if (_output != null) CloseOutput();
                if (wanted)
                {
                    if (TryEnsureOutput(ev))
                    {
                        ev.Add(() => MessageRaised?.Invoke(this, reason + " Playback continued on the current device."));
                    }
                    else
                    {
                        _pending = Pending.None;
                        SetState(TransportState.Stopped, ev);
                        ev.Add(() => MessageRaised?.Invoke(this, reason + " Playback stopped."));
                    }
                }
            }

            if (_pending != Pending.None && (_output == null || Engine.TransportLevel <= 0.0005f))
            {
                CloseOutput();
                _pending = Pending.None;
            }

            foreach (var c in Engine.RemoveFinished()) (c.Source as IDisposable)?.Dispose();

            if (_sleepEnd is { } end)
            {
                var left = end - _time.GetUtcNow();
                if (left <= TimeSpan.Zero)
                {
                    _sleepEnd = null;
                    StopLocked(0.5, ev);
                    ev.Add(() => SleepTimerChanged?.Invoke(this, EventArgs.Empty));
                }
                else if (_sleepFade && _sleepFadeSeconds > 0 && left.TotalSeconds <= _sleepFadeSeconds)
                {
                    _sleepEnd = null;
                    StopLocked(left.TotalSeconds, ev);
                    ev.Add(() => SleepTimerChanged?.Invoke(this, EventArgs.Empty));
                }
            }
        }
        Flush(ev);
    }

    private bool TryEnsureOutput(List<Action> ev)
    {
        if (_output != null) return true;
        IAudioOutput? o = null;
        try
        {
            try
            {
                o = _factory.Create(Options.OutputDeviceId, Options.LatencyMs);
            }
            catch when (Options.OutputDeviceId != null)
            {
                ev.Add(() => MessageRaised?.Invoke(this, "The selected output device is unavailable; using the Windows default device."));
                o = _factory.Create(null, Options.LatencyMs);
            }
            o.Init(Engine);
            o.Stopped += OnOutputStopped;
            o.DeviceChanged += OnOutputDeviceChanged;
            o.Play();
            _output = o;
            return true;
        }
        catch (Exception ex)
        {
            o?.Dispose();
            ev.Add(() => MessageRaised?.Invoke(this, "Could not start audio output: " + ex.Message));
            return false;
        }
    }

    private void CloseOutput()
    {
        var o = _output;
        _output = null;
        if (o == null) return;
        o.Stopped -= OnOutputStopped;
        o.DeviceChanged -= OnOutputDeviceChanged;
        try { o.Dispose(); } catch { /* device may already be gone */ }
    }

    private void OnOutputStopped(object? sender, Exception? ex)
    {
        if (ex == null) return;
        _restartReason = "The audio device stopped (" + ex.Message + ").";
        _restartRequested = true;
    }

    private void OnOutputDeviceChanged(object? sender, EventArgs e)
    {
        _restartReason = "The audio output device changed.";
        _restartRequested = true;
    }

    /// <summary>Re-opens the output after the device or latency options changed.</summary>
    public void ApplyOutputOptions()
    {
        _restartReason = "Audio output settings changed.";
        _restartRequested = true;
        Tick();
    }

    private void SetState(TransportState s, List<Action> ev)
    {
        if (_state == s) return;
        _state = s;
        ev.Add(() => StateChanged?.Invoke(this, EventArgs.Empty));
    }

    private static void Flush(List<Action> ev)
    {
        foreach (var a in ev) a();
    }

    // ----- channels -----

    private bool Audible => _output != null;

    private double FadeForNewChannel => Audible ? 0.3 : 0;

    private MixerChannel AddChannelInternal(MixerChannel c, double fadeIn)
    {
        if (fadeIn > 0)
        {
            c.SetFadeLevelImmediate(0f);
            c.FadeTo(1f, fadeIn);
        }
        Engine.AddChannel(c);
        return c;
    }

    public MixerChannel AddNoise(NoiseType type = NoiseType.White, double volume = 0.5, string? name = null)
    {
        MixerChannel c;
        lock (_gate)
        {
            var src = new NoiseSource(Options.SampleRate, type);
            c = new MixerChannel(Guid.NewGuid().ToString("N"), name ?? NoiseName(type), ChannelKind.Noise, src, Options.SampleRate, (float)volume)
            { Icon = "\U0001F4FB" };
            AddChannelInternal(c, FadeForNewChannel);
        }
        ChannelsChanged?.Invoke(this, EventArgs.Empty);
        return c;
    }

    public MixerChannel AddBinaural(double baseHz = BinauralLimits.DefaultBaseHz, double beatHz = BinauralLimits.DefaultBeatHz, double volume = 0.25, string? name = null)
    {
        MixerChannel c;
        lock (_gate)
        {
            var src = new BinauralSource(Options.SampleRate, baseHz, beatHz);
            c = new MixerChannel(Guid.NewGuid().ToString("N"), name ?? "Binaural beat", ChannelKind.Binaural, src, Options.SampleRate, (float)volume)
            { Icon = "\U0001F3A7" };
            AddChannelInternal(c, FadeForNewChannel);
        }
        ChannelsChanged?.Invoke(this, EventArgs.Empty);
        return c;
    }

    /// <summary>
    /// Adds an imported file channel. The file is decoded on a background thread; until it is ready (or if it
    /// is missing or unreadable) the channel stays silent and reports its status. The file itself is never modified.
    /// </summary>
    public MixerChannel AddFile(string path, string? name = null, double volume = 0.5)
    {
        MixerChannel c;
        lock (_gate)
            c = AddFileLocked(path, name ?? Path.GetFileNameWithoutExtension(path), volume, FadeForNewChannel);
        ChannelsChanged?.Invoke(this, EventArgs.Empty);
        return c;
    }

    /// <summary>Waits for pending file decodes (used by tests and when applying presets).</summary>
    public Task WaitForLoadsAsync()
    {
        Task[] tasks;
        lock (_gate) tasks = _loads.Values.ToArray();
        return Task.WhenAll(tasks);
    }

    public void RemoveChannel(string id)
    {
        lock (_gate)
        {
            var c = Engine.Channels.FirstOrDefault(x => x.Id == id && !x.IsRemoving);
            if (c == null) return;
            RemoveInternal(c, 0.3);
            _loads.Remove(id);
        }
        ChannelsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RemoveInternal(MixerChannel c, double fade)
    {
        if (Audible && fade > 0)
        {
            c.BeginRemoval(fade);
        }
        else if (Engine.RemoveChannel(c))
        {
            (c.Source as IDisposable)?.Dispose();
        }
    }

    public IReadOnlyList<MixerChannel> ChannelsSnapshot() => Channels;

    // ----- scenarios and presets -----

    public void LoadScenario(string scenarioId, double? crossfadeSeconds = null)
    {
        var scenario = BuiltInScenarios.Find(scenarioId) ?? throw new ArgumentException("Unknown scenario: " + scenarioId, nameof(scenarioId));
        double fade = crossfadeSeconds ?? Options.CrossfadeSeconds;
        lock (_gate)
        {
            foreach (var c in Channels.Where(x => x.Kind == ChannelKind.Layer).ToList())
                RemoveInternal(c, fade);

            foreach (var layerId in scenario.LayerIds)
                AddLayerInternal(layerId, null, fade);
            CurrentScenarioId = scenario.Id;
        }
        ChannelsChanged?.Invoke(this, EventArgs.Empty);
    }

    private MixerChannel? AddLayerInternal(string layerId, ChannelState? state, double fade)
    {
        if (!LayerCatalog.TryGet(layerId, out var def)) return null;
        var c = new MixerChannel(Guid.NewGuid().ToString("N"), state?.Name ?? def.Name, ChannelKind.Layer,
            def.Create(Options.SampleRate), Options.SampleRate, (float)(state?.Volume ?? def.DefaultVolume))
        { LayerId = layerId, Icon = def.Icon };
        if (state != null) { c.Muted = state.Muted; c.Enabled = state.Enabled; }
        return AddChannelInternal(c, Audible ? fade : 0);
    }

    /// <summary>Applies a preset. Unknown layers are skipped; missing files load as silent channels with a warning.</summary>
    public List<string> ApplyPreset(Preset preset)
    {
        var validation = PresetValidator.Validate(preset);
        if (!validation.IsValid) throw new InvalidDataException(string.Join(" ", validation.Errors));
        var warnings = new List<string>(validation.Warnings);
        double fade = preset.FadeSeconds ?? Options.CrossfadeSeconds;

        lock (_gate)
        {
            foreach (var c in Channels.ToList()) RemoveInternal(c, fade);
            Engine.MasterVolume = (float)preset.MasterVolume;
            CurrentScenarioId = BuiltInScenarios.Find(preset.ScenarioId)?.Id;

            foreach (var s in preset.Channels)
            {
                switch (s.Kind)
                {
                    case ChannelKind.Layer:
                        if (AddLayerInternal(s.LayerId!, s, fade) == null)
                            warnings.Add($"Unknown sound layer '{s.LayerId}' was skipped.");
                        break;
                    case ChannelKind.Noise:
                    {
                        var c = AddChannelInternal(new MixerChannel(Guid.NewGuid().ToString("N"), s.Name, ChannelKind.Noise,
                            new NoiseSource(Options.SampleRate, s.NoiseType), Options.SampleRate, (float)s.Volume) { Icon = "\U0001F4FB" }, Audible ? fade : 0);
                        c.Muted = s.Muted; c.Enabled = s.Enabled;
                        break;
                    }
                    case ChannelKind.Binaural:
                    {
                        var c = AddChannelInternal(new MixerChannel(Guid.NewGuid().ToString("N"), s.Name, ChannelKind.Binaural,
                            new BinauralSource(Options.SampleRate, s.BaseFrequency, s.BeatFrequency), Options.SampleRate, (float)s.Volume) { Icon = "\U0001F3A7" }, Audible ? fade : 0);
                        c.Muted = s.Muted; c.Enabled = s.Enabled;
                        break;
                    }
                    case ChannelKind.File:
                    {
                        var c = AddFileLocked(s.FilePath!, s.Name, s.Volume, Audible ? fade : 0);
                        c.Muted = s.Muted; c.Enabled = s.Enabled;
                        if (c.Status == ChannelStatus.Missing) warnings.Add($"Missing file for '{s.Name}': {s.FilePath}");
                        break;
                    }
                }
            }
        }
        ChannelsChanged?.Invoke(this, EventArgs.Empty);
        return warnings;
    }

    private MixerChannel AddFileLocked(string path, string name, double volume, double fade)
    {
        // Same as AddFile but already inside the gate and without raising ChannelsChanged.
        var c = new MixerChannel(Guid.NewGuid().ToString("N"), name, ChannelKind.File, new SilentSource(Options.SampleRate),
            Options.SampleRate, (float)volume) { FilePath = path, Icon = "\U0001F3B5" };
        if (!File.Exists(path)) c.SetStatus(ChannelStatus.Missing, "File not found: " + path);
        else if (!AudioFileDecoder.IsSupported(path)) c.SetStatus(ChannelStatus.Error, "Unsupported file type: " + Path.GetExtension(path));
        else c.SetStatus(ChannelStatus.Loading, "Loading…");
        AddChannelInternal(c, fade);
        if (c.Status == ChannelStatus.Loading)
        {
            int rate = Options.SampleRate;
            _loads[c.Id] = Task.Run(() =>
            {
                try
                {
                    var d = AudioFileDecoder.Decode(path, rate);
                    c.SetSource(new LoopingBufferSource(d.Samples, d.Frames, rate));
                    c.SetStatus(ChannelStatus.Ready, d.Truncated ? "Playing first 10 minutes only" : null);
                }
                catch (Exception ex)
                {
                    c.SetStatus(ChannelStatus.Error, "Could not read file: " + ex.Message);
                }
            });
        }
        return c;
    }

    /// <summary>Snapshot of the current mix, suitable for saving as a preset or as the last session.</summary>
    public Preset CaptureState(string name)
    {
        var preset = new Preset
        {
            Name = name,
            ScenarioId = CurrentScenarioId,
            MasterVolume = Engine.MasterVolume,
        };
        foreach (var c in Channels)
        {
            preset.Channels.Add(new ChannelState
            {
                Id = c.Id,
                Kind = c.Kind,
                Name = c.Name,
                Volume = c.Volume,
                Muted = c.Muted,
                Enabled = c.Enabled,
                LayerId = c.LayerId,
                NoiseType = c.Noise?.Type ?? NoiseType.White,
                BaseFrequency = c.Binaural?.BaseFrequency ?? BinauralLimits.DefaultBaseHz,
                BeatFrequency = c.Binaural?.BeatFrequency ?? BinauralLimits.DefaultBeatHz,
                FilePath = c.FilePath,
            });
        }
        return preset;
    }

    public static string NoiseName(NoiseType t) => t switch
    {
        NoiseType.Pink => "Pink noise",
        NoiseType.Brown => "Brown noise",
        _ => "White noise",
    };

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _timer?.Dispose();
            CloseOutput();
            foreach (var c in Engine.Channels) (c.Source as IDisposable)?.Dispose();
        }
    }
}
