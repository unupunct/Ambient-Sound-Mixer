namespace AmbientSoundMixer.Core.Audio;

/// <summary>
/// One-pole parameter smoother. The target may be written from any thread;
/// <see cref="Next"/> must only be called from the audio thread.
/// </summary>
public sealed class Smoother
{
    private volatile float _target;
    private float _current;
    private readonly float _coeff;

    public Smoother(float initial, int sampleRate, float timeConstantSeconds = 0.02f)
    {
        _current = initial;
        _target = initial;
        _coeff = 1f - MathF.Exp(-1f / (sampleRate * timeConstantSeconds));
    }

    public float Target { get => _target; set => _target = value; }
    public float Current => _current;
    public bool IsSettled => _current == _target;

    public float Next()
    {
        float t = _target;
        float d = t - _current;
        if (MathF.Abs(d) < 1e-6f) _current = t;
        else _current += d * _coeff;
        return _current;
    }

    public void Snap() => _current = _target;
}
