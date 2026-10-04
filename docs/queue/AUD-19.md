# `AUD-19` — the History panel will not follow the per-field metadata rule, and that will read as a failed fix

[← Builder Queue index](../BUILDER_QUEUE.md)

🟡 **P2.** Filed 2026-09-09. ⚠ **This row was referenced before it existed.** `docs/queue/AUD-1.md:113`
has said *"filed as `AUD-19`, documented in…"* since 2026-09-08 — **it was neither**, and `AUD-1`'s
Builder is told to cite it in its PR body before merge. Caught by `UI-12`'s Builder while enumerating
adjacent rows; the citation was a coordinator error, corrected here and in `AUD-1`.

## The divergence

The owner's decision on `AUD-1` (2026-09-08):

> *"When metadata is available from the audio source, use the source metadata (song name, album name,
> album art). When one or more of these is missing, use fingerprinting to augment the missing data."*

`AUD-1` delivers that on the **now-playing** path. **It does not reach History.**

`PlayHistoryTracker` re-points its rows at the fingerprint record **regardless of which branch the
source took** — it sets `MetadataSource = Fingerprinting` on *any* landed identification
(`:563-573`, `:631-641`) without consulting whether the source's own metadata won per field.

**So after `AUD-1` ships, the now-playing display obeys the rule and History does not.** Same track,
two surfaces, two answers — and the History one will show SongRec's guess where the source supplied a
name.

## ⚠ Why this needs to be visible before `AUD-1` merges, not after

**Left undocumented, it reads as a failed fix.** The owner will change a phone's metadata behaviour,
look at History, and see the old wrong titles still there. `AUD-1`'s PR body must name this row
explicitly so the divergence is a known, scoped follow-up rather than a bug report.

⭐ That is the whole reason this is a separate row rather than folded in: **different file, different
lifecycle, and folding it in roughly doubles the blast radius of a change already on the live audio
path.**

## Scope questions for the plan

1. **Is History even wrong?** ⚠ **Ask before assuming.** There is a real argument that history is a
   record of *what fingerprinting identified*, and that recording SongRec's answer is correct there
   even when the display prefers the source's. **If so this row closes as "correct, documented" and
   that is a legitimate outcome.** Do not treat the divergence as self-evidently a defect.
2. **If it is wrong, where does the rule live?** `AUD-1` will have built a shared per-field precedence
   helper. History should call the same one rather than re-implement it — two implementations of one
   rule is how the rule stops being one rule.
3. **What about rows already written?** The DB holds history under the old behaviour. Say plainly
   whether existing rows are migrated, left, or re-derived — and if left, that History will show two
   eras with different semantics.
4. ⚠ **`MetadataSource` records provenance and is load-bearing elsewhere.** `AUD-17` established that
   `Source` on `TrackMetadata` records the **title's** provenance, not the art's, and that misreading
   it produced a retracted row. **Do not change what the column means without checking who reads it.**

## Verification

Unit-testable: land an identification where the source supplied a title, and assert the history row
keeps the **source's** title rather than the fingerprint's. **Must fail first** — today it does not,
so confirm RED against `main` rather than assuming.

⚠ **Beware a vacuous UAT here.** `AUD-12`'s stall gates fingerprinting off entirely and `AUD-18`'s tap
can return zero bytes for hours — with either live, *no* identification lands and a broken build looks
correct. Check the tap is alive first, as `AUD-1`'s §4.4 does.

## Depends on

**`AUD-1`** — this row has no meaning until the per-field rule exists to diverge from. ⛔ **Do not claim
it first.**

---

## ✅ OWNER RULING 2026-09-10 — **PRE-GA**, together with `AUD-1`

The owner ruled both this row and [`AUD-1`](AUD-1.md) **pre-GA**, resolving a contradiction in which
`HANDOFF-GA-PUNCH-LIST.md` §5 listed `AUD-1` as *"P2 — Post-GA"* while this queue scheduled **this row
behind it** — treating it as buildable now. ⛔ **The two positions could not both be acted on.**

⚠ **The dependency is unchanged: `AUD-1` still ships first.** This row *"has no meaning until the
per-field rule exists to diverge from."* Pre-GA changes **when**, not **what**.

---

## 🚧 BUILT 2026-10-04 — [PR #784](https://github.com/mmackelprang/RTest/pull/784), branch `fix/aud-19-history-per-field-precedence`, HELD for the owner's UAT

**Not merged, not deployed.** It changes what History shows, so it waits for the owner's check
(below). Branched from `origin/main` `ed25f352`.

**Gates on `f0804cc` (Windows):**
- `dotnet build -c Release --no-incremental`: **46 warnings, 0 errors**, which is the baseline.
- `dotnet test`: **5,896 passed**, 6 failed, 5 skipped.
  - The 6 failures are the known `SrcVariableResamplerTests` (`libsamplerate.so.0`).
  - `NwsObservationIntegrationTests` (live network) passed on this run; it failed on the two earlier
    gate runs on this branch.

