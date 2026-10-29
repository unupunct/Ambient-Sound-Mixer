using AmbientSoundMixer.Core.Audio;
using AmbientSoundMixer.Core.Models;
using AmbientSoundMixer.Core.Scenarios;
using AmbientSoundMixer.Core.Session;
using NAudio.Wave;
using Xunit;

namespace AmbientSoundMixer.Tests;

public class SessionTests
{
    private static SessionOptions Fast => new() { FadeInSeconds = 0.05, FadeOutSeconds = 0.05, CrossfadeSeconds = 0.1 };

    private static void Pump(MixerSession s, double seconds)
    {
        Audio.Render(s.Engine, (int)(seconds * s.Options.SampleRate));
        s.Tick();
    }

    [Fact]
    public void PlayOpensOutputAndStopClosesItAfterFade()
    {
        using var s = Audio.NewSession(out var f, out _, Fast);
        s.AddNoise();
        s.Play();
        Assert.Equal(TransportState.Playing, s.State);
        Assert.Single(f.Created);
        Assert.True(f.Created[0].Played);

        s.Stop();
        Assert.Equal(TransportState.Stopped, s.State);
        Assert.False(f.Created[0].Disposed); // still fading
        Pump(s, 0.5);
        Assert.True(f.Created[0].Disposed);
        Assert.False(s.IsOutputOpen);
    }

    [Fact]
    public void PauseThenResumeReopensOutput()
    {
        using var s = Audio.NewSession(out var f, out _, Fast);
        s.AddNoise();
        s.Play();
        s.Pause();
        Assert.Equal(TransportState.Paused, s.State);
        Pump(s, 0.5);
        Assert.False(s.IsOutputOpen);
        s.Resume();
        Assert.Equal(TransportState.Playing, s.State);
        Assert.Equal(2, f.Created.Count);
    }

    [Fact]
    public void PlayDuringFadeOutCancelsTheStop()
    {
        using var s = Audio.NewSession(out var f, out _, new SessionOptions { FadeOutSeconds = 2, FadeInSeconds = 0.05 });
        s.AddNoise();
        s.Play();
        s.Stop();
        s.Play();
        Pump(s, 3);
        Assert.True(s.IsOutputOpen);
        Assert.Single(f.Created);
        Assert.Equal(1f, s.Engine.TransportLevel, 2);
    }

    [Fact]
    public void FailingDeviceReportsErrorAndStaysStopped()
    {
        using var s = Audio.NewSession(out var f, out _, Fast);
        f.FailCreate = true;
        string? msg = null;
        s.MessageRaised += (_, m) => msg = m;
        s.Play();
        Assert.Equal(TransportState.Stopped, s.State);
        Assert.Contains("Could not start audio output", msg);
    }

    [Fact]
    public void SelectedDeviceFallsBackToDefaultWhenUnavailable()
    {
        var factory = new FallbackFactory();
        using var s = new MixerSession(factory, new SessionOptions { OutputDeviceId = "gone" }, autoTick: false);
        string? msg = null;
        s.MessageRaised += (_, m) => msg = m;
        s.Play();
        Assert.Equal(TransportState.Playing, s.State);
        Assert.Contains("unavailable", msg);
        Assert.Null(factory.LastId);
    }

    private sealed class FallbackFactory : IAudioOutputFactory
    {
        public string? LastId = "unset";
        public IAudioOutput Create(string? deviceId, int latencyMs)
        {
            LastId = deviceId;
            if (deviceId != null) throw new InvalidOperationException("missing");
            return new FakeOutput();
        }
    }

    [Fact]
    public void DeviceFailureWhilePlayingRestartsOutput()
    {
        using var s = Audio.NewSession(out var f, out _, Fast);
        s.AddNoise();
        s.Play();
        f.Created[0].Fail(new InvalidOperationException("unplugged"));
        s.Tick();
        Assert.Equal(2, f.Created.Count);
        Assert.True(f.Created[0].Disposed);
        Assert.Equal(TransportState.Playing, s.State);
    }

    [Fact]
    public void DeviceFailureWithNoReplacementStopsPlayback()
    {
        using var s = Audio.NewSession(out var f, out _, Fast);
        s.AddNoise();
        s.Play();
        f.FailCreate = true;
        f.Created[0].ChangeDevice();
        s.Tick();
        Assert.Equal(TransportState.Stopped, s.State);
        Assert.False(s.IsOutputOpen);
    }

