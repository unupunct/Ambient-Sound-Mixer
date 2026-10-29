using AmbientSoundMixer.Core.Audio;

namespace AmbientSoundMixer.Core.Synthesis;

/// <param name="RatePerSecond">Mean phrases per second (Poisson).</param>
/// <param name="ChirpsMin">Chirps per phrase.</param>
/// <param name="SweepMin">Ratio of end to start frequency, e.g. 0.6 falls, 1.4 rises.</param>
/// <param name="Harmonic">Amount of 2nd harmonic (0..1) for a brighter, more vocal tone.</param>
public sealed record ChirpParams(
    float RatePerSecond,
    float FreqMin, float FreqMax,
    int ChirpsMin, int ChirpsMax,
    float ChirpMsMin, float ChirpMsMax,
    float SweepMin, float SweepMax,
    float GapMs,
    float Level,
    float Harmonic,
    float VibratoHz = 0,
    float VibratoDepth = 0);

/// <summary>Sparse, randomly timed tonal events: birds, gulls, owls. Up to 24 overlapping voices, random pan.</summary>
public sealed class ChirpLayer : StereoSynth
{
    private sealed class Voice
    {
        public bool Active;
        public int Delay, Pos, Dur;
        public float F0, F1, Amp, PanL, PanR, VibPhase;
        public double Phase;
    }

    private readonly ChirpParams _p;
    private readonly FastRandom _rng;
    private readonly Voice[] _voices = new Voice[24];
    private int _countdown;

    public ChirpLayer(int sampleRate, ChirpParams p, int? seed = null) : base(sampleRate)
    {
        _p = p;
        _rng = new FastRandom(seed);
        for (int i = 0; i < _voices.Length; i++) _voices[i] = new Voice();
        _countdown = (int)(_rng.Range(0.2f, 1f) * SampleRate / Math.Max(0.01f, p.RatePerSecond));
    }

    protected override void NextFrame(out float left, out float right)
    {
        if (--_countdown <= 0)
        {
            SpawnPhrase();
            _countdown = Math.Max(1, (int)(_rng.Exponential(1f / _p.RatePerSecond) * SampleRate));
        }

        float l = 0, r = 0;
        double twoPiOverSr = 2 * Math.PI / SampleRate;
        foreach (var v in _voices)
        {
            if (!v.Active) continue;
            if (v.Delay > 0) { v.Delay--; continue; }
            float t = (float)v.Pos / v.Dur;
            float win = MathF.Sin(MathF.PI * t);
            float env = win * win;
            float f = v.F0 + (v.F1 - v.F0) * t;
            if (_p.VibratoHz > 0)
            {
                v.VibPhase += (float)(twoPiOverSr * _p.VibratoHz);
                f *= 1f + _p.VibratoDepth * MathF.Sin(v.VibPhase);
            }
            v.Phase += twoPiOverSr * f;
            float s = (float)Math.Sin(v.Phase);
            if (_p.Harmonic > 0) s += _p.Harmonic * 0.5f * (float)Math.Sin(v.Phase * 2);
            s *= env * v.Amp;
            l += s * v.PanL;
            r += s * v.PanR;
            if (++v.Pos >= v.Dur) v.Active = false;
        }
        left = l * _p.Level;
        right = r * _p.Level;
    }

    private void SpawnPhrase()
    {
        int n = _p.ChirpsMin + (int)(_rng.NextFloat() * (_p.ChirpsMax - _p.ChirpsMin + 1));
        float baseF = _rng.Range(_p.FreqMin, _p.FreqMax);
        float pan = _rng.Range(0.1f, 0.9f);
        float amp = _rng.Range(0.35f, 1f);
        int delay = 0;
        for (int i = 0; i < n; i++)
        {
            var v = _voices.FirstOrDefault(x => !x.Active);
            if (v == null) return;
            float dur = _rng.Range(_p.ChirpMsMin, _p.ChirpMsMax) * 0.001f * SampleRate;
            float sweep = _rng.Range(_p.SweepMin, _p.SweepMax);
            float f0 = baseF * _rng.Range(0.92f, 1.08f);
            v.Active = true;
            v.Delay = delay;
            v.Pos = 0;
            v.Dur = Math.Max(16, (int)dur);
            v.F0 = f0;
            v.F1 = f0 * sweep;
            v.Amp = amp * _rng.Range(0.7f, 1f);
            v.PanL = MathF.Cos(pan * MathF.PI / 2);
            v.PanR = MathF.Sin(pan * MathF.PI / 2);
            v.Phase = 0;
            v.VibPhase = 0;
            delay += v.Dur + (int)(_p.GapMs * 0.001f * SampleRate * _rng.Range(0.6f, 1.4f));
        }
    }
}

