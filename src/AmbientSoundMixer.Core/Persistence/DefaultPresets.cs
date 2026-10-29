using System.Security.Cryptography;
using System.Text;
using AmbientSoundMixer.Core.Audio;
using AmbientSoundMixer.Core.Models;
using AmbientSoundMixer.Core.Scenarios;

namespace AmbientSoundMixer.Core.Persistence;

public static class DefaultPresets
{
    public static IReadOnlyList<Preset> Create()
    {
        var list = new List<Preset>();
        foreach (var s in BuiltInScenarios.All)
            list.Add(FromScenario(s));

        list.Add(new Preset
        {
            Id = StableId("default.deepsleep"), Name = "Deep sleep", IsDefault = true, MasterVolume = 0.6, FadeSeconds = 3,
            Channels =
            {
                new ChannelState { Id = "n1", Kind = ChannelKind.Noise, Name = "Brown noise", NoiseType = NoiseType.Brown, Volume = 0.6 },
                new ChannelState { Id = "b1", Kind = ChannelKind.Binaural, Name = "Binaural beat (Delta)", BaseFrequency = 200, BeatFrequency = 2, Volume = 0.25 },
            },
        });
        list.Add(new Preset
        {
            Id = StableId("default.focus"), Name = "Focus", IsDefault = true, MasterVolume = 0.7, FadeSeconds = 2,
            Channels =
            {
                new ChannelState { Id = "n1", Kind = ChannelKind.Noise, Name = "Pink noise", NoiseType = NoiseType.Pink, Volume = 0.5 },
                new ChannelState { Id = "b1", Kind = ChannelKind.Binaural, Name = "Binaural beat (Beta)", BaseFrequency = 220, BeatFrequency = 18, Volume = 0.2 },
            },
        });
        return list;
    }

    private static Preset FromScenario(Scenario s)
    {
        var p = new Preset { Id = StableId("default." + s.Id), Name = s.Title, IsDefault = true, ScenarioId = s.Id, MasterVolume = 0.8 };
        foreach (var layerId in s.LayerIds)
        {
            var def = LayerCatalog.Get(layerId);
            p.Channels.Add(new ChannelState
            {
                Id = layerId, Kind = ChannelKind.Layer, LayerId = layerId, Name = def.Name, Volume = def.DefaultVolume,
            });
        }
        return p;
    }

    private static Guid StableId(string key) => new(MD5.HashData(Encoding.UTF8.GetBytes(key)));
}
