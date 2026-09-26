# `AUD-32` — some MP3s' tags silently fail to load, so tagged fields are filled by fingerprinting

[← Builder Queue index](../BUILDER_QUEUE.md)

🟠 **P1-ish for the file player.** Filed 2026-09-26 from `AUD-1`'s box UAT. Pre-dates AUD-1 (the old
overwrite replaced every field anyway), but it defeats AUD-1's per-field rule for the affected files.

## What was observed

| File (`/opt/radio-console/media/audio`) | Tags per `ffprobe` | Logged duration | Filled by fingerprint |
|---|---|---|---|
| `Hear What They Say.mp3` | `artist=Kevin MacLeod` only | `00:00:00` | title, **artist**, cover art → shown as *Prezis In My Pocket* / **Dsp Records North** |
| `Meditating Beat.mp3` | `title`, `artist=Kevin MacLeod`, `album=FreePD Music` | `00:00:00` | title, **artist, album**, cover art |
| `08-I'm Not In Love.mp3` | full + embedded art | `00:06:04` | nothing (correct) |

The artist and album fills are wrong: those fields are tagged in the file.

## Mechanism (from the code, not yet reproduced)

`FilePlayerAudioSource.UpdateMetadataFromFile` seeds placeholders (filename title, default artist/album,
default art), then overwrites them only inside `if (result.IsSuccess && result.Value != null)` after
`SoundMetadataReader.Read(filePath)`. Duration, TagLib duration, embedded-art extraction **and** the tags
are all inside that block. A `Duration: 00:00:00` in the `Starting playback` line is consistent with the
whole block being skipped. **Nothing is logged when it is** — no WRN/ERR line appeared for either file.
AUD-1's `SourceMetadataPrecedence` then correctly treats the placeholders as missing.

Both affected files carry Logic Pro X / iTunes frames (`TSS`, `iTunNORM`, `iTunSMPB`); that is a lead, not a cause.

## First task

Run `SoundMetadataReader.Read` on the two files (copies from the box) and record what it returns. Then:
log a warning when it fails, and fall back to TagLib (already referenced for duration and art) for the
tags rather than dropping to placeholders.

## Verification

Both files show their tagged artist (and album, for *Meditating Beat*) before and after an
identification; the fill log lists only genuinely missing fields; no `Duration: 00:00:00` for either.
