using NAudio.Wave;

namespace AmbientSoundMixer.Core.Audio;

public enum NoiseType { White, Pink, Brown }

/// <summary>
/// Continuous generated noise (white, pink via Kellet filter, brown via leaky integrator) with
/// independent left/right streams. Switching type cross-fades over 50 ms to avoid clicks.
/// </summary>
public sealed class NoiseSource : ISampleProvider
{
    private readonly FastRandom _rng;
    private readonly float[] _pink = new float[14];
    private readonly float[] _brown = new float[2];
    private readonly float _fadeStep;
    private volatile int _requested;
    private NoiseType _current;
    private NoiseType _previous;
    private float _mix = 1f;

    public NoiseSource(int sampleRate, NoiseType type = NoiseType.White, int? seed = null)
    {
        WaveFormat = EngineFormat.Create(sampleRate);
        _rng = new FastRandom(seed);
        _fadeStep = 1f / (0.05f * sampleRate);
        _requested = (int)type;
        _current = _previous = type;
    }

    public WaveFormat WaveFormat { get; }

    public NoiseType Type
    {
        get => (NoiseType)_requested;
        set => _requested = (int)value;
    }

    public int Read(float[] buffer, int offset, int count)
    {
        var req = (NoiseType)_requested;
        if (req != _current)
        {
            _previous = _current;
            _current = req;
            _mix = 0f;
        }

        int frames = count / 2;
        for (int i = 0; i < frames; i++)
        {
            for (int ch = 0; ch < 2; ch++)
            {
                float s;
                if (_mix < 1f)
                    s = Generate(_current, ch) * _mix + Generate(_previous, ch) * (1f - _mix);
                else
                    s = Generate(_current, ch);
                buffer[offset + 2 * i + ch] = s;
            }
            if (_mix < 1f) _mix = MathF.Min(1f, _mix + _fadeStep);
        }
        return count;
    }

    private float Generate(NoiseType type, int ch)
    {
        float w = _rng.NextBipolar();
        switch (type)
        {
            case NoiseType.Pink:
            {
                int o = ch * 7;
                var b = _pink;
                b[o] = 0.99886f * b[o] + w * 0.0555179f;
                b[o + 1] = 0.99332f * b[o + 1] + w * 0.0750759f;
                b[o + 2] = 0.96900f * b[o + 2] + w * 0.1538520f;
                b[o + 3] = 0.86650f * b[o + 3] + w * 0.3104856f;
                b[o + 4] = 0.55000f * b[o + 4] + w * 0.5329522f;
                b[o + 5] = -0.7616f * b[o + 5] - w * 0.0168980f;
                float pink = b[o] + b[o + 1] + b[o + 2] + b[o + 3] + b[o + 4] + b[o + 5] + b[o + 6] + w * 0.5362f;
                b[o + 6] = w * 0.115926f;
                return pink * PinkScale;
            }
            case NoiseType.Brown:
            {
                float v = (_brown[ch] + 0.02f * w) / 1.02f;
                _brown[ch] = v;
                return v * BrownScale;
            }
            default:
                return w * WhiteScale;
        }
    }

    // Scales chosen so the three colours have a similar perceived level (see NoiseSourceTests).
    internal const float WhiteScale = 0.35f;
    internal const float PinkScale = 0.114f;
    internal const float BrownScale = 3.4f;
}
