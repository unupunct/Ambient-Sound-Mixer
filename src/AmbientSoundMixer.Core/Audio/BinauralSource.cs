using NAudio.Wave;

namespace AmbientSoundMixer.Core.Audio;

public static class BinauralLimits
{
    public const double MinBaseHz = 40;
    public const double MaxBaseHz = 1000;
    public const double MinBeatHz = 0.5;
    public const double MaxBeatHz = 40;
    public const double DefaultBaseHz = 200;
    public const double DefaultBeatHz = 6;

    public static double ClampBase(double hz) => double.IsFinite(hz) ? Math.Clamp(hz, MinBaseHz, MaxBaseHz) : DefaultBaseHz;
    public static double ClampBeat(double hz) => double.IsFinite(hz) ? Math.Clamp(hz, MinBeatHz, MaxBeatHz) : DefaultBeatHz;
}

public sealed record BinauralBand(string Name, double MinHz, double MaxHz, double DefaultHz);

public static class BinauralBands
{
    public static readonly IReadOnlyList<BinauralBand> All = new[]
    {
        new BinauralBand("Delta", 1, 4, 2),
        new BinauralBand("Theta", 4, 8, 6),
        new BinauralBand("Alpha", 8, 13, 10),
        new BinauralBand("Beta", 13, 30, 20),
        new BinauralBand("Gamma", 30, 40, 35),
    };

    public static BinauralBand? Find(double beatHz) =>
        All.FirstOrDefault(b => beatHz >= b.MinHz && beatHz < b.MaxHz) ?? (beatHz >= 40 ? All[^1] : null);
}

/// <summary>
/// Two continuous sine tones: left = base frequency, right = base + beat. Frequencies are smoothed
/// and phase is accumulated in double precision, so parameter changes never click.
/// </summary>
public sealed class BinauralSource : ISampleProvider
{
    private const float Amplitude = 0.5f;
    private readonly int _sampleRate;
    private readonly Smoother _baseHz;
    private readonly Smoother _beatHz;
    private double _phaseL, _phaseR;

    public BinauralSource(int sampleRate, double baseHz = BinauralLimits.DefaultBaseHz, double beatHz = BinauralLimits.DefaultBeatHz)
    {
        _sampleRate = sampleRate;
        WaveFormat = EngineFormat.Create(sampleRate);
        _baseHz = new Smoother((float)BinauralLimits.ClampBase(baseHz), sampleRate, 0.05f);
        _beatHz = new Smoother((float)BinauralLimits.ClampBeat(beatHz), sampleRate, 0.05f);
    }

    public WaveFormat WaveFormat { get; }

    public double BaseFrequency
    {
        get => _baseHz.Target;
        set => _baseHz.Target = (float)BinauralLimits.ClampBase(value);
    }

    public double BeatFrequency
    {
        get => _beatHz.Target;
        set => _beatHz.Target = (float)BinauralLimits.ClampBeat(value);
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int frames = count / 2;
        double k = 2.0 * Math.PI / _sampleRate;
        for (int i = 0; i < frames; i++)
        {
            double b = _baseHz.Next();
            double d = _beatHz.Next();
            _phaseL += k * b;
            _phaseR += k * (b + d);
            if (_phaseL > Math.PI * 2) _phaseL -= Math.PI * 2;
            if (_phaseR > Math.PI * 2) _phaseR -= Math.PI * 2;
            buffer[offset + 2 * i] = (float)Math.Sin(_phaseL) * Amplitude;
            buffer[offset + 2 * i + 1] = (float)Math.Sin(_phaseR) * Amplitude;
        }
        return count;
    }
}
