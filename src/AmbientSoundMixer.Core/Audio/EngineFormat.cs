using NAudio.Wave;

namespace AmbientSoundMixer.Core.Audio;

public static class EngineFormat
{
    public const int DefaultSampleRate = 48000;
    public static readonly int[] SupportedSampleRates = { 44100, 48000 };

    public static WaveFormat Create(int sampleRate) => WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 2);

    /// <summary>Slider position (0..1) to linear gain. A squared taper feels more natural than linear.</summary>
    public static float VolumeToGain(float volume)
    {
        volume = Math.Clamp(volume, 0f, 1f);
        return volume * volume;
    }
}
