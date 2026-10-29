using System.Text.Json;
using AmbientSoundMixer.Core.Models;

namespace AmbientSoundMixer.Core.Persistence;

public sealed class SettingsStore
{
    private readonly AppPaths _paths;

    public SettingsStore(AppPaths paths) => _paths = paths;

    /// <summary>Loads settings. A missing file yields defaults; a corrupt file is moved aside (.bad) and defaults are used.</summary>
    public AppSettings Load(out string? warning)
    {
        warning = null;
        string path = _paths.SettingsFile;
        if (!File.Exists(path)) return new AppSettings();
        try
        {
            var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Json.Options)
                    ?? throw new JsonException("Empty settings file.");
            if (s.SchemaVersion > AppSettings.CurrentSchemaVersion)
                warning = "Settings were saved by a newer version; unknown values are ignored.";
            s.Normalize();
            if (s.LastSession != null && !PresetValidator.Validate(s.LastSession).IsValid) s.LastSession = null;
            return s;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            try { File.Move(path, path + ".bad", overwrite: true); } catch { /* best effort */ }
            warning = "Settings could not be read and were reset to defaults.";
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        settings.Normalize();
        Json.WriteAtomic(_paths.SettingsFile, JsonSerializer.Serialize(settings, Json.Options));
    }
}
