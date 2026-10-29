# Testing

## Automated (`dotnet test`)

Covers, without audio hardware: multi-source summing, independent channel volume, master volume, mute/enable, click-free
gain ramps, limiter and clipping indicator, transport fades, faulting sources, white/pink/brown spectra and bounds,
binaural left/right frequencies and continuity, parameter validation, every synthesized layer (finite, audible, bounded),
loop crossfade, scenario switching and crossfade, play/pause/stop state machine, sleep timer (with and without fade),
device failure/fallback/recovery (fake output), preset save/load/rename/duplicate/delete/restore, preset validation,
missing-file handling, imported-file decode/mix/loop, corrupt files, settings persistence and clamping, window size persistence.

One integration test opens the real default output device and plays quietly for about a second (skipped if no device).

## Manual checklist (hardware or UI dependent)

Record the result and date when you run these on a release candidate.

- [ ] Sound is audible and continuous through speakers and headphones; no clicks when moving sliders, muting, switching noise type.
- [ ] Binaural beat: different pitch in each ear with headphones.
- [ ] Switch default output device in Windows while playing: playback follows (device set to System default).
- [ ] Unplug a selected USB/Bluetooth device while playing: message appears and playback continues on the default device.
- [ ] Import WAV, MP3, FLAC by picker and by drag and drop; rename; remove; original file still on disk.
- [ ] Preset with an imported file: move the file, load the preset: warning shown, other channels load.
- [ ] Tray: minimize to tray, Show, Play/Pause, Stop, Exit; close-to-tray; restore after hide.
- [ ] Resize, maximize, compact mode, restart: size, position and last session restored.
- [ ] Always on top; theme switch; start with Windows (reboot) launches minimized.
- [ ] Sleep timer fades out and stops; cancel works.
- [ ] Exit from the app and from the tray: process gone from Task Manager, device released.
- [ ] Installer: install, launch, upgrade over existing version keeps presets, uninstall prompts before deleting data.
