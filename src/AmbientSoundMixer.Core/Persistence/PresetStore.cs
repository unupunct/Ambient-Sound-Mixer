using System.Text.Json;
using AmbientSoundMixer.Core.Models;

namespace AmbientSoundMixer.Core.Persistence;

/// <summary>One JSON file per preset in the per-user presets folder. Never touches user audio files.</summary>
public sealed class PresetStore
{
    private readonly AppPaths _paths;

    public PresetStore(AppPaths paths) => _paths = paths;

    /// <summary>Seeds the built-in presets the first time the presets folder is created.</summary>
    public void EnsureDefaults()
    {
        if (Directory.Exists(_paths.PresetsDirectory)) return;
        RestoreDefaults();
    }

    public void RestoreDefaults()
    {
        // Only recreates missing defaults; never overwrites a preset the user edited or renamed.
        foreach (var p in DefaultPresets.Create())
            if (!File.Exists(FilePath(p.Id))) Save(p);
    }

    public List<Preset> LoadAll(out List<string> warnings)
    {
        warnings = new List<string>();
        var result = new List<Preset>();
        if (!Directory.Exists(_paths.PresetsDirectory)) return result;
        foreach (var file in Directory.EnumerateFiles(_paths.PresetsDirectory, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var preset = JsonSerializer.Deserialize<Preset>(File.ReadAllText(file), Json.Options);
                var v = PresetValidator.Validate(preset);
                if (!v.IsValid)
                {
                    warnings.Add($"Skipped '{Path.GetFileName(file)}': {string.Join(" ", v.Errors)}");
                    continue;
                }
                warnings.AddRange(v.Warnings.Select(w => $"{preset!.Name}: {w}"));
                result.Add(preset!);
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                warnings.Add($"Skipped '{Path.GetFileName(file)}': {ex.Message}");
            }
        }
        return result.OrderByDescending(p => p.IsDefault).ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public void Save(Preset preset)
    {
        var v = PresetValidator.Validate(preset);
        if (!v.IsValid) throw new InvalidDataException(string.Join(" ", v.Errors));
        Json.WriteAtomic(FilePath(preset.Id), JsonSerializer.Serialize(preset, Json.Options));
    }

    public void Delete(Guid id)
    {
        string path = FilePath(id);
        if (File.Exists(path)) File.Delete(path);
    }

    public Preset Rename(Preset preset, string newName)
    {
        preset.Name = newName;
        preset.IsDefault = false;
        Save(preset);
        return preset;
    }

    public Preset Duplicate(Preset preset)
    {
        var json = JsonSerializer.Serialize(preset, Json.Options);
        var copy = JsonSerializer.Deserialize<Preset>(json, Json.Options)!;
        copy.Id = Guid.NewGuid();
        copy.IsDefault = false;
        copy.Name = $"{preset.Name} (copy)";
        Save(copy);
        return copy;
    }

    private string FilePath(Guid id) => Path.Combine(_paths.PresetsDirectory, id.ToString("N") + ".json");
}
