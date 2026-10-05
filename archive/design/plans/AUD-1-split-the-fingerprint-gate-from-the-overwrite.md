# PLAN — `AUD-1` · Per-field precedence: the source's metadata wins where it exists, fingerprinting fills only what is missing

> **Row:** `AUD-1`, [`docs/queue/AUD-1.md`](../../docs/queue/AUD-1.md). Index row `docs/BUILDER_QUEUE.md:33`.
> **Branch:** `fix/split-shazam-fingerprint-vs-overwrite`
> **Estimate:** **1.25 d**. §0.11 derives it. ⚠ **Up from the pre-amendment 0.75 d**, and §0.11 says why.
> ⛔ **NOT auto-mergeable.** §0.12. Live audio path, user-visible metadata, UAT needs a phone.
> **Planned against** `main` at **`5d78b835`** (*"docs: file AUD-18 …"*, #613). Every line number below
> was re-derived at that commit on 2026-09-08.
>
> ⚠⚠ **AMENDED 2026-09-08 — the F1/F2 question this plan was written around is SUPERSEDED and is
> gone.** Read §0.0 before anything else. The owner has replaced *"does FilePlayer follow
> Bluetooth?"* with a single rule that is true of both sources, and the rule is **not** what the
> superseded §0.8 recommended doing. It changes the task list, not just the framing: the previous
> revision's *"one-line change, twice"* would have **shipped a regression**. §0.0c is that finding.
>
> ⚠ **The working tree flipped between `main` and `test/bt-capture-branch-dispatch-coverage` three
> times while this amendment was being written** — a Builder is mid-cycle on `TEST-2` and its WIP
> commits moved twice (`0dd65f0e` → `4a8c7b8a`). Every anchor below was therefore taken from
> `git show main:<path>` or from a file verified byte-identical to `main` with
> `git diff --stat main -- <path>`, **not from whatever the checkout happened to hold**. §0.13 records
> which files that mattered for.

---

## 0. Read this before Task 1

### 0.0 ⛔⛔ THE OWNER DECISION, and what it replaced

**Recorded 2026-09-08 in [`docs/queue/AUD-1.md`](../../docs/queue/AUD-1.md) and in the index row at
`docs/BUILDER_QUEUE.md:33`:**

> *"When metadata is available from the audio source, use the source metadata (song name, album
> name, album art). When one or more of these is missing, use fingerprinting to augment the missing
> data."*

**⛔ Options F1, F2 and F3 no longer exist. Do not re-litigate them, do not implement them, and do
not read the superseded recommendation as advice.** They asked *which source imitates which*. The
owner answered a different and better question — **what is the rule?** — and the answer is one rule,
applied per field, in both sources. FilePlayer does end up following Bluetooth, which is what F1
proposed, but it gets there by both sources obeying the same rule rather than by one copying the
other's code path. The distinction is not rhetorical: it changes what gets written.

The superseded §0.8 is preserved verbatim in **Appendix A**, because two of its findings (the
measured FilePlayer damage, and the "no re-identification loop" check) are still load-bearing and a
reader who meets them without their original framing will mistake them for new claims.

#### 0.0a What the rule means concretely — four consequences, all of them mechanical

1. **Precedence is per FIELD, not per track.** Title, Artist, Album and cover art are decided
   independently. A track whose AVRCP supplies title and artist but no art keeps both and takes
   *only* art from fingerprinting.
2. **The always-fingerprint gate at `BluetoothAudioSource.cs:837` stays exactly as it is.** If
   fingerprinting does not run there is nothing to fill gaps with, and
   `BackgroundIdentificationService.cs:260` (`if (!audioTap.NeedsFingerprintingLookup)`) hard-returns
   when the gate is off. It is the **overwrite** at `:867-891` that goes.
3. **The same rule for FilePlayer.** ID3 tags are source metadata. `FilePlayerAudioSource.cs:2126-2128`
   replaces embedded cover art unconditionally today and may only fill art that is absent.
4. ⭐ **An empty string from the source counts as MISSING, not as an authoritative blank.**

#### 0.0b ⭐ `C-246` — the empty-string rule is not an edge case on Bluetooth. It is the common case for Album

The brief called this "the obvious edge". On this codebase it is the ordinary path, and that is worth
knowing before writing the helper, because it changes how much the rule is doing.

`BluetoothPlaybackMetadata` (`src/Radio.Core/Interfaces/Audio/IBluetoothService.cs:23-25`):

```csharp
  public string Title { get; init; } = string.Empty;
  public string Artist { get; init; } = string.Empty;
  public string Album { get; init; } = string.Empty;
```

`LinuxBluetoothService.UpdateMetadata` (`:2754`) fills them with `GetString(track, "Album")`, which
returns `""` when BlueZ does not publish the attribute — and **`BluetoothAudioSource.OnMetadataChanged`
writes all three through unconditionally** (`:770-772`), with no `IsNullOrEmpty` guard:

```csharp
    MetadataInternal[StandardMetadataKeys.Title] = e.Title;
    MetadataInternal[StandardMetadataKeys.Artist] = e.Artist;
    MetadataInternal[StandardMetadataKeys.Album] = e.Album;
```

**So a phone that reports no album leaves `""` in the metadata dictionary — the key is present and
the value is empty.** Any "missing" test written as `!ContainsKey` or `== DefaultAlbum` sees that as
*supplied*, and the fill never happens. Empty-string handling is the difference between the rule
working on Bluetooth and the rule doing nothing on Bluetooth.

⚠ **The two sources encode "missing" differently, and a single helper must accept both.**

| | Bluetooth | FilePlayer |
|---|---|---|
| Title absent | `""` (`:770`) | the filename (`:1903`), replaced only if a Title tag exists (`:1951-1954`) |
| Artist absent | `""` (`:771`) | `DefaultArtist` = `"--"` (`:1904`) |
| Album absent | `""` (`:772`) | `DefaultAlbum` = `"--"` (`:1905`) |
| Art absent | key **removed** (`:805`) | `DefaultAlbumArtUrl` (`:1906`) |

FilePlayer guards its tag reads with `!string.IsNullOrEmpty` (`:1951`, `:1955`, `:1959`), so an empty
ID3 tag never reaches the dictionary — on that source "missing" is always a sentinel. Bluetooth has
no such guard, so on that source "missing" is always `""` or an absent key. **One definition has to
cover null, empty/whitespace, a sentinel, and an absent key, or it covers one source and not the
other.** §1.2 is that definition.

#### 0.0c ⭐⭐ `C-247` — the finding that most changes the work: the previous revision would have SHIPPED A REGRESSION

The superseded plan's §1.3 said the fix was *"a one-line change, twice"* — point `:867` at a new
default-off property and fall through to the preserve branch at `:893-905`, **unmodified**. That is
wrong under the owner's rule, and it is wrong under today's behaviour too.

**Read `:893-905`. It fills cover art and nothing else.** It never touches Title, Artist or Album:

```csharp
    var hasArt = MetadataInternal.TryGetValue(StandardMetadataKeys.AlbumArtUrl, out var existingArt)
      && existingArt is string artStr && !string.IsNullOrEmpty(artStr);

    if (!hasArt && !string.IsNullOrEmpty(e.Track.CoverArtUrl) && _serviceScopeFactory != null)
    {
      _ = CacheAndSetCoverArtAsync(e.Track.CoverArtUrl, e.Track.Title, e.Track.Artist);
    }
```

So today, on Bluetooth:

| AVRCP supplies | Flag | Branch taken | Title outcome |
|---|---|---|---|
| title + artist | `true` (the box) | overwrite `:867-891` | **replaced** — the defect |
| **no title** (`""`) | `true` (the box) | overwrite `:867-891` | **filled from SongRec** — correct, and the only reason it works |
| **no title** (`""`) | `false` | preserve `:893-905` | **stays `""`** — ⚠ a latent defect today |

**The previous plan moved the box from row 2 to row 3.** A phone that reports no AVRCP title — which
`MetadataChanged_WithEmptyTitle_SetsNeedsFingerprintingLookup`
(`BluetoothAudioSourceTests.cs:66`) shows is a tested, expected state — would have gone from
*"SongRec supplies the title"* to *"no title at all, forever"*. Nothing in the previous plan's test
list would have caught it: all three of its new tests arranged **populated** AVRCP metadata.

⭐ **This is why the amendment is a restructure rather than a note.** The owner's rule requires the
preserve path to *gain* per-field fill for Title/Artist/Album, not merely to become reachable. Under
the rule, row 3 becomes "filled from SongRec" — the same as row 2 — and row 1 becomes "kept". Every
row ends up right, and the latent defect in row 3 is fixed as a side effect rather than deepened.

**Test `B3` (§Task 6) is the direct guard on this, and it fails against today's code.**

#### 0.0d The rule restated as the implementable predicate

> A field is **MISSING** when its value is absent, null, empty, whitespace-only, or equal to that
> field's placeholder sentinel. A fingerprint result may write a field **only** when that field is
> missing, and each field is decided on its own.

⚠ **"Placeholder sentinel" is a real extension of the owner's words and it is deliberate.** The
owner named the empty string. `"--"` (`StandardMetadataKeys.DefaultArtist` / `DefaultAlbum`,
`src/Radio.Core/Models/Audio/StandardMetadataKeys.cs:69`, `:74`) and
`"/images/default-album-art.png"` (`:59`) are the same thing under a different spelling — they exist
precisely to mean *"nothing here"*, and `PlayHistoryTracker.cs:373-386` already maps them back to
`null` for exactly that reason. Treating them as authoritative values would make the rule a no-op on
FilePlayer, where they are the only form "missing" takes. **If the owner disagrees, this is the one
line to push back on.**

### 0.1 What this row is, in one paragraph

One boolean decides two unrelated things. `FingerprintingOptions.UseShazamForAllSources`
(`src/Radio.Fingerprinting/FingerprintingOptions.cs:19`) feeds **a gate** —
`BluetoothAudioSource.cs:837`, `NeedsFingerprintingLookup = hasIncompleteMetadata ||
FpOptions.UseShazamForAllSources`, which decides *whether SongRec runs at all* — and **a fork** —
`BluetoothAudioSource.cs:867`, which decides *whether SongRec's answer overwrites the phone's own
AVRCP title/artist/album*. The gate is necessary on this box. The fork is wrong. Because they share
one switch, the only way to stop the overwrite today is to turn off the gate, which stops album art
entirely. **After the owner's decision the fix is to delete the fork and replace both of its arms
with one per-field merge** — see §1.1 for why deleting beats adding a second flag.

**This is not theoretical.** Confirmed live 2026-09-06: SongRec logged *"Shazam metadata replaced
AVRCP for BT: 'Spirit In The Sky'"* while the phone played Green Day, having misidentified from
residual radio audio seconds after a BT reconnect. The next AVRCP update restored the right title
with no art, leaving the placeholder.

### 0.2 `C-240` (re-run 2026-09-08) — anchor re-derivation at `main` @ `5d78b835`

The previous revision was planned against `a529ccf7`. `main` has advanced. Re-derived, every one of
these read directly out of `main` rather than the checkout:

| Citation | At `5d78b835` | Verdict |
|---|---|---|
| `FingerprintingOptions.cs:19` | `public bool UseShazamForAllSources { get; set; } = false;` | ✅ |
| `appsettings.json:93` | `"UseShazamForAllSources": false,` (block opens `:91`) | ✅ |
| `ApiModels.cs:788` | `public bool UseShazamForAllSources { get; set; } = false;` | ✅ |
| `BluetoothAudioSource.cs:770-772` | the unguarded AVRCP write | ✅ **new citation** (§0.0b) |
| `BluetoothAudioSource.cs:837` | the gate | ✅ |
| `BluetoothAudioSource.cs:845` | `private void OnTrackIdentified(...)` | ✅ |
| `BluetoothAudioSource.cs:867-891` | the overwrite branch | ✅ |
| `BluetoothAudioSource.cs:893-905` | the **art-only** preserve branch | ✅ (and §0.0c re-reads it) |
| `BackgroundIdentificationService.cs:260` | `if (!audioTap.NeedsFingerprintingLookup)` | ✅ |
| `FilePlayerAudioSource.cs:1988` | the gate | ✅ |
| `FilePlayerAudioSource.cs:2112` | the overwrite | ✅ |
| `FilePlayerAudioSource.cs:2126-2128` | the unconditional art replace | ✅ |
| `FilePlayerAudioSource.cs:2142-2149` | art fill, sentinel-guarded | ✅ |
| `FilePlayerAudioSource.cs:2161-2178` | Artist/Album/Title fills, sentinel-guarded | ✅ |
| `FilePlayerAudioSource.cs:2196-2204` | bookkeeping | ✅ |
| `SystemConfigPage.razor:718` | the Fingerprinting info alert | ✅ |
| `SystemConfigPage.razor:721-729` | the `4`/`8` checkbox row | ✅ |
| `design/appsettings.example.json:33` | `"UseShazamForAllSources": false,` | ✅ |
| `LinuxBluetoothService.cs:2761-2765` | the MPRIS-name art read (`AUD-17`) | ✅ |
| **`docs/BUILDER_QUEUE.md:34`** | the `AUD-1` index row is now at **`:33`** | ❌ **moved −1** |
| **`FilePlayerAudioSourceTests.cs`** | is at `tests/Radio.Infrastructure.Tests/Audio/**Sources/Primary/**FilePlayerAudioSourceTests.cs` | ❌ **path was wrong** in the previous revision, which wrote `tests/…/FilePlayerAudioSourceTests.cs` |
| `FilePlayerAudioSourceTests.cs:1709-1729` | `OnTrackIdentified_WhileThisSourceIsActive_UpdatesMetadata` | ✅ (closing brace `:1729`) |
| `FilePlayerAudioSourceTests.cs:1731-1735` / `:1736` | the doc comment / `CreateSourceForIdentification` | ✅ |
| `FilePlayerAudioSourceTests.cs:1753-1765` | `CreateTrackMetadata` | ✅ |
| `BluetoothAudioSourceTests.cs:850-895` | `TrackIdentified_WhileThisSourceIsActive_StillUpdatesMetadata` | ⚠ ends `:895`, not `:897` |

⚠ **`src/Radio.API/appsettings.Production.json` still does not exist.** The repo's Production overlay
for this box is `deploy/debian-x64/appsettings.Production.json:43`; the *deployed* file is
`/opt/radio-console/api/appsettings.Production.json`. §0.5 is why the distinction is load-bearing.

### 0.3 ⛔⛔ `C-241` — RETRACTED IN FULL. `AUD-17` disproved this section's own correction