    [Fact]
    public void SleepTimerStopsAfterDuration()
    {
        using var s = Audio.NewSession(out var f, out var time, Fast);
        s.AddNoise();
        s.Play();
        s.StartSleepTimer(TimeSpan.FromMinutes(10), fadeOut: false, 0);
        Assert.NotNull(s.SleepTimerRemaining);
        time.Advance(TimeSpan.FromMinutes(9));
        s.Tick();
        Assert.Equal(TransportState.Playing, s.State);
        time.Advance(TimeSpan.FromMinutes(1.1));
        s.Tick();
        Assert.Equal(TransportState.Stopped, s.State);
        Assert.Null(s.SleepTimerRemaining);
    }

    [Fact]
    public void SleepTimerFadesOutGraduallyBeforeExpiry()
    {
        using var s = Audio.NewSession(out _, out var time, Fast);
        s.AddNoise();
        s.Play();
        s.StartSleepTimer(TimeSpan.FromSeconds(60), fadeOut: true, 20);
        time.Advance(TimeSpan.FromSeconds(30));
        s.Tick();
        Assert.Equal(TransportState.Playing, s.State);
        time.Advance(TimeSpan.FromSeconds(11));
        s.Tick();
        Assert.Equal(TransportState.Stopped, s.State);
        // transport is ramping down over the remaining ~19 s, not cut instantly
        Assert.True(s.Engine.TransportLevel > 0.9f);
        Audio.Render(s.Engine, 48000 * 10);
        Assert.InRange(s.Engine.TransportLevel, 0.05f, 0.95f);
    }

    [Fact]
    public void CancelledSleepTimerNeverStops()
    {
        using var s = Audio.NewSession(out _, out var time, Fast);
        s.AddNoise();
        s.Play();
        s.StartSleepTimer(TimeSpan.FromMinutes(1), false, 0);
        s.CancelSleepTimer();
        time.Advance(TimeSpan.FromMinutes(5));
        s.Tick();
        Assert.Equal(TransportState.Playing, s.State);
    }

    [Fact]
    public void ScenarioSwitchReplacesLayersButKeepsGenerators()
    {
        using var s = Audio.NewSession(out _, out _, Fast);
        s.AddNoise(NoiseType.Pink);
        s.LoadScenario("ocean");
        Assert.Equal("ocean", s.CurrentScenarioId);
        Assert.Equal(BuiltInScenarios.Find("ocean")!.LayerIds.Count, s.Channels.Count(c => c.Kind == ChannelKind.Layer));
        s.Play();

        s.LoadScenario("storm");
        // During the crossfade the outgoing layers are still in the engine but hidden from the mixer list.
        Assert.Equal(BuiltInScenarios.Find("storm")!.LayerIds.Count, s.Channels.Count(c => c.Kind == ChannelKind.Layer));
        Assert.True(s.Engine.Channels.Length > s.Channels.Count);
        Assert.Single(s.Channels, c => c.Kind == ChannelKind.Noise);

        Pump(s, 0.5);
        Assert.Equal(s.Channels.Count, s.Engine.Channels.Length); // old layers pruned after the fade
        Assert.Equal(TransportState.Playing, s.State);
    }

    [Fact]
    public void ScenarioSwitchWhileStoppedIsImmediate()
    {
        using var s = Audio.NewSession(out _, out _, Fast);
        s.LoadScenario("rain");
        s.LoadScenario("forest");
        Assert.Equal(s.Channels.Count, s.Engine.Channels.Length);
    }

    [Fact]
    public void AdjustingOneSliderDoesNotInterruptPlayback()
    {
        using var s = Audio.NewSession(out var f, out _, Fast);
        s.LoadScenario("ocean");
        s.Play();
        s.Channels[0].Volume = 0.1f;
        s.Tick();
        Assert.Equal(TransportState.Playing, s.State);
        Assert.Single(f.Created);
        Assert.False(f.Created[0].Disposed);
    }

