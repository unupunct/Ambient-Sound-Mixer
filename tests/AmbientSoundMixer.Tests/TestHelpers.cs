using AmbientSoundMixer.Core.Audio;
using AmbientSoundMixer.Core.Session;
using NAudio.Wave;

namespace AmbientSoundMixer.Tests;

internal sealed class FakeOutput : IAudioOutput
{
    public bool Disposed, Played;
    public ISampleProvider? Source;
    public event EventHandler<Exception?>? Stopped;
    public event EventHandler? DeviceChanged;
    public void Init(ISampleProvider source) => Source = source;
    public void Play() => Played = true;
    public void Dispose() => Disposed = true;
    public void Fail(Exception ex) => Stopped?.Invoke(this, ex);
    public void ChangeDevice() => DeviceChanged?.Invoke(this, EventArgs.Empty);
}

internal sealed class FakeFactory : IAudioOutputFactory
{
    public List<FakeOutput> Created { get; } = new();
    public bool FailCreate;
    public string? LastDeviceId;
    public IAudioOutput Create(string? deviceId, int latencyMs)
    {
        LastDeviceId = deviceId;
        if (FailCreate) throw new InvalidOperationException("no device");
        var o = new FakeOutput();
        Created.Add(o);
        return o;
    }
}

internal sealed class ManualTime : TimeProvider
{
    private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan t) => _now += t;
}

internal sealed class ConstantSource : ISampleProvider
{
    private readonly float _l, _r;
    public ConstantSource(float l, float r = float.NaN) { _l = l; _r = float.IsNaN(r) ? l : r; WaveFormat = EngineFormat.Create(48000); }
    public WaveFormat WaveFormat { get; }
    public int Read(float[] buffer, int offset, int count)
    {
        for (int i = 0; i < count / 2; i++) { buffer[offset + 2 * i] = _l; buffer[offset + 2 * i + 1] = _r; }
        return count;
    }
}

internal static class Audio
{
    public static float[] Render(ISampleProvider p, int frames)
    {
        var buf = new float[frames * 2];
        int done = 0;
        while (done < buf.Length)
        {
            int n = Math.Min(4096, buf.Length - done);
            var tmp = new float[n];
            p.Read(tmp, 0, n);
            Array.Copy(tmp, 0, buf, done, n);
            done += n;
        }
        return buf;
    }

    public static double Rms(float[] b, int start = 0, int length = -1)
    {
        if (length < 0) length = b.Length - start;
        double sum = 0;
        for (int i = start; i < start + length; i++) sum += b[i] * b[i];
        return Math.Sqrt(sum / length);
    }

    public static float Peak(float[] b) => b.Max(Math.Abs);

    /// <summary>Largest absolute difference between consecutive samples of one channel.</summary>
    public static float MaxStep(float[] b, int channel)
    {
        float m = 0;
        for (int i = 2 + channel; i < b.Length; i += 2) m = Math.Max(m, Math.Abs(b[i] - b[i - 2]));
        return m;
    }

    public static MixerSession NewSession(out FakeFactory factory, out ManualTime time, SessionOptions? opts = null)
    {
        factory = new FakeFactory();
        time = new ManualTime();
        return new MixerSession(factory, opts ?? new SessionOptions(), time, autoTick: false);
    }
}