**The previous revision of this plan claimed AVRCP had supplied album art 66 times between
2026-03-05 and 2026-07-19, and that something broke on ~2026-07-19. Both claims are false and were
withdrawn the same night in [`docs/queue/AUD-17.md`](../../docs/queue/AUD-17.md) and PR #610.** Read
that dossier before touching anything in this area; the short form:

- **`TrackMetadata.Source` records the TITLE's provenance, not the art's.**
  `BluetoothAudioSource.UpdateRecentPlayHistoryCoverArtAsync` (`:1070-1112`) writes `CoverArtUrl` via
  `Track with { CoverArtUrl = … }` and never touches `Source`, and its only live caller is the SongRec
  path. **26 of the 66 share a content-addressed filename with a `Shazam` row** — filenames are
  `ComputeHash(imageData)` (`AlbumArtCacheService.cs:46`/`:80`), so a shared name means byte-identical
  bytes. The previous revision inferred art provenance from a title column. That is the same class of
  error it was written to correct.
- **There was no cliff.** The monthly rate declines smoothly, 11.1% → 5.2% → 4.4% → 0/16 → 0/15, and
  0-of-16 against a 4.4% base rate is **p ≈ 0.49**. The 2026-07-19 boundary was chosen *post hoc* as
  the date of the last success and then tested for zeros after it — a construction that cannot fail.
- ⭐ **The real mechanism is better and is not a regression.** `LinuxBluetoothService.cs:2761-2765`
  reads the MPRIS names `ArtUrl` / `mpris:artUrl` from a proxy on **`org.bluez.MediaPlayer1`**, which
  publishes **`ImgHandle`**. Those reads always return empty, so `CacheAvrcpArtAsync` (`:926`) **has
  never executed**, and `file://` is zero across all 45,210 `TrackMetadata` rows. **AVRCP has never
  supplied album art on this box.** BIP is a second blocker behind our own attribute name, not the
  first one.

**What that means for this row, and it is all good news:**

- ✅ **The `AUD-1` conclusion is unchanged and now rests on a mechanism that is actually true.** Keep
  the always-fingerprint gate; never set `UseShazamForAllSources` false.
- ⭐ **`AUD-17` makes the owner's rule self-correcting on Bluetooth, with no special case.** Art is
  *always* missing there, so fingerprinting *always* fills it — which is exactly today's working
  ~99%. The per-field rule needs no Bluetooth-specific art exemption, and the fact that it needs none
  is a point in the rule's favour.
- ⛔ **Do NOT "correct" `docs/queue/AUD-1.md:26` or `docs/ROADMAP.md:133` the way the previous
  revision's Task 8 instructed.** Their *conclusion* is right. Only their stated *mechanism* — *"BlueZ
  5.72 ships no BIP"* — is incomplete. **Amend the mechanism; never reverse the conclusion.** Task 10
  gives the exact replacement wording.
- ⚠ **Delete the previous revision's `:882-885` argument.** It said the fix is "strictly better for
  the 66-row case" on a phone that supplies AVRCP art. There is no such case on this box. The art
  guard is still right — see test `B4` — but justify it as a general invariant, not as protecting 66
  observations that were never AVRCP art.

### 0.4 `C-242` — the `&& needsLookup` guard on FilePlayer is operationally vacuous

The row describes `FilePlayerAudioSource.cs:2112` as *"the overwrite, additionally guarded by
`needsLookup`"*, which reads as a narrowing. Trace it:

- `:1985-1987` — `hasIncompleteMetadata` = Artist **or** Album equals its default.
- `:1988` — `needsFingerprinting = hasIncompleteMetadata || FpOptions.UseShazamForAllSources`.
- `:1994` — if that is true, `_metadata["NeedsFingerprintingLookup"] = true`.
- `:2107` — `needsLookup` reads back that same key.

**So whenever the flag is on, `needsLookup` is true by construction.** The guard narrows nothing in
the configuration this row exists to fix. It matters only on the *second* identification of the same
track, because `:2131` resets the key to `false` after a successful replace. Do not treat it as a
safety property — but **do keep it**, because it is what stops SongRec re-applying every cycle. §2's
Task 5 preserves its position exactly.

### 0.5 ⭐⭐ `C-243` — THE CONSTRAINT THAT DECIDES THE DESIGN: the key cannot be renamed

Unchanged by the owner's decision, and now doing more work than before — see §1.1.

**Three facts, each verified:**

