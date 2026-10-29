using AmbientSoundMixer.Core.Audio;
using NAudio.Wave;

namespace AmbientSoundMixer.Core.Synthesis;

/// <summary>Base for procedural ambience layers: renders one stereo frame at a time and never blocks.</summary>
public abstract class StereoSynth : ISampleProvider
{
    protected StereoSynth(int sampleRate)
    {
        SampleRate = sampleRate;
        WaveFormat = EngineFormat.Create(sampleRate);
    }

    public int SampleRate { get; }
    public WaveFormat WaveFormat { get; }

    protected abstract void NextFrame(out float left, out float right);

    public int Read(float[] buffer, int offset, int count)
    {
        int frames = count / 2;
        for (int i = 0; i < frames; i++)
        {
            NextFrame(out float l, out float r);
            buffer[offset + 2 * i] = l;
            buffer[offset + 2 * i + 1] = r;
        }
        return count;
    }
}

public enum NoiseColor { White, Pink, Brown }

/// <summary>Per-voice noise generator with optional spectral tilt, used inside synth layers.</summary>
internal sealed class ColoredNoise
{
    private readonly FastRandom _rng;
    private readonly NoiseColor _color;
    private float _b0, _b1, _b2, _brown;

    public ColoredNoise(FastRandom rng, NoiseColor color) { _rng = rng; _color = color; }

    public float Next()
    {
        float w = _rng.NextBipolar();
        switch (_color)
        {
            case NoiseColor.Pink:
                _b0 = 0.99765f * _b0 + w * 0.0990460f;
                _b1 = 0.96300f * _b1 + w * 0.2965164f;
                _b2 = 0.57000f * _b2 + w * 1.0526913f;
                return (_b0 + _b1 + _b2 + w * 0.1848f) * 0.25f;
            case NoiseColor.Brown:
                _brown = (_brown + 0.02f * w) / 1.02f;
                return _brown * 3.2f;
            default:
                return w * 0.6f;
        }
    }
}
