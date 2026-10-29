# Contributing

Thanks for helping improve Ambient Sound Mixer.

## Setup

1. Install the .NET 10 SDK and, for installer builds, Inno Setup 6.
2. `dotnet build AmbientSoundMixer.sln`
3. `dotnet test AmbientSoundMixer.sln`

## Layout

- `src/AmbientSoundMixer.Core`: audio engine, sources, synthesis, session, presets, settings (no UI).
- `src/AmbientSoundMixer.App`: WPF UI (MVVM).
- `tests/AmbientSoundMixer.Tests`: unit tests plus one integration test that uses the real output device and is skipped if there is none.

## Guidelines

- Keep the audio callback (`MixerEngine.Read` and every `ISampleProvider.Read`) free of allocations, locks, disk access and exceptions.
- Parameter changes that reach the audio thread must be smoothed (see `Smoother`) to avoid clicks.
- Add a test for any logic that can run without audio hardware.
- Bundled audio must have a verified, redistributable license and a row in `docs/ASSET-MANIFEST.md`.
- Do not add health or sleep claims to the UI or docs.
- By contributing you agree your code is released under the MIT License.

## Pull requests

Describe what changed and why, and note how you tested it. CI must pass.