/// <param name="Rate">Mean droplets per second.</param>
/// <param name="Slope">Pitch change per second as a fraction of start pitch (positive = rising, like stream bubbles).</param>
/// <param name="NoiseBurst">0..1 amount of broadband "tick" mixed with the tone.</param>
public sealed record DropletParams(
    float Rate,
    float FreqMin, float FreqMax,
    float DecayMsMin, float DecayMsMax,
    float Slope,
    float NoiseBurst,
    float Level);

/// <summary>Random droplets: rain on glass or roof, stream bubbling. Each droplet is a short decaying resonance.</summary>
public sealed class DropletLayer : StereoSynth
{
    private sealed class Voice
    {
        public bool Active;
        public double Phase;
        public float Freq, Slope, Env, Decay, Amp, PanL, PanR;
    }

    private readonly DropletParams _p;
    private readonly FastRandom _rng;
    private readonly Voice[] _voices = new Voice[96];
    private readonly float _spawnProb;

    public DropletLayer(int sampleRate, DropletParams p, int? seed = null) : base(sampleRate)
    {
        _p = p;
        _rng = new FastRandom(seed);
        for (int i = 0; i < _voices.Length; i++) _voices[i] = new Voice();
        _spawnProb = p.Rate / sampleRate;
    }

    protected override void NextFrame(out float left, out float right)
    {
        if (_rng.NextFloat() < _spawnProb) Spawn();

        float l = 0, r = 0;
        double twoPiOverSr = 2 * Math.PI / SampleRate;
        foreach (var v in _voices)
        {
            if (!v.Active) continue;
            v.Freq += v.Freq * v.Slope / SampleRate;
            v.Phase += twoPiOverSr * v.Freq;
            float s = (float)Math.Sin(v.Phase);
            if (_p.NoiseBurst > 0) s = s * (1f - _p.NoiseBurst * 0.5f) + _rng.NextBipolar() * _p.NoiseBurst * v.Env;
            s *= v.Env * v.Amp;
            l += s * v.PanL;
            r += s * v.PanR;
            v.Env *= v.Decay;
            if (v.Env < 0.002f) v.Active = false;
        }
        left = l * _p.Level;
        right = r * _p.Level;
    }

    private void Spawn()
    {
        foreach (var v in _voices)
        {
            if (v.Active) continue;
            float decayMs = _rng.Range(_p.DecayMsMin, _p.DecayMsMax);
            v.Active = true;
            v.Phase = 0;
            v.Freq = _rng.Range(_p.FreqMin, _p.FreqMax);
            v.Slope = _p.Slope * _rng.Range(0.6f, 1.4f);
            v.Env = 1f;
            v.Decay = MathF.Exp(-1f / (decayMs * 0.001f * SampleRate / 4.6f)); // reach ~-40 dB after decayMs
            v.Amp = _rng.Range(0.15f, 1f);
            float pan = _rng.NextFloat();
            v.PanL = MathF.Cos(pan * MathF.PI / 2);
            v.PanR = MathF.Sin(pan * MathF.PI / 2);
            return;
        }
    }
}

/// <param name="MeanIntervalSeconds">Average gap between rolls.</param>
/// <param name="Crack">0 = distant rumble only, up to 1 = loud initial crack.</param>
public sealed record ThunderParams(
    float MeanIntervalSeconds,
    float Level,
    float LowPassHz,
    float MinSeconds, float MaxSeconds,
    float Crack);

