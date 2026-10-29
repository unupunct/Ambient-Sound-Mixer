using NAudio.Wave;

namespace AmbientSoundMixer.Core.Audio;

public enum ChannelKind { Layer, Noise, Binaural, File }

public enum ChannelStatus { Ready, Loading, Missing, Error }

/// <summary>
/// One independent input of the mixer: a source plus its own smoothed gain, mute,
/// enable switch and fade envelope. All setters are safe to call from the UI thread.
/// </summary>
public sealed class MixerChannel
{
    private const float HalfPi = MathF.PI / 2f;

    private readonly int _sampleRate;
    private readonly Smoother _gain;
    private volatile ISampleProvider _source;
    private volatile float _volume;
    private volatile bool _muted;
    private volatile bool _enabled = true;
    private volatile int _status = (int)ChannelStatus.Ready;
    private volatile string? _statusMessage;

    private volatile float _fadeLevel = 1f;
    private volatile float _fadeTarget = 1f;
    private volatile float _fadeStep;
    private volatile bool _removeWhenFaded;
    private volatile bool _faulted;

    public MixerChannel(string id, string name, ChannelKind kind, ISampleProvider source, int sampleRate, float volume = 0.5f)
    {
        Id = id;
        Name = name;
        Kind = kind;
        _source = source;
        _sampleRate = sampleRate;
        _volume = Math.Clamp(volume, 0f, 1f);
        _gain = new Smoother(EngineFormat.VolumeToGain(_volume), sampleRate, 0.02f);
    }

    public string Id { get; }
    public string Name { get; set; }
    public ChannelKind Kind { get; }
    public string? LayerId { get; init; }
    public string? FilePath { get; init; }
    public string Icon { get; init; } = "";

    public ISampleProvider Source => _source;
    public NoiseSource? Noise => _source as NoiseSource;
    public BinauralSource? Binaural => _source as BinauralSource;

    public float Volume
    {
        get => _volume;
        set => _volume = Math.Clamp(value, 0f, 1f);
    }

    public bool Muted { get => _muted; set => _muted = value; }
    public bool Enabled { get => _enabled; set => _enabled = value; }

    public ChannelStatus Status => (ChannelStatus)_status;
    public string? StatusMessage => _statusMessage;
    public bool IsRemoving => _removeWhenFaded;
    public bool IsFinished => _removeWhenFaded && _fadeLevel <= 0f && _fadeTarget <= 0f;

    /// <summary>True when the channel currently contributes to the mix.</summary>
    public bool IsActive => _enabled && !_muted && !_faulted && Status == ChannelStatus.Ready;

    public float CurrentGain => _gain.Current;

    public void SetStatus(ChannelStatus status, string? message = null)
    {
        _statusMessage = message;
        _status = (int)status;
    }

    /// <summary>Replace the source (used when an imported file finishes decoding).</summary>
    public void SetSource(ISampleProvider source) => _source = source;

    public void FadeTo(float target, double seconds)
    {
        target = Math.Clamp(target, 0f, 1f);
        if (seconds <= 0.001)
        {
            _fadeLevel = target;
            _fadeTarget = target;
            _fadeStep = 0f;
            return;
        }
        _fadeStep = (float)(1.0 / (seconds * _sampleRate));
        _fadeTarget = target;
    }

    public void BeginRemoval(double fadeSeconds)
    {
        _removeWhenFaded = true;
        FadeTo(0f, fadeSeconds);
    }

    public void SetFadeLevelImmediate(float level)
    {
        _fadeLevel = level;
    }

    /// <summary>Audio thread only. Adds this channel's contribution into <paramref name="dest"/>.</summary>
    internal void MixInto(float[] dest, float[] scratch, int count)
    {
        if (IsFinished || _faulted) return;

        bool audible = _enabled && !_muted && Status == ChannelStatus.Ready;
        _gain.Target = audible ? EngineFormat.VolumeToGain(_volume) : 0f;
        bool fading = _fadeLevel != _fadeTarget;
        if (!fading && _gain.IsSettled && _gain.Target == 0f) return;
        if (!fading && _fadeLevel <= 0f) return;

        int got;
        try
        {
            got = _source.Read(scratch, 0, count);
            if (got < count) Array.Clear(scratch, got, count - got);
        }
        catch (Exception ex)
        {
            _faulted = true;
            SetStatus(ChannelStatus.Error, ex.Message);
            return;
        }

        float fl = _fadeLevel;
        float ft = _fadeTarget;
        float step = _fadeStep;
        int frames = count / 2;
        for (int i = 0; i < frames; i++)
        {
            float fg;
            if (fl != ft)
            {
                fl = fl < ft ? MathF.Min(fl + step, ft) : MathF.Max(fl - step, ft);
                fg = MathF.Sin(fl * HalfPi);
            }
            else
            {
                fg = fl;
            }
            float g = _gain.Next() * fg;
            dest[2 * i] += scratch[2 * i] * g;
            dest[2 * i + 1] += scratch[2 * i + 1] * g;
        }
        _fadeLevel = fl;
    }
}