### Owner UAT

⚠ **The branch must be deployed first.** Check that `curl -s http://radio:5000/api/health/version` and
`:5002` both report the branch head.

⚠ **Then check that the tap is alive.** In the newest `/opt/radio-console/logs/radio-*.txt`, an
`Identified track:` line must appear within a few minutes of playback. If no identification lands, every
check below passes vacuously (`AUD-12`, `AUD-18`).

1. **Bluetooth.**
   - Play a track on the phone and wait for an `Identified track:` line.
   - In History, the newest row shows **the phone's title and artist**, never SongRec's spelling, and
     one row per track.
   - Cover art appears if the phone sent none.
   - The file sink shows `Enriched existing play history entry … (identified as '…')` or
     `Created new play history entry …`.
2. **Bluetooth, next track.**
   - Skip on the phone.
   - A new row with the phone's title appears, and the previous row ends.
3. **Tagged file.**
   - Play a file with Title/Artist tags and let it be identified.
   - The row shows the tag title and artist; album and art are filled only if the tags had none.
   - Let it auto-advance to the next tagged file: a new row appears with **that file's tags**.
4. **Optional.** `GET http://radio:5000/api/playhistory` shows `metadataSource` `Avrcp` / `FileTag` for
   these rows.

Rows recorded before the deploy keep SongRec's titles; they are not migrated.

**Scope question 1 — is History wrong?** ⚠ **There is no recorded owner answer in those words.** The
owner ruled this row pre-GA on 2026-09-10, behind `AUD-1`. The coordinator who dispatched this build
framed the owner's check as *"History shows the source's fields, with fingerprinting filling only the
gaps"*. The build follows that framing, which is one more reason it is held for the owner rather than
auto-merged. If the owner decides History should record what fingerprinting identified, close this PR
and close the row as "correct, documented".

**What changed** (`src/Radio.Infrastructure/Audio/Services/PlayHistoryTracker.cs`):
- An identification lands on a History row through `ApplyIdentification`, which calls
  `SourceMetadataPrecedence.FillMissingFrom` / `ShouldFillAlbumArt` — `AUD-1`'s helper, not a second
  rule. For **Bluetooth and the file player**, the title, artist, album and cover art the source
  supplied are kept; the identification fills only what is missing. Art fills with no artist-match
  guard, as on now-playing since the owner's 2026-09-26 `AUD-1` decision.
- **A fingerprint title that differs from the source's no longer starts a new History row.** In
  `OnSongChanged` the baseline is the source's *live* metadata (AVRCP, or the current file's tags), so
  *"Basket Case (Remastered)"* over AVRCP's *"Basket Case"*, or SongRec matching residual audio, fills the
  current row's gaps instead of finalizing it and recording SongRec's title.
- **SDR radio, vinyl and USB are unchanged** — they still take an identification whole, because their
  now-playing still does (`USBAudioSourceBase.UpdateMetadataFromFingerprint`; `AUD-1` covered BT and
  File only). Pinned by a test whose radio row is titled with its frequency.
- `IsPlaceholderMetadata` is built on `SourceMetadataPrecedence.IsMissing`; its placeholders are
  History's own fallbacks. The remark on `SourceMetadataPrecedence` that called it a divergent second
  definition is updated.

**Scope question 3 — existing rows: left as they are, not migrated.** A row's source fields were never
stored separately from the fingerprint's, so there is nothing to re-derive them from. History therefore
shows two eras: rows before this build carry SongRec's title wherever an identification landed.

**Scope question 4 — `MetadataSource`.** Readers checked: the column is written by `PlayHistoryTracker`
and the API's manual POST, returned by `GET /api/playhistory` (`PlayHistoryController.cs:483`), carried
in the Web model (`ApiModels.cs:316`) and displayed nowhere. It now records the **title's** provenance —
the same reading `AUD-17` established for `TrackMetadata.Source`: `Avrcp` / `FileTag` when the source's
title survived, `Fingerprinting` when the identification supplied it. Whether an identification landed
is `IdentificationConfidence`. Old rows say `Fingerprinting` for any identification (see the XML doc on
`PlayHistoryEntry.MetadataSource`).

### Premises in this dossier that were wrong or incomplete

1. **The anchors had drifted by one line** (`:563-572` and `:631-640`, not `:563-573`, `:631-641`).
2. **The row understated the defect.** It names the `MetadataSource` assignment. The larger effect was in
   `OnSongChanged`: when SongRec's title differed from AVRCP's or the tags' at all, the source's row was
   **finalized and a second row started under SongRec's title**. The next AVRCP refresh of the right title
   was then dropped by the 5-minute duplicate check, so SongRec's row stayed the latest. The new tests
   show both rows on `main`.
