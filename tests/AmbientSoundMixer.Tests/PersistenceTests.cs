using AmbientSoundMixer.Core.Audio;
using AmbientSoundMixer.Core.Models;
using AmbientSoundMixer.Core.Persistence;
using Xunit;

namespace AmbientSoundMixer.Tests;

public sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "asm-" + Guid.NewGuid().ToString("N"));
    public TempDir() => Directory.CreateDirectory(Path);
    public void Dispose() { try { Directory.Delete(Path, true); } catch { } }
}

public class PersistenceTests
{
    [Fact]
    public void SettingsRoundTripAndAreClamped()
    {
        using var d = new TempDir();
        var store = new SettingsStore(new AppPaths(d.Path));
        var s = new AppSettings { Theme = AppTheme.Light, MinimizeToTray = true, DefaultMasterVolume = 7, LatencyMs = 1, SampleRate = 12345 };
        s.Window.Width = 5; s.Window.Left = 100; s.Window.Top = 50;
        store.Save(s);
        var loaded = store.Load(out var warning);
        Assert.Null(warning);
        Assert.Equal(AppTheme.Light, loaded.Theme);
        Assert.True(loaded.MinimizeToTray);
        Assert.Equal(1.0, loaded.DefaultMasterVolume);
        Assert.Equal(30, loaded.LatencyMs);
        Assert.Equal(EngineFormat.DefaultSampleRate, loaded.SampleRate);
        Assert.Equal(640, loaded.Window.Width);
        Assert.Equal(100, loaded.Window.Left);
    }

    [Fact]
    public void CorruptSettingsResetToDefaultsAndAreBackedUp()
    {
        using var d = new TempDir();
        var paths = new AppPaths(d.Path);
        File.WriteAllText(paths.SettingsFile, "{ not json");
        var s = new SettingsStore(paths).Load(out var warning);
        Assert.NotNull(warning);
        Assert.Equal(AppTheme.Dark, s.Theme);
        Assert.True(File.Exists(paths.SettingsFile + ".bad"));
    }

    [Fact]
    public void MissingSettingsFileGivesDefaults()
    {
        using var d = new TempDir();
        var s = new SettingsStore(new AppPaths(d.Path)).Load(out var w);
        Assert.Null(w);
        Assert.Equal(AppTheme.Dark, s.Theme);
    }

    [Fact]
    public void WindowSizeAndLastSessionPersist()
    {
        using var d = new TempDir();
        var store = new SettingsStore(new AppPaths(d.Path));
        var s = new AppSettings();
        s.Window.Width = 900; s.Window.Height = 600; s.Window.Left = 10; s.Window.Top = 20; s.Window.Maximized = true;
        s.LastSession = new Preset { Name = "Last session", ScenarioId = "rain" };
        s.LastSession.Channels.Add(new ChannelState { Kind = ChannelKind.Noise, Name = "N", Volume = 0.3 });
        store.Save(s);
        var l = store.Load(out _);
        Assert.Equal(900, l.Window.Width);
        Assert.Equal(600, l.Window.Height);
        Assert.True(l.Window.Maximized);
        Assert.Equal("rain", l.LastSession!.ScenarioId);
        Assert.Single(l.LastSession.Channels);
    }

    [Fact]
    public void PresetCrudAndDefaults()
    {
        using var d = new TempDir();
        var store = new PresetStore(new AppPaths(d.Path));
        store.EnsureDefaults();
        var all = store.LoadAll(out var warnings);
        Assert.Empty(warnings);
        Assert.Equal(7, all.Count);
        Assert.Contains(all, p => p.Name == "Storm" && p.Channels.Count == 5);

        var mine = new Preset { Name = "Mine", ScenarioId = "rain" };
        mine.Channels.Add(new ChannelState { Kind = ChannelKind.Noise, Name = "N", NoiseType = NoiseType.Pink });
        store.Save(mine);
        var dup = store.Duplicate(mine);
        Assert.Equal("Mine (copy)", dup.Name);
        Assert.NotEqual(mine.Id, dup.Id);
        store.Rename(dup, "Renamed");
        Assert.Contains(store.LoadAll(out _), p => p.Name == "Renamed");

        store.Delete(dup.Id);
        Assert.DoesNotContain(store.LoadAll(out _), p => p.Id == dup.Id);

        // deleted default comes back with Restore, user presets and edits are preserved
        var ocean = store.LoadAll(out _).First(p => p.Name == "Ocean");
        store.Delete(ocean.Id);
        store.RestoreDefaults();
        var after = store.LoadAll(out _);
        Assert.Contains(after, p => p.Name == "Ocean");
        Assert.Contains(after, p => p.Name == "Mine");
    }

