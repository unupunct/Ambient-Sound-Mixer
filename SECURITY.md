# Security policy

## Reporting a vulnerability

Please do **not** open a public issue for security problems.
Use GitHub's private reporting: the repository's **Security** tab → **Report a vulnerability**.
Include the app version, Windows version, and steps to reproduce. You can expect an acknowledgement within a week.

## Scope

The app runs locally, has no network access, accounts or telemetry, and reads only the audio files you import plus its own
settings and presets in `%APPDATA%\AmbientSoundMixer`. Preset and settings files are validated before use.
Reports about malformed audio files or preset files causing crashes or unsafe behavior are in scope.