**(a) The deploy PRESERVES the Production overlay; it does not ship it.**
`deploy/Deploy-ToLinux.ps1:271` rsyncs with `--exclude='appsettings.Production.json'` for both `api/`
and `web/`, and the seed block at `:314-353` copies `deploy/<configDir>/appsettings.Production.json`
**only into a directory that does not already have one** (`:326-327`, *"present — left alone"*).
`deploy/deploy-to-pi.sh:152-213` implements the same policy — that symmetry is what `OPS-8` (#576)
and `OPS-7` (#570) landed. **Editing `deploy/debian-x64/appsettings.Production.json` therefore has
no effect on this box, ever.**

**(b) The deployed overlay has already diverged from the repo template**, which independently proves
(a) is in force:

| | repo `deploy/debian-x64/…` | live `/opt/radio-console/api/…` |
|---|---|---|
| `Devices.Radio.USBPort` | `""` | `"AB13X"` |
| `FilePlayer.AllowedBrowseDirectories` | present | **absent** |
| `Fingerprinting.UseShazamForAllSources` | `true` | `true` |

**(c) ⭐ And the SQLite config store outranks both JSON layers, and it holds the key today.**
`CLAUDE.md` § *Config Bridge* and `Program.cs` register the SQLite bridge **after** `appsettings.json`,
so store rows win. From `/opt/radio-console/data/config/configuration.db`:

```
fingerprinting:enabled|true
fingerprinting:sampleDurationSeconds|15
fingerprinting:identificationIntervalSeconds|30
fingerprinting:minimumConfidenceThreshold|0.65
fingerprinting:duplicateSuppressionMinutes|5
fingerprinting:fpcalcPath|
fingerprinting:databasePath|./data/fingerprints.db
fingerprinting:useShazamForAllSources|true
```

The operator has saved that tab — `minimumConfidenceThreshold` is `0.65`, not the `0.5` default, and
`identificationIntervalSeconds` is `30`, not `15`. **So the effective authority for the gate on this
box is the SQLite row, not either JSON file.**

⚠ **`fingerprinting:fpcalcPath` is an orphan and it is the proof this hazard is real.**
`FingerprintingOptions` has no `FpcalcPath` property — fpcalc/AcoustID was replaced by SongRec — yet
the row is still in the store because nothing cleaned it up. **This store has already accumulated one
dead key from exactly the kind of rename being contemplated.**

**Therefore:**

> ⛔ **Do NOT rename `UseShazamForAllSources`.** A rename orphans the SQLite row, leaves the new key
> absent from the store *and* from the live Production overlay, and lets it fall through to
> `appsettings.json`'s `false` — **turning the gate off and killing BT album art on the appliance**,
> which is the one outcome the row forbids. There is no code change that prevents this, because the
> value that would have to move lives in a database and a file the deploy is contractually forbidden
> to touch.

The greenfield "no backward compatibility" rule in project memory does not rescue a rename here. That
rule is about *code* shape; this is about **operator data on a live box that the deploy deliberately
will not overwrite**.

### 0.6 `C-244` — the FilePlayer evidence, measured rather than assumed. ⚠ one inference in it was too strong

The measurements below still stand and are still the reason FilePlayer's overwrite has to go. **One
of the previous revision's inferences from them does not, and it is corrected in (b).**

**(a) ID3 on this library is largely complete, and art is the norm.** `ffprobe` across
`/opt/radio-console/media/audio`:

| Directory | Files | Title | Artist | Album | **Embedded art** |
|---|---|---|---|---|---|
| music root | 24 | 21 | 23 | 20 | **19 (79%)** |
| `alarm/` | 12 | 12 | 2 | 1 | 0 |
| `notify/` | 12 | 0 | 0 | 0 | 0 |

The code already extracts that art — `ExtractEmbeddedAlbumArt` (`FilePlayerAudioSource.cs:2026`,
called at `:1946`) → `TryGetEmbeddedAlbumArtUrl` (`:2043-2082`, TagLib, prefers
`PictureType.FrontCover`) → the content-addressed album-art cache. Corroborated in the fingerprint DB:
`FileTag` rows are **146 of 162 with art**.

**(b) The overwrite has been measured, and it is destructive — ⚠ with one link in the chain that the
previous revision skipped.** `PlayHistory` since 2026-03-11 records **44 file plays with
`MetadataSource = Fingerprinting` against 8 with `FileTag`**, and those 44 rows carry SongRec's
titles:

- **Two outright fabrications** (2026-08-18): `Dont Spit Out Venom (Remix) — Deviana sharon S` and
  `Hold Me — Gadgets Inc`. Neither title matches any file on the box.
- **Systematic corruption of correct tags:** ID3 `Blink-182` → `blink-182`; AcoustID wrote
  `blink‐182` with a **U+2010 non-breaking hyphen**. `Here's To The Night` → `Here's to the Night`.
  `Boys Like Girls` → `BOYS LIKE GIRLS`.
- **A remix mismatch of exactly the `Enter Sandman (Remastered)` class:**
  `All the Small Things (Dimatik Un… — Dimatik`.
- ⭐ **Art churn.** For the *same song*, every `FileTag` row carries **one stable hash**
  (`/api/albumart/c8d4539b92c87615.jpg` — the content-addressed embedded APIC), while `Shazam` rows
  for that song carry **at least nine different hashes**. The embedded art is stable; SongRec's is not.

⚠ **`C-248` — what those 44 rows prove, stated precisely.** The previous revision wrote *"44 of 52
file plays had their metadata overwritten"*, reading `PlayHistoryTracker.cs:420`/`:567`/`:635` as a
record of the **source's** metadata being overwritten. It is not.
**`PlayHistoryTracker` subscribes to the identification service directly and never consults the
source's per-field decision** — `:563-573` and `:631-641` re-point the row at SongRec's `TrackMetadata`
and set `MetadataSource = Fingerprinting` **whenever an identification lands**, whichever branch
`FilePlayerAudioSource.OnTrackIdentified` took. So the honest claim is two claims:

1. **Proven directly by the 44 rows:** the **play-history record** — which the History panel renders —
   carries SongRec's titles rather than the files' own ID3 titles, fabrications included.
2. **Proven separately, by reading the code:** the **source's live metadata** was overwritten too,
   because `UseShazamForAllSources` is `true` on the box and `needsLookup` is therefore true by
   construction (§0.4), so `:2112` fires on the first identification of every track.

Both are true. The count alone establishes only the first. **This correction is not pedantry — it is
the reason §0.9's scope note exists**, and it is the same failure mode `AUD-17` was retracted for:
reading a column that records one thing as evidence for another.

**(c) Ten tracks no fingerprinting service can improve.** `Cary High Chorus` / `Fall Concert 2006`,
titles `Track 7`…`Track 16`, all with embedded art, Artist+Album complete and human-supplied. A
high-school chorus recording is unidentifiable by Shazam, and `hasIncompleteMetadata` is already false
for them — so today the *only* reason they get fingerprinted at all is the flag.

⚠ **The comment at `FilePlayerAudioSource.cs:2110-2111` asserts the opposite of all this** —
*"SongRec is more authoritative and has better cover art from Apple Music CDN"*. The 44-play record
contradicts both halves. Task 5 deletes it with the branch it describes. `CLAUDE.md` §
*Pre-Merge Review* makes an over-claiming comment a finding in its own right, and this row is already
carrying three (this one, `:800-801` in the BT tests, `:1731-1735` in the FilePlayer tests).

### 0.7 ⚠ `C-245` — the config drift that makes FilePlayer UAT vacuous unless it is staged

**Three layers disagree about where the file player's root is, and the winner is empty.**

| Layer | `FilePlayer:RootDirectory` |
|---|---|
| `src/Radio.API/appsettings.json:185` | `media/audio` |
| `deploy/debian-x64/appsettings.Production.json:30` | `/mnt/nas/music` |
| Live overlay on the box | `/mnt/nas/music` |
| **SQLite store (wins — §0.5c)** | **`/home/mmack/RTest/src/Radio.API/media/audio`** |

Verified live: `/mnt/nas` is **not a mountpoint** (matching the long-deferred *G.8 NAS mount* note in
project memory), the SQLite path exists but holds **0 files**, and
`curl http://localhost:5000/api/files` returns `{"currentPath":"/","items":[]}`.

- ✅ **The FilePlayer blast radius on this box is currently nil**, which lowers risk.
- ⛔ **But a "no regression" UAT result would be vacuous.** §4.5 says what to do instead.

⚠ **Do not fix the drift as part of this row.** It is a separate defect (an operator SQLite row
pointing at a Windows-style dev path on a Linux appliance) and fixing it changes what the file player
browses — a user-visible change with no diagnosis behind it. §6 proposes filing it.

### 0.8 ~~THE OWNER DECISION — what happens to `FilePlayerAudioSource`~~ — ✅ DISCHARGED

**Answered 2026-09-08. See §0.0.** The section that stood here — options F1/F2/F3 and a
recommendation — is preserved in **Appendix A** and must not be implemented. **Nothing in this plan
is now gated on an owner answer**; Builder may start at Task 1.

⚠ There is one decision left in the plan, and it is **not blocking**: §1.1 chooses to *delete* the
overwrite rather than keep it behind a new default-off flag. It is recommended, its reasoning is
given, and **Appendix B holds the exact deltas to reverse it** if the owner prefers the flag. Builder
implements §1.1's recommendation without waiting.

### 0.9 ⚠ `C-249` — what this row does NOT deliver, and it will be visible at UAT

**Changing the two sources does not make the History panel obey the owner's rule.** Per §0.6b,
`PlayHistoryTracker` re-points a play-history row at SongRec's `TrackMetadata` on every identification
without consulting the source's fields (`:563-573`, `:631-641`, `:659-672`). So after this row ships:

- The **now-playing display** obeys the rule — that is what `_metadata` / `MetadataInternal` drive.
- The **History panel** does not. A file play whose ID3 says *I'm Not in Love* will still be recorded
  as whatever SongRec guessed.

⛔ **Do not widen this row to fix it.** `PlayHistoryTracker` is a different file with a different
lifecycle, its own tests, and a defensible reason for its behaviour (the row is a *fingerprint
record*, and `TrackMetadataId` is a real foreign key into the identified track). Widening doubles the
blast radius on a row that is already not auto-mergeable. §6 proposes the follow-up.

⭐ **But say it out loud in the PR body and at UAT.** The failure mode this guards against is the
owner playing a tagged file after the merge, seeing the right title on the panel and the wrong one in
History, and reasonably concluding the fix did not work.

⚠ `PlayHistoryTracker.cs:373-386` is also **a third, independent implementation of "missing"** in this
codebase (`DefaultAlbum` → `null`, `DefaultAlbumArtUrl` → `null`, `IsNullOrWhiteSpace` fallbacks).
That is the strongest argument for §1.2's shared helper: the notion already exists three times and
disagrees with itself. **This row unifies two of the three; the tracker is the natural third customer
and is out of scope.**

### 0.10 What is decided and needs no further discussion

- The BT gate at `:837` **does not change**, and neither does `FilePlayerAudioSource.cs:1988`.
- The BT fork at `:867` is **deleted**; both arms are replaced by one per-field merge (§1.1).
- The FilePlayer fork at `:2112` is **deleted**, same way.
- **No rename.** §0.5.
- ⛔ **Do not "fix" this by setting `UseShazamForAllSources` false.** It kills BT album art.
- **`design/TESTING.md` and ADR-030 are not on `main`** — they arrive with `TEST-2`. If `TEST-2` lands
  first, its seam classification is binding on this row's tests. §0.13. **This plan adds no new
  production seam either way** — see the ⚠ under Task 6.

### 0.11 The estimate — **1.25 d**, up from 0.75 d

| Task | | Hours |
|---|---|---|
| 1 | `SourceMetadataPrecedence` — the shared per-field rule | 1.0 |
| 2 | Its unit tests in `Radio.Core.Tests` | 1.0 |
| 3 | `FingerprintingOptions` — doc rewrite (no new property) | 0.5 |
| 4 | `BluetoothAudioSource.OnTrackIdentified` — delete the fork, merge per field | 1.0 |
| 5 | `FilePlayerAudioSource.OnTrackIdentified` — the same | 1.0 |
| 6 | BT tests — 5 new, 1 rewritten, 1 comment corrected | 2.0 |
| 7 | FilePlayer tests — 3 new, 1 rewritten, 1 comment corrected | 2.0 |
| 8 | Config surface — help copy only | 0.5 |
| 9 | Migration verification on the box (read-only) | 0.5 |
| 10 | Docs: the `AUD-17` mechanism correction in 3 places | 0.75 |
| | Gates, self-review, PR | 1.0 |
| | **Total** | **≈ 11.25 h → 1.25 d** |

⚠ **Why it went up when the owner's decision was supposed to simplify things.** The decision *did*
simplify the question — one rule instead of a per-source argument — but it made the code change
larger, and pretending otherwise would put Builder half a day behind on day one:

- The previous 0.75 d assumed *"a one-line change, twice"* with the preserve branches reused
  unmodified. §0.0c shows that reuse does not work: the BT preserve branch fills art only, so
  Title/Artist/Album per-field fill is **new behaviour that has to be written and tested**, not
  reached.
- The rule is now one thing, so it must live in one place (§1.2). That is a new class in `Radio.Core`
  with its own tests — genuine work, and the cheapest place in the whole change to prove the
  empty-string rule.
- Against that, **Task 8 shrank from 1.0 h to 0.5 h** and the DTO edit vanished entirely, because
  §1.1 adds no config key.

### 0.12 ⛔ Auto-merge — **still NO**, on three grounds instead of four

Re-checked against the user's four-gate policy after the owner decision:

1. ⛔ **It touches the live audio path and user-visible metadata**, which the policy names explicitly
   as a reason to pause. `AUD-4` and `AUD-12` are marked not-auto-mergeable for the same reason.
2. ✅ ~~It carries an owner decision.~~ **Discharged 2026-09-08** (§0.0). This is no longer a reason.
3. ⛔ **UAT needs a phone and cannot be automated.** The BT half is only truly verified by playing a
   track with correct AVRCP metadata and watching the title survive an identification. §4.4 lists
   **three** separate live conditions that can make that UAT silently vacuous — `AUD-12`, `AUD-10`
   and, new since the previous revision, **`AUD-18`**.
4. ⛔ **The change is now behavioural rather than a condition swap.** It deletes two branches and adds
   a shared merge rule that runs on every identification on two live sources. The previous revision
   could argue it was reversible by a config flip; §1.1 deliberately removes that escape hatch, which
   is right for correctness and is a reason for a human to look at the diff.
5. ✅ Not sensitive in the auth/secrets/migration sense: no schema change, no credential path.

### 0.13 Collisions — one active Builder, three rows, and a checkout that moved under the planner

| Row / branch | Shared file | Verdict |
|---|---|---|
| **`TEST-2`** (`test/bt-capture-branch-dispatch-coverage`, **in flight now**) | `BluetoothAudioSource.cs`, `BluetoothAudioSourceTests.cs` | ⚠ **No textual collision; real anchor drift.** §0.14 |
| **`AUD-12`** (`design/plans/AUD-12-…md`) | `BluetoothAudioSource.cs` | ✅ No textual overlap — it works at `:454-460` and `:1125-1151`. **Semantically adjacent:** it fixes the `Ready` stall that gates fingerprinting off entirely, so `AUD-12` first makes `AUD-1`'s UAT *possible*. Prefer `AUD-12` first if both are queued; neither blocks the other. |
| **`AUD-13`** | `SystemConfigPage.razor` | ⚠ **Anchor collision only.** It edits the Devices tab at `:283-284`; this plan edits the Fingerprinting tab at `:718`. Its edit is *above* mine, so if `AUD-13` lands first the alert anchor shifts. Re-derive, do not trust the number. |
| **`AUD-17`** | `LinuxBluetoothService.cs` | ✅ No file overlap with this row. **But its findings are load-bearing here** — §0.3. If `AUD-17` ships the "delete the dead path" option, nothing in this plan changes: art stays always-missing on BT and the rule still fills it. |
| **`AUD-18`** | — | ✅ No file overlap. **Affects UAT only** — §4.4. |

⚠⚠ **`C-250` — the checkout is not a reliable source of line numbers right now, and this cost real
time.** While this amendment was being written the working tree flipped between `main` and
`test/bt-capture-branch-dispatch-coverage` **three times**, and `TEST-2`'s WIP head moved from
`0dd65f0e` to `4a8c7b8a`. A `git diff --stat main...HEAD` taken minutes apart returned a full
ten-file listing and then nothing at all. **Every anchor in this plan was therefore taken from
`git show main:<path>`, or from a file proven byte-identical to `main` by
`git diff --stat main -- <path>` returning empty.** Builder should do the same until `TEST-2` merges,
and should re-derive rather than trusting any number here if the two rows overlap in time.

Files this mattered for — on `TEST-2`'s branch but **not** on `main`: `design/TESTING.md`,
`design/DECISION-LOG.md` (ADR-030), `tests/Radio.Core.Tests/TestSeamLabelLintTests.cs`,
`tests/Radio.Infrastructure.Tests/Audio/WasapiLoopbackTests.cs`, and modifications to
`BluetoothAudioSource.cs`, `BluetoothAudioSourceTests.cs`, `GoogleCastOutput.cs`, `CLAUDE.md`.

### 0.14 `TEST-2` — no textual collision; **+3 lines of drift** if it lands first

`TEST-2` modifies `BluetoothAudioSource.cs` in exactly one place — `:445-460`, narrowing
`ApplyDeferredCaptureState` from `internal` to `private` and rewriting its doc comment. Measured on
its branch, the hunk is **net +3 lines**, all of them above `:460`.

> ⚠ **So if `TEST-2` merges first, every anchor this plan cites in `BluetoothAudioSource.cs` below
> `:460` moves +3**: the gate `:837 → :840`, `OnTrackIdentified` `:845 → :848`, the fork
> `:867 → :870`, the overwrite `:867-891 → :870-894`, the preserve branch `:893-905 → :896-908`.
> **Re-derive; do not apply the shift arithmetically without checking.** The hunk may change before it
> merges — it is WIP.

**`TEST-2` also rewrites `BluetoothAudioSourceTests.cs` substantially (+337 lines on its branch).**
Prefer landing `AUD-1` first if the choice is free, because this plan's test edits are keyed to two
named methods and `TEST-2`'s are spread through the file. If `TEST-2` lands first, find
`TrackIdentified_WhileThisSourceIsActive_StillUpdatesMetadata` and
`TrackIdentified_WhileDifferentSourceIsActive_DoesNotOverwriteAvrcpMetadata` **by name**, not at
`:798`/`:850`.

⛔ **Neither plan may edit the other's region.** `TEST-2` owns `:445-460` in the source and the
deferred-capture tests; this row owns `OnTrackIdentified` and the two cross-source-contamination
tests.

### 0.15 `C-NNN` numbering

This plan owns **`C-240`…`C-245`** from its first revision and **`C-246`…`C-251`** from this
amendment. `C-241` is retracted in place rather than renumbered (§0.3) so that anything citing it
lands on the retraction. If a concurrent plan also claims `C-246+`, leave both and note the collision;
do not renumber a merged plan.

---

## 1. Decision

### 1.1 Delete the overwrite. Do not add a second flag. ⭐ `Recommended:`

The previous revision proposed `ShazamOverwritesSourceMetadata`, default `false`, so the old
behaviour stayed reachable. **The owner's decision makes that flag a switch whose only setting is
"wrong", and this plan drops it.**

| | **O1 — delete the overwrite (recommended)** | **O2 — keep it behind a new default-off flag** |
|---|---|---|
| `FingerprintingOptions` | doc rewrite only | + `ShazamOverwritesSourceMetadata` |
| `appsettings.json`, `appsettings.example.json` | untouched | + one key each |
| `ApiModels.cs` DTO | untouched | + one property |
| `SystemConfigPage.razor` | help copy only | + a third checkbox, and a `4`/`8` → `4`/`4`/`4` regrid |
| SQLite config store | **one key, unchanged** | **a second fingerprinting key an operator can set** |
| Rollback if UAT fails | `git revert` | a config flip |

**Three reasons O1 wins, in order of weight:**

1. ⭐ **The whole hazard analysis in §0.5 is about operator config outliving code.** The SQLite store
   outranks both JSON layers, the deploy is forbidden to touch the live overlay, and the store already
   carries one orphan (`fingerprinting:fpcalcPath`). **Adding a second fingerprinting boolean to that
   store — in a tab the operator has demonstrably saved at least once — creates a new way for this
   exact defect to come back silently, months from now, with no code change to point at.** A row that
   exists because one config key did too much should not answer with two.
2. **The owner stated a rule, not a preference.** *"When metadata is available from the source, use
   the source metadata"* has no configurable half. A flag would encode a question the owner has
   closed.
3. **It is less work and less surface** — no DTO, no JSON, no third checkbox, no regrid, and no new
   `IOptionsMonitor` read on the hot path.

**What O1 costs, stated honestly:** if UAT finds the per-field rule wrong in some case nobody
anticipated, the recovery is a revert rather than a checkbox. Given that this row is **not
auto-mergeable** and the owner runs UAT before merge, that is an acceptable trade — but it is a real
one, and it is the reason O1 is *recommended* rather than *decided*.

**Appendix B holds the exact deltas for O2** — five edits, ~1.5 h — if the owner prefers the flag.
**Builder implements O1 without waiting for an answer.**

⚠ **`UseShazamForAllSources` keeps its name, its meaning and its value.** Under O1 it is no longer
overloaded at all: it is the gate, only the gate, and the SQLite row that reads `true` continues to
mean exactly what it means today. **The migration is a genuine no-op with zero new config keys** —
strictly safer than the previous revision's, which added one.

### 1.2 One rule, one place: `SourceMetadataPrecedence`

The brief's instruction was *"make the per-field decision one place, not four"*, and the codebase
argues the same way: "missing" is already implemented **three times, inconsistently** — sentinel-based
in `FilePlayerAudioSource` (`:2144`, `:2161`, `:2166`), empty-string-based nowhere despite §0.0b
requiring it on BT, and a fourth mixed form in `PlayHistoryTracker.cs:373-386`.

**The decision goes in a static helper in `Radio.Core`; the *action* stays in each source.** That
split is deliberate and is the answer to "why not a base-class method":

- ⛔ **A protected base method will not work.** `BluetoothAudioSource` reaches metadata through
  `USBAudioSourceBase.MetadataInternal` (`:98`); `FilePlayerAudioSource` has its own
  `private readonly Dictionary<string, object> _metadata` (`:38`). Their nearest common ancestor is
  `PrimaryAudioSourceBase`, which owns **neither** dictionary. A static helper taking
  `IDictionary<string, object>` is the only shape both can call.
- ✅ **`Radio.Core` is the right home.** It has no project references, `StandardMetadataKeys` and
  `TrackMetadata` already live there, and the result is testable in `Radio.Core.Tests` with **no
  fixture, no mock and no timing** — which is where the empty-string rule earns its coverage cheaply.
- ⚠ **Cover art is decided by the helper but applied by the source, because the two sources apply it
  differently.** Bluetooth must download the URL into the local cache first
  (`CacheAndSetCoverArtAsync`, `:907`), because a raw remote URL is not what the UI can render;
  FilePlayer assigns `track.CoverArtUrl` directly. Forcing both through one method would mean either
  an `async` helper in `Radio.Core` or an injected cache service — both worse than exposing
  `ShouldFillAlbumArt(...)` as a boolean and letting each source do its own thing with the answer.
  **The decision is still in one place; only the write is not.**

### 1.3 The migration is a no-op, and that is the point

| Layer | Holds today | After this change | Effect |
|---|---|---|---|
| SQLite store (**wins**) | `useShazamForAllSources = true` | unchanged | Gate stays **on** → **BT album art preserved** ✅ |
| Live Production overlay | `UseShazamForAllSources: true` | unchanged (deploy excludes it) | consistent |
| `appsettings.json` | `UseShazamForAllSources: false` | unchanged | consistent |
| The overwrite | reachable, and firing | **deleted from the code** | AVRCP title preserved ✅ |

**Nothing has to be edited on the box, and no key is added anywhere.** Task 9 verifies that prediction
rather than performing a migration.

---

## 2. Tasks

### Task 1 — `SourceMetadataPrecedence`: the shared rule

**New file:** `src/Radio.Core/Models/Audio/SourceMetadataPrecedence.cs`.

```csharp
namespace Radio.Core.Models.Audio;

/// <summary>
/// The single definition of "the audio source did not supply this field", and the only
/// place a fingerprint result is merged into source metadata.
/// </summary>
/// <remarks>
/// <para>
/// <b>The rule (AUD-1, owner decision 2026-09-08):</b> when metadata is available from the
/// audio source, the source's value wins; where it is missing, fingerprinting fills the gap.
/// <b>Precedence is per FIELD, not per track</b> — a track whose AVRCP supplies title and
/// artist but no cover art keeps both and takes only art from the identification.
/// </para>
/// <para>
/// ⚠ <b>An empty string counts as MISSING, not as an authoritative blank.</b> That is not an
/// edge case: <c>BluetoothPlaybackMetadata.Title/Artist/Album</c> all default to
/// <c>string.Empty</c> (<c>IBluetoothService.cs:23-25</c>) and
/// <c>BluetoothAudioSource.OnMetadataChanged</c> writes them through unguarded
/// (<c>:770-772</c>), so a phone that reports no album leaves <c>""</c> in the dictionary
/// rather than removing the key. A predicate that only tested <c>ContainsKey</c> or the
/// sentinel would do nothing at all on Bluetooth.
/// </para>
/// <para>
/// ⚠ <b>Placeholder sentinels count as missing too.</b> <c>StandardMetadataKeys.DefaultArtist</c>
/// and <c>DefaultAlbum</c> are both the literal <c>"--"</c>, and <c>DefaultAlbumArtUrl</c> is the
/// fallback image path. They exist to mean "nothing here", and on <c>FilePlayerAudioSource</c>
/// they are the <i>only</i> form missing takes, because its tag reads are already guarded with
/// <c>!string.IsNullOrEmpty</c> (<c>:1951</c>, <c>:1955</c>, <c>:1959</c>).
/// </para>
/// <para>
/// This is a static helper rather than a base-class method because the two callers do not share
/// a metadata field: <c>BluetoothAudioSource</c> uses <c>USBAudioSourceBase.MetadataInternal</c>
/// and <c>FilePlayerAudioSource</c> owns a private dictionary. Their common ancestor
/// (<c>PrimaryAudioSourceBase</c>) has neither.
/// </para>
/// </remarks>
public static class SourceMetadataPrecedence
{
  /// <summary>
  /// True when <paramref name="value"/> carries nothing the source actually supplied:
  /// null, a non-string that stringifies to nothing, an empty or whitespace-only string,
  /// or any of <paramref name="placeholders"/>.
  /// </summary>
  public static bool IsMissing(object? value, params string[] placeholders)
  {
    if (value is null)
    {
      return true;
    }

    var text = value as string ?? value.ToString();
    if (string.IsNullOrWhiteSpace(text))
    {
      return true;
    }

    foreach (var placeholder in placeholders)
    {
      // Ordinal: these are sentinels compared against themselves, never user text.
      if (string.Equals(text, placeholder, StringComparison.Ordinal))
      {
        return true;
      }
    }

    return false;
  }

  /// <summary>
  /// True when <paramref name="metadata"/> has no usable value under <paramref name="key"/>.
  /// An absent key is missing; so is a present-but-empty one.
  /// </summary>
  /// <remarks>
  /// Deliberately NOT an overload of <see cref="IsMissing(object?, string[])"/>: a dictionary
  /// is an <c>object</c>, so an overload pair would let <c>IsMissing(metadata, "Album")</c>
  /// bind to the value form with "Album" as a placeholder and silently answer the wrong
  /// question.
  /// </remarks>
  public static bool IsFieldMissing(
    IReadOnlyDictionary<string, object> metadata,
    string key,
    params string[] placeholders)
  {
    return !metadata.TryGetValue(key, out var value) || IsMissing(value, placeholders);
  }

  /// <summary>
  /// Writes <paramref name="candidate"/> into <paramref name="key"/> only if the field is
  /// missing and the candidate is not itself empty. Returns whether it wrote.
  /// </summary>
  public static bool TryFill(
    IDictionary<string, object> metadata,
    string key,
    string? candidate,
    params string[] placeholders)
  {
    if (string.IsNullOrWhiteSpace(candidate))
    {
      return false;
    }

    if (metadata.TryGetValue(key, out var existing) && !IsMissing(existing, placeholders))
    {
      return false;
    }

    metadata[key] = candidate;
    return true;
  }

  /// <summary>
  /// Applies the per-field rule to Title, Artist and Album. Cover art is deliberately not
  /// handled here — see <see cref="ShouldFillAlbumArt"/>.
  /// </summary>
  /// <param name="metadata">The source's own metadata, mutated in place.</param>
  /// <param name="identified">The fingerprint result.</param>
  /// <param name="extraTitlePlaceholder">
  /// An additional value that means "no title" for this source. <c>FilePlayerAudioSource</c>
  /// passes the filename without extension, because <c>UpdateMetadataFromFile</c> seeds Title
  /// with it (<c>:1903</c>) and replaces it only when a Title tag exists (<c>:1951-1954</c>),
  /// so "title equals filename" means "no title tag" — the assumption the pre-AUD-1 code at
  /// <c>:2172-2178</c> already made. Bluetooth passes nothing.
  /// </param>
  public static MetadataFillResult FillMissingFrom(
    IDictionary<string, object> metadata,
    TrackMetadata identified,
    string? extraTitlePlaceholder = null)
  {
    var titlePlaceholders = string.IsNullOrWhiteSpace(extraTitlePlaceholder)
      ? new[] { StandardMetadataKeys.DefaultTitle }
      : new[] { StandardMetadataKeys.DefaultTitle, extraTitlePlaceholder };

    return new MetadataFillResult(
      Title: TryFill(metadata, StandardMetadataKeys.Title, identified.Title, titlePlaceholders),
      Artist: TryFill(metadata, StandardMetadataKeys.Artist, identified.Artist, StandardMetadataKeys.DefaultArtist),
      Album: TryFill(metadata, StandardMetadataKeys.Album, identified.Album, StandardMetadataKeys.DefaultAlbum));
  }

  /// <summary>
  /// The cover-art half of the same rule. Returns the DECISION only; the caller performs the
  /// write, because Bluetooth must first download the URL into the local album-art cache
  /// while FilePlayer can assign it directly.
  /// </summary>
  public static bool ShouldFillAlbumArt(IReadOnlyDictionary<string, object> metadata)
  {
    return IsFieldMissing(
      metadata,
      StandardMetadataKeys.AlbumArtUrl,
      StandardMetadataKeys.DefaultAlbumArtUrl);
  }
}

/// <summary>Which fields <see cref="SourceMetadataPrecedence.FillMissingFrom"/> actually wrote.</summary>
public readonly record struct MetadataFillResult(bool Title, bool Artist, bool Album)
{
  /// <summary>True when at least one text field was filled.</summary>
  public bool Any => Title || Artist || Album;

  /// <summary>
  /// A comma-separated list of what was filled, for logging. Returns "nothing" when the
  /// source had supplied every field — which is the expected, quiet case.
  /// </summary>
  public string Describe(bool filledAlbumArt = false)
  {
    var parts = new List<string>(4);
    if (Title) { parts.Add("title"); }
    if (Artist) { parts.Add("artist"); }
    if (Album) { parts.Add("album"); }
    if (filledAlbumArt) { parts.Add("cover art"); }
    return parts.Count == 0 ? "nothing" : string.Join(", ", parts);
  }
}
```

⚠ **Builder: check `Radio.Core`'s `GlobalUsings`/`ImplicitUsings` before assuming `List<T>` and
`StringComparison` resolve without a `using System;` / `using System.Collections.Generic;`.** Cheap to
check, fatal to guess.

### Task 2 — the helper's tests

**New file:** `tests/Radio.Core.Tests/Models/Audio/SourceMetadataPrecedenceTests.cs` (create the
directory if `tests/Radio.Core.Tests/Models/` does not exist — the project currently has
`Configuration/`, `Interfaces/` and `Utilities/` subdirectories, so a `Models/Audio/` one matches the
convention).

⚠ **Be honest about what "fails first" means here.** These test a class that does not exist yet, so
their red state is a compile error — trivially red, and **not** evidence about the defect. The
meaningful red→green evidence is in Tasks 6 and 7, against unmodified production code. Write these
first anyway: they are where the empty-string rule is pinned cheaply and deterministically, and where
a future field gets added without a source-level fixture.

```csharp
using Radio.Core.Models.Audio;
using Xunit;

namespace Radio.Core.Tests.Models.Audio;

/// <summary>
/// AUD-1. The owner's rule: source metadata wins where it exists; fingerprinting fills only
/// what is missing, decided per field. These tests are the definition of "missing".
/// </summary>
public class SourceMetadataPrecedenceTests
{
  [Theory]
  [InlineData(null)]
  [InlineData("")]
  [InlineData("   ")]
  [InlineData("\t")]
  public void IsMissing_NullEmptyOrWhitespace_IsMissing(string? value)
  {
    // ⭐ The empty string is the owner's named edge, and on Bluetooth it is the COMMON case:
    // BluetoothPlaybackMetadata.Album defaults to string.Empty and is written through
    // unguarded, so "" is what "the phone reported no album" looks like in the dictionary.
    Assert.True(SourceMetadataPrecedence.IsMissing(value));
  }

  [Fact]
  public void IsMissing_PlaceholderSentinel_IsMissing()
  {
    Assert.True(SourceMetadataPrecedence.IsMissing("--", StandardMetadataKeys.DefaultArtist));
    Assert.True(SourceMetadataPrecedence.IsMissing(
      StandardMetadataKeys.DefaultAlbumArtUrl, StandardMetadataKeys.DefaultAlbumArtUrl));
  }

  [Fact]
  public void IsMissing_RealValue_IsNotMissing()
  {
    Assert.False(SourceMetadataPrecedence.IsMissing("Metallica", StandardMetadataKeys.DefaultArtist));
  }

  [Fact]
  public void IsMissing_ValueThatMerelyContainsThePlaceholder_IsNotMissing()
  {
    // Ordinal equality, not Contains — "--" inside a real title must not read as missing.
    Assert.False(SourceMetadataPrecedence.IsMissing("Blink-182 -- Greatest Hits", "--"));
  }

  [Fact]
  public void IsFieldMissing_AbsentKey_IsMissing()
  {
    var metadata = new Dictionary<string, object>();
    Assert.True(SourceMetadataPrecedence.IsFieldMissing(metadata, StandardMetadataKeys.AlbumArtUrl));
  }

  [Fact]
  public void TryFill_DoesNotOverwriteAValueTheSourceSupplied()
  {
    var metadata = new Dictionary<string, object>
    {
      [StandardMetadataKeys.Title] = "Enter Sandman (Remastered)"
    };

    var wrote = SourceMetadataPrecedence.TryFill(
      metadata, StandardMetadataKeys.Title, "Enter Sandman");

    Assert.False(wrote);
    Assert.Equal("Enter Sandman (Remastered)", metadata[StandardMetadataKeys.Title]);
  }

  [Fact]
  public void TryFill_FillsAnEmptyString()
  {
    var metadata = new Dictionary<string, object> { [StandardMetadataKeys.Album] = "" };

    var wrote = SourceMetadataPrecedence.TryFill(
      metadata, StandardMetadataKeys.Album, "Metallica", StandardMetadataKeys.DefaultAlbum);

    Assert.True(wrote);
    Assert.Equal("Metallica", metadata[StandardMetadataKeys.Album]);
  }

  [Fact]
  public void TryFill_IgnoresAnEmptyCandidate()
  {
    // A fingerprint result with no album must not turn "" into "" and claim it filled.
    var metadata = new Dictionary<string, object> { [StandardMetadataKeys.Album] = "" };

    Assert.False(SourceMetadataPrecedence.TryFill(metadata, StandardMetadataKeys.Album, ""));
    Assert.False(SourceMetadataPrecedence.TryFill(metadata, StandardMetadataKeys.Album, null));
  }

  [Fact]
  public void FillMissingFrom_DecidesEachFieldIndependently()
  {
    // ⭐ The core of the rule: title supplied, artist supplied, album empty.
    // Only the album may change.
    var metadata = new Dictionary<string, object>
    {
      [StandardMetadataKeys.Title] = "Enter Sandman (Remastered)",
      [StandardMetadataKeys.Artist] = "Metallica",
      [StandardMetadataKeys.Album] = ""
    };

    var filled = SourceMetadataPrecedence.FillMissingFrom(metadata, Track("Enter Sandman", "Metallica", "Metallica"));

    Assert.False(filled.Title);
    Assert.False(filled.Artist);
    Assert.True(filled.Album);
    Assert.Equal("Enter Sandman (Remastered)", metadata[StandardMetadataKeys.Title]);
    Assert.Equal("Metallica", metadata[StandardMetadataKeys.Album]);
    Assert.Equal("album", filled.Describe());
  }

  [Fact]
  public void FillMissingFrom_TreatsTheFilenamePlaceholderAsNoTitle()
  {
    // FilePlayerAudioSource seeds Title with the filename and replaces it only when the file
    // carries a Title tag, so "title == filename" means "no title tag".
    var metadata = new Dictionary<string, object>
    {
      [StandardMetadataKeys.Title] = "08-track",
      [StandardMetadataKeys.Artist] = StandardMetadataKeys.DefaultArtist,
      [StandardMetadataKeys.Album] = StandardMetadataKeys.DefaultAlbum
    };

    var filled = SourceMetadataPrecedence.FillMissingFrom(
      metadata, Track("I'm Not in Love", "10cc", "Very Best Of"), extraTitlePlaceholder: "08-track");

    Assert.True(filled.Title);
    Assert.True(filled.Artist);
    Assert.True(filled.Album);
    Assert.Equal("I'm Not in Love", metadata[StandardMetadataKeys.Title]);
    Assert.Equal("title, artist, album", filled.Describe());
  }

  [Fact]
  public void FillMissingFrom_WithEverythingSupplied_WritesNothing()
  {
    var metadata = new Dictionary<string, object>
    {
      [StandardMetadataKeys.Title] = "I'm Not in Love",
      [StandardMetadataKeys.Artist] = "10cc",
      [StandardMetadataKeys.Album] = "Very Best Of"
    };

    var filled = SourceMetadataPrecedence.FillMissingFrom(metadata, Track("Wrong", "Wrong", "Wrong"));

    Assert.False(filled.Any);
    Assert.Equal("nothing", filled.Describe());
    Assert.Equal("I'm Not in Love", metadata[StandardMetadataKeys.Title]);
    Assert.Equal("10cc", metadata[StandardMetadataKeys.Artist]);
    Assert.Equal("Very Best Of", metadata[StandardMetadataKeys.Album]);
  }

  [Fact]
  public void ShouldFillAlbumArt_SentinelOrAbsent_True_CachedPath_False()
  {
    Assert.True(SourceMetadataPrecedence.ShouldFillAlbumArt(new Dictionary<string, object>()));
    Assert.True(SourceMetadataPrecedence.ShouldFillAlbumArt(new Dictionary<string, object>
    {
      [StandardMetadataKeys.AlbumArtUrl] = StandardMetadataKeys.DefaultAlbumArtUrl
    }));
    Assert.True(SourceMetadataPrecedence.ShouldFillAlbumArt(new Dictionary<string, object>
    {
      [StandardMetadataKeys.AlbumArtUrl] = ""
    }));
    Assert.False(SourceMetadataPrecedence.ShouldFillAlbumArt(new Dictionary<string, object>
    {
      [StandardMetadataKeys.AlbumArtUrl] = "/api/albumart/c8d4539b92c87615.jpg"
    }));
  }

  private static TrackMetadata Track(string title, string artist, string? album = null) => new()
  {
    Id = Guid.NewGuid().ToString(),
    Title = title,
    Artist = artist,
    Album = album,
    Source = MetadataSource.Shazam,
    CreatedAt = DateTime.UtcNow,
    UpdatedAt = DateTime.UtcNow
  };
}
```

### Task 3 — `FingerprintingOptions`: say what the flag does, and what it does not

**File:** `src/Radio.Fingerprinting/FingerprintingOptions.cs`. Replace `:14-19`. **No new property**
(§1.1) — this is a doc change, and it is the doc that stops the next person re-conflating the two
decisions.

```csharp
  /// <summary>
  /// When true, runs SongRec on ALL sources even when AVRCP or ID3 already supply a
  /// title and artist. This is a <b>gate</b>: it decides whether fingerprinting runs at
  /// all. It does <b>not</b> decide what is done with the answer.
  /// </summary>
  /// <remarks>
  /// <para>
  /// ⚠ <b>Since AUD-1 there is no "overwrite" decision to pair this with.</b> A fingerprint
  /// result may only fill fields the source left missing — see
  /// <see cref="Radio.Core.Models.Audio.SourceMetadataPrecedence"/>, which is where that rule
  /// lives for every source. Turning this flag off does not stop metadata being replaced,
  /// because nothing replaces it any more; all it does is stop fingerprinting.
  /// </para>
  /// <para>
  /// ⚠ <b>Do not set this false.</b> It makes <c>BackgroundIdentificationService</c>
  /// hard-return for any track whose source already supplied a title and artist, so nothing
  /// is fingerprinted and no cover art is ever found. On the appliance that removes Bluetooth
  /// album art entirely: AVRCP has <b>never</b> supplied cover art there, because
  /// <c>LinuxBluetoothService</c> reads the MPRIS attribute names <c>ArtUrl</c>/<c>mpris:artUrl</c>
  /// from a proxy on <c>org.bluez.MediaPlayer1</c>, which publishes <c>ImgHandle</c> (see
  /// <c>AUD-17</c>). SongRec is the only art source Bluetooth has.
  /// </para>
  /// <para>
  /// ⚠ <b>The name is imprecise and is kept deliberately.</b> It reads as "use Shazam's
  /// answer", but it only decides whether Shazam is <i>asked</i>. Renaming it would orphan
  /// the <c>fingerprinting:useShazamForAllSources</c> row in the operator's SQLite config
  /// store — which outranks both JSON layers — and the live
  /// <c>appsettings.Production.json</c>, which the deploy is contractually forbidden to
  /// overwrite (<c>Deploy-ToLinux.ps1:271</c>, <c>:314-353</c>). The renamed key would be
  /// absent from every layer, fall through to the <c>false</c> default, and take album art
  /// with it. <c>fingerprinting:fpcalcPath</c> is already orphaned in that store from the
  /// AcoustID→SongRec rename, so this is observed, not hypothetical. See <c>AUD-1</c>.
  /// </para>
  /// </remarks>
  public bool UseShazamForAllSources { get; set; } = false;
```

### Task 4 — `BluetoothAudioSource`: delete the fork, merge per field

**File:** `src/Radio.Infrastructure/Audio/Sources/Primary/BluetoothAudioSource.cs`.
⚠ **Re-derive these anchors** — `TEST-2` shifts them +3 if it lands first (§0.14).

**4a — replace `:865-905` in full** (the fork, both arms) with:

```csharp
    // AUD-1: per-FIELD precedence, decided by the owner 2026-09-08. The source's own
    // metadata wins for every field it actually supplied; a SongRec result may only fill
    // fields the source left missing, and each field is decided on its own.
    //
    // This deliberately does NOT branch on UseShazamForAllSources. That flag is the gate at
    // :837 — whether SongRec runs at all — and conflating the two is the defect this row
    // exists to remove. There is no longer a configuration in which an identification
    // replaces AVRCP metadata.
    //
    // "Missing" includes the EMPTY STRING, which is the common case here rather than an
    // edge: BluetoothPlaybackMetadata.Title/Artist/Album all default to string.Empty
    // (IBluetoothService.cs:23-25) and OnMetadataChanged writes them through unguarded at
    // :770-772, so a phone that reports no album leaves "" under the key.
    var filled = SourceMetadataPrecedence.FillMissingFrom(MetadataInternal, e.Track);

    // Cover art follows the same rule but is applied here rather than in the helper,
    // because a remote URL has to be downloaded into the local album-art cache before the
    // browser can fetch it. On this appliance art is missing on EVERY track — AVRCP has
    // never supplied any (AUD-17) — so in practice this fires every time, which is exactly
    // the ~99% SongRec-sourced art the gate exists to preserve.
    var fillingArt = SourceMetadataPrecedence.ShouldFillAlbumArt(MetadataInternal)
      && !string.IsNullOrEmpty(e.Track.CoverArtUrl)
      && _serviceScopeFactory != null;

    if (fillingArt)
    {
      _ = CacheAndSetCoverArtAsync(e.Track.CoverArtUrl!, e.Track.Title, e.Track.Artist);
    }

    if (filled.Any || fillingArt)
    {
      Logger.LogInformation(
        "Fingerprinting filled missing BT metadata from '{Title}' by '{Artist}': {Fields}",
        e.Track.Title, e.Track.Artist, filled.Describe(fillingArt));
    }
```

⚠ **The `"Shazam metadata replaced AVRCP for BT"` log message is deleted with the branch.** It is the
string §4.4's UAT greps for today, and the string project memory records seeing on 2026-09-06. The
replacement line above is the new grep target; §4.4 uses it.

**4b — replace the comment at `:829-835`**, which currently justifies the gate by the overwrite:

```csharp
    // If metadata is incomplete (no title or artist), request fingerprinting.
    // When UseShazamForAllSources is enabled, always fingerprint — on this box SongRec is
    // the only source of cover art there is, because AVRCP has never supplied any: the
    // BlueZ MediaPlayer1 interface publishes ImgHandle and we read the MPRIS names
    // ArtUrl/mpris:artUrl, so CacheAvrcpArtAsync has never executed (AUD-17).
    //
    // What is done with the ANSWER is a separate decision and is not made here or by this
    // flag: since AUD-1 an identification may only fill fields the source left missing.
    // See SourceMetadataPrecedence.
```

**4c — add the using** if `Radio.Core.Models.Audio` is not already imported in this file (it is —
`StandardMetadataKeys` is in use — but check rather than assume; the project may reach it via
`GlobalUsings`).

⛔ **Do not touch `:837`** (the gate) **or `:770-772`** (the AVRCP write). §5's follow-up list explains
why `:770-772` is tempting and out of scope.

### Task 5 — `FilePlayerAudioSource`: the same rule, the same way

**File:** `src/Radio.Infrastructure/Audio/Sources/Primary/FilePlayerAudioSource.cs`.

**5a — delete `:2110-2140` entirely** (the comment and the whole overwrite branch, through its
`return;`). Nothing replaces it in that position.

⚠ **Note what goes with it:** `:2134` is the only production writer of `_metadata["MetadataSource"] =
"Shazam"`. Verified: **nothing in `src/` reads that key** — the only other writers are `:2200` and
`SDRRadioAudioSource.cs:737`, both `"Fingerprinting"`, and the only readers are two test assertions.
So this is a rename of a value nothing consumes, not a behaviour change. Say so in the PR body so a
reviewer does not go looking for a consumer.

**5b — replace `:2142-2149`** (the art fill) with the shared decision, keeping its position **before**
the `if (!needsLookup) return;` at `:2151`:

```csharp
    // AUD-1: cover art, by the same per-field rule as everything else. Kept above the
    // needsLookup return, as it was before this row: a file whose tags are complete but which
    // carried no embedded art still gets art from the first identification.
    //
    // ExtractEmbeddedAlbumArt (:1946) has already put a cache-relative /api/albumart/<hash>
    // path here when the file had an APIC frame; absent that, AlbumArtUrl still holds
    // DefaultAlbumArtUrl from :1906, which the shared rule treats as missing.
    //
    // ⚠ The line this replaces assigned track.CoverArtUrl UNCONDITIONALLY. Measured before
    // this change: for one song the embedded APIC produced a single stable content hash while
    // SongRec produced at least nine different ones for the same track.
    var filledArt = false;
    if (SourceMetadataPrecedence.ShouldFillAlbumArt(_metadata)
        && !string.IsNullOrEmpty(track.CoverArtUrl))
    {
      _metadata[StandardMetadataKeys.AlbumArtUrl] = track.CoverArtUrl;
      filledArt = true;
      Logger.LogInformation("Album art URL set for '{Title}': {Url}", track.Title, track.CoverArtUrl);
    }
```

**5c — replace `:2156-2178`** (the log line and the three hand-rolled per-field fills) with:

```csharp
    // AUD-1: the same per-field rule the Bluetooth source uses, from the same helper.
    // ID3 tags are source metadata and win where they exist. Measured before this change:
    // 44 of 52 file plays in PlayHistory carried SongRec titles rather than their own,
    // including two outright fabrications, and "Blink-182" rewritten with a U+2010
    // non-breaking hyphen.
    //
    // The filename is passed as an extra "no title" placeholder because
    // UpdateMetadataFromFile seeds Title with Path.GetFileNameWithoutExtension (:1903) and
    // replaces it only when the file carries a Title tag (:1951-1954) — so "title equals
    // filename" means "no title tag". That is the same assumption the code this replaces
    // made; it now lives in one place instead of two.
    var filled = SourceMetadataPrecedence.FillMissingFrom(
      _metadata,
      track,
      Path.GetFileNameWithoutExtension(_currentFile ?? string.Empty));

    if (filled.Any || filledArt)
    {
      Logger.LogInformation(
        "Fingerprinting filled missing file metadata from '{Title}' by '{Artist}': {Fields}",
        track.Title, track.Artist, filled.Describe(filledArt));
    }
```

⛔ **Leave `:2180-2194` (Genre/Year/TrackNumber) and `:2196-2204` (the bookkeeping) exactly as they
are.** `:2197`'s `NeedsFingerprintingLookup = false` is what stops SongRec re-applying every cycle,
and the optional-field fills already implement the same "only if absent" idea by hand — folding them
into the helper is a tidier diff and a wider blast radius, and this row does not need it.

⛔ **Leave `:1988` (the gate) alone.**

**5d — correct the constructor param doc at `:74`:**

```csharp
  /// <param name="fingerprintingOptions">Optional fingerprinting options. Controls the
  /// UseShazamForAllSources gate only — what is done with a fingerprint ANSWER is decided
  /// per field by SourceMetadataPrecedence and is not configurable (AUD-1).</param>
```

**5e — correct the method doc at `:2086-2089`**, which says *"Updates metadata with identified track
information when file tags are incomplete"* — true of the tag fields, but it now needs to say art is
filled independently of `needsLookup`:

```csharp
  /// <summary>
  /// Handles the TrackIdentified event from the fingerprinting service.
  /// Fills metadata fields the file's own tags left missing, deciding each field
  /// independently (AUD-1). Cover art is filled whenever it is absent, including for a file
  /// whose tags are complete; the remaining fields are filled once per track, gated by
  /// NeedsFingerprintingLookup.
  /// </summary>
```

### Task 6 — Bluetooth tests

**File:** `tests/Radio.Infrastructure.Tests/Audio/BluetoothAudioSourceTests.cs`.
⚠ **Find the two existing tests by NAME, not by line** — `TEST-2` rewrites this file (§0.14).

⚠ **This row adds NO new production seam.** The previous revision's `SetMetadataForTest` helper does
not exist and must not be invented: `MockBluetoothService.SimulateMetadataChange(title, artist,
albumArtUrl = null)` plus an `IAlbumArtCacheService` mock is the fixture's existing idiom for
establishing art, used by `MetadataChanged_WithHttpsArtUrl_StoresCachedRelativeUrl`. If `TEST-2` has
landed, ADR-030 / `design/TESTING.md` make an unlabelled new seam a first-class review finding.

**6a — write these five FIRST, before Tasks 1–5, and record which ones actually go red.** They compile
against today's tree because they touch only existing API.

```csharp
  // -----------------------------------------------------------------------
  // AUD-1: per-FIELD precedence. The source's own metadata wins for every field it
  // supplied; a SongRec result may only fill what is missing, decided field by field.
  // "Missing" includes the empty string — which on Bluetooth is the ordinary way a
  // phone reports "no album", because BluetoothPlaybackMetadata defaults those
  // properties to string.Empty and OnMetadataChanged writes them through unguarded.
  //
  // RED/GREEN status against pre-AUD-1 main is stated on each test. Three of the five
  // fail today; two are regression guards that pass both before and after, and say so.
  // -----------------------------------------------------------------------

  private BluetoothAudioSource BuildActiveSourceWithGate(
    bool gateOn,
    out BackgroundIdentificationService identificationService,
    IAlbumArtCacheService? albumArtCache = null)
  {
    var fpMonitor = new Mock<IOptionsMonitor<FingerprintingOptions>>();
    fpMonitor.Setup(o => o.CurrentValue).Returns(new FingerprintingOptions
    {
      UseShazamForAllSources = gateOn
    });

    identificationService = BuildIdentificationServiceForTests();
    BluetoothAudioSource? active = null;
    active = new BluetoothAudioSource(
      _loggerMock.Object,
      _deviceManagerMock.Object,
      _mockBluetooth,
      _options,
      identificationService: identificationService,
      metricsCollector: _metricsMock.Object,
      fingerprintingOptions: fpMonitor.Object,
      serviceScopeFactory: BuildScopeFactory(),
      albumArtCache: albumArtCache,
      getActiveSource: () => active);
    return active;
  }

  /// <summary>B1 — RED on pre-AUD-1 main: :867 takes the overwrite branch and replaces both.</summary>
  [Fact]
  public async Task TrackIdentified_WithGateOn_DoesNotReplaceAvrcpTitleAndArtist()
  {
    // The live 2026-09-06 defect: SongRec misidentified from residual radio audio seconds
    // after a BT reconnect and replaced the phone's correct metadata.
    await _source.DisposeAsync();
    _source = BuildActiveSourceWithGate(gateOn: true, out var identificationService);

    _mockBluetooth.SimulateMetadataChange("Enter Sandman (Remastered)", "Metallica");

    identificationService.RaiseTrackIdentifiedForTesting(new TrackIdentifiedEventArgs(
      new TrackMetadata
      {
        Id = Guid.NewGuid().ToString(),
        Title = "Spirit In The Sky",
        Artist = "Norman Greenbaum",
        Album = "Spirit In The Sky",
        Source = MetadataSource.Shazam,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
      },
      confidence: 0.95));
    await Task.Delay(100);

    Assert.Equal("Enter Sandman (Remastered)", _source.Metadata[StandardMetadataKeys.Title]);
    Assert.Equal("Metallica", _source.Metadata[StandardMetadataKeys.Artist]);
  }

  /// <summary>
  /// B2 — RED on pre-AUD-1 main, and it is the per-field test: the album SHOULD be filled
  /// and the title SHOULD NOT be touched. Today the overwrite branch does both, so the
  /// title assertion fails while the album assertion passes.
  /// </summary>
  [Fact]
  public async Task TrackIdentified_WithEmptyAvrcpAlbum_FillsAlbumAndKeepsTitleAndArtist()
  {
    await _source.DisposeAsync();
    _source = BuildActiveSourceWithGate(gateOn: true, out var identificationService);

    // SimulateMetadataChange supplies no album, so BluetoothPlaybackMetadata.Album is
    // string.Empty and OnMetadataChanged (:772) writes "" under the key. That is the
    // "empty string means missing" case, and it is what most phones actually do.
    _mockBluetooth.SimulateMetadataChange("Enter Sandman (Remastered)", "Metallica");
    Assert.Equal("", _source.Metadata[StandardMetadataKeys.Album]);

    identificationService.RaiseTrackIdentifiedForTesting(new TrackIdentifiedEventArgs(
      new TrackMetadata
      {
        Id = Guid.NewGuid().ToString(),
        Title = "Enter Sandman",
        Artist = "Metallica",
        Album = "Metallica",
        Source = MetadataSource.Shazam,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
      },
      confidence: 0.95));
    await Task.Delay(100);

    Assert.Equal("Metallica", _source.Metadata[StandardMetadataKeys.Album]);          // filled
    Assert.Equal("Enter Sandman (Remastered)", _source.Metadata[StandardMetadataKeys.Title]);  // kept
    Assert.Equal("Metallica", _source.Metadata[StandardMetadataKeys.Artist]);         // kept
  }

  /// <summary>
  /// ⭐ B3 — RED on pre-AUD-1 main, and it guards the regression an earlier revision of the
  /// AUD-1 plan would have shipped. With the gate OFF the pre-AUD-1 code reaches the
  /// preserve branch at :893-905, which fills COVER ART ONLY and never touches the title —
  /// so a phone that reports no AVRCP title gets no title at all. Under the owner's rule
  /// the title is missing and must be filled.
  /// </summary>
  [Fact]
  public async Task TrackIdentified_WithEmptyAvrcpTitle_FillsTitleFromFingerprinting()
  {
    await _source.DisposeAsync();
    _source = BuildActiveSourceWithGate(gateOn: false, out var identificationService);

    // An empty title is a tested, expected AVRCP state — see
    // MetadataChanged_WithEmptyTitle_SetsNeedsFingerprintingLookup.
    _mockBluetooth.SimulateMetadataChange("", "Some Artist");
    Assert.Equal("", _source.Metadata[StandardMetadataKeys.Title]);

    identificationService.RaiseTrackIdentifiedForTesting(new TrackIdentifiedEventArgs(
      new TrackMetadata
      {
        Id = Guid.NewGuid().ToString(),
        Title = "Enter Sandman",
        Artist = "Metallica",
        Source = MetadataSource.Shazam,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
      },
      confidence: 0.95));
    await Task.Delay(100);

    Assert.Equal("Enter Sandman", _source.Metadata[StandardMetadataKeys.Title]);  // filled
    Assert.Equal("Some Artist", _source.Metadata[StandardMetadataKeys.Artist]);   // kept
  }

  /// <summary>
  /// B4 — RED on pre-AUD-1 main: the overwrite branch caches SongRec art unconditionally
  /// at :882-885, replacing art already in place.
  /// ⚠ Honest scope: AVRCP has never supplied art on the appliance (AUD-17), so the live
  /// route into this state is the resolved-art cache restoring art on a metadata refresh or
  /// a repeated track (:807-813), not a phone. It is a real invariant either way, and the
  /// https path this test uses is real for MPRIS-exposing local-music players.
  /// </summary>
  [Fact]
  public async Task TrackIdentified_WithArtAlreadyInPlace_DoesNotReplaceIt()
  {
    var cacheMock = new Mock<IAlbumArtCacheService>();
    cacheMock.Setup(c => c.SaveFromUrlAsync("https://example.com/existing.jpg"))
             .ReturnsAsync("/api/albumart/existing.jpg");
    cacheMock.Setup(c => c.SaveFromUrlAsync("https://itunes.apple.com/songrec.jpg"))
             .ReturnsAsync("/api/albumart/songrec.jpg");

    await _source.DisposeAsync();
    _source = BuildActiveSourceWithGate(gateOn: true, out var identificationService, cacheMock.Object);

    _mockBluetooth.SimulateMetadataChange(
      "Enter Sandman (Remastered)", "Metallica", albumArtUrl: "https://example.com/existing.jpg");
    await Task.Delay(200);  // let the fire-and-forget CacheAvrcpArtAsync complete
    Assert.Equal("/api/albumart/existing.jpg", _source.Metadata[StandardMetadataKeys.AlbumArtUrl]);

    identificationService.RaiseTrackIdentifiedForTesting(new TrackIdentifiedEventArgs(
      new TrackMetadata
      {
        Id = Guid.NewGuid().ToString(),
        Title = "Enter Sandman",
        Artist = "Metallica",
        CoverArtUrl = "https://itunes.apple.com/songrec.jpg",
        Source = MetadataSource.Shazam,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
      },
      confidence: 0.95));
    await Task.Delay(200);

    Assert.Equal("/api/albumart/existing.jpg", _source.Metadata[StandardMetadataKeys.AlbumArtUrl]);
    cacheMock.Verify(c => c.SaveFromUrlAsync("https://itunes.apple.com/songrec.jpg"), Times.Never);
  }

  /// <summary>
  /// B5 — GREEN both before and after. This is the half that must NOT regress: with no art
  /// in place, SongRec's art is still fetched and cached. Stated as a regression guard
  /// rather than as red→green evidence, because it passes on pre-AUD-1 main too.
  /// </summary>
  [Fact]
  public async Task TrackIdentified_WithNoArtInPlace_StillCachesFingerprintArt()
  {
    var cacheMock = new Mock<IAlbumArtCacheService>();
    cacheMock.Setup(c => c.SaveFromUrlAsync("https://itunes.apple.com/songrec.jpg"))
             .ReturnsAsync("/api/albumart/songrec.jpg");

    await _source.DisposeAsync();
    _source = BuildActiveSourceWithGate(gateOn: true, out var identificationService, cacheMock.Object);

    _mockBluetooth.SimulateMetadataChange("Enter Sandman (Remastered)", "Metallica");
    Assert.False(_source.Metadata.ContainsKey(StandardMetadataKeys.AlbumArtUrl));

    identificationService.RaiseTrackIdentifiedForTesting(new TrackIdentifiedEventArgs(
      new TrackMetadata
      {
        Id = Guid.NewGuid().ToString(),
        Title = "Enter Sandman",
        Artist = "Metallica",
        CoverArtUrl = "https://itunes.apple.com/songrec.jpg",
        Source = MetadataSource.Shazam,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
      },
      confidence: 0.95));
    await Task.Delay(200);

    Assert.Equal("/api/albumart/songrec.jpg", _source.Metadata[StandardMetadataKeys.AlbumArtUrl]);
    Assert.Equal("Enter Sandman (Remastered)", _source.Metadata[StandardMetadataKeys.Title]);
  }
```

⚠ **Builder must verify four mechanical details before assuming this compiles** — all cheap, all fatal
if wrong: (i) `BuildIdentificationServiceForTests()`'s return type, and that
`RaiseTrackIdentifiedForTesting` is on it — it is used at `:457`, `:499`, `:558`, `:602`, `:649`,
`:699`, `:757`, `:831`, `:879` on `main`; (ii) `BuildScopeFactory()` (`:329`) and the
`albumArtCache:` / `serviceScopeFactory:` constructor parameter names; (iii) that `TrackMetadata`'s
`CoverArtUrl` is settable via `init` in an object initialiser — it is (`TrackMetadata.cs:48`);
(iv) whether `_source` is reassigned in this fixture's other tests the same way (it is —
`await _source.DisposeAsync();` then reassign).

**6b — rewrite `TrackIdentified_WhileThisSourceIsActive_StillUpdatesMetadata`** (`:850-895` on `main`).
It asserts the overwrite (`"Shazam Title"`, `"Shazam Artist"`, `"Shazam Album"`) and will fail after
Task 4 — **correctly**. ⛔ **Do not delete it:** its real subject is the active-source guard, which
still works and is `TEST-2`-adjacent. Replace its comment at `:852-853` and its assertion block at
`:892-894`:

```csharp
    // The guard must not break the normal path: when BT *is* the active source the
    // identification is still processed — but since AUD-1 "processed" means missing fields
    // are filled and supplied ones are kept, not that AVRCP metadata is replaced.
```

```csharp
    // Assert — AVRCP title/artist survive; the empty album is filled; the handler ran to
    // completion rather than being short-circuited by the active-source guard.
    Assert.Equal("Enter Sandman", _source.Metadata[StandardMetadataKeys.Title]);
    Assert.Equal("Metallica", _source.Metadata[StandardMetadataKeys.Artist]);
    Assert.Equal("Shazam Album", _source.Metadata[StandardMetadataKeys.Album]);
    Assert.False(_source.NeedsFingerprintingLookup);
```

The `NeedsFingerprintingLookup` assertion is what keeps the test meaningful: it proves
`OnTrackIdentified` reached `:857-863` rather than returning at the guard.

**6c — correct one false comment.**
`TrackIdentified_WhileDifferentSourceIsActive_DoesNotOverwriteAvrcpMetadata` (`:797-848` on `main`)
**passes unchanged** — it returns at `:850-853` before reaching any of this. Do not modify the test.
But its comment at `:800-801` says *"SongRec metadata unconditionally replaces AVRCP metadata in the
handler"*, which after Task 4 is false:

```csharp
    // Arrange — UseShazamForAllSources ON is the exact production configuration. What the
    // guard is protecting is that a NON-active source adopts nothing at all: it returns
    // before the per-field merge, so even a field it is missing stays missing.
```

**6d — the two gate tests at `:260-312` pass unchanged.** They assert `NeedsFingerprintingLookup`
only, which nothing in this row touches. **Their survival is itself the evidence that the gate was
left alone — say so in the PR body.**

### Task 7 — FilePlayer tests

**File:** `tests/Radio.Infrastructure.Tests/Audio/**Sources/Primary/**FilePlayerAudioSourceTests.cs`.
⚠ The previous revision cited this path without the `Sources/Primary/` segments.

**7a — extend `CreateTrackMetadata` (`:1753-1765`)** so an art-precedence test can exist at all. It
does not set `CoverArtUrl` today; an optional parameter leaves all existing callers untouched:

```csharp
  private static TrackMetadata CreateTrackMetadata(
    string title, string artist, string? album = "Some Album", string? coverArtUrl = null)
  {
    return new TrackMetadata
    {
      Id = Guid.NewGuid().ToString(),
      Title = title,
      Artist = artist,
      Album = album,
      CoverArtUrl = coverArtUrl,
      Source = MetadataSource.Shazam,
      CreatedAt = DateTime.UtcNow,
      UpdatedAt = DateTime.UtcNow
    };
  }
```

**7b — rewrite `OnTrackIdentified_WhileThisSourceIsActive_UpdatesMetadata` (`:1709-1729`).** It asserts
the overwrite, including `Assert.Equal("Shazam", active.Metadata["MetadataSource"])`, so it fails after
Task 5 — correctly. Rewrite the arrange so the tags are **defaults** (the case the fill path is for)
and assert `MetadataSource == "Fingerprinting"` (`FilePlayerAudioSource.cs:2200`):

```csharp
  [Fact]
  public void OnTrackIdentified_WhileThisSourceIsActive_FillsMissingTags()
  {
    // Arrange — the file player is both Playing and the active source, and its tags are the
    // defaults UpdateMetadataFromFile seeds when a file carries none.
    FilePlayerAudioSource? active = null;
    active = CreateSourceForIdentification(getActiveSource: () => active);
    SetState(active, AudioSourceState.Playing);

    var metadata = GetMetadataDictionary(active);
    metadata[StandardMetadataKeys.Title] = "Local File Title";
    metadata[StandardMetadataKeys.Artist] = StandardMetadataKeys.DefaultArtist;   // "--"
    metadata[StandardMetadataKeys.Album] = StandardMetadataKeys.DefaultAlbum;     // "--"
    metadata["NeedsFingerprintingLookup"] = true;

    // Act
    InvokeOnTrackIdentified(active, CreateTrackMetadata("Shazam Song", "Shazam Artist"), 0.95);

    // Assert — the two placeholder fields were filled; the real title was NOT replaced.
    Assert.Equal("Shazam Artist", active.Metadata[StandardMetadataKeys.Artist]);
    Assert.Equal("Some Album", active.Metadata[StandardMetadataKeys.Album]);
    Assert.Equal("Local File Title", active.Metadata[StandardMetadataKeys.Title]);
    Assert.Equal("Fingerprinting", active.Metadata["MetadataSource"]);
  }
```

⚠ Its current comment at `:1725` reads *"Shazam metadata replaced the incomplete ID3 tags, as
before"*, but the fixture seeds `_metadata` with **populated** values — **nothing in the test
established that the tags were incomplete.** The rewrite makes that claim true for the first time.

**7c — add the per-field test. RED on pre-AUD-1 main** (the overwrite branch replaces the title):

```csharp
  /// <summary>
  /// AUD-1. RED on pre-AUD-1 main: :2112 fires (needsLookup is true by construction whenever
  /// UseShazamForAllSources is on) and replaces every field, so the title assertion fails.
  /// The library on the appliance is curated — ffprobe over /opt/radio-console/media/audio
  /// returns real artist/album/title and embedded cover art on 19 of 24 music files — and
  /// PlayHistory records 44 of 52 file plays carrying SongRec titles instead, two of them
  /// outright fabrications.
  /// </summary>
  [Fact]
  public void OnTrackIdentified_FillsAnEmptyAlbumWithoutTouchingPopulatedTags()
  {
    FilePlayerAudioSource? active = null;
    active = CreateSourceForIdentification(getActiveSource: () => active);
    SetState(active, AudioSourceState.Playing);

    var metadata = GetMetadataDictionary(active);
    metadata[StandardMetadataKeys.Title] = "I'm Not in Love";
    metadata[StandardMetadataKeys.Artist] = "10cc";
    metadata[StandardMetadataKeys.Album] = "";              // empty string == missing
    metadata["NeedsFingerprintingLookup"] = true;

    InvokeOnTrackIdentified(
      active, CreateTrackMetadata("Wrong Song", "Wrong Artist", album: "The Very Best of 10cc"), 0.95);

    Assert.Equal("The Very Best of 10cc", active.Metadata[StandardMetadataKeys.Album]);  // filled
    Assert.Equal("I'm Not in Love", active.Metadata[StandardMetadataKeys.Title]);        // kept
    Assert.Equal("10cc", active.Metadata[StandardMetadataKeys.Artist]);                  // kept
  }
```

**7d — ⭐ add the art-precedence test that has never existed. RED on pre-AUD-1 main.**
`FilePlayerAudioSource.cs:2126-2128` — the line that replaces embedded cover art with SongRec's — is
**entirely uncovered today**: the five album-art tests around `:893-1011` cover the *queue* paths, not
`OnTrackIdentified`.

```csharp
  /// <summary>
  /// AUD-1. RED on pre-AUD-1 main: :2126-2128 assigns track.CoverArtUrl unconditionally.
  /// The embedded APIC art is content-addressed and therefore stable — one hash per song —
  /// while SongRec produced at least nine different hashes for the same track.
  /// </summary>
  [Fact]
  public void OnTrackIdentified_DoesNotReplaceEmbeddedAlbumArt()
  {
    FilePlayerAudioSource? active = null;
    active = CreateSourceForIdentification(getActiveSource: () => active);
    SetState(active, AudioSourceState.Playing);

    var metadata = GetMetadataDictionary(active);
    metadata[StandardMetadataKeys.Title] = "I'm Not in Love";
    metadata[StandardMetadataKeys.Artist] = "10cc";
    metadata[StandardMetadataKeys.Album] = "The Very Best of 10cc";
    // What ExtractEmbeddedAlbumArt (:1946) leaves behind for a file with an APIC frame.
    metadata[StandardMetadataKeys.AlbumArtUrl] = "/api/albumart/c8d4539b92c87615.jpg";
    metadata["NeedsFingerprintingLookup"] = true;

    InvokeOnTrackIdentified(
      active,
      CreateTrackMetadata("Wrong Song", "Wrong Artist", coverArtUrl: "https://itunes.apple.com/wrong.jpg"),
      0.95);

    Assert.Equal("/api/albumart/c8d4539b92c87615.jpg", active.Metadata[StandardMetadataKeys.AlbumArtUrl]);
  }

  /// <summary>
  /// The half that must NOT regress. GREEN both before and after — stated as a regression
  /// guard, not as red→green evidence.
  /// </summary>
  [Fact]
  public void OnTrackIdentified_WithNoEmbeddedArt_StillTakesFingerprintArt()
  {
    FilePlayerAudioSource? active = null;
    active = CreateSourceForIdentification(getActiveSource: () => active);
    SetState(active, AudioSourceState.Playing);

    var metadata = GetMetadataDictionary(active);
    metadata[StandardMetadataKeys.Title] = "I'm Not in Love";
    metadata[StandardMetadataKeys.Artist] = "10cc";
    metadata[StandardMetadataKeys.Album] = "The Very Best of 10cc";
    metadata[StandardMetadataKeys.AlbumArtUrl] = StandardMetadataKeys.DefaultAlbumArtUrl;
    metadata["NeedsFingerprintingLookup"] = true;

    InvokeOnTrackIdentified(
      active,
      CreateTrackMetadata("Wrong Song", "Wrong Artist", coverArtUrl: "https://itunes.apple.com/art.jpg"),
      0.95);

    Assert.Equal("https://itunes.apple.com/art.jpg", active.Metadata[StandardMetadataKeys.AlbumArtUrl]);
  }
```

**7e — correct the `CreateSourceForIdentification` doc comment (`:1731-1735`)**, which says the path
*"unconditionally replaces file tags"*. After Task 5 that is false:

```csharp
  /// <summary>
  /// Builds a source with UseShazamForAllSources enabled — the appliance's production
  /// configuration — so the identification path is reached. Since AUD-1 that path fills only
  /// the fields the file's tags left missing; it no longer replaces tags it finds.
  /// </summary>
```

⚠ `CLAUDE.md` § *Pre-Merge Review* makes a comment that outlives its code a first-class finding, and
this row is carrying **four**: `FilePlayerAudioSource.cs:2110-2111`, the BT test comment at
`:800-801`, this one, and `AUD-17`'s `LinuxBluetoothService.cs:2761` (out of scope here, but do not
"fix" it in passing).