    [Fact]
    public void CaptureThenApplyRoundTripsTheMix()
    {
        using var s = Audio.NewSession(out _, out _, Fast);
        s.LoadScenario("rain");
        var n = s.AddNoise(NoiseType.Brown, 0.33);
        n.Muted = true;
        var b = s.AddBinaural(250, 12, 0.2);
        s.MasterVolume = 0.42f;
        var state = s.CaptureState("t");

        using var s2 = Audio.NewSession(out _, out _, Fast);
        var warnings = s2.ApplyPreset(state);
        Assert.Empty(warnings);
        Assert.Equal(0.42f, s2.MasterVolume, 3);
        Assert.Equal("rain", s2.CurrentScenarioId);
        Assert.Equal(s.Channels.Count, s2.Channels.Count);
        var noise = s2.Channels.Single(c => c.Kind == ChannelKind.Noise);
        Assert.Equal(NoiseType.Brown, noise.Noise!.Type);
        Assert.True(noise.Muted);
        Assert.Equal(0.33f, noise.Volume, 3);
        var bin = s2.Channels.Single(c => c.Kind == ChannelKind.Binaural).Binaural!;
        Assert.Equal(250, bin.BaseFrequency);
        Assert.Equal(12, bin.BeatFrequency);
    }

    [Fact]
    public void MissingFileLoadsRestOfPresetWithWarning()
    {
        using var s = Audio.NewSession(out _, out _, Fast);
        var p = new Preset { Name = "x" };
        p.Channels.Add(new ChannelState { Kind = ChannelKind.Noise, Name = "N", Volume = 0.4 });
        p.Channels.Add(new ChannelState { Kind = ChannelKind.File, Name = "Gone", FilePath = @"C:\definitely\missing\sound.wav" });
        p.Channels.Add(new ChannelState { Kind = ChannelKind.Layer, Name = "?", LayerId = "no.such.layer" });
        var warnings = s.ApplyPreset(p);
        Assert.Equal(2, warnings.Count);
        Assert.Contains(warnings, w => w.Contains("Missing file"));
        Assert.Equal(2, s.Channels.Count);
        var missing = s.Channels.Single(c => c.Kind == ChannelKind.File);
        Assert.Equal(ChannelStatus.Missing, missing.Status);
        // missing channel survives a re-save so the reference is not lost
        Assert.Contains(s.CaptureState("again").Channels, c => c.FilePath == @"C:\definitely\missing\sound.wav");
    }

    [Fact]
    public async Task ImportedFileIsDecodedMixedAndNeverDeleted()
    {
        string dir = Path.Combine(Path.GetTempPath(), "asm-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, "tone.wav");
        try
        {
            // 0.5 s mono 44.1 kHz sine: exercises mono->stereo, resampling and looping
            using (var w = new WaveFileWriter(file, new WaveFormat(44100, 16, 1)))
                for (int i = 0; i < 22050; i++) w.WriteSample((float)Math.Sin(2 * Math.PI * 440 * i / 44100) * 0.5f);

            using var s = Audio.NewSession(out _, out _, Fast);
            s.MasterVolume = 1f;
            var c = s.AddFile(file, null, 1.0);
            Assert.Equal("tone", c.Name);
            await s.WaitForLoadsAsync();
            Assert.Equal(ChannelStatus.Ready, c.Status);

            Audio.Render(s.Engine, 48000);
            var b = Audio.Render(s.Engine, 48000 * 2); // longer than the clip, so it must loop
            Assert.True(Audio.Rms(b) > 0.1);

            s.RemoveChannel(c.Id);
            Assert.Empty(s.Channels);
            Assert.True(File.Exists(file));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task CorruptAudioFileReportsErrorStatus()
    {
        string file = Path.Combine(Path.GetTempPath(), "asm-bad-" + Guid.NewGuid().ToString("N") + ".wav");
        File.WriteAllText(file, "this is not audio");
        try
        {
            using var s = Audio.NewSession(out _, out _, Fast);
            var c = s.AddFile(file);
            await s.WaitForLoadsAsync();
            Assert.Equal(ChannelStatus.Error, c.Status);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void RenamingAChannelOnlyChangesThatChannel()
    {
        using var s = Audio.NewSession(out _, out _, Fast);
        var a = s.AddNoise(); var b = s.AddNoise();
        a.Name = "Fan";
        Assert.Equal("Fan", a.Name);
        Assert.Equal("White noise", b.Name);
    }

    [Fact]
    public void DisposeReleasesOutput()
    {
        var s = Audio.NewSession(out var f, out _, Fast);
        s.AddNoise();
        s.Play();
        s.Dispose();
        Assert.True(f.Created[0].Disposed);
    }
}
