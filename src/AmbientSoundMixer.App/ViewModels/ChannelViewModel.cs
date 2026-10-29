using System.Windows.Input;
using AmbientSoundMixer.App.Infrastructure;
using AmbientSoundMixer.Core.Audio;

namespace AmbientSoundMixer.App.ViewModels;

public sealed class ChannelViewModel : ObservableObject
{
    public static readonly NoiseType[] NoiseTypes = { NoiseType.White, NoiseType.Pink, NoiseType.Brown };
    public static readonly string[] BandNames = BinauralBands.All.Select(b => b.Name).ToArray();

    private readonly MainViewModel _owner;
    private bool _isRenaming;

    public ChannelViewModel(MixerChannel channel, MainViewModel owner)
    {
        Channel = channel;
        _owner = owner;
        RenameCommand = new RelayCommand(() => IsRenaming = !IsRenaming);
        EndRenameCommand = new RelayCommand(() => IsRenaming = false);
        RemoveCommand = new RelayCommand(() => _owner.RemoveChannel(this));
        SetBandCommand = new RelayCommand(p =>
        {
            var band = BinauralBands.All.FirstOrDefault(b => b.Name == p as string);
            if (band != null) BeatFrequency = band.DefaultHz;
        });
    }

    public MixerChannel Channel { get; }
    public ICommand RenameCommand { get; }
    public ICommand EndRenameCommand { get; }
    public ICommand RemoveCommand { get; }
    public ICommand SetBandCommand { get; }

    public string Icon => Channel.Icon;
    public bool IsNoise => Channel.Kind == ChannelKind.Noise;
    public bool IsBinaural => Channel.Kind == ChannelKind.Binaural;
    public bool IsFile => Channel.Kind == ChannelKind.File;
    public string KindText => Channel.Kind switch
    {
        ChannelKind.Layer => "Scenario sound",
        ChannelKind.Noise => "Generated noise",
        ChannelKind.Binaural => "Binaural beat",
        _ => "Imported file",
    };
    public string FilePath => Channel.FilePath ?? "";

    public string Name
    {
        get => Channel.Name;
        set
        {
            string v = (value ?? "").Trim();
            if (v.Length == 0 || v == Channel.Name) return;
            Channel.Name = v.Length > 60 ? v[..60] : v;
            OnPropertyChanged();
            _owner.RequestSave();
        }
    }

    public bool IsRenaming { get => _isRenaming; set => SetProperty(ref _isRenaming, value); }

    public double VolumePercent
    {
        get => Math.Round(Channel.Volume * 100);
        set
        {
            Channel.Volume = (float)(value / 100.0);
            OnPropertyChanged();
            _owner.RequestSave();
        }
    }

    public bool IsMuted
    {
        get => Channel.Muted;
        set { Channel.Muted = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusText)); _owner.RequestSave(); }
    }

    public bool IsEnabled
    {
        get => Channel.Enabled;
        set { Channel.Enabled = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusText)); _owner.RequestSave(); }
    }

    public NoiseType SelectedNoise
    {
        get => Channel.Noise?.Type ?? NoiseType.White;
        set
        {
            if (Channel.Noise == null) return;
            var old = Channel.Noise.Type;
            Channel.Noise.Type = value;
            if (Channel.Name == Core.Session.MixerSession.NoiseName(old)) Channel.Name = Core.Session.MixerSession.NoiseName(value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(Name));
            _owner.RequestSave();
        }
    }

    public double BaseFrequency
    {
        get => Channel.Binaural?.BaseFrequency ?? 0;
        set
        {
            if (Channel.Binaural == null) return;
            Channel.Binaural.BaseFrequency = Math.Round(value);
            OnPropertyChanged();
            _owner.RequestSave();
        }
    }

    public double BeatFrequency
    {
        get => Channel.Binaural?.BeatFrequency ?? 0;
        set
        {
            if (Channel.Binaural == null) return;
            Channel.Binaural.BeatFrequency = Math.Round(value * 2) / 2.0;
            OnPropertyChanged();
            OnPropertyChanged(nameof(BandName));
            _owner.RequestSave();
        }
    }

    public string BandName => BinauralBands.Find(BeatFrequency)?.Name ?? "Custom";
    public double MinBase => BinauralLimits.MinBaseHz;
    public double MaxBase => BinauralLimits.MaxBaseHz;
    public double MinBeat => BinauralLimits.MinBeatHz;
    public double MaxBeat => BinauralLimits.MaxBeatHz;

    public string StatusText => Channel.Status switch
    {
        ChannelStatus.Loading => "Loading…",
        ChannelStatus.Missing => "File missing. Remove it or restore the file and reload.",
        ChannelStatus.Error => Channel.StatusMessage ?? "Error",
        _ => !Channel.Enabled ? "Disabled" : Channel.Muted ? "Muted" : _owner.IsPlaying ? "Playing" : "Ready",
    };

    public bool HasProblem => Channel.Status is ChannelStatus.Missing or ChannelStatus.Error;

    public void Refresh()
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(HasProblem));
    }
}
