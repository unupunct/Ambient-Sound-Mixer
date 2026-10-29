namespace AmbientSoundMixer.Core.Synthesis;

/// <summary>RBJ-cookbook biquad filter (transposed direct form II).</summary>
public sealed class Biquad
{
    private float _b0 = 1, _b1, _b2, _a1, _a2, _z1, _z2;

    public float Process(float x)
    {
        float y = _b0 * x + _z1;
        _z1 = _b1 * x - _a1 * y + _z2;
        _z2 = _b2 * x - _a2 * y;
        return y;
    }

    public void SetLowPass(int sr, float freq, float q = 0.707f) => Set(sr, freq, q, 0);
    public void SetHighPass(int sr, float freq, float q = 0.707f) => Set(sr, freq, q, 1);
    public void SetBandPass(int sr, float freq, float q = 1f) => Set(sr, freq, q, 2);

    private void Set(int sr, float freq, float q, int type)
    {
        freq = Math.Clamp(freq, 10f, sr * 0.45f);
        float w0 = 2f * MathF.PI * freq / sr;
        float cos = MathF.Cos(w0), sin = MathF.Sin(w0);
        float alpha = sin / (2f * q);
        float a0 = 1f + alpha;
        float b0, b1, b2;
        switch (type)
        {
            case 0: b0 = (1 - cos) / 2; b1 = 1 - cos; b2 = b0; break;
            case 1: b0 = (1 + cos) / 2; b1 = -(1 + cos); b2 = b0; break;
            default: b0 = alpha; b1 = 0; b2 = -alpha; break;
        }
        _b0 = b0 / a0; _b1 = b1 / a0; _b2 = b2 / a0;
        _a1 = -2f * cos / a0; _a2 = (1f - alpha) / a0;
    }
}
