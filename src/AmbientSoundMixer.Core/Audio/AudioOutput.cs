using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wave;

namespace AmbientSoundMixer.Core.Audio;

public interface IAudioOutput : IDisposable
{
    void Init(ISampleProvider source);
    void Play();
    /// <summary>Raised on a device thread when playback ends; the exception is non-null for failures (e.g. device unplugged).</summary>
    event EventHandler<Exception?>? Stopped;
    /// <summary>Raised when the Windows default device (or the selected device) changed or vanished.</summary>
    event EventHandler? DeviceChanged;
}

public interface IAudioOutputFactory
{
    /// <param name="deviceId">Null follows the Windows default device.</param>
    IAudioOutput Create(string? deviceId, int latencyMs);
}

public sealed record AudioDeviceInfo(string Id, string Name);

public static class AudioDevices
{
    public static IReadOnlyList<AudioDeviceInfo> GetOutputDevices()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            return enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                .Select(d => new AudioDeviceInfo(d.ID, d.FriendlyName)).ToList();
        }
        catch
        {
            return Array.Empty<AudioDeviceInfo>();
        }
    }
}

public sealed class WasapiOutputFactory : IAudioOutputFactory
{
    public IAudioOutput Create(string? deviceId, int latencyMs) => new WasapiAudioOutput(deviceId, latencyMs);
}

/// <summary>WASAPI shared-mode output routed to the chosen device (or the default device when none is chosen).</summary>
public sealed class WasapiAudioOutput : IAudioOutput
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly string? _deviceId;
    private readonly MMDevice _device;
    private readonly WasapiOut _out;
    private readonly NotificationClient _client;
    private bool _disposed;

    public WasapiAudioOutput(string? deviceId, int latencyMs)
    {
        _deviceId = deviceId;
        _device = deviceId == null
            ? _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)
            : _enumerator.GetDevice(deviceId);
        _out = new WasapiOut(_device, AudioClientShareMode.Shared, true, latencyMs);
        _out.PlaybackStopped += (_, e) => Stopped?.Invoke(this, e.Exception);
        _client = new NotificationClient(this);
        _enumerator.RegisterEndpointNotificationCallback(_client);
    }

    public event EventHandler<Exception?>? Stopped;
    public event EventHandler? DeviceChanged;

    public void Init(ISampleProvider source) => _out.Init(source);
    public void Play() => _out.Play();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _enumerator.UnregisterEndpointNotificationCallback(_client); } catch { }
        try { _out.Stop(); } catch { }
        try { _out.Dispose(); } catch { }
        try { _device.Dispose(); } catch { }
        try { _enumerator.Dispose(); } catch { }
    }

    private sealed class NotificationClient : IMMNotificationClient
    {
        private readonly WasapiAudioOutput _owner;
        public NotificationClient(WasapiAudioOutput owner) => _owner = owner;

        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
        {
            if (flow == DataFlow.Render && role == Role.Multimedia && _owner._deviceId == null)
                _owner.DeviceChanged?.Invoke(_owner, EventArgs.Empty);
        }

        public void OnDeviceStateChanged(string deviceId, DeviceState newState)
        {
            if (newState != DeviceState.Active && deviceId == _owner._device.ID)
                _owner.DeviceChanged?.Invoke(_owner, EventArgs.Empty);
        }

        public void OnDeviceAdded(string pwstrDeviceId) { }
        public void OnDeviceRemoved(string deviceId) { }
        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }
    }
}