3. **The file player's History rows after the first track come only from fingerprinting.** An
   auto-advance or Next stays in `Playing` and raises no `StateChanged`
   (`FilePlayerAudioSource.NextAsync`), so `OnSongChanged` is the only thing that starts the next file's
   row. The fix therefore keeps fingerprinting as the boundary signal and takes the row's fields from
   the new file's tags. Suppressing song changes for tagged sources would have dropped every
   auto-advanced track from History.
4. **A source can hold a title it filled itself.** Bluetooth keeps a fingerprint-filled title until the
   next AVRCP event writes the field. Read naively as source data, that title would hold History on the
   first song for a phone that publishes no track names. For Bluetooth the tracker now keeps the last
   AVRCP event and counts a field as source data only when it equals that event's value; art counts only
   when the event carried art. The file player re-seeds its metadata on every file, so a fill can only
   come from an earlier identification of the same file. It is kept, as on now-playing.

5. **Bluetooth needed a second notion of "source data".** The first build trusted the source's live
   metadata. The pre-merge review showed that the guard for fills could not fire on the real event order
   (the first identification raises no `SongChanged`). It also showed that art filled into a History row
   could never be corrected. On Bluetooth the tracker now keeps the last AVRCP event, and a field counts
   as AVRCP's only when it equals that event's value. Three exceptions apply:
   - a field is replaced only when the identification carries a value for it;
   - an artist filled under AVRCP's own title is kept, as on now-playing;
   - art counts as AVRCP's whenever the event carried any.

### Not changed, or known gaps (owner or follow-up)

- **`AUD-33` M3, stale results: not fixed here.** History still consumes an identification sampled
  before a skip. The sources drop those using their own track boundary; History has none. The effect
  differs by source:
  - **Bluetooth:** a stale result arriving before the new track's AVRCP event can put the previous
    track's art on the row. The next identification that carries art replaces it.
  - **File player:** a stale result shares the previous identification's track key, so it raises no
    `SongChanged`. `OnTrackIdentified` no longer touches finished rows.
- **File player art and album fill once per row.** A misidentification's art on a tagged file without
  embedded art stays on that History row. Now-playing does the same: `FilePlayerAudioSource` fills each
  field once per file. If the owner wants History to refresh it, that is a ruling, not a fix.
- **The AVRCP snapshot is not cleared on disconnect, on a second phone or on a source switch.** Until
  the phone's first event after a reconnect, the previous session's values can count as AVRCP's.
- `BluetoothAudioSource.UpdateRecentPlayHistoryCoverArtAsync` still patches a BT row's art when it has
  none. It matches on the identification's title, so it now misses whenever SongRec's title differs from
  AVRCP's. The merge fills art on its own, so nothing is lost.
- **Bluetooth rows recorded on a `Playing` transition now say `Avrcp` (they said `Manual`).** Nothing
  reads `Manual` for Bluetooth.

### Pre-merge review (two adversarial passes, session model)

**Pass 1:** no CRITICAL; 2 HIGH, 2 MEDIUM and 7 LOW findings, all on `00540f1`. Fixed in `6a9f4dc`:
- **H1:** the sticky-fill guard could not fire on the real event order. Its test raised `SongChanged`
  for the first identification, which the service never does. The guard was replaced by the AVRCP
  filter, and the test now follows the real order.
- **H2:** art and album filled into a BT row by a misidentification were frozen. They are now
  corrected by the next identification.
- **M1:** `OnTrackIdentified` merged onto a finished row using the current file's name. It now skips
  finished rows.
- **M2:** the `MetadataSource` doc overclaimed. It now states where it is not exact, and BT rows
  recorded on `Playing` say `Avrcp`.
- **L1–L4:** fixed in comments and docs, and the live read moved before the first await. L5–L7 were
  pre-existing or informational and are not changed.

**Pass 2:** on `6a9f4dc`, no HIGH; 2 MEDIUM and 4 LOW findings. Fixed in `f0804cc`:
- **M-A:** an identification with no art or album erased the row's. It now does not.
- **M-B:** a phone that sends a title but no artist got a new row for each misidentification. The
  artist under AVRCP's title is now kept.
- **L-1:** the AVRCP snapshot is now read with the live metadata.
- **L-2, L-4:** stated in comments. L-3 is the `AUD-33` gap above.

**Tests:** `PlayHistoryTrackerPrecedenceTests`, 12 tests.
- **RED-first, in three rounds:**
  - 5 of the first 7 failed on `main`; the 2 radio controls passed.
  - The 3 tests added in pass 1 failed on `c662fc8`.
  - The 2 added in pass 2 failed on `6a9f4dc`.
- **Mutation checks:** 18 mutations, each reverted with `git checkout` after committing. All 18 were
  killed by the targeted tests.