### Task 8 — the config surface (help copy only)

Under §1.1 there is **no new key, no DTO change and no new control**. What is left is one alert whose
wording describes the old conflation.

⚠ **Re-derive the line first** — `AUD-13` edits `SystemConfigPage.razor` above this point (§0.13).

**`src/Radio.Web/Components/Pages/SystemConfigPage.razor`, replace the alert body at `:718`:**

```razor
                  Fingerprinting uses SongRec (Shazam) for audio recognition. "Use Shazam for All Sources" decides whether SongRec runs even when Bluetooth AVRCP or file ID3 tags already supply a title and artist — on this system it is the only source of Bluetooth cover art, so turning it off removes Bluetooth album art entirely. A recognition result never replaces information the phone or the file already provided; it only fills in what is missing, field by field.
```

**And the help text under the checkbox at `:729`:**

```razor
                <span style="font-size:0.75rem; color:var(--text-low); margin-left:32px; display:block">Run SongRec even when Bluetooth AVRCP or file ID3 tags already supply a title and artist. Results only fill in missing fields — they never replace a title, artist, album or cover art the source already reported. Turning this off removes Bluetooth album art entirely.</span>
```

⚠ **Leave `src/Radio.API/appsettings.json`, `design/appsettings.example.json`,
`src/Radio.Web/Models/ApiModels.cs`, `tools/Radio.Tools.AudioUAT/appsettings.json` and
`tests/Radio.IntegrationTests/appsettings.IntegrationTests.json` alone.** None needs a change under
O1. **If the owner picks O2, Appendix B is where those edits live.**

