using AmbientSoundMixer.Core.Audio;

namespace AmbientSoundMixer.Core.Synthesis;

/// <param name="Level">Output amplitude at the peak of the swell.</param>
/// <param name="HighPassHz">0 disables.</param>
/// <param name="LowPassHz">0 disables. Modulated by the swell when <paramref name="CutoffMod"/> &gt; 0.</param>
/// <param name="SwellDepth">0 = constant level, 1 = fades fully to silence between swells.</param>
/// <param name="CutoffMod">Fraction of the low-pass cutoff that follows the swell (waves get brighter as they crest).</param>
/// <param name="Period1">Primary swell period in seconds (0 disables swell).</param>
/// <param name="Period2">Secondary, incommensurate period for a natural, non-repeating pattern.</param>
public sealed record SwellNoiseParams(
    float Level,
    float HighPassHz,
    float LowPassHz,
    float Q,
    NoiseColor Color,
    float SwellDepth,
    float CutoffMod,
    float Period1,
    float Period2,
    float BandPassHz = 0);

/// <summary>
/// Filtered noise with slow, irregular amplitude/brightness swells. Covers waves, wind, rain beds,
/// waterfall roar and forest air by parameterisation. Left and right use independent noise and swell phases.
/// </summary>
public sealed class SwellNoiseLayer : StereoSynth
{
    private const int UpdateInterval = 32;

    private sealed class Voice
    {
        public ColoredNoise Noise = null!;
        public readonly Biquad Hp = new(), Lp = new(), Bp = new();
        public double Phase1, Phase2;
        public float Swell = 1f;
    }

    private readonly SwellNoiseParams _p;
    private readonly Voice[] _voices = new Voice[2];
    private readonly double _inc1, _inc2;
    private int _counter;

    public SwellNoiseLayer(int sampleRate, SwellNoiseParams p, int? seed = null) : base(sampleRate)
    {
        _p = p;
        var rng = new FastRandom(seed);
        for (int i = 0; i < 2; i++)
        {
            var v = new Voice { Noise = new ColoredNoise(new FastRandom(rng.NextULong() > 0 ? (int)(rng.NextULong() & 0x7fffffff) : 1), p.Color) };
            v.Phase1 = rng.NextFloat() * Math.PI * 2;
            v.Phase2 = rng.NextFloat() * Math.PI * 2;
            _voices[i] = v;
        }
        _inc1 = p.Period1 > 0 ? 2 * Math.PI / (p.Period1 * sampleRate) : 0;
        _inc2 = p.Period2 > 0 ? 2 * Math.PI / (p.Period2 * sampleRate) : 0;
        UpdateFilters(true);
    }

    private void UpdateFilters(bool force)
    {
        foreach (var v in _voices)
        {
            if (_p.HighPassHz > 0) v.Hp.SetHighPass(SampleRate, _p.HighPassHz, 0.707f);
            if (_p.BandPassHz > 0) v.Bp.SetBandPass(SampleRate, _p.BandPassHz, _p.Q);
            if (_p.LowPassHz > 0)
            {
                float cutoff = _p.LowPassHz * (1f - _p.CutoffMod * (1f - v.Swell));
                v.Lp.SetLowPass(SampleRate, MathF.Max(60f, cutoff), _p.Q > 0 && _p.BandPassHz <= 0 ? _p.Q : 0.707f);
            }
        }
    }

    protected override void NextFrame(out float left, out float right)
    {
        if (++_counter >= UpdateInterval)
        {
            _counter = 0;
            foreach (var v in _voices) v.Swell = ComputeSwell(v);
            UpdateFilters(false);
        }

        left = Render(_voices[0]);
        right = Render(_voices[1]);
        foreach (var v in _voices)
        {
            v.Phase1 += _inc1; v.Phase2 += _inc2;
        }
    }

    private float ComputeSwell(Voice v)
    {
        if (_inc1 == 0) return 1f;
        float s = 0.6f * (float)Math.Sin(v.Phase1) + 0.4f * (float)Math.Sin(v.Phase2);
        float e = 0.5f + 0.5f * s;               // 0..1
        e = e * e * (3f - 2f * e);               // smoothstep: longer lulls, clearer crests
        return e;
    }

    private float Render(Voice v)
    {
        float x = v.Noise.Next();
        if (_p.HighPassHz > 0) x = v.Hp.Process(x);
        if (_p.BandPassHz > 0) x = v.Bp.Process(x);
        if (_p.LowPassHz > 0) x = v.Lp.Process(x);
        float amp = (1f - _p.SwellDepth) + _p.SwellDepth * v.Swell;
        return x * amp * _p.Level;
    }
}
