using NAudio.Wave;

namespace AmbientSoundMixer.Core.Audio;

/// <summary>
/// The real-time mixing pipeline: sums any number of stereo float sources, applies master gain,
/// a transport fade (play/pause/stop) and a soft limiter. <see cref="Read"/> never blocks,
/// never allocates after warm-up and always returns the full request (silence when idle).
/// </summary>
public sealed class MixerEngine : ISampleProvider
{
    /// <summary>Fixed headroom so several full-scale layers do not clip immediately.</summary>
    public const float MixHeadroom = 0.8f;
    private const float LimiterThreshold = 0.85f;
    public const float NearClipLevel = 0.9f;

    private readonly object _lock = new();
    private volatile MixerChannel[] _channels = Array.Empty<MixerChannel>();
    private readonly Smoother _master;
    private volatile float _masterVolume = 0.8f;
    private float _transport = 1f;
    private volatile float _transportTarget = 1f;
    private volatile float _transportStep;
    private float[] _mix = new float[0];
    private float[] _scratch = new float[0];
    private volatile float _peak;
    private volatile bool _clipped;

    public MixerEngine(int sampleRate = EngineFormat.DefaultSampleRate)
    {
        SampleRate = sampleRate;
        WaveFormat = EngineFormat.Create(sampleRate);
        _master = new Smoother(EngineFormat.VolumeToGain(_masterVolume), sampleRate, 0.03f);
    }

    public int SampleRate { get; }
    public WaveFormat WaveFormat { get; }

    public float MasterVolume
    {
        get => _masterVolume;
        set => _masterVolume = Math.Clamp(value, 0f, 1f);
    }

    public float TransportLevel => _transport;

    /// <summary>Decaying pre-limiter peak of the last blocks (1.0 = full scale).</summary>
    public float PeakLevel => _peak;
    public bool IsNearClipping => _peak >= NearClipLevel;

    /// <summary>True if the pre-limiter signal exceeded full scale since the last <see cref="ResetClipIndicator"/>.</summary>
    public bool HasClipped => _clipped;
    public void ResetClipIndicator() => _clipped = false;

    public MixerChannel[] Channels => _channels;

    public void AddChannel(MixerChannel channel)
    {
        lock (_lock) _channels = _channels.Append(channel).ToArray();
    }

    public bool RemoveChannel(MixerChannel channel)
    {
        lock (_lock)
        {
            var list = _channels.ToList();
            bool removed = list.Remove(channel);
            if (removed) _channels = list.ToArray();
            return removed;
        }
    }

    /// <summary>Removes channels whose fade-out completed. Returns them so the caller can dispose their sources.</summary>
    public List<MixerChannel> RemoveFinished()
    {
        lock (_lock)
        {
            var finished = _channels.Where(c => c.IsFinished).ToList();
            if (finished.Count > 0) _channels = _channels.Where(c => !c.IsFinished).ToArray();
            return finished;
        }
    }

    public void SetTransport(float target, double seconds)
    {
        target = Math.Clamp(target, 0f, 1f);
        if (seconds <= 0.001)
        {
            _transport = target;
            _transportTarget = target;
            _transportStep = 0f;
            return;
        }
        _transportStep = (float)(1.0 / (seconds * SampleRate));
        _transportTarget = target;
    }

    public int Read(float[] buffer, int offset, int count)
    {
        if (_mix.Length < count)
        {
            _mix = new float[count];
            _scratch = new float[count];
        }
        Array.Clear(_mix, 0, count);

        foreach (var channel in _channels)
            channel.MixInto(_mix, _scratch, count);

        _master.Target = EngineFormat.VolumeToGain(_masterVolume);
        float t = _transport;
        float tt = _transportTarget;
        float ts = _transportStep;
        float blockPeak = 0f;
        int frames = count / 2;

        for (int i = 0; i < frames; i++)
        {
            if (t != tt) t = t < tt ? MathF.Min(t + ts, tt) : MathF.Max(t - ts, tt);
            float g = _master.Next() * t * t * MixHeadroom;
            float l = _mix[2 * i] * g;
            float r = _mix[2 * i + 1] * g;
            float al = MathF.Abs(l), ar = MathF.Abs(r);
            if (al > blockPeak) blockPeak = al;
            if (ar > blockPeak) blockPeak = ar;
            buffer[offset + 2 * i] = SoftLimit(l);
            buffer[offset + 2 * i + 1] = SoftLimit(r);
        }
        _transport = t;
        _peak = MathF.Max(blockPeak, _peak * 0.9f);
        if (blockPeak > 1f) _clipped = true;
        return count;
    }

    public static float SoftLimit(float x)
    {
        float a = MathF.Abs(x);
        if (a <= LimiterThreshold) return x;
        float over = (a - LimiterThreshold) / (1f - LimiterThreshold);
        float y = LimiterThreshold + (1f - LimiterThreshold) * MathF.Tanh(over);
        return MathF.CopySign(y, x);
    }
}
