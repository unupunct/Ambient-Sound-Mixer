using AmbientSoundMixer.Core.Audio;

namespace AmbientSoundMixer.Core.Models;

public sealed class ChannelState
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public ChannelKind Kind { get; set; }
    public string Name { get; set; } = "";
    public double Volume { get; set; } = 0.5;
    public bool Muted { get; set; }
    public bool Enabled { get; set; } = true;
    public string? LayerId { get; set; }
    public NoiseType NoiseType { get; set; } = NoiseType.White;
    public double BaseFrequency { get; set; } = BinauralLimits.DefaultBaseHz;
    public double BeatFrequency { get; set; } = BinauralLimits.DefaultBeatHz;
    public string? FilePath { get; set; }
}

public sealed class Preset
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New preset";
    public bool IsDefault { get; set; }
    public string? ScenarioId { get; set; }
    public double MasterVolume { get; set; } = 0.8;
    /// <summary>Optional crossfade used when this preset is loaded; null uses the app default.</summary>
    public double? FadeSeconds { get; set; }
    public List<ChannelState> Channels { get; set; } = new();
}
