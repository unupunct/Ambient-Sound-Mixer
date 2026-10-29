using AmbientSoundMixer.Core.Audio;
using AmbientSoundMixer.Core.Models;

namespace AmbientSoundMixer.Core.Persistence;

public sealed class ValidationResult
{
    public List<string> Errors { get; } = new();
    public List<string> Warnings { get; } = new();
    public bool IsValid => Errors.Count == 0;
}

public static class PresetValidator
{
    public const int MaxChannels = 32;
    public const int MaxNameLength = 80;

    /// <summary>Checks a preset and sanitizes recoverable problems in place (clamping, dropping bad channels).</summary>
    public static ValidationResult Validate(Preset? preset)
    {
        var r = new ValidationResult();
        if (preset == null) { r.Errors.Add("Preset is empty."); return r; }
        if (preset.SchemaVersion > Preset.CurrentSchemaVersion)
        {
            r.Errors.Add($"Preset was created by a newer version (schema {preset.SchemaVersion}).");
            return r;
        }
        if (string.IsNullOrWhiteSpace(preset.Name))
        {
            r.Errors.Add("Preset has no name.");
            return r;
        }
        preset.Name = preset.Name.Trim();
        if (preset.Name.Length > MaxNameLength) preset.Name = preset.Name[..MaxNameLength];
        if (preset.Id == Guid.Empty) preset.Id = Guid.NewGuid();
        preset.Channels ??= new List<ChannelState>();

        preset.MasterVolume = ClampUnit(preset.MasterVolume, 0.8);
        if (preset.FadeSeconds is { } f) preset.FadeSeconds = double.IsFinite(f) ? Math.Clamp(f, 0, 15) : null;

        if (preset.Channels.Count > MaxChannels)
        {
            r.Warnings.Add($"Only the first {MaxChannels} channels were kept.");
            preset.Channels = preset.Channels.Take(MaxChannels).ToList();
        }

        var seen = new HashSet<string>();
        var kept = new List<ChannelState>();
        foreach (var c in preset.Channels)
        {
            if (c == null) continue;
            if (!Enum.IsDefined(c.Kind)) { r.Warnings.Add($"Channel '{c.Name}' has an unknown type and was skipped."); continue; }
            if (c.Kind == ChannelKind.File && string.IsNullOrWhiteSpace(c.FilePath)) { r.Warnings.Add($"Channel '{c.Name}' has no file path and was skipped."); continue; }
            if (c.Kind == ChannelKind.Layer && string.IsNullOrWhiteSpace(c.LayerId)) { r.Warnings.Add($"Channel '{c.Name}' has no sound layer and was skipped."); continue; }
            if (string.IsNullOrWhiteSpace(c.Id) || !seen.Add(c.Id)) c.Id = Guid.NewGuid().ToString("N");
            c.Name = string.IsNullOrWhiteSpace(c.Name) ? c.Kind.ToString() : c.Name.Trim();
            c.Volume = ClampUnit(c.Volume, 0.5);
            if (!Enum.IsDefined(c.NoiseType)) c.NoiseType = NoiseType.White;
            c.BaseFrequency = BinauralLimits.ClampBase(c.BaseFrequency);
            c.BeatFrequency = BinauralLimits.ClampBeat(c.BeatFrequency);
            kept.Add(c);
        }
        preset.Channels = kept;
        return r;
    }

    private static double ClampUnit(double v, double fallback) => double.IsFinite(v) ? Math.Clamp(v, 0, 1) : fallback;
}
