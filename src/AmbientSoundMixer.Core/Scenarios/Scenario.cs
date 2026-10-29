namespace AmbientSoundMixer.Core.Scenarios;

public sealed record Scenario(
    string Id,
    string Title,
    string Subtitle,
    string Icon,
    string ColorFrom,
    string ColorTo,
    IReadOnlyList<string> LayerIds);

public static class BuiltInScenarios
{
    public static readonly IReadOnlyList<Scenario> All = new[]
    {
        new Scenario("ocean", "Ocean", "Slow waves, a salt breeze and far-off gulls", "\U0001F30A", "#0F4C75", "#3282B8",
            new[] { "ocean.waves", "ocean.surf", "ocean.breeze", "ocean.gulls" }),
        new Scenario("forest", "Forest", "Birdsong and wind moving through the canopy", "\U0001F332", "#1B5E3A", "#4C9A6A",
            new[] { "forest.ambience", "forest.birds", "forest.wind", "forest.wildlife" }),
        new Scenario("waterfall", "Waterfall", "A steady cascade over a bubbling stream", "\U0001F4A7", "#0B6E78", "#3FB6B2",
            new[] { "waterfall.roar", "waterfall.stream", "waterfall.mist", "forest.ambience" }),
        new Scenario("rain", "Rain", "Soft rain on glass and roof, thunder far away", "\U0001F327", "#3B4A6B", "#7B8FB5",
            new[] { "rain.gentle", "rain.window", "rain.roof", "rain.thunder" }),
        new Scenario("storm", "Storm", "Heavy rain, strong gusts and rolling thunder", "⛈", "#2A2D45", "#6A5ACD",
            new[] { "storm.rain", "storm.drops", "storm.wind", "storm.thunder", "storm.thunder.strong" }),
    };

    public static Scenario? Find(string? id) => All.FirstOrDefault(s => s.Id == id);
}