### Task 9 — verify the migration prediction on the box (read-only)

§1.3 predicts that nothing needs migrating and that no key is added. **Verify it; do not assume it.**
After deploying:

```bash
# 1. The gate is still on, from the store, and no new fingerprinting key appeared.
ssh mmack@radio "sqlite3 -readonly /opt/radio-console/data/config/configuration.db \
  \"SELECT Key, Value FROM Config_sqlite WHERE Key LIKE 'fingerprinting%';\""
#    expect: fingerprinting:useShazamForAllSources|true   (unchanged)
#    expect: EXACTLY the eight rows listed in §0.5c — no new row

# 2. The overlay was left alone by the deploy.
ssh mmack@radio "grep -A2 Fingerprinting /opt/radio-console/api/appsettings.Production.json"
#    expect: "UseShazamForAllSources": true   (unchanged)
```

⚠ **Under O1 the correct result is "nothing changed anywhere".** That is not a failed deploy — it is
the design. Confirm the binaries actually landed with the SHA endpoints instead:

```bash
curl -s http://radio:5000/api/health/version   # API — gitSha, assemblyName "Radio.API"
curl -s http://radio:5002/api/health/version   # Web — gitSha, assemblyName "Radio.Web"
```

⚠ **Do not write to either config file.** Both are operator-owned; `OPS-8` exists because a deploy
wrote to one of them. Use `sqlite3 -readonly`, per `AUD-17`'s precedent.

