namespace AmbientSoundMixer.Core.Audio;

/// <summary>Allocation-free xorshift64* generator; one instance per audio source (not thread-safe by design).</summary>
public sealed class FastRandom
{
    private ulong _s;

    public FastRandom(int? seed = null)
    {
        ulong v = seed.HasValue ? (ulong)seed.Value : (ulong)Random.Shared.NextInt64();
        _s = v * 0x9E3779B97F4A7C15UL + 0xD1B54A32D192ED03UL;
        if (_s == 0) _s = 0x1234567;
    }

    public ulong NextULong()
    {
        _s ^= _s >> 12;
        _s ^= _s << 25;
        _s ^= _s >> 27;
        return _s * 0x2545F4914F6CDD1DUL;
    }

    /// <summary>[0,1)</summary>
    public float NextFloat() => (NextULong() >> 40) * (1f / (1UL << 24));

    /// <summary>[-1,1)</summary>
    public float NextBipolar() => NextFloat() * 2f - 1f;

    public float Range(float min, float max) => min + (max - min) * NextFloat();

    /// <summary>Exponentially distributed interval with the given mean.</summary>
    public float Exponential(float mean) => -MathF.Log(1f - NextFloat() + 1e-9f) * mean;
}
