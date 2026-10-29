First release of Ambient Sound Mixer: a local ambient mixer for Windows 10/11 (64-bit). No account, no cloud, no ads.

**Downloads**
- `AmbientSoundMixer-Setup-1.0.0.exe`: installer (Start Menu shortcut, optional desktop shortcut, clean uninstall that asks before deleting your presets and settings)
- `AmbientSoundMixer-1.0.0-win-x64-portable.zip`: unzip and run `AmbientSoundMixer.exe`
- `SHA256SUMS.txt`: verify with `Get-FileHash <file> -Algorithm SHA256`

Both builds are self-contained; no separate .NET install is needed.

**Highlights**
- Five scenarios (Ocean, Forest, Waterfall, Rain, Storm), each a set of layers with its own volume slider
- White, pink and brown noise; binaural beats with Delta to Gamma presets (stereo headphones required)
- Import your own WAV, MP3, FLAC and other files; presets; sleep timer; system tray; dark and light themes

**Known limitations**
- Scenario sounds are synthesized in real time, not field recordings; no third-party audio is bundled.
- The installer is not code-signed, so Windows SmartScreen may warn on first run.
- Hardware-dependent behaviors (device unplug, Bluetooth, tray edge cases) have not been tested on a wide range of machines; see `docs/TESTING.md`.
- Binaural band names are conventional labels only; no health or sleep claims are made.