/// <summary>Rolling thunder: low-passed brown noise shaped by a randomised multi-burst envelope.</summary>
public sealed class ThunderLayer : StereoSynth
{
    private const int EnvInterval = 64;

    private readonly ThunderParams _p;
    private readonly FastRandom _rng;
    private readonly ColoredNoise[] _noise = new ColoredNoise[2];
    private readonly Biquad[] _lp = { new(), new() };
    private readonly Biquad[] _crackHp = { new(), new() };
    private readonly ColoredNoise[] _crackNoise = new ColoredNoise[2];
    private readonly float[] _burstT = new float[5];
    private readonly float[] _burstA = new float[5];
    private int _countdown;
    private bool _active;
    private int _pos, _dur;
    private float _env, _envTarget, _envStep, _duration;
    private int _envCounter;

    public ThunderLayer(int sampleRate, ThunderParams p, int? seed = null) : base(sampleRate)
    {
        _p = p;
        _rng = new FastRandom(seed);
        for (int i = 0; i < 2; i++)
        {
            _noise[i] = new ColoredNoise(new FastRandom((int)(_rng.NextULong() & 0x7fffffff)), NoiseColor.Brown);
            _crackNoise[i] = new ColoredNoise(new FastRandom((int)(_rng.NextULong() & 0x7fffffff)), NoiseColor.White);
            _lp[i].SetLowPass(sampleRate, p.LowPassHz, 0.6f);
            _crackHp[i].SetHighPass(sampleRate, 700f, 0.7f);
        }
        _countdown = (int)(_rng.Range(0.1f, 0.5f) * p.MeanIntervalSeconds * sampleRate);
    }

    protected override void NextFrame(out float left, out float right)
    {
        if (!_active)
        {
            if (--_countdown <= 0) Begin();
            left = right = 0;
            return;
        }

        if (++_envCounter >= EnvInterval)
        {
            _envCounter = 0;
            _envTarget = ComputeEnvelope((float)_pos / SampleRate);
            _envStep = (_envTarget - _env) / EnvInterval;
        }
        _env += _envStep;

        float t = (float)_pos / SampleRate;
        float crack = _p.Crack > 0 && t < 0.25f ? MathF.Exp(-t * 18f) * _p.Crack : 0f;

        float l = _lp[0].Process(_noise[0].Next()) * _env * 3f + _crackHp[0].Process(_crackNoise[0].Next()) * crack;
        float r = _lp[1].Process(_noise[1].Next()) * _env * 3f + _crackHp[1].Process(_crackNoise[1].Next()) * crack;
        left = l * _p.Level;
        right = r * _p.Level;

        if (++_pos >= _dur)
        {
            _active = false;
            _countdown = Math.Max(1, (int)(_rng.Exponential(_p.MeanIntervalSeconds) * SampleRate)) + SampleRate * 4;
        }
    }

    private void Begin()
    {
        _duration = _rng.Range(_p.MinSeconds, _p.MaxSeconds);
        _dur = (int)(_duration * SampleRate);
        _pos = 0;
        _env = 0;
        _envTarget = 0;
        _envStep = 0;
        _envCounter = 0;
        for (int i = 0; i < _burstT.Length; i++)
        {
            _burstT[i] = i == 0 ? 0f : _rng.Range(0.3f, _duration * 0.7f);
            _burstA[i] = i == 0 ? 1f : _rng.Range(0.25f, 0.8f);
        }
        _active = true;
    }

    private float ComputeEnvelope(float t)
    {
        float tau = _duration * 0.22f;
        float sum = 0;
        for (int i = 0; i < _burstT.Length; i++)
        {
            float x = t - _burstT[i];
            if (x < 0) continue;
            sum += _burstA[i] * (1f - MathF.Exp(-x / 0.25f)) * MathF.Exp(-x / tau);
        }
        return MathF.Min(sum, 1.2f);
    }
}
