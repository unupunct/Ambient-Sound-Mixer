using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using AmbientSoundMixer.App.Infrastructure;
using AmbientSoundMixer.Core.Scenarios;

namespace AmbientSoundMixer.App.ViewModels;

public sealed class ScenarioViewModel : ObservableObject
{
    private readonly MainViewModel _owner;
    private bool _isActive;
    private bool _isPlaying;

    public ScenarioViewModel(Scenario scenario, MainViewModel owner)
    {
        Scenario = scenario;
        _owner = owner;
        Gradient = new LinearGradientBrush(
            (Color)ColorConverter.ConvertFromString(scenario.ColorFrom),
            (Color)ColorConverter.ConvertFromString(scenario.ColorTo),
            new Point(0, 0), new Point(1, 1));
        Gradient.Freeze();
        SoundsText = string.Join(" · ", scenario.LayerIds.Select(id => LayerCatalog.Get(id).Name));
        PlayCommand = new RelayCommand(() => _owner.PlayScenario(Scenario));
        StopCommand = new RelayCommand(() => _owner.StopPlayback());
        SelectCommand = new RelayCommand(() => _owner.SelectScenario(Scenario));
    }

    public Scenario Scenario { get; }
    public string Id => Scenario.Id;
    public string Title => Scenario.Title;
    public string Subtitle => Scenario.Subtitle;
    public string Icon => Scenario.Icon;
    public Brush Gradient { get; }
    public string SoundsText { get; }
    public ICommand PlayCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand SelectCommand { get; }

    public bool IsActive { get => _isActive; set => SetProperty(ref _isActive, value); }
    public bool IsPlaying { get => _isPlaying; set => SetProperty(ref _isPlaying, value); }
}
