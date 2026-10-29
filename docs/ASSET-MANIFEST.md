# Audio asset manifest

| Asset | Source | Author | License | Attribution required |
|---|---|---|---|---|
| All scenario layers (`ocean.*`, `forest.*`, `waterfall.*`, `rain.*`, `storm.*`) | Generated in real time by `AmbientSoundMixer.Core/Synthesis` | Project contributors | MIT (original code) | No |
| White, pink and brown noise | Generated (`NoiseSource`) | Project contributors | MIT | No |
| Binaural tones | Generated (`BinauralSource`) | Project contributors | MIT | No |

There are **no recorded or sampled third-party audio files** in the application or installer, so there are no
external licenses or attributions to track.

## What this means for sound quality

The scenarios are synthesized (filtered noise, random chirps, droplets and thunder envelopes). They are designed to run
indefinitely without looping, but they are not field recordings and will sound more "designed" than a recording of a real beach.

## Adding recordings later

If you want to add real recordings, only add files whose license you have verified allows redistribution
(for example CC0 / public domain, or CC BY with attribution). For each file add a row here with the source URL,
author, license and attribution text, and add the notice to `THIRD-PARTY-NOTICES.md`.
The mixer already plays any decoded file through the same pipeline (`AudioFileDecoder` + `LoopingBufferSource`),
so a bundled-recording layer only needs a `LayerDefinition` that wraps a decoded buffer.
