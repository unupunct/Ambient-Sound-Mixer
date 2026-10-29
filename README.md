# Ambient Sound Mixer

A local, no-account ambient sound mixer for Windows 10/11. Layer ocean, forest, waterfall, rain and storm sounds, add
white/pink/brown noise or a binaural beat, import your own audio files, and set each sound's volume independently.

No subscription, no cloud, no ads, no network access.

## Screenshots

_Screenshots to be added before the first public release (the app window at 1120x760 in dark and light themes)._

## Features

- **Five scenarios**: Ocean, Forest, Waterfall, Rain, Storm. Each has its own sound layers (for example Rain: gentle rainfall, rain on window, rain on roof, distant thunder) with independent sliders and a short, configurable crossfade when you switch.
- **Real mixer**: independent volume, mute and on/off per channel, master volume, smoothed changes (no clicks), soft limiter and a clip warning.
- **Noise generator**: white, pink and brown noise computed in real time.
- **Binaural beats**: two sine tones, one per ear, adjustable base frequency (40 to 1000 Hz) and beat difference (0.5 to 40 Hz) with Delta/Theta/Alpha/Beta/Gamma presets.
- **Your own files**: WAV, MP3, FLAC, AIFF, M4A, AAC, WMA (formats depend on the codecs installed in Windows). Drag and drop or use the picker. Files are looped seamlessly, never modified or deleted.
- **Presets** and **last session** restore, stored in `%APPDATA%\AmbientSoundMixer`.
- **Sleep timer** with optional gradual fade-out.
- **Window**: dark/light theme, compact mode, always on top, system tray (Show, Play/Pause, Stop, Exit), remembered size and position.
- **Audio**: choose the output device (or follow the Windows default), buffer size, and recovery if a device disappears.

> **About the sounds.** All scenario ambience is synthesized by the app in real time; no recordings are bundled. It plays indefinitely without audible looping, but it is not a field recording. See `docs/ASSET-MANIFEST.md`.

> **About binaural beats.** They need stereo headphones to be perceived as intended. Delta, Theta, Alpha, Beta and Gamma are conventional frequency band names only. This app makes no medical claims and promises no effect on sleep, concentration or health. Keep the volume moderate.

## Install

Download from the Releases page:

- `AmbientSoundMixer-Setup-<version>.exe`: installer (Start Menu shortcut, optional desktop shortcut, clean uninstall; asks before deleting your presets/settings).
- `AmbientSoundMixer-<version>-win-x64-portable.zip`: unzip and run `AmbientSoundMixer.exe`.
- `SHA256SUMS.txt`: verify downloads with `Get-FileHash <file> -Algorithm SHA256`.

Both are self-contained; no separate .NET install is required. Windows 10 or 11, 64-bit.

## Usage

1. Pick a scenario card and press **Play** (or **Play** in the transport bar).
2. Adjust any slider; playback is never interrupted. Use the speaker button to mute, the switch to turn a layer off.
3. **+ Noise**, **+ Binaural beat** and **Import audio…** add more channels. Drop files anywhere on the window.
4. **Presets** saves and restores the whole mix. **Settings** has theme, device, fades, sleep timer, tray behavior and About.

## Development

Requires the .NET 10 SDK.

```powershell
dotnet build AmbientSoundMixer.sln
dotnet test AmbientSoundMixer.sln
dotnet run --project src/AmbientSoundMixer.App
```

Architecture: `Core` (engine, sources, synthesis, session, persistence) is UI-free; `App` is WPF with MVVM.
Any `ISampleProvider` (generated or file-based) is mixed through the same `MixerEngine`.
See `CONTRIBUTING.md` and `docs/TESTING.md`.

## Build a release

Needs the .NET 10 SDK and Inno Setup 6 (`winget install JRSoftware.InnoSetup`):

```powershell
powershell -ExecutionPolicy Bypass -File scripts\build.ps1
```

Outputs in `artifacts\`: installer, portable ZIP and `SHA256SUMS.txt`. Pushing a `v*` tag runs the same script in GitHub Actions and attaches the files to a release.

## License

The MIT License (see `LICENSE`) applies to this project's **original code**. Third-party libraries, fonts and any other
assets keep their own licenses; see `THIRD-PARTY-NOTICES.md`.
