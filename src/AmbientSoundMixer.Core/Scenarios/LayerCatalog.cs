using AmbientSoundMixer.Core.Synthesis;
using NAudio.Wave;

namespace AmbientSoundMixer.Core.Scenarios;

public sealed record LayerDefinition(
    string Id,
    string Name,
    string Icon,
    float DefaultVolume,
    Func<int, ISampleProvider> Create);

/// <summary>All procedurally synthesized ambience layers. Everything here is generated in real time: no recordings.</summary>
public static class LayerCatalog
{
    private static readonly Dictionary<string, LayerDefinition> Layers = Build().ToDictionary(l => l.Id);

    public static IReadOnlyCollection<LayerDefinition> All => Layers.Values;
    public static bool TryGet(string id, out LayerDefinition def) => Layers.TryGetValue(id, out def!);
    public static LayerDefinition Get(string id) => Layers[id];

    private static IEnumerable<LayerDefinition> Build()
    {
        // Ocean
        yield return new("ocean.waves", "Ocean waves", "\U0001F30A", 0.80f, sr => new SwellNoiseLayer(sr,
            new SwellNoiseParams(1.1f, 0, 1400, 0.7f, NoiseColor.Pink, 0.85f, 0.6f, 9.5f, 14.3f)));
        yield return new("ocean.surf", "Surf foam", "\U0001FAE7", 0.35f, sr => new SwellNoiseLayer(sr,
            new SwellNoiseParams(0.45f, 1800, 7000, 0.7f, NoiseColor.White, 0.9f, 0.3f, 9.5f, 11.1f)));
        yield return new("ocean.breeze", "Sea breeze", "\U0001F32C", 0.35f, sr => new SwellNoiseLayer(sr,
            new SwellNoiseParams(1.4f, 0, 0, 0.5f, NoiseColor.Pink, 0.55f, 0, 12f, 19f, BandPassHz: 650)));
        yield return new("ocean.gulls", "Distant seagulls", "\U0001F54A", 0.25f, sr => new ChirpLayer(sr,
            new ChirpParams(0.07f, 900, 1500, 2, 4, 220, 420, 0.55f, 0.85f, 120, 0.22f, 0.6f, 6, 0.02f)));

        // Forest
        yield return new("forest.ambience", "Forest ambience", "\U0001F332", 0.50f, sr => new SwellNoiseLayer(sr,
            new SwellNoiseParams(0.8f, 120, 2200, 0.6f, NoiseColor.Pink, 0.25f, 0.2f, 17f, 23f)));
        yield return new("forest.birds", "Birdsong", "\U0001F426", 0.55f, sr => new ChirpLayer(sr,
            new ChirpParams(0.6f, 2400, 5200, 2, 6, 45, 130, 0.65f, 1.5f, 70, 0.16f, 0.25f)));
        yield return new("forest.wind", "Wind through trees", "\U0001F343", 0.35f, sr => new SwellNoiseLayer(sr,
            new SwellNoiseParams(1.5f, 0, 0, 0.8f, NoiseColor.Pink, 0.75f, 0, 7.5f, 11.3f, BandPassHz: 520)));
        yield return new("forest.wildlife", "Distant wildlife", "\U0001F989", 0.20f, sr => new ChirpLayer(sr,
            new ChirpParams(0.035f, 330, 470, 2, 3, 380, 520, 0.8f, 0.95f, 260, 0.2f, 0.15f, 5, 0.01f)));

        // Waterfall
        yield return new("waterfall.roar", "Waterfall", "\U0001F4A7", 0.80f, sr => new SwellNoiseLayer(sr,
            new SwellNoiseParams(1.0f, 180, 7500, 0.7f, NoiseColor.Pink, 0.18f, 0.1f, 6.5f, 10.7f)));
        yield return new("waterfall.stream", "Flowing stream", "\U0001F3DE", 0.50f, sr => new DropletLayer(sr,
            new DropletParams(55, 500, 2200, 18, 55, 3.5f, 0.1f, 0.35f)));
        yield return new("waterfall.mist", "Stream wash", "\U0001F30A", 0.40f, sr => new SwellNoiseLayer(sr,
            new SwellNoiseParams(0.7f, 400, 4200, 0.7f, NoiseColor.White, 0.3f, 0.2f, 5.2f, 8.1f)));

        // Rain
        yield return new("rain.gentle", "Gentle rainfall", "\U0001F327", 0.80f, sr => new SwellNoiseLayer(sr,
            new SwellNoiseParams(0.75f, 900, 9000, 0.6f, NoiseColor.Pink, 0.15f, 0.1f, 21f, 31f)));
        yield return new("rain.window", "Rain on window", "\U0001FA9F", 0.50f, sr => new DropletLayer(sr,
            new DropletParams(70, 1800, 5200, 6, 22, 0f, 0.5f, 0.4f)));
        yield return new("rain.roof", "Rain on roof", "\U0001F3E0", 0.40f, sr => new DropletLayer(sr,
            new DropletParams(110, 380, 1500, 14, 48, -0.4f, 0.3f, 0.35f)));
        yield return new("rain.thunder", "Distant thunder", "⛈", 0.25f, sr => new ThunderLayer(sr,
            new ThunderParams(55, 0.7f, 110, 5, 8, 0f)));

        // Storm
        yield return new("storm.rain", "Heavy rain", "\U0001F327", 0.80f, sr => new SwellNoiseLayer(sr,
            new SwellNoiseParams(1.1f, 600, 11000, 0.6f, NoiseColor.Pink, 0.2f, 0.1f, 13f, 19f)));
        yield return new("storm.drops", "Rain impacts", "\U0001F4A7", 0.45f, sr => new DropletLayer(sr,
            new DropletParams(240, 700, 4200, 5, 26, -0.1f, 0.5f, 0.35f)));
        yield return new("storm.wind", "Wind gusts", "\U0001F32C", 0.60f, sr => new SwellNoiseLayer(sr,
            new SwellNoiseParams(1.8f, 0, 0, 0.7f, NoiseColor.Pink, 0.9f, 0, 6.5f, 9.7f, BandPassHz: 420)));
        yield return new("storm.thunder", "Distant thunder", "⛈", 0.60f, sr => new ThunderLayer(sr,
            new ThunderParams(28, 0.8f, 130, 5, 9, 0f)));
        yield return new("storm.thunder.strong", "Strong thunder", "⚡", 0.40f, sr => new ThunderLayer(sr,
            new ThunderParams(70, 1.1f, 220, 4, 7, 0.45f)));
    }
}
