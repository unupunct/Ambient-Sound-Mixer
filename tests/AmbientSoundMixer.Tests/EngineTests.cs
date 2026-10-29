using AmbientSoundMixer.Core.Audio;
using Xunit;

namespace AmbientSoundMixer.Tests;

public class EngineTests
{
    private static MixerChannel Chan(float v, float level = 0.5f, string id = "c") =>
        new(id, id, ChannelKind.Noise, new ConstantSource(level), 48000, v);

    private static float SteadyLevel(MixerEngine e)
    {
        Audio.Render(e, 48000); // let smoothers settle
        var b = Audio.Render(e, 4800);
        return b[^2];
    }

    [Fact]
    public void MultipleSourcesAreSummed()
    {
        var e = new MixerEngine { MasterVolume = 1f };
        e.AddChannel(Chan(1f, 0.2f, "a"));
        e.AddChannel(Chan(1f, 0.1f, "b"));
        float expected = (0.2f + 0.1f) * MixerEngine.MixHeadroom;
        Assert.Equal(expected, SteadyLevel(e), 3);
    }

    [Fact]
    public void ChannelVolumesAreIndependent()
    {
        var e = new MixerEngine { MasterVolume = 1f };
        var a = Chan(1f, 0.3f, "a");
        var b = Chan(1f, 0.3f, "b");
        e.AddChannel(a); e.AddChannel(b);
        float both = SteadyLevel(e);
        a.Volume = 0.5f;
        float afterA = SteadyLevel(e);
        Assert.Equal(1f, b.Volume);
        Assert.Equal(0.5f, a.Volume);
        // a contributes 0.25 gain now, b unchanged: 0.3*0.25 + 0.3 = 0.375 (before headroom)
        Assert.Equal((0.3f * 0.25f + 0.3f) * MixerEngine.MixHeadroom, afterA, 3);
        Assert.True(afterA < both);
    }

    [Fact]
    public void MasterVolumeScalesTheWholeMix()
    {
        var e = new MixerEngine { MasterVolume = 1f };
        e.AddChannel(Chan(1f, 0.4f));
        float full = SteadyLevel(e);
        e.MasterVolume = 0.5f;
        float half = SteadyLevel(e);
        Assert.Equal(full * 0.25f, half, 3);
        e.MasterVolume = 0f;
        Assert.Equal(0f, SteadyLevel(e), 4);
    }

    [Fact]
    public void MuteAndDisableSilenceOnlyThatChannel()
    {
        var e = new MixerEngine { MasterVolume = 1f };
        var a = Chan(1f, 0.2f, "a");
        var b = Chan(1f, 0.1f, "b");
        e.AddChannel(a); e.AddChannel(b);
        a.Muted = true;
        Assert.Equal(0.1f * MixerEngine.MixHeadroom, SteadyLevel(e), 3);
        a.Muted = false; b.Enabled = false;
        Assert.Equal(0.2f * MixerEngine.MixHeadroom, SteadyLevel(e), 3);
    }

    [Fact]
    public void VolumeChangesAreSmoothedWithoutClicks()
    {
        var e = new MixerEngine { MasterVolume = 1f };
        var c = new MixerChannel("c", "c", ChannelKind.Noise, new ConstantSource(0.8f), 48000, 1f);
        e.AddChannel(c);
        SteadyLevel(e);
        c.Volume = 0f;
        var b = Audio.Render(e, 4800);
        // The biggest sample-to-sample step must be tiny compared to the 0.64 jump.
        Assert.True(Audio.MaxStep(b, 0) < 0.01f, $"step {Audio.MaxStep(b, 0)}");
        Assert.True(b[^2] < 0.02f);
    }

    [Fact]
    public void OutputNeverExceedsFullScaleEvenWhenOverdriven()
    {
        var e = new MixerEngine { MasterVolume = 1f };
        for (int i = 0; i < 6; i++) e.AddChannel(Chan(1f, 1f, "c" + i));
        var b = Audio.Render(e, 48000);
        Assert.True(Audio.Peak(b) <= 1f);
        Assert.True(e.HasClipped);
        Assert.True(e.IsNearClipping);
    }

    [Fact]
    public void QuietMixDoesNotReportClipping()
    {
        var e = new MixerEngine { MasterVolume = 0.5f };
        e.AddChannel(Chan(0.5f, 0.1f));
        Audio.Render(e, 48000);
        Assert.False(e.IsNearClipping);
        Assert.False(e.HasClipped);
    }

    [Fact]
    public void TransportFadeReachesSilenceAndBack()
    {
        var e = new MixerEngine { MasterVolume = 1f };
        e.AddChannel(Chan(1f, 0.5f));
        SteadyLevel(e);
        e.SetTransport(0f, 0.1);
        Audio.Render(e, 9600);
        Assert.True(e.TransportLevel <= 0.0005f);
        Assert.Equal(0f, Audio.Render(e, 480)[^2], 4);
        e.SetTransport(1f, 0.1);
        Audio.Render(e, 9600);
        Assert.Equal(1f, e.TransportLevel, 3);
    }

    [Fact]
    public void FadingOutChannelFinishesAndIsRemoved()
    {
        var e = new MixerEngine { MasterVolume = 1f };
        var c = Chan(1f, 0.5f);
        e.AddChannel(c);
        c.BeginRemoval(0.05);
        Audio.Render(e, 4800);
        Assert.True(c.IsFinished);
        Assert.Single(e.RemoveFinished());
        Assert.Empty(e.Channels);
    }

    [Fact]
    public void FaultingSourceDoesNotBreakTheMix()
    {
        var e = new MixerEngine { MasterVolume = 1f };
        e.AddChannel(new MixerChannel("bad", "bad", ChannelKind.File, new ThrowingSource(), 48000, 1f));
        e.AddChannel(Chan(1f, 0.2f, "ok"));
        Assert.Equal(0.2f * MixerEngine.MixHeadroom, SteadyLevel(e), 3);
        Assert.Equal(ChannelStatus.Error, e.Channels[0].Status);
    }

    private sealed class ThrowingSource : NAudio.Wave.ISampleProvider
    {
        public NAudio.Wave.WaveFormat WaveFormat => EngineFormat.Create(48000);
        public int Read(float[] buffer, int offset, int count) => throw new InvalidOperationException("boom");
    }
}