### Task 10 — correct the `AUD-17` mechanism where it is recorded

⛔ **Read `docs/queue/AUD-17.md` before writing a word of this.** Its retraction is explicit:
*"do NOT 'correct' `docs/queue/AUD-1.md:26` or `docs/ROADMAP.md:133` as the original row instructed…
their conclusion is right and only their mechanism is incomplete. Amend the mechanism; do not reverse
the conclusion."* The previous revision of this plan's Task 8 instructed exactly the reversal that
`AUD-17` withdrew. **It is replaced by the following.**

**10a — `docs/ROADMAP.md:133`.** Replace the parenthetical
*"(BlueZ 5.72 ships no BIP/cover-art implementation; 7 days of data show 0 AVRCP-sourced art against
2,560 SongRec-sourced)"* with:

> (⚠ **the mechanism was stated wrongly and is corrected here; the conclusion is unchanged.** AVRCP
> has **never** supplied album art on this box, and BIP is not the first reason:
> `LinuxBluetoothService.cs:2761-2765` reads the MPRIS names `ArtUrl`/`mpris:artUrl` from a proxy on
> `org.bluez.MediaPlayer1`, which publishes `ImgHandle`, so `CacheAvrcpArtAsync` has never executed.
> `file://` is zero across all 45,210 `TrackMetadata` rows. BlueZ 5.72 shipping no BIP client is a
> **second** blocker behind that one. See `AUD-17`.)

