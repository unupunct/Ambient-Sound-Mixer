namespace AmbientSoundMixer.Core.Persistence;

/// <summary>Per-user data locations (never the install directory).</summary>
public sealed class AppPaths
{
    public AppPaths(string? root = null)
    {
        Root = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AmbientSoundMixer");
    }

    public string Root { get; }
    public string SettingsFile => Path.Combine(Root, "settings.json");
    public string PresetsDirectory => Path.Combine(Root, "presets");
}
