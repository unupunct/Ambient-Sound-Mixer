using AmbientSoundMixer.Core.Audio;
using AmbientSoundMixer.Core.Session;
using Xunit;

namespace AmbientSoundMixer.Tests;

/// <summary>
/// Opens the real default Windows output device. Skipped automatically when no device exists (e.g. CI).
/// Plays very quietly for about a second.
/// </summary>
public class AudioDeviceIntegrationTests
{
    [Fact]
    public void RealWasapiOutputPlaysAndReleasesDevice()
    {
        if (AudioDevices.GetOutputDevices().Count == 0) return;

        using var s = new MixerSession(new WasapiOutputFactory(), new SessionOptions { FadeInSeconds = 0.05, FadeOutSeconds = 0.05 });
        s.MasterVolume = 0.05f;
        s.LoadScenario("rain", 0);
        s.AddNoise(NoiseType.Pink, 0.2);
        s.AddBinaural(200, 10, 0.1);
        s.Play();
        Assert.Equal(TransportState.Playing, s.State);
        Assert.True(s.IsOutputOpen);
        Thread.Sleep(1000);
        Assert.True(s.Engine.PeakLevel > 0, "engine produced no signal");
        s.Stop();
        Thread.Sleep(600); // background timer closes the device after the fade
        Assert.False(s.IsOutputOpen);
    }
}
