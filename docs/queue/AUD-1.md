# AUD-1 — Split `UseShazamForAllSources` into the two independent decisions it currently conflates.

> Queue dossier for row **`AUD-1`** of [`BUILDER_QUEUE.md`](../BUILDER_QUEUE.md).
> The detail below was moved verbatim out of that row's Item cell on 2026-09-06; only
> whitespace, the table's `\|` escapes and docs-relative link prefixes changed.
>
> ⚠ **Directional words in the prose were written when every row shared one file.**
> *above*, *below* and *this file* may now point across files — most often at
> [`BUILDER_QUEUE_ARCHIVE.md`](../BUILDER_QUEUE_ARCHIVE.md) or a sibling in this
> directory. They were left verbatim rather than reworded, which would be a content edit.

| Field | Value |
|---|---|
| Status | 📋 |
| Plan | _plan TBD (small-to-medium; the behaviour already exists — the work is splitting the switch and deciding FilePlayer's side)_ |
| Spec / handoff | _no spec doc — the diagnosis is in this row_ · provenance: 2026-08-10 debugging session; PR #469 is the adjacent merged fix |
| Depends on | — _(no row dependency. **Touches the same file and method region as PR #469** (`BluetoothAudioSource.OnTrackIdentified`), which is merged — rebase, don't re-derive. **Also touches `FilePlayerAudioSource.cs`, which #468 changed on 2026-08-11** — the anchors above are already re-sited, but rebase rather than trusting any earlier copy.)_ |
| Branch | `fix/split-shazam-fingerprint-vs-overwrite` |

## Detail

**Split `UseShazamForAllSources` into the two independent decisions it currently conflates.**

**⚠ READ THIS BEFORE TOUCHING THE FLAG: do NOT "fix" the overwrite behaviour by setting `UseShazamForAllSources` to `false`. That turns off BT album art entirely.** One boolean (`src/Radio.Fingerprinting/FingerprintingOptions.cs:19`, surfaced at `src/Radio.API/appsettings.json:91` and `src/Radio.Web/Models/ApiModels.cs:788`) governs two things that have nothing to do with each other.

**Decision 1 — "always fingerprint BT" — is NECESSARY and must not change.** The 2026-08-10 investigation established that **AVRCP cannot supply album art on this box**: BlueZ 5.72 ships **no BIP / cover-art implementation**, and 7 days of fingerprint-DB data show **0 AVRCP-sourced art against 2,560 SongRec-sourced**. SongRec is not an enhancement here, it is the only source. Mechanically: the flag feeds the gate at `src/Radio.Infrastructure/Audio/Sources/Primary/BluetoothAudioSource.cs:837` (`NeedsFingerprintingLookup = hasIncompleteMetadata || FpOptions.UseShazamForAllSources`), and a false gate makes `src/Radio.Fingerprinting/Services/BackgroundIdentificationService.cs:260` (`if (!audioTap.NeedsFingerprintingLookup) return;`) **hard-return** — so whenever AVRCP supplies a title *and* artist, nothing is ever fingerprinted and no art is ever found.

**Decision 2 — "let SongRec overwrite AVRCP title/artist/album" — is NOT necessary and is actively wrong.** It rewrites `Enter Sandman (Remastered)` to `Enter Sandman`; AVRCP is the authoritative name for what the phone is actually playing, and SongRec is guessing from audio.

**The wanted behaviour already exists in the same method, on the branch that never runs when the flag is on.** `BluetoothAudioSource.OnTrackIdentified` (`:845`) forks at `:867`: **`:867-891` is the overwrite branch** (assigns Title/Artist/Album unconditionally, caches art, logs *"Shazam metadata replaced AVRCP for BT"*, `return`s) and **`:893-905` is the preserve branch** (computes `hasArt` from existing metadata and calls `CacheAndSetCoverArtAsync` **only when art is absent**, never touching title/artist/album).

**The fix is to reach `:893-905`'s art-fill behaviour while keeping `:837`'s always-fingerprint gate** — i.e. two flags, not one.

**⚠ Caveat that probably forces a per-source split rather than a rename:** the same flag is read by `src/Radio.Infrastructure/Audio/Sources/Primary/FilePlayerAudioSource.cs:1988` (the gate) and `:2112` (the overwrite, additionally guarded by `needsLookup`), where **ID3 tags are authoritative in a different way** and the current behaviour may well be correct as-is. A single global rename will not serve both sources — expect `FingerprintingOptions` to gain two properties and the config/DTO surfaces above to follow.

**Decide deliberately whether FilePlayer's overwrite behaviour changes at all**, and say which way in the plan; silently dragging it along with BT is the failure mode here. _All line numbers verified against `main` @ `c129e0d`; **PR #469 moved this file by ~28 lines**, so pre-merge citations are stale._

**⚠ RE-VERIFIED 2026-08-11 against `main` @ `8b1ce0a` (post-#468), and TWO ANCHORS IN THIS ROW WERE STALE AND ARE NOW CORRECTED: `FilePlayerAudioSource.cs:1968 → :1988` and `:2092 → :2112`.** #468 touched that file (`StopCoreAsync` / `DisposeAsync` resume-position handling, nothing to do with fingerprinting) and pushed everything below `:872` down **+20 lines**.

**Everything else in this row re-verified byte-exact and unchanged:** `FingerprintingOptions.cs:19`, `appsettings.json:91` (**checked specifically — #468 added a line to that file and it landed below `:91`, so the citation survives**), `ApiModels.cs:788`, `BluetoothAudioSource.cs:837`/`:845`/`:867-891`/`:893-905` (#468 did not touch that file at all), and `BackgroundIdentificationService.cs:260`.

---

## ⭐ OWNER DECISION, 2026-09-08 — per-field precedence, and it resolves the FilePlayer question

> "When metadata is available from the audio source, use the source metadata (song name, album name,
> album art). When one or more of these is missing, use fingerprinting to augment the missing data."

**This is a stronger and simpler rule than the F1/F2 split the plan posed, and it applies uniformly
to both sources.** It answers the blocked question — **FilePlayer follows Bluetooth (F1)** — but by
making the *same* rule true everywhere rather than by making one source imitate the other.

### What it means concretely

- **Precedence is per FIELD, not per track.** Title, artist, album and art are decided independently.
  A track whose AVRCP gives title and artist but no art keeps both, and gets *only* art from
  fingerprinting. This is exactly the behaviour already sitting on the unreachable preserve branch at
  `BluetoothAudioSource.cs:893-905`.
- **The always-fingerprint gate at `:837` stays.** Fingerprinting must still run, or there is nothing
  to fill gaps with. It is the *overwrite* at `:867-891` that goes.
- **Same rule for FilePlayer.** ID3 tags are source metadata. `:2126-2128` currently replaces embedded
  art unconditionally; under this rule it may only fill art that is absent. The measured harm it
  prevents: **44 of 52 file plays had their tags overwritten**, including a fabricated title and a
  U+2010 hyphen corrupting `blink‐182`.
- **A field the source supplies as an empty string counts as missing**, not as an authoritative blank.
  Say so in the plan; it is the obvious edge and it decides real cases.

### ⚠ Interaction with `AUD-17` — this rule is what makes BT album art work at all

`AUD-17` established that **AVRCP on this box never supplies art**: `LinuxBluetoothService.cs:2761-2765`
reads the MPRIS names off an `org.bluez.MediaPlayer1` proxy, which publishes `ImgHandle`, so
`CacheAvrcpArtAsync` has never executed. Under the owner's rule that is **fine and self-correcting** —
art is always "missing" on Bluetooth, so fingerprinting always fills it, which is precisely today's
working behaviour and the reason ~99% of rows carry SongRec art.

**So the rule preserves BT album art without depending on the flag's old meaning.** Title and artist
stop being overwritten; art keeps arriving. That is the whole point of the row.

⚠⚠ **The flag still must not be renamed** — see the row above. The SQLite store already holds
`fingerprinting:useShazamForAllSources|true`, and a rename orphans it, falls through to a `false`
default, and kills BT art. The decision above changes *behaviour*, not the *key*.