**10b — `docs/queue/AUD-1.md:26`.** The same correction in the row's own words, and **delete the
"0 AVRCP-sourced art against 2,560 SongRec-sourced" framing** — not because the counts were wrong but
because `Source` is the title's provenance, so the comparison is not measuring what the sentence
claims (`AUD-17` § *`Source` records the TITLE's provenance*).

**10c — `design/AUDIO-DATAFLOW.md:278`.** It reads *"AVRCP metadata used: BT metadata sets
`NeedsFingerprintingLookup` based on completeness + `UseShazamForAllSources` setting"*. Add a sibling
bullet:

> 6. **Source metadata has precedence, per field**: an identification fills only the fields the
>    source left missing — title, artist, album and cover art are decided independently, and an empty
>    string from the source counts as missing. One rule, in
>    `Radio.Core.Models.Audio.SourceMetadataPrecedence`, used by both `BluetoothAudioSource` and
>    `FilePlayerAudioSource` (`AUD-1`).

**10d — `design/FUTURE-WORK.md`.** Record §0.9: the History panel does **not** obey the rule after this
row, because `PlayHistoryTracker` re-points rows at the SongRec `TrackMetadata` without consulting the
source's per-field decision. **This is the row's most likely "it didn't work" report and it must be
written down before the PR, not after the question is asked.**

⚠ **Do not edit `docs/BUILDER_QUEUE.md` or `docs/queue/*.md` while another agent has them open.** §6
gives the index-row text as a separate, later step.

---

## 3. Ordering

1. ✅ **The owner decision is already answered** (§0.0). Nothing is gated.
2. **Task 6a and Task 7c/7d** — write the source-level tests and **run them against unmodified
   `main`**. Record which go red and which do not; §4.1 lists the expected split. This is the only
   moment the red state is observable without contriving it.
3. **Task 1** (the helper), then **Task 2** (its tests). Task 2 goes green here; Task 6/7 do not yet.
4. **Task 4** (Bluetooth). Tests `B1`–`B3` go green.
5. **Task 6b/6c** — rewrite the one BT test the change correctly breaks, and fix its neighbour's
   comment.
6. **Task 5** (FilePlayer). Tests `7c`/`7d` go green.
7. **Task 7a/7b/7e** — the helper extension, the rewritten test, the corrected doc comment.
8. **Task 3** and **Task 8** (docs on the option, help copy). Independent; parallelisable.
9. **Task 9** (box verification) — after deploy, before the PR is marked ready.
10. **Task 10** (docs) last, so the corrected text cites what actually shipped.

---

## 4. Test plan

### 4.1 `T1` — the red-first evidence, and an honest account of which tests have it

⚠ **The previous revision claimed "red → green with no test edit in between, which is the strongest
form available here" for all three of its tests. That is achievable for some of this row's tests and
not others, and the difference is worth stating rather than blurring.**

| Test | Against unmodified `main` | Why |
|---|---|---|
| `B1` title/artist not replaced | ⭐ **RED** | `:867` takes the overwrite branch |
| `B2` empty album filled, title kept | ⭐ **RED** | overwrite replaces the title too; the album half already passes |
| `B3` empty title filled with gate off | ⭐ **RED** | preserve branch `:893-905` fills art only |
| `B4` existing art not replaced | ⭐ **RED** | overwrite caches SongRec art at `:882-885` |
| `B5` art still filled when absent | 🟢 GREEN | regression guard, both before and after |
| `7b` rewritten active-source test | ⭐ RED after Task 5, by design | it asserts the overwrite |
| `7c` empty album filled, tags kept | ⭐ **RED** | `:2112` replaces every field |
| `7d` embedded art not replaced | ⭐ **RED** | `:2126-2128` assigns unconditionally |
| `7d`-pair art still taken when absent | 🟢 GREEN | regression guard |
| Task 2 helper tests | ⚪ compile error | the class does not exist yet; trivially red, **not evidence** |

**Six tests genuinely fail against unmodified production code.** All six compile against `main`,
because none of them references anything this row adds — they set only `UseShazamForAllSources`, which
exists today. **Record the actual failure output in the PR body**, expecting on `main`:
`Assert.Equal() Failure: Expected: Enter Sandman (Remastered)  Actual: Spirit In The Sky`.

⭐ **`B3` is the one to read carefully at review.** It is red for the opposite reason to all the
others: not because today's code does too much, but because it does too little. It is the guard on
§0.0c.

### 4.2 `T2` — the limits of what a unit test proves about art

`B4` and `B5` assert against a mocked `IAlbumArtCacheService`, so they prove the **decision** and the
**call**, not that an image was fetched. `CacheAndSetCoverArtAsync` is fire-and-forget (`:901-904`) and
the `await Task.Delay(200)` is a bounded negative window, not a rendezvous — per `CLAUDE.md`
§ *Test Timing*, that shape is acceptable **only** because starvation can weaken these assertions, not
flip them. **Say so in the test comments rather than implying end-to-end art coverage.** True
end-to-end art verification is UAT (§4.4).

### 4.3 `T3` — the suite

`dotnet test RadioConsole.sln -c Release > /tmp/test.log 2>&1; echo "exit=$?"` — **never piped to
`tail`**, per `CLAUDE.md`. Read the per-project summary lines. Known-failing on Windows and **not**
regressions: four `SrcVariableResamplerTests` (`libsamplerate.so.0`), `NwsObservationIntegrationTests.RealNwsCall_*`,
and `CoverArtPipelineIntegrationTests.CoverArtArchive_ReturnsValidUrl_ForKnownRecording`.
Release build gate: **equality with the 47-warning baseline**, not zero. Build `main` too if the count
looks off.

⚠ **This row adds a test project directory (`tests/Radio.Core.Tests/Models/Audio/`).** Confirm
`Radio.Core.Tests.csproj` globs `**/*.cs` rather than listing files — it does by SDK default, but a
missing test is indistinguishable from a passing one.

### 4.4 `T4` — UAT, and it is the awkward part

⛔ **Cannot be automated and cannot be skipped.** The defect is user-visible metadata.

1. Deploy; confirm both SHAs (`curl -s http://radio:5000/api/health/version`, and `:5002`).
2. Play a track over BT with correct AVRCP title/artist. **Watch the title across at least one SongRec
   cycle.** ⚠ **The interval on this box is 30 s, not the 15 s default** — `fingerprinting:identificationIntervalSeconds|30`
   in the store (§0.5c). Pass = the AVRCP title is still on screen.
3. **Confirm album art still appears.** This is the half that must not regress, and per `AUD-17` it is
   *entirely* SongRec's doing.
4. ⭐ **Confirm an identification actually happened, before recording any pass.** Look for the new
   line — `"Fingerprinting filled missing BT metadata from … : cover art"` — and confirm the old one
   is gone:
   ```bash
   ssh mmack@radio 'F=$(ls -t /opt/radio-console/logs/radio-*.txt | head -1); grep -c "replaced AVRCP" $F'   # expect 0
   ssh mmack@radio 'F=$(ls -t /opt/radio-console/logs/radio-*.txt | head -1); grep -c "filled missing BT metadata" $F'  # expect > 0
   ```
   ⚠ **Read the FILE sink, not the journal.** Since `LOG-11`, `journalctl -u radio-api` carries WARNING
   and above only, and both of these are `LogInformation`.
5. **Try a track whose phone reports no album** and confirm the album *appears* after an
   identification. That is `B3`/`B2`'s live counterpart, and it is the half most likely to be
   forgotten because it looks like a feature rather than a fix.

⚠⚠ **THREE known live conditions can make this UAT silently vacuous, and none of them is this row's
bug.** A UAT in which SongRec never ran proves nothing about SongRec:

- **`AUD-12`** — the BT source stalling at `Ready` gates fingerprinting off entirely.
- **`AUD-10`** — BT playback is effectively single-use per connection.
- ⭐ **`AUD-18`, filed 2026-09-08 and new since the previous revision of this plan** — the fingerprint
  tap returned **zero bytes for 11 h 23 m** on 2026-09-07/08 while audio played normally, producing
  **zero `TrackMetadata` rows** for the whole window. It recovered on a *source stream* restart with
  `radio-api` uptime unbroken. **If the box has been up for a day or more, check for it before
  trusting a UAT result:**
  ```bash
  ssh mmack@radio 'F=$(ls -t /opt/radio-console/logs/radio-*.txt | head -1); grep -c "No audio data captured" $F'
  ```
  **The established baseline is 0.** Anything else means the tap is dead and every "the title was not
  replaced" observation is vacuous.

⚠ **And §0.9: the History panel will still show SongRec's titles for file plays after this row.** Do
not treat that as a failure of the fix.

### 4.5 `T5` — FilePlayer UAT ⚠ must be staged, or it is vacuous

Per §0.7 the file player's effective root — the SQLite row, which wins — is
`/home/mmack/RTest/src/Radio.API/media/audio`, and it is **empty**. `/api/files` returns no items.
**Playing "a file" is not possible without staging one**, and a UAT that browses an empty directory
and reports no regression has tested nothing.

Stage it read-only and reversibly: copy **one** tagged file with embedded art from
`/opt/radio-console/media/audio` — `08-I'm Not In Love.mp3` (10cc / Very Best Of, mjpeg APIC) is the
clearest case — into the effective root, play it, and confirm across at least one SongRec cycle that
the title stays *I'm Not in Love*, the artist stays *10cc*, and the art URL stays the
`/api/albumart/<hash>` embedded hash rather than changing to an Apple Music CDN URL.

⛔ **Do not "fix" the root path to make this easier.** §0.7 — it is a separate defect and changing it
changes what the operator's file browser shows.

⚠ **Remove the staged file afterwards**, and say in the PR body that it was staged, so a later reader
does not mistake it for library content.

---

## 5. Docs impact

| File | Change |
|---|---|
| `src/Radio.Core/Models/Audio/SourceMetadataPrecedence.cs` | **New.** Carries the rule's own documentation (Task 1) |
| `design/AUDIO-DATAFLOW.md:278` | The per-field precedence bullet (Task 10c) |
| `docs/ROADMAP.md:133` | The `AUD-17` mechanism correction — **mechanism only** (Task 10a) |
| `docs/queue/AUD-1.md:26` | The same, in the row's words (Task 10b) |
| `design/FUTURE-WORK.md` | §0.9 — the History panel does not obey the rule after this row (Task 10d) |
| `src/Radio.Fingerprinting/FingerprintingOptions.cs` | The flag's docs, saying what it is *not* (Task 3) |
| `src/Radio.Web/Components/Pages/SystemConfigPage.razor` | Alert + help copy (Task 8) |
| `design/appsettings.example.json` | ⚪ **No change under O1.** Under O2 only — Appendix B |
| `CLAUDE.md` § *Pre-Merge Review* | ⭐ Optional but earned: this row corrects **four** comments that outlived their code, and `AUD-17` supplies a fifth. Worth one line noting that the failure mode now has a documented instance in **prose about the code** (`ROADMAP.md`, `queue/AUD-1.md`) as well as in comments — that is the form that propagates furthest, because two rows re-derived a wrong mechanism from it |

---

## 6. Queue row — what still needs changing, and what already does not

⛔ **Do not apply this while another agent has `docs/BUILDER_QUEUE.md` open.** A Builder is mid-cycle
on `TEST-2` and a second Planner is amending `OPS-3`.

⚠ **The index row at `docs/BUILDER_QUEUE.md:33` was already updated with the owner decision** and is
substantially correct. Only these parts are now stale:

1. **The estimate.** `**0.75 d** (F1) / **0.5 d** (F2)` → `**1.25 d**`. §0.11 says why it went up.
2. **The F1/F2 framing.** *"so FilePlayer follows BT (F1)"* should lose the `(F1)` — the options are
   gone and the parenthetical will send a reader looking for them.
3. **The mechanism sentence.** *"replace the overwrite at `:867-891` with the fill-if-missing
   behaviour already sitting on the unreachable preserve branch at `:893-905`"* is **wrong and is the
   single most important correction**: `:893-905` fills **cover art only**. Replace with:
   *"delete the overwrite at `:867-891`; the preserve branch at `:893-905` is art-only, so per-field
   fill for title/artist/album is NEW behaviour, not behaviour that merely becomes reachable — see the
   plan's §0.0c, which is the regression the earlier revision would have shipped."*
4. **The anchor note.** `⚠ Anchor drift: appsettings.json:91 → :93` still holds. Add:
   *"the row's own index line is `:33`, not `:34`; `FilePlayerAudioSourceTests.cs` is under
   `Audio/Sources/Primary/`; and if `TEST-2` lands first every `BluetoothAudioSource.cs` anchor below
   `:460` moves +3."*
5. **Add:** *"no new config key — §1.1 deletes the overwrite rather than making it configurable, so
   the SQLite store gains nothing an operator can set wrong."*

**Six follow-ups the owner may want filed. None is in scope here, and none should be smuggled in.**

1. ⭐ **The History panel does not obey the owner's rule.** `PlayHistoryTracker.cs:563-573`,
   `:631-641`, `:659-672` re-point a play-history row at SongRec's `TrackMetadata` on every
   identification, without consulting the source's per-field decision. §0.9. **This is the most likely
   follow-up to be wanted, because it is the visible remainder of the defect this row fixes.**
2. ⭐ **`PlayHistoryTracker.cs:373-386` is a fourth implementation of "missing"** and is the natural
   second customer for `SourceMetadataPrecedence`. Cheap, and it would delete code rather than add it.
3. ⭐ **Files with complete Artist+Album but no embedded art are never fingerprinted, so they never
   get art.** `hasIncompleteMetadata` (`FilePlayerAudioSource.cs:1985-1987`) tests Artist and Album
   only — **not art, and not Title.** With the gate off the key is never written,
   `SoundFlowAudioTap` reports `false`, and `BackgroundIdentificationService.cs:260` hard-returns.
   `Meditating Beat.mp3` is this case live. **This is the file-source analogue of `AUD-1`'s own
   split**, and the owner's rule arguably requires it: art is missing, so fingerprinting should be
   augmenting it. It is an additional art term in the gate predicate and is deliberately not in this
   row.
4. ⚠ **`BluetoothAudioSource.cs:770-772` writes empty AVRCP values over fields fingerprinting has
   already filled.** After an AVRCP refresh of the same track the album goes back to `""` and waits
   for the next identification — and with `duplicateSuppressionMinutes = 5` on the box, that wait can
   be minutes. **This is pre-existing and unchanged by this row** (today's overwrite branch has the
   same churn), which is why it is not fixed here. The art path already solved it with the
   resolved-art cache (`:807-813`); the same treatment for text fields is the fix.
5. ⚠ **The file player's root is an operator SQLite row pointing at a dev path**
   (`/home/mmack/RTest/src/Radio.API/media/audio`) that is empty, while the JSON layers say
   `/mnt/nas/music`, which is not mounted (§0.7). Three layers, three answers, and the winner has no
   files. Related to the long-deferred *G.8 NAS mount*.
6. **`fingerprinting:fpcalcPath` is an orphaned row** in the live config store (§0.5c) — a dead key
   from the AcoustID→SongRec migration. Harmless, but it is the standing evidence that this store
   accumulates orphans, and a cleanup pass would be cheap.

---

## Appendix A — the superseded §0.8, preserved so its evidence is not mistaken for new

⛔ **DO NOT IMPLEMENT ANY OF THIS.** It is kept because two findings inside it are still cited above —
the measured FilePlayer damage (now §0.6, with `C-248`'s correction) and the "no re-identification
loop" check — and a reader meeting them without their original framing will not know they were argued
for a different conclusion.

> **Option F1 — FilePlayer follows BT. The new flag governs both sources. ⭐ `Recommended:`**
> `:2112` becomes `if (FpOptions.ShazamOverwritesSourceMetadata && needsLookup)`. With the new flag
> default-off, execution falls through to the block already below it, which is a **complete and
> better-shaped preserve implementation** than BT's: `:2143-2149` fills art only when it is still
> `DefaultAlbumArtUrl`; `:2161-2178` fills Artist, Album and Title only where they still hold
> defaults; `:2197-2200` sets `NeedsFingerprintingLookup = false` and
> `MetadataSource = "Fingerprinting"`. That last line matters: **there is no re-identification loop.**
>
> **Option F2 — leave FilePlayer exactly as it is. The new flag governs BT only.**
> **Option F3 — per-source properties (four booleans).** ⛔ Recommend against.

⚠ **Where F1's own reasoning was wrong, and it is instructive.** F1 called the FilePlayer preserve
path *"more careful than BT's"* and proposed reusing both unmodified. It was right about FilePlayer —
`:2143-2178` really is per-field — and **wrong about BT**, whose preserve branch fills art and nothing
else. The error came from arguing by symmetry between the two sources after explicitly promising not
to. That is `C-247`, and it is why the owner's "one rule everywhere" framing is better than either
option: a rule can be checked against each source's code, whereas "does A follow B" invites exactly
the assumption that A and B are the same shape.

---

## Appendix B — O2, if the owner wants the overwrite kept behind a flag

§1.1 recommends deleting the overwrite outright. If the owner prefers it configurable, these are the
exact deltas. **Budget +1.5 h.** ⚠ Re-read §0.5 first: this adds a second fingerprinting key to a
SQLite store that outranks both JSON layers and already carries one orphan.

1. **`FingerprintingOptions.cs`** — add after `UseShazamForAllSources`:
   ```csharp
     /// <summary>
     /// When true, a SongRec identification <b>replaces</b> title, artist, album and cover art
     /// that the source already supplied. When false (the default), it may only <b>fill in what
     /// is missing</b>, field by field — see
     /// <see cref="Radio.Core.Models.Audio.SourceMetadataPrecedence"/>.
     /// </summary>
     /// <remarks>
     /// ⚠ The owner's 2026-09-08 decision is that source metadata wins per field. This flag exists
     /// only as a rollback and should stay false. Setting it true restores the behaviour AUD-1 was
     /// filed to remove: SongRec replacing correct AVRCP metadata after misidentifying residual
     /// audio, and replacing stable embedded ID3 cover art with a different hash each cycle.
     /// </remarks>
     public bool ShazamOverwritesSourceMetadata { get; set; } = false;
   ```
2. **`BluetoothAudioSource.cs`** — keep `:867-891` as-is but change its condition to
   `if (FpOptions.ShazamOverwritesSourceMetadata)`, and put Task 4a's per-field block below it in
   place of `:893-905`.
3. **`FilePlayerAudioSource.cs`** — keep `:2110-2140` but change `:2112` to
   `if (FpOptions.ShazamOverwritesSourceMetadata && needsLookup)`; Task 5b/5c are unchanged.
4. **Config surface** — `src/Radio.API/appsettings.json` after `:93` and
   `design/appsettings.example.json` after `:33`: `"ShazamOverwritesSourceMetadata": false,`;
   `src/Radio.Web/Models/ApiModels.cs` after `:788`:
   `public bool ShazamOverwritesSourceMetadata { get; set; } = false;`.
5. **`SystemConfigPage.razor`** — regrid the checkbox row from `4`/`8` to `4`/`4`/`4` and add a third
   `RadzenCheckBox` bound to `_fingerprintingConfig.ShazamOverwritesSourceMetadata`, matching the
   existing flex-`div` + `<span>` label + sibling help-`<span>` idiom at `:724-729`.
   `LoadConfigurationAsync` (`:2318`) and `SaveFingerprintingConfigAsync` (`:3026-3037`) need no
   change — both move the whole DTO.

⚠ **Under O2, Task 9's expected result changes**: the store *may* gain
`fingerprinting:shazamOverwritesSourceMetadata|false` the first time the operator saves the tab.
**The absence of the row is still the correct state until then** — it is what makes the code default
apply. Anyone "fixing" it by adding the key with `true` re-creates the bug this row removes.
