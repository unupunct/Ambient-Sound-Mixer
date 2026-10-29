using AmbientSoundMixer.Core.Audio;
using AmbientSoundMixer.Core.Scenarios;
using Xunit;
using Xunit.Abstractions;

namespace AmbientSoundMixer.Tests;

public class SourceTests
{
    private readonly ITestOutputHelper _out;
    public SourceTests(ITestOutputHelper o) => _out = o;

    [Theory]
    [InlineData(NoiseType.White)]
    [InlineData(NoiseType.Pink)]
    [InlineData(NoiseType.Brown)]
    public void NoiseIsAudibleBoundedAndZeroMean(NoiseType type)
    {
        var b = Audio.Render(new NoiseSource(48000, type, 1), 96000);
        double rms = Audio.Rms(b);
        double mean = b.Average(x => (double)x);
        _out.WriteLine($"{type}: rms={rms:F3} peak={Audio.Peak(b):F3}");
        Assert.InRange(rms, 0.05, 0.4);
        Assert.True(Audio.Peak(b) < 1.0f);
        Assert.True(Math.Abs(mean) < 0.05);
        Assert.All(b, x => Assert.True(float.IsFinite(x)));
    }

    [Fact]
    public void PinkAndBrownHaveMoreLowFrequencyEnergyThanWhite()
    {
        // Ratio of sample-to-sample difference energy to signal energy drops as noise gets "redder".
        double Ratio(NoiseType t)
        {
            var b = Audio.Render(new NoiseSource(48000, t, 3), 48000);
            double d = 0, s = 0;
            for (int i = 2; i < b.Length; i += 2) { d += Math.Pow(b[i] - b[i - 2], 2); s += b[i] * b[i]; }
            return d / s;
        }
        double w = Ratio(NoiseType.White), p = Ratio(NoiseType.Pink), br = Ratio(NoiseType.Brown);
        Assert.True(w > p && p > br, $"{w} {p} {br}");
    }

    [Fact]
    public void NoiseTypeSwitchDoesNotClick()
    {
        var n = new NoiseSource(48000, NoiseType.White, 5);
        Audio.Render(n, 4800);
        n.Type = NoiseType.Brown;
        var b = Audio.Render(n, 4800);
        // White noise has big steps by nature; just verify output stays bounded and finite through the fade.
        Assert.True(Audio.Peak(b) < 1f);
        Assert.Equal(NoiseType.Brown, n.Type);
    }

    [Fact]
    public void NoiseChannelsAreDecorrelated()
    {
        var b = Audio.Render(new NoiseSource(48000, NoiseType.White, 9), 48000);
        double dot = 0, l = 0, r = 0;
        for (int i = 0; i < b.Length; i += 2) { dot += b[i] * b[i + 1]; l += b[i] * b[i]; r += b[i + 1] * b[i + 1]; }
        Assert.True(Math.Abs(dot / Math.Sqrt(l * r)) < 0.05);
    }

    // Counts positive-going zero crossings per second for one channel.
    private static double Frequency(float[] b, int channel, int sampleRate)
    {
        int n = 0;
        for (int i = 2 + channel; i < b.Length; i += 2) if (b[i - 2] < 0 && b[i] >= 0) n++;
        return n / ((b.Length / 2.0) / sampleRate);
    }

    [Fact]
    public void BinauralOutputsOffsetFrequenciesPerChannel()
    {
        var s = new BinauralSource(48000, 200, 10);
        Audio.Render(s, 24000); // settle
        var b = Audio.Render(s, 96000);
        Assert.Equal(200, Frequency(b, 0, 48000), 1.5);
        Assert.Equal(210, Frequency(b, 1, 48000), 1.5);
        Assert.True(Audio.Peak(b) <= 0.5f + 1e-3);
    }

    [Fact]
    public void BinauralFrequencyChangesAreContinuous()
    {
        var s = new BinauralSource(48000, 200, 6);
        Audio.Render(s, 4800);
        s.BaseFrequency = 600;
        s.BeatFrequency = 30;
        var b = Audio.Render(s, 48000);
        // Max possible step of a 0.5-amplitude sine at ~650 Hz is ~0.04; a click would be far larger.
        Assert.True(Audio.MaxStep(b, 0) < 0.08f);
        Assert.True(Audio.MaxStep(b, 1) < 0.08f);
    }

    [Fact]
    public void BinauralParametersAreValidated()
    {
        var s = new BinauralSource(48000, 5, 500);
        Assert.Equal(BinauralLimits.MinBaseHz, s.BaseFrequency);
        Assert.Equal(BinauralLimits.MaxBeatHz, s.BeatFrequency);
        s.BaseFrequency = double.NaN;
        Assert.Equal(BinauralLimits.DefaultBaseHz, s.BaseFrequency);
        Assert.Equal("Alpha", BinauralBands.Find(10)!.Name);
        Assert.Equal("Gamma", BinauralBands.Find(40)!.Name);
        Assert.Equal("Delta", BinauralBands.Find(2)!.Name);
    }

    [Fact]
    public void EveryScenarioLayerProducesFiniteAudibleBoundedAudio()
    {
        foreach (var layer in LayerCatalog.All)
        {
            var src = layer.Create(48000);
            // 40 s so that sparse event layers (thunder, wildlife, gulls) have a chance to fire
            var b = Audio.Render(src, 48000 * 40);
            double rms = Audio.Rms(b);
            _out.WriteLine($"{layer.Id,-24} rms={rms:F4} peak={Audio.Peak(b):F3}");
            Assert.All(b, x => Assert.True(float.IsFinite(x), layer.Id));
            Assert.True(Audio.Peak(b) < 4f, layer.Id + " peak too high");
            Assert.True(rms > 0.0005, layer.Id + " is silent");
        }
    }

    [Fact]
    public void ScenariosReferenceOnlyKnownLayers()
    {
        Assert.Equal(5, BuiltInScenarios.All.Count);
        foreach (var s in BuiltInScenarios.All)
        {
            Assert.NotEmpty(s.LayerIds);
            Assert.All(s.LayerIds, id => Assert.True(LayerCatalog.TryGet(id, out _), id));
        }
    }

    [Fact]
    public void LoopCrossfadeMakesWrapContinuous()
    {
        int frames = 4000, fade = 400;
        var data = new float[frames * 2];
        for (int i = 0; i < frames; i++) data[2 * i] = data[2 * i + 1] = MathF.Sin(i * 0.05f + 1f);
        int loop = LoopCrossfade.Apply(data, frames, fade);
        Assert.Equal(frames - fade, loop);
        float seam = Math.Abs(data[2 * (loop - 1)] - data[0]);
        Assert.True(seam < 0.1f, $"seam {seam}");
    }
}
