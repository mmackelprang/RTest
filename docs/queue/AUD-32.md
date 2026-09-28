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

## Verification (planned)

Both files show their tagged artist (and album, for *Meditating Beat*) before and after an
identification; the fill log lists only genuinely missing fields; no `Duration: 00:00:00` for either.

---

## 🚧 BUILT 2026-09-26 — branch `fix/aud-32-tag-read-fallback`

**Cause, reproduced off the box:** SoundFlow 1.4.1's `SoundMetadataReader.Read` returns
`CorruptFrameError: A 'ID3v2.2' frame is corrupted` for both files; TagLib reads every tag. ⚠ **The
"Logic/iTunes frames" lead above was a red herring** — the adversarial reviewer hand-built a minimal,
well-formed v2.2 tag and SoundFlow rejected that too. **Blast radius: every ID3v2.2 file in the library**,
not two Kevin MacLeod tracks. SoundFlow handles ID3v1 and v2.3 correctly.

**Fix:** new `Radio.Infrastructure.Audio.Services.AudioTagReader` — SoundFlow first, TagLib when it fails,
blank tags → `null`, bitrate always bps. All four tag-reading sites now go through it:
`FilePlayerAudioSource.UpdateMetadataFromFile`, both queue-item builders, and
`FileBrowser.ExtractMetadataAsync` (the file browser showed no artist/album for these files either).
Embedded-art extraction now also runs for v2.2 files; before, it was skipped along with the tags.

**Logging:** the reader logs only at Debug (queue renders re-read every queued file, and log volume
correlates with audio distortion on the box). A file **neither** reader can open is a Warning, once per
track load.

**Tests** (fixture: first 18 KB of `Meditating Beat.mp3`, public domain — `TestData/id3v22-soundflow-rejects.mp3`):
red first against unmodified code, 7 of 9 failing — including the box case verbatim
(`Expected: Meditating Beat / Actual: Prezis In My Pocket`) and the file browser showing the filename.
`Fixture_IsStillRejectedBySoundFlow` guards against a SoundFlow upgrade making the fallback tests vacuous.
Known gap: `CreateQueueItemWithState` (full-playlist view) has no fallback test of its own; its code is
identical to `CreateQueueItem`'s today.

**Box check (for the owner's UAT):** play `Meditating Beat.mp3` and `Hear What They Say.mp3` from the queue.
Before any identification, both show artist **Kevin MacLeod** (and album **FreePD Music** for the first); the
`Starting playback` line shows a real duration, not `00:00:00`; after an identification the artist is
unchanged and the fill line lists only genuinely missing fields.