    [Fact]
    public void EnsureDefaultsDoesNotResurrectDeletedPresets()
    {
        using var d = new TempDir();
        var store = new PresetStore(new AppPaths(d.Path));
        store.EnsureDefaults();
        store.Delete(store.LoadAll(out _).First().Id);
        store.EnsureDefaults();
        Assert.Equal(6, store.LoadAll(out _).Count);
    }

    [Fact]
    public void InvalidPresetFilesAreSkippedWithWarnings()
    {
        using var d = new TempDir();
        var paths = new AppPaths(d.Path);
        var store = new PresetStore(paths);
        store.Save(new Preset { Name = "Good" });
        File.WriteAllText(Path.Combine(paths.PresetsDirectory, "broken.json"), "{{{");
        File.WriteAllText(Path.Combine(paths.PresetsDirectory, "noname.json"), "{\"name\":\"\"}");
        File.WriteAllText(Path.Combine(paths.PresetsDirectory, "future.json"), "{\"schemaVersion\":99,\"name\":\"F\"}");
        var all = store.LoadAll(out var warnings);
        Assert.Single(all);
        Assert.Equal(3, warnings.Count);
    }

    [Fact]
    public void ValidatorClampsAndDropsBadChannels()
    {
        var p = new Preset { Name = "  x  ", MasterVolume = 5, FadeSeconds = 999 };
        p.Channels.Add(new ChannelState { Kind = ChannelKind.Noise, Volume = -3, NoiseType = (NoiseType)42, Id = "same" });
        p.Channels.Add(new ChannelState { Kind = ChannelKind.Binaural, BaseFrequency = 1, BeatFrequency = double.NaN, Id = "same" });
        p.Channels.Add(new ChannelState { Kind = ChannelKind.File, FilePath = " " });
        p.Channels.Add(new ChannelState { Kind = (ChannelKind)77 });
        var r = PresetValidator.Validate(p);
        Assert.True(r.IsValid);
        Assert.Equal("x", p.Name);
        Assert.Equal(1.0, p.MasterVolume);
        Assert.Equal(15, p.FadeSeconds);
        Assert.Equal(2, p.Channels.Count);
        Assert.Equal(0, p.Channels[0].Volume);
        Assert.Equal(NoiseType.White, p.Channels[0].NoiseType);
        Assert.NotEqual(p.Channels[0].Id, p.Channels[1].Id);
        Assert.Equal(BinauralLimits.MinBaseHz, p.Channels[1].BaseFrequency);
        Assert.Equal(BinauralLimits.DefaultBeatHz, p.Channels[1].BeatFrequency);
        Assert.Equal(2, r.Warnings.Count);
    }

    [Fact]
    public void ValidatorRejectsNullAndNamelessPresets()
    {
        Assert.False(PresetValidator.Validate(null).IsValid);
        Assert.False(PresetValidator.Validate(new Preset { Name = " " }).IsValid);
    }

    [Fact]
    public void ImportedFilePathsPersistInPresets()
    {
        using var d = new TempDir();
        var store = new PresetStore(new AppPaths(d.Path));
        var p = new Preset { Name = "Files" };
        p.Channels.Add(new ChannelState { Kind = ChannelKind.File, Name = "Rain loop", FilePath = @"D:\Sounds\rain.flac" });
        store.Save(p);
        var loaded = store.LoadAll(out _).Single();
        Assert.Equal(@"D:\Sounds\rain.flac", loaded.Channels[0].FilePath);
    }
}
