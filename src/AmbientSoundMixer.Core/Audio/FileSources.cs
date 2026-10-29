using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace AmbientSoundMixer.Core.Audio;

/// <summary>Produces silence; stands in for a channel whose file is loading, missing or broken.</summary>
public sealed class SilentSource : ISampleProvider
{
    public SilentSource(int sampleRate) => WaveFormat = EngineFormat.Create(sampleRate);
    public WaveFormat WaveFormat { get; }

    public int Read(float[] buffer, int offset, int count)
    {
        Array.Clear(buffer, offset, count);
        return count;
    }
}

/// <summary>Plays a fully decoded stereo buffer from memory in a loop. No disk access on the audio thread.</summary>
public sealed class LoopingBufferSource : ISampleProvider
{
    private readonly float[] _samples;
    private readonly int _length;
    private int _pos;

    public LoopingBufferSource(float[] stereoSamples, int frames, int sampleRate)
    {
        _samples = stereoSamples;
        _length = Math.Max(1, frames) * 2;
        WaveFormat = EngineFormat.Create(sampleRate);
    }

    public WaveFormat WaveFormat { get; }
    public int Frames => _length / 2;

    public int Read(float[] buffer, int offset, int count)
    {
        int written = 0;
        while (written < count)
        {
            int n = Math.Min(count - written, _length - _pos);
            Array.Copy(_samples, _pos, buffer, offset + written, n);
            _pos += n;
            written += n;
            if (_pos >= _length) _pos = 0;
        }
        return count;
    }
}

public sealed record DecodedAudio(float[] Samples, int Frames, bool Truncated);

public static class AudioFileDecoder
{
    public static readonly string[] SupportedExtensions = { ".wav", ".mp3", ".flac", ".aiff", ".aif", ".m4a", ".aac", ".wma" };
    public static readonly TimeSpan MaxDuration = TimeSpan.FromMinutes(10);

    public static bool IsSupported(string path) =>
        SupportedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>Decodes and resamples the file to stereo float at <paramref name="sampleRate"/>, with a seamless loop crossfade.</summary>
    public static DecodedAudio Decode(string path, int sampleRate, TimeSpan? maxDuration = null, CancellationToken ct = default)
    {
        using var reader = OpenReader(path);
        ISampleProvider sp = reader.ToSampleProvider();
        if (sp.WaveFormat.Channels == 1) sp = new MonoToStereoSampleProvider(sp);
        else if (sp.WaveFormat.Channels != 2) throw new NotSupportedException($"{sp.WaveFormat.Channels}-channel audio is not supported.");
        if (sp.WaveFormat.SampleRate != sampleRate) sp = new WdlResamplingSampleProvider(sp, sampleRate);

        long maxFrames = (long)((maxDuration ?? MaxDuration).TotalSeconds * sampleRate);
        var data = new List<float[]>();
        var chunk = new float[sampleRate * 2];
        long frames = 0;
        bool truncated = false;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            int n = sp.Read(chunk, 0, chunk.Length);
            if (n <= 0) break;
            n -= n % 2;
            if (frames + n / 2 > maxFrames)
            {
                n = (int)((maxFrames - frames) * 2);
                truncated = true;
            }
            var copy = new float[n];
            Array.Copy(chunk, copy, n);
            data.Add(copy);
            frames += n / 2;
            if (truncated) break;
        }
        if (frames == 0) throw new InvalidDataException("The file contains no audio.");

        var all = new float[frames * 2];
        int pos = 0;
        foreach (var d in data) { Array.Copy(d, 0, all, pos, d.Length); pos += d.Length; }

        int fade = (int)Math.Min(sampleRate, frames / 4);
        int loopFrames = LoopCrossfade.Apply(all, (int)frames, fade);
        return new DecodedAudio(all, loopFrames, truncated);
    }

    private static WaveStream OpenReader(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is ".wav" or ".mp3" or ".aiff" or ".aif")
        {
            try { return new AudioFileReader(path); }
            catch when (ext == ".mp3") { /* fall back to Media Foundation */ }
        }
        return new MediaFoundationReader(path);
    }
}

public static class LoopCrossfade
{
    /// <summary>
    /// Blends the tail of the buffer into its head so that wrapping from end to start is continuous.
    /// Returns the new loop length in frames (frames - fadeFrames), or the original length when the clip is too short.
    /// </summary>
    public static int Apply(float[] stereo, int frames, int fadeFrames)
    {
        if (fadeFrames <= 0 || frames < fadeFrames * 4) return frames;
        int loop = frames - fadeFrames;
        for (int i = 0; i < fadeFrames; i++)
        {
            float w = (float)i / fadeFrames; // weight of the head
            float head = MathF.Sin(w * MathF.PI / 2f);
            float tail = MathF.Cos(w * MathF.PI / 2f);
            for (int c = 0; c < 2; c++)
            {
                float h = stereo[2 * i + c];
                float t = stereo[2 * (loop + i) + c];
                stereo[2 * i + c] = h * head + t * tail;
            }
        }
        return loop;
    }
}
