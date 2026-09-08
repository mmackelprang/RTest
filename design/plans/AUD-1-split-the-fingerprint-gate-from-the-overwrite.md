# PLAN — `AUD-1` · Split the always-fingerprint gate from the overwrite, and do it without renaming the key

> **Row:** `AUD-1`, [`docs/queue/AUD-1.md`](../../docs/queue/AUD-1.md). Index row `docs/BUILDER_QUEUE.md:34`.
> **Branch:** `fix/split-shazam-fingerprint-vs-overwrite`
> **Estimate:** **0.75 d** under the recommended option. §0.10 derives it.
> ⛔ **NOT auto-mergeable.** §0.11. Live audio path, user-visible metadata, and one owner decision.
> **Planned against** `main` at **`a529ccf7`**. Every line number below was re-derived at that commit.
> ⚠ The planning session's working directory was checked out on
> `fix/gv-texts-polish-overflow-unread-align` (a Builder is mid-cycle). That branch's diff against
> `main` touches `PhoneTextsPanel.razor`, `PhoneTextsPanelTests.cs`, `design-system.css` and
> `docs/BUILDER_QUEUE.md` — **none** of which this plan cites. Verified with `git diff --stat
> main...HEAD`, not assumed, so every anchor below is equally true of `main`.
> **The box was read, never written.** Four `sqlite3` selects, one `cat`, one `ffprobe`, one `ls`.
> No deploy, no restart, no config change. §0.5 and §0.7 are the findings that came out of it.

---

## 0. Read this before Task 1

### 0.1 What this row is, in one paragraph

One boolean decides two unrelated things. `FingerprintingOptions.UseShazamForAllSources`
(`src/Radio.Fingerprinting/FingerprintingOptions.cs:19`) feeds **a gate** —
`BluetoothAudioSource.cs:837`, `NeedsFingerprintingLookup = hasIncompleteMetadata ||
FpOptions.UseShazamForAllSources`, which decides *whether SongRec runs at all* — and **a fork** —
`BluetoothAudioSource.cs:867`, which decides *whether SongRec's answer overwrites the phone's own
AVRCP title/artist/album*. The gate is necessary on this box. The fork is wrong. Because they share
one switch, the only way to stop the overwrite today is to turn off the gate, which stops album art
entirely. The fix is a second property, and the wanted behaviour already exists twelve lines further
down the same method (`:893-905`) on the fork that never runs while the flag is on.

**This is not theoretical.** Confirmed live 2026-09-06: SongRec logged *"Shazam metadata replaced
AVRCP for BT: 'Spirit In The Sky'"* while the phone played Green Day, having misidentified from
residual radio audio seconds after a BT reconnect. The next AVRCP update restored the right title
with no art, leaving the placeholder.

### 0.2 `C-240` — anchor re-derivation. Nine of ten held; one moved, and one cited file does not exist

The row states its anchors were verified at `main` @ `8b1ce0a`. Re-derived at `a529ccf7`:

| Row's claim | At `a529ccf7` | Verdict |
|---|---|---|
| `FingerprintingOptions.cs:19` | `public bool UseShazamForAllSources { get; set; } = false;` | ✅ exact |
| `appsettings.json:91` | **`:93`** — `:91` is where the `Fingerprinting` block *opens* | ❌ **moved +2** |
| `ApiModels.cs:788` | `public bool UseShazamForAllSources { get; set; } = false;` | ✅ exact |
| `BluetoothAudioSource.cs:837` | the gate | ✅ exact |
| `BluetoothAudioSource.cs:845` | `private void OnTrackIdentified(...)` | ✅ exact |
| `BluetoothAudioSource.cs:867-891` | the overwrite branch | ✅ exact |
| `BluetoothAudioSource.cs:893-905` | the preserve branch | ✅ exact |
| `BackgroundIdentificationService.cs:260` | `if (!audioTap.NeedsFingerprintingLookup) return;` | ✅ exact |
| `FilePlayerAudioSource.cs:1988` | the gate | ✅ exact |
| `FilePlayerAudioSource.cs:2112` | the overwrite | ✅ exact |

**The `appsettings.json` drift is the row's third stale-anchor event**, after `+28` (PR #469) and
`+20` (PR #468). Seven commits touched that file between `8b1ce0a` and `a529ccf7` —
`c61f9276`, `ba1ae4a6`, `6b3dcc2e`, `f79eef17`, `09616183`, `3b787952`, `8214a45a`. The row
specifically claimed this citation had been "checked specifically" and survived #468; it did not
survive the six commits after it. Fixed in §6's row text.

⚠ **`src/Radio.API/appsettings.Production.json` does not exist**, and the task framing that
commissioned this plan cites it as `:51`. `src/Radio.API/` holds only `appsettings.json` and
`appsettings.Development.json`. The repo's Production overlay for this box is
**`deploy/debian-x64/appsettings.Production.json:43`**, whose entire `Fingerprinting` block is the
two lines `:42-44`. The *deployed* file is `/opt/radio-console/api/appsettings.Production.json` and
it does read `"UseShazamForAllSources": true` — read directly on the box, so the substance of the
framing is right and only the path is wrong. §0.5 is why the distinction is load-bearing rather
than pedantic.

### 0.3 ⚠⚠ `C-241` — the row's "0 AVRCP-sourced art" premise is FALSE. The conclusion survives; the reason must be rewritten

`docs/queue/AUD-1.md:26` and `docs/ROADMAP.md:133` both assert, in identical words, that **AVRCP
cannot supply album art on this box** — *"BlueZ 5.72 ships no BIP / cover-art implementation, and 7
days of fingerprint-DB data show 0 AVRCP-sourced art against 2,560 SongRec-sourced."* It is stated
as a mechanism, and the mechanism is what makes the "never turn the gate off" rule feel absolute.

**Measured on the box, 2026-09-08, against `/opt/radio-console/data/fingerprints/fingerprints.db`:**

```sql
SELECT Source, COUNT(*) n,
       SUM(CASE WHEN CoverArtUrl IS NOT NULL AND CoverArtUrl<>'' THEN 1 ELSE 0 END) with_art
FROM TrackMetadata GROUP BY Source ORDER BY n DESC;
```

| Source | rows | with art |
|---|---|---|
| `Shazam` | 43,405 | 43,082 |
| `Manual` | 818 | 77 |
| **`Avrcp`** | **713** | **66** |
| `FileTag` | 162 | 146 |
| `AcoustID` | 112 | 93 |

**AVRCP has supplied album art 66 times.** Not zero. And it was *real* art, not a rejected
`file://` URL — every one of the 66 is a `/api/albumart/<hash>` path, meaning it was fetched and
cached successfully by `CacheAvrcpArtAsync` (`BluetoothAudioSource.cs:926`). The date range is
`2026-03-05T19:12:35Z` → `2026-07-19T12:33:15Z`.

**Why the row's measurement was still honest, and why the conclusion holds.** The investigation ran
2026-08-10 over a 7-day window. Restricting to that era reproduces its number exactly:

| Source (since 2026-08-01) | rows | with art |
|---|---|---|
| `Shazam` | 9,577 | 9,517 |
| `Avrcp` | **31** | **0** |
| `FileTag` | 3 | 2 |

So: **the count was right for its window; the generalisation to a mechanism was wrong.** AVRCP art
worked for four and a half months and stopped on or about **2026-07-19**. "BlueZ 5.72 ships no BIP"
cannot explain 66 successes on the same BlueZ.

**What changes, and what does not:**

- ⛔ **The decided design does NOT change.** Keeping the always-fingerprint gate is still correct:
  0/31 in the last five weeks, against 9,517 SongRec arts. SongRec is the only art source *in
  practice today* — which is all the design needs.
- ✅ **The prohibition on setting the flag `false` still stands**, for the same practical reason.
- ⚠ **The justification text must be corrected in three places** (Task 8), because a rule defended
  by a false mechanism is a rule the next person will overturn by disproving the mechanism.
- ⭐ **There is a latent second defect here and this plan does NOT fix it.** Something stopped
  AVRCP art on ~2026-07-19. That is a separate row, not scope creep into this one. §6 proposes the
  wording; the owner decides whether to file it.

⚠ Note the asymmetry this creates with the preserve branch, and why it *helps*: under today's
overwrite branch, `:882-885` caches SongRec art unconditionally, so on a phone that *does* supply
AVRCP art SongRec clobbers it. The preserve branch's `!hasArt` guard at `:901` keeps it. The fix is
therefore strictly better for the 66-row case as well as the 43,082-row case.

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
safety property.

### 0.5 ⭐⭐ `C-243` — THE CONSTRAINT THAT DECIDES THE DESIGN: the key cannot be renamed

This is the finding that most changes what Builder should do, and it is invisible from the source
tree alone.

**Three facts, each verified:**

**(a) The deploy PRESERVES the Production overlay; it does not ship it.**
`deploy/Deploy-ToLinux.ps1:271` rsyncs with `--exclude='appsettings.Production.json'` for both `api/`
and `web/`, and the seed block at `:314-353` copies `deploy/<configDir>/appsettings.Production.json`
**only into a directory that does not already have one** (`:326-327`, *"present — left alone"*).
`deploy/deploy-to-pi.sh:152-213` implements the same policy — that symmetry is what `OPS-8` (#576)
and `OPS-7` (#570) landed. **Editing `deploy/debian-x64/appsettings.Production.json` therefore has
no effect on this box, ever.** The live file already exists.

**(b) The deployed overlay has already diverged from the repo template**, which independently proves
(a) is in force. Read from the box:

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

⚠ **`fingerprinting:fpcalcPath` is an orphan and it is the proof this hazard is real, not
hypothetical.** `FingerprintingOptions` has no `FpcalcPath` property — fpcalc/AcoustID was replaced
by SongRec — yet the row is still sitting in the store because nothing ever cleaned it up. **This
store has already accumulated one dead key from exactly the kind of rename being contemplated.**

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

**The design that follows from this is a no-op migration** (§1.2), and it is why this plan keeps an
imperfect name rather than a clean one.

### 0.6 `C-244` — the FilePlayer evidence, measured rather than assumed

The row's caveat says ID3 tags are *"authoritative in a different way"* and warns that dragging
FilePlayer along with BT is the failure mode. It does not say which way the evidence points. Gathered
from the box and from the play-history record:

**(a) ID3 on this library is largely complete, and art is the norm.** `ffprobe` across
`/opt/radio-console/media/audio`:

| Directory | Files | Title | Artist | Album | **Embedded art** |
|---|---|---|---|---|---|
| music root | 24 | 21 | 23 | 20 | **19 (79%)** |
| `alarm/` | 12 | 12 | 2 | 1 | 0 |
| `notify/` | 12 | 0 | 0 | 0 | 0 |

The code already extracts that art — `ExtractEmbeddedAlbumArt` (`FilePlayerAudioSource.cs:2026`) →
`TryGetEmbeddedAlbumArtUrl` (`:2043-2082`, TagLib, prefers `PictureType.FrontCover`) → the
content-addressed album-art cache. Corroborated in the fingerprint DB: `FileTag` rows are **146 of
162 with art**.

**(b) ⭐ The overwrite has been measured, and it is destructive.** `PlayHistory` since 2026-03-11
records **44 file plays with `MetadataSource = Fingerprinting` against 8 with `FileTag`** —
`PlayHistoryTracker.cs:420` writes `FileTag` on creation and `:567`/`:635` upgrade it when an
identification lands. **So 44 of 52 file plays had their metadata overwritten.** What it wrote:

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

**(c) Ten tracks no fingerprinting service can improve.** `Cary High Chorus` / `Fall Concert 2006`,
titles `Track 7`…`Track 16`, all with embedded art, Artist+Album complete and human-supplied. A
high-school chorus recording is unidentifiable by Shazam, and `hasIncompleteMetadata` is already false
for them — so today the *only* reason they get fingerprinted at all is the flag.

⭐ **This is the decisive asymmetry, and it points the opposite way from the BT case.** The entire
argument for keeping BT's gate on is *SongRec is the only art source we have*. For files that is
inverted — they supply art 79% of the time, and it is **better** art because it is stable. **The
conclusion for FilePlayer is therefore the same as for BT, but reached from opposite evidence rather
than by symmetry** — which is exactly what the row asked the planner not to fake.

⚠ **The comment at `FilePlayerAudioSource.cs:2110-2111` asserts the opposite** — *"SongRec is more
authoritative and has better cover art from Apple Music CDN"*. The 44-play record contradicts both
halves. Under F1 that comment is replaced (Task 3); under F2 it must still be corrected, because
`CLAUDE.md` § *Pre-Merge Review* makes an over-claiming comment a finding in its own right.

### 0.7 ⚠ `C-245` — the config drift that makes FilePlayer UAT vacuous unless it is fixed first

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

**So the file player on this appliance is browsing an empty directory today, whichever layer wins.**
The 52 historical plays date from when the root pointed somewhere populated. Two consequences:

- ✅ **F1's blast radius on this box is currently nil**, which lowers its risk.
- ⛔ **But a "no regression" UAT result would be vacuous.** §4.5 says what to do instead.

⚠ **Do not fix the drift as part of this row.** It is a separate defect (an operator SQLite row
pointing at a Windows-style dev path on a Linux appliance) and fixing it changes what the file player
browses — a user-visible change with no diagnosis behind it. §6 proposes filing it.

### 0.8 ⛔⛔ THE OWNER DECISION — what happens to `FilePlayerAudioSource`

**This is the one thing in this plan that is not mine to settle.** The row is explicit that silently
dragging FilePlayer along with BT is the failure mode. Builder must not start Task 3 until the owner
has picked one.

---

**Option F1 — FilePlayer follows BT. The new flag governs both sources. ⭐ `Recommended:`**

`:2112` becomes `if (FpOptions.ShazamOverwritesSourceMetadata && needsLookup)`. With the new flag
default-off, execution falls through to the block already below it, which is a **complete and
better-shaped preserve implementation** than BT's:

- `:2143-2149` fills art **only when it is still `DefaultAlbumArtUrl`** — the `!hasArt` equivalent.
- `:2161-2178` fills Artist, Album and Title **only where they still hold defaults**, field by field.
- `:2197-2200` sets `NeedsFingerprintingLookup = false` and `MetadataSource = "Fingerprinting"`.

That last line matters: **there is no re-identification loop.** The preserve path does its own
bookkeeping, so SongRec will not re-fire every 15 s on a tagged file. I checked this specifically
because it was the obvious way for F1 to be quietly expensive, and it is not.

- **Why:** per §0.6, ID3 on this box is curated, complete and carries stable art, and the overwrite
  has been **measured destroying it** — 44 of 52 file plays overwritten, two fabricated titles, a
  U+2010 hyphen, and nine competing art hashes where the embedded art had one. `:2126-2128` replaces
  art unconditionally, without the `DefaultAlbumArtUrl` check the preserve path uses. This is not a
  precaution against a hypothetical; it is stopping an observed loss.
- **What it costs:** it changes behaviour on a second live source, so the blast radius is larger than
  BT alone; and it makes `OnTrackIdentified_WhileThisSourceIsActive_UpdatesMetadata`
  (`FilePlayerAudioSourceTests.cs:1709-1729`) fail, because that test asserts the overwrite —
  including `MetadataSource == "Shazam"`. It must be rewritten, not deleted (Task 6a). Budget ~1.5 h.
- **Why the risk is smaller than it looks:** the file player's effective root is empty on this
  appliance (§0.7), so the change is currently unobservable in normal use — which also means UAT
  must be staged deliberately rather than by "playing something". §4.5.

**Option F2 — leave FilePlayer exactly as it is. The new flag governs BT only.**

`:1988` and `:2112` keep reading `UseShazamForAllSources`; only `:867` moves to the new property.

- **What it gives up:** coherence, and the row's own stated purpose. `UseShazamForAllSources` would
  then mean *"always fingerprint"* for Bluetooth and *"overwrite my tags"* for files — **the same
  conflation this row exists to remove, preserved in one source and now harder to see because the
  name no longer matches either meaning.** The next person to read `FingerprintingOptions.cs` meets
  the identical trap with worse signposting. It also leaves the measured damage in §0.6b running.
- **What it buys:** the smallest possible diff, no FilePlayer test churn, and no behaviour change on
  a source that is currently idle on this box. Defensible on "don't change what you cannot watch" —
  though note §0.6b means the damage is already *recorded*, even if not currently *accruing*.
  Budget ~0 h beyond BT.

**Option F3 — per-source properties (four booleans).**

`FingerprintingOptions` gains `Bluetooth`/`File` sub-objects, each with a gate and an overwrite.

- **What it costs:** four config keys, four UI controls in a Razor row whose grid budget is already
  spent (`SizeMD="4"` + `SizeMD="8"` = 12), a nested-section change to the schemaless
  `ConfigurationController` path, and roughly double the test surface. Budget +1.0 d.
- **What it buys:** the ability to answer this question differently per source later. **Nothing today
  needs that** — §0.6 says both sources want the same answer, for different reasons.
- ⛔ Recommend against. The row anticipated *"`FingerprintingOptions` to gain two properties"*, and
  two is what the evidence supports.

---

**Recommendation: F1.** The evidence in §0.6 says ID3 is at least as authoritative as AVRCP and,
unlike AVRCP, brings its own art; the preserve path already exists, is more careful than BT's, and
closes its own bookkeeping. **Its cost is one rewritten test and a second live source in the blast
radius.** F2's saving is real but it buys that saving by leaving the row's central defect standing in
one of the two places it was filed about.

⚠ **If the owner picks F2, Task 3 and Task 6b are struck**, the estimate drops to **0.5 d**, and §6's
queue text must say FilePlayer was considered and deliberately left alone — not that it was missed.

### 0.9 What is decided and needs no further discussion

- The BT gate at `:837` **does not change**. §0.3 confirms it, from a stronger dataset than the row had.
- The BT fork at `:867` **moves to a new property**. The target behaviour is `:893-905`, unmodified.
- **No rename.** §0.5.
- ⛔ **Do not "fix" this by setting `UseShazamForAllSources` false.** It kills BT album art.

### 0.10 What the two properties mean, in one line each

| Property | Governs | Default | On the box after this change |
|---|---|---|---|
| `UseShazamForAllSources` | *Run SongRec even when the source already has title+artist.* `BluetoothAudioSource.cs:837`, `FilePlayerAudioSource.cs:1988` | `false` | **`true`** — unchanged, from the SQLite row |
| `ShazamOverwritesSourceMetadata` | *Let a SongRec result replace title/artist/album that the source already supplied.* `BluetoothAudioSource.cs:867`, and under F1 `FilePlayerAudioSource.cs:2112` | `false` | **`false`** — absent everywhere, so the code default applies |

### 0.11 The estimate — **0.75 d** (F1) / **0.5 d** (F2)

| Task | | F1 |
|---|---|---|
| 1 | `FingerprintingOptions` — new property + doc rewrite on both | 0.5 h |
| 2 | `BluetoothAudioSource.cs:867` + comments | 0.5 h |
| 3 | `FilePlayerAudioSource.cs:2112` + comments **(F1 only)** | 0.5 h |
| 4 | Config surface: 2 JSON files, DTO, Razor checkbox | 1.0 h |
| 5 | BT tests — 3 new, 1 rewritten | 1.5 h |
| 6 | FilePlayer tests — 1 rewritten, 2 new **(F1 only)** | 1.5 h |
| 7 | Migration verification on the box (read-only) | 0.5 h |
| 8 | Docs: the false-premise correction in 3 places | 0.75 h |
| | Gates, self-review, PR | 1.0 h |
| | **Total** | **≈ 7.75 h → 0.75 d** |

### 0.12 ⛔ Auto-merge — **NO**

Three of the four gates in the user's auto-merge policy are met (tests, review, no unresolved
findings), and the fourth is not:

1. ⛔ **It touches the live audio path and user-visible metadata**, which the policy names explicitly
   as a reason to pause. `AUD-4` and `AUD-12` are marked not-auto-mergeable for the same reason.
2. ⛔ **It carries an owner decision** (§0.8). A merge before that is answered is a merge of an
   assumption.
3. ⚠ **UAT needs a phone and cannot be automated.** The BT half is only truly verified by playing a
   track with correct AVRCP metadata and watching the title survive an identification. Note that
   `AUD-12`'s dossier reports UAT is **blocked on the owner's phone**, and `AUD-10` makes BT
   playback effectively single-use per connection — so expect the UAT to be awkward and plan for it
   rather than discovering it. §4.4.
4. ✅ Not sensitive in the auth/secrets/migration sense: no schema change, no credential path.

### 0.13 Collisions — three rows and one Builder claim files this touches

| Row / branch | Shared file | Verdict |
|---|---|---|
| **`TEST-2`** (`design/plans/TEST-2-…md`, untracked) | `BluetoothAudioSource.cs` | ✅ **No collision. Either order.** §0.13 |
| **`AUD-12`** (`design/plans/AUD-12-…md`) | `BluetoothAudioSource.cs` | ✅ No textual overlap — it works at `:454-460` and `:1125-1151`. **Semantically adjacent:** it fixes the `Ready` stall that gates fingerprinting off entirely, so `AUD-12` first makes `AUD-1`'s UAT *possible*. Prefer `AUD-12` first if both are queued, but neither blocks the other. |
| **`AUD-13`** (`design/plans/AUD-13-…md`, untracked, being written concurrently) | `SystemConfigPage.razor` | ⚠ **Anchor collision only.** It edits the Devices tab at `:283-284`; this plan edits the Fingerprinting tab at `:721-730`. Its edit is *above* mine, so if `AUD-13` lands first my `:726` anchor shifts. Re-derive, do not trust the number. |
| Builder on `fix/gv-texts-polish-overflow-unread-align` | — | ✅ None. Verified by `git diff --stat main...HEAD`. |

### 0.14 `TEST-2` — **no collision, and this plan should land first**

`TEST-2`'s plan modifies `BluetoothAudioSource.cs` in exactly two places: `:454`
(`internal` → `private` on `ApplyDeferredCaptureState`) and its doc comment at `:447-452`. This plan
modifies `:865-891`. **Four hundred lines apart, in different methods, with no shared symbol** —
`git` will merge them without a conflict in either order.

**Prefer `AUD-1` first, but only weakly.** `TEST-2` adds theory tests to
`BluetoothAudioSourceTests.cs` and deletes two existing ones (`:907-957`); this plan rewrites a
different test (`:850-897`) and appends three more. Landing `AUD-1` first means `TEST-2` rebases onto
a test file whose tail has grown, which is the cheaper direction — `TEST-2`'s changes are keyed to
method names, mine to line ranges. If `TEST-2` lands first, re-derive `:850-897` before editing.

⛔ **Neither plan may edit the other's test region.** `TEST-2` owns `:907-957`; this plan owns
`:850-897` and appends after `:897`.

### 0.15 `C-NNN` numbering — collision warning

This plan uses **`C-240`…`C-245`**. `C-213` was the highest in the tree before tonight; `C-214`–`C-219`
are `TEST-2`'s and `C-220`–`C-237` are already claimed across `UI-7`, `UX-1` and `AUD-13`, which were
written concurrently. **A gap was left deliberately.** If another concurrent plan also claims `C-240+`,
leave both and note the collision; do not renumber a merged plan.

---

## 1. Decision

### 1.1 Two properties, one of them new, neither renamed

```
UseShazamForAllSources        →  the GATE.       Unchanged name, unchanged semantics, unchanged sites.
ShazamOverwritesSourceMetadata →  the FORK. NEW. Default false. Reached at :867 (and :2112 under F1).
```

The name `UseShazamForAllSources` is now imprecise — it means *"fingerprint all sources"*, not *"use
Shazam's answer"*. **That imprecision is bought deliberately** to make the migration a no-op (§0.5),
and Task 1 pays for it with a doc comment that says so in the file where the next reader will be
standing.

### 1.2 The migration is a no-op, and that is the point

| Layer | Holds today | After this change | Effect |
|---|---|---|---|
| SQLite store (**wins**) | `useShazamForAllSources = true` | unchanged | Gate stays **on** → **BT album art preserved** ✅ |
| Live Production overlay | `UseShazamForAllSources: true` | unchanged (deploy excludes it) | consistent |
| `appsettings.json` | `UseShazamForAllSources: false` | + `ShazamOverwritesSourceMetadata: false` | new key defaults off |
| New property, everywhere | — | absent → **code default `false`** | Overwrite **off** → **AVRCP title preserved** ✅ |

**Nothing has to be edited on the box.** No `sqlite3` write, no overlay edit, no manual step in the
deploy. Task 7 verifies that prediction rather than performing a migration.

⚠ **This is only true because the name is kept.** Under a rename, row 1 becomes an orphan, the gate
falls to `appsettings.json`'s `false`, and BT album art dies. Re-read §0.5 before proposing a tidier
name.

### 1.3 Reaching the wanted behaviour is a one-line change, twice

`BluetoothAudioSource.OnTrackIdentified` already contains both behaviours. `:867`'s condition is the
only thing selecting between them. Changing which property it reads is the entire fix; `:893-905` is
not modified at all. The same is true at `FilePlayerAudioSource.cs:2112` under F1.

---

## 2. Tasks

### Task 1 — `FingerprintingOptions`: add the second property

**File:** `src/Radio.Fingerprinting/FingerprintingOptions.cs`. Replace `:14-19`:

```csharp
  /// <summary>
  /// When true, runs SongRec on ALL sources even when AVRCP or ID3 already supply a
  /// title and artist. This is the <b>gate</b>: it decides whether fingerprinting runs
  /// at all, not what is done with the answer.
  /// </summary>
  /// <remarks>
  /// <para>
  /// ⚠ <b>Do not set this false to stop SongRec overwriting metadata — use
  /// <see cref="ShazamOverwritesSourceMetadata"/> for that.</b> Turning this off makes
  /// <c>BackgroundIdentificationService</c> hard-return for any track whose source already
  /// supplied a title and artist, so nothing is fingerprinted and no cover art is ever
  /// found. On the appliance that removes Bluetooth album art entirely: measured over the
  /// five weeks to 2026-09-08, AVRCP supplied art for 0 of 31 tracks while SongRec supplied
  /// it for 9,517 of 9,577.
  /// </para>
  /// <para>
  /// ⚠ <b>The name is imprecise and is kept deliberately.</b> It reads as "use Shazam's
  /// answer", but it only decides whether Shazam is <i>asked</i>. Renaming it would orphan
  /// the <c>fingerprinting:useShazamForAllSources</c> row in the operator's SQLite config
  /// store — which outranks both JSON layers — and the live
  /// <c>appsettings.Production.json</c>, which the deploy is contractually forbidden to
  /// overwrite (<c>Deploy-ToLinux.ps1:271</c>, <c>:314-353</c>). The renamed key would be
  /// absent from every layer, fall through to the <c>false</c> default, and take album art
  /// with it. See <c>AUD-1</c>.
  /// </para>
  /// </remarks>
  public bool UseShazamForAllSources { get; set; } = false;

  /// <summary>
  /// When true, a SongRec identification <b>replaces</b> title, artist, album and cover art
  /// that the source already supplied. When false (the default), SongRec may only <b>fill in
  /// what is missing</b> — existing AVRCP or ID3 values are left alone.
  /// </summary>
  /// <remarks>
  /// Defaults to <c>false</c> because the source's own metadata is authoritative for what is
  /// actually playing and SongRec is inferring from audio. It has been observed replacing
  /// correct AVRCP metadata with a track misidentified from residual audio after a source
  /// switch, and it rewrites <c>Enter Sandman (Remastered)</c> to <c>Enter Sandman</c>.
  /// Independent of <see cref="UseShazamForAllSources"/>: the two decide different things.
  /// </remarks>
  public bool ShazamOverwritesSourceMetadata { get; set; } = false;
```

### Task 2 — `BluetoothAudioSource`: point the fork at the new property

**File:** `src/Radio.Infrastructure/Audio/Sources/Primary/BluetoothAudioSource.cs`.

**2a** — replace `:865-867`:

```csharp
    // AUD-1: the overwrite is a SEPARATE decision from the always-fingerprint gate at :837.
    // When ShazamOverwritesSourceMetadata is false (the default), fall through to the
    // art-fill block below, which never touches title/artist/album. AVRCP is authoritative
    // for what the phone is actually playing; SongRec is inferring from audio and has been
    // observed identifying residual audio from a previously-active source after a reconnect.
    if (FpOptions.ShazamOverwritesSourceMetadata)
```

**2b** — `:830-831`, correct the comment that currently justifies the gate by the overwrite:

```csharp
    // If metadata is incomplete (no title or artist), request fingerprinting.
    // When UseShazamForAllSources is enabled, always fingerprint — on this box SongRec is
    // in practice the only working source of cover art (AUD-1). Whether the result is
    // allowed to REPLACE AVRCP title/artist/album is a different decision, taken at :867
    // by ShazamOverwritesSourceMetadata.
```

⛔ **Do not touch `:893-905`.** It is already the wanted behaviour and this row's whole point is
that it is reachable, not that it needs rewriting.

### Task 3 — `FilePlayerAudioSource` **(Option F1 only — do not start before §0.8 is answered)**

**File:** `src/Radio.Infrastructure/Audio/Sources/Primary/FilePlayerAudioSource.cs`. Replace `:2110-2112`:

```csharp
    // AUD-1: same split as BluetoothAudioSource:867. Default-off, so execution falls through
    // to the block below, which fills art only when it is still the default (:2143) and fills
    // artist/album/title only where they still hold defaults (:2161-2178) — then closes its own
    // bookkeeping at :2197. Measured before this change: 44 of 52 file plays had their tags
    // overwritten by fingerprinting, producing two fabricated titles, a U+2010 hyphen in
    // "blink-182", and nine competing cover-art hashes for a track whose embedded art had one.
    // ID3 here is curated and its art is stable; a SongRec guess replacing it is a loss.
    if (FpOptions.ShazamOverwritesSourceMetadata && needsLookup)
```

Also correct `:74`:

```csharp
  /// <param name="fingerprintingOptions">Optional fingerprinting options (the
  /// UseShazamForAllSources gate and the ShazamOverwritesSourceMetadata fork).</param>
```

⚠ Leave `:1988` alone. That is the gate, and it keeps reading `UseShazamForAllSources` under every
option.

### Task 4 — the config surface

There is **no API-side DTO and no mapping code** — `ConfigurationController` reads and writes the
store schemalessly (`GET/POST /api/configuration/{section}`, `:223`/`:313`), so only these four edits
exist.

**4a** — `src/Radio.API/appsettings.json`, after `:93`:

```json
    "UseShazamForAllSources": false,
    "ShazamOverwritesSourceMetadata": false,
```

**4b** — `design/appsettings.example.json`, after `:33`: the same pair.

**4c** — `src/Radio.Web/Models/ApiModels.cs`, after `:788`:

```csharp
  public bool ShazamOverwritesSourceMetadata { get; set; } = false;
```

**4d** — `src/Radio.Web/Components/Pages/SystemConfigPage.razor`. ⚠ Re-derive these line numbers
first (§0.12: `AUD-13` edits this file above them).

Replace the info alert at `:718`:

```razor
                  Fingerprinting uses SongRec (Shazam) for audio recognition. "Always Fingerprint All Sources" decides whether SongRec runs even when Bluetooth AVRCP or file ID3 tags already supply a title and artist — on this system it is the only reliable source of cover art. "Let Shazam Overwrite Source Metadata" decides whether its answer replaces that title and artist, and is normally off.
```

Rebalance the checkbox row from `4`/`8` to `4`/`4`/`4` and add the third control, matching the rich
idiom already at `:725-729` (flex `div`, `<span>` label, then a sibling help `<span>`, not `<small>`):

```razor
              <RadzenColumn Size="12" SizeMD="4">
                <div style="display:flex;align-items:center;gap:8px"><RadzenCheckBox TValue="bool" @bind-Value="_fingerprintingConfig.Enabled" /><span>Enabled</span></div>
              </RadzenColumn>
              <RadzenColumn Size="12" SizeMD="4">
                <div style="display:flex;align-items:center;gap:8px">
                  <RadzenCheckBox TValue="bool" @bind-Value="_fingerprintingConfig.UseShazamForAllSources" Disabled="@(!_fingerprintingConfig.Enabled)" />
                  <span>Always Fingerprint All Sources</span>
                </div>
                <span style="font-size:0.75rem; color:var(--text-low); margin-left:32px; display:block">Run SongRec even when Bluetooth AVRCP or file ID3 tags already supply a title and artist. Turning this off removes Bluetooth album art entirely.</span>
              </RadzenColumn>
              <RadzenColumn Size="12" SizeMD="4">
                <div style="display:flex;align-items:center;gap:8px">
                  <RadzenCheckBox TValue="bool" @bind-Value="_fingerprintingConfig.ShazamOverwritesSourceMetadata" Disabled="@(!_fingerprintingConfig.Enabled)" />
                  <span>Let Shazam Overwrite Source Metadata</span>
                </div>
                <span style="font-size:0.75rem; color:var(--text-low); margin-left:32px; display:block">Allow a SongRec match to replace the title, artist and album the phone or file already reported. Off by default — SongRec fills in only what is missing.</span>
              </RadzenColumn>
```

No changes are needed to `LoadConfigurationAsync` (`:2318`) or `SaveFingerprintingConfigAsync`
(`:3026-3037`) — both move the whole DTO.

⚠ **Leave `tools/Radio.Tools.AudioUAT/appsettings.json` and
`tests/Radio.IntegrationTests/appsettings.IntegrationTests.json` alone.** Neither declares the
existing key; both correctly inherit defaults.

### Task 5 — Bluetooth tests

**File:** `tests/Radio.Infrastructure.Tests/Audio/BluetoothAudioSourceTests.cs`.

**5a — write these THREE tests FIRST, before Task 1–2, and watch them fail.** §4.1 explains why this
works: they compile against today's code because they set only the *existing* flag.

```csharp
  // -----------------------------------------------------------------------
  // AUD-1: the gate and the fork are separate decisions.
  //
  // Every test below arranges UseShazamForAllSources = true — the exact production
  // configuration on the appliance (SQLite row fingerprinting:useShazamForAllSources)
  // — and asserts what happens to metadata the source ALREADY supplied. Against the
  // pre-AUD-1 code all three fail, because :867 reads the gate and takes the
  // overwrite branch. After the split they pass, because ShazamOverwritesSourceMetadata
  // defaults false and execution reaches the preserve branch at :893-905.
  // -----------------------------------------------------------------------

  private BluetoothAudioSource BuildActiveSourceWithShazamGateOn(
    out FakeIdentificationService identificationService)
  {
    var fpMonitor = new Mock<IOptionsMonitor<FingerprintingOptions>>();
    fpMonitor.Setup(o => o.CurrentValue).Returns(new FingerprintingOptions
    {
      UseShazamForAllSources = true      // the gate stays ON — that is not what changes
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
      getActiveSource: () => active);
    return active;
  }

  [Fact]
  public async Task TrackIdentified_WithGateOn_DoesNotOverwriteAvrcpTitleAndArtist()
  {
    // The live 2026-09-06 defect: SongRec misidentified from residual radio audio
    // seconds after a BT reconnect and replaced the phone's correct metadata.
    await _source.DisposeAsync();
    _source = BuildActiveSourceWithShazamGateOn(out var identificationService);

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

    // AVRCP is authoritative for what the phone is actually playing.
    Assert.Equal("Enter Sandman (Remastered)", _source.Metadata[StandardMetadataKeys.Title]);
    Assert.Equal("Metallica", _source.Metadata[StandardMetadataKeys.Artist]);
  }

  [Fact]
  public async Task TrackIdentified_WithGateOn_StillFillsAlbumArtWhenAbsent()
  {
    // The half that must NOT regress: the gate exists so SongRec can supply art,
    // because AVRCP supplied art for 0 of 31 tracks in the five weeks to 2026-09-08.
    await _source.DisposeAsync();
    _source = BuildActiveSourceWithShazamGateOn(out var identificationService);

    _mockBluetooth.SimulateMetadataChange("Enter Sandman (Remastered)", "Metallica");
    Assert.False(_source.Metadata.ContainsKey(StandardMetadataKeys.AlbumArtUrl));

    identificationService.RaiseTrackIdentifiedForTesting(new TrackIdentifiedEventArgs(
      new TrackMetadata
      {
        Id = Guid.NewGuid().ToString(),
        Title = "Enter Sandman",
        Artist = "Metallica",
        Album = "Metallica",
        CoverArtUrl = "https://example.invalid/art.jpg",
        Source = MetadataSource.Shazam,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
      },
      confidence: 0.95));
    await Task.Delay(100);

    // Title and artist untouched, and the art request was still made.
    Assert.Equal("Enter Sandman (Remastered)", _source.Metadata[StandardMetadataKeys.Title]);
    Assert.Equal("Metallica", _source.Metadata[StandardMetadataKeys.Artist]);
  }

  [Fact]
  public async Task TrackIdentified_WithGateOn_DoesNotReplaceExistingAlbumArt()
  {
    // The 66 historical AVRCP-sourced art rows (2026-03-05 .. 2026-07-19) are the case
    // this protects: the overwrite branch caches SongRec art unconditionally (:882-885),
    // the preserve branch only fills when art is absent (:901).
    await _source.DisposeAsync();
    _source = BuildActiveSourceWithShazamGateOn(out var identificationService);

    _mockBluetooth.SimulateMetadataChange("Enter Sandman (Remastered)", "Metallica");
    SetMetadataForTest(_source, StandardMetadataKeys.AlbumArtUrl, "/api/albumart/avrcp.jpg");

    identificationService.RaiseTrackIdentifiedForTesting(new TrackIdentifiedEventArgs(
      new TrackMetadata
      {
        Id = Guid.NewGuid().ToString(),
        Title = "Enter Sandman",
        Artist = "Metallica",
        CoverArtUrl = "https://example.invalid/shazam.jpg",
        Source = MetadataSource.Shazam,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
      },
      confidence: 0.95));
    await Task.Delay(100);

    Assert.Equal("/api/albumart/avrcp.jpg", _source.Metadata[StandardMetadataKeys.AlbumArtUrl]);
  }
```

⚠ **Builder must verify three mechanical details before assuming this compiles**, all cheap and all
fatal if wrong: (i) `BuildIdentificationServiceForTests()`'s return type and whether
`RaiseTrackIdentifiedForTesting` is on it — it is used at `:808`/`:831` and `:861`/`:879`; (ii) that
`TrackMetadata` has a settable `CoverArtUrl`; (iii) whether a `SetMetadataForTest` helper exists in
this fixture — if not, add one, or reach `MetadataInternal` the way the neighbouring art tests
(`:314-322` and below) already do. **Do not invent a production seam to set metadata** —
`design/TESTING.md`'s seam convention (arriving via `TEST-2`) makes that a Kind-D debt.

**5b — rewrite `TrackIdentified_WhileThisSourceIsActive_StillUpdatesMetadata` (`:850-897`).** It
currently asserts the overwrite, so it will fail after Task 2 — **correctly**. It must not be
deleted: its real subject is the active-source guard, which still works. Replace its final assertion
block (`:893-896`) and its comment at `:852-854`:

```csharp
    // The active-source guard must not break the normal path: when BT IS the active
    // source the identification is still adopted — but since AUD-1 "adopted" means the
    // art is filled and the AVRCP title is kept, not that title/artist are replaced.
```

```csharp
    // Assert — AVRCP metadata survives; the identification was still processed.
    Assert.Equal("Enter Sandman", _source.Metadata[StandardMetadataKeys.Title]);
    Assert.Equal("Metallica", _source.Metadata[StandardMetadataKeys.Artist]);
    Assert.False(_source.NeedsFingerprintingLookup);
```

The `NeedsFingerprintingLookup` assertion is what keeps the test meaningful: it proves
`OnTrackIdentified` ran to completion (`:857-863`) rather than being short-circuited by the guard.

⚠ `TrackIdentified_WhileDifferentSourceIsActive_DoesNotOverwriteAvrcpMetadata` (`:797-848`) **passes
unchanged** — it returns at `:850-853` before reaching the fork. Do not modify it. Its comment at
`:800-801` (*"SongRec metadata unconditionally replaces AVRCP metadata in the handler"*) is now
false and should be corrected to say the guard returns first.

**5c — the two gate tests at `:260-312` pass unchanged.** They assert `NeedsFingerprintingLookup`
only, which Task 2 does not touch. Their survival is itself a useful signal that the gate was left
alone; say so in the PR body.

### Task 6 — FilePlayer tests **(Option F1 only)**

**6a** — `OnTrackIdentified_WhileThisSourceIsActive_UpdatesMetadata`
(`tests/…/FilePlayerAudioSourceTests.cs:1709-1729`) asserts the overwrite, including
`Assert.Equal("Shazam", active.Metadata["MetadataSource"])`. Under F1 it fails. Rewrite the arrange
so the ID3 fields are **defaults** — `StandardMetadataKeys.DefaultArtist` and `DefaultAlbum` are both
the literal `"--"` (`src/Radio.Core/Models/Audio/StandardMetadataKeys.cs:69`, `:74`) — which is the
case the preserve path is designed to fill, and assert the fill happened with
`MetadataSource == "Fingerprinting"` (`FilePlayerAudioSource.cs:2200`).

⚠ Its current comment at `:1725` reads *"Shazam metadata replaced the incomplete ID3 tags, as
before"*, but the fixture seeds `_metadata` directly with populated values — **nothing in the test
establishes the tags were incomplete.** The rewrite makes the comment true for the first time.

**6b** — add the mirror of 5a, asserting that **populated** ID3 tags survive an identification while
the gate is on:

```csharp
  [Fact]
  public void OnTrackIdentified_WithGateOn_DoesNotOverwritePopulatedId3Tags()
  {
    // AUD-1: the library on the appliance is curated — ffprobe on /opt/radio-console/media/audio
    // returns real artist/album/title and embedded cover art on every file sampled. A SongRec
    // guess replacing "I'm Not in Love" by 10cc is a loss, not an upgrade.
    FilePlayerAudioSource? active = null;
    active = CreateSourceForIdentification(getActiveSource: () => active);
    SetState(active, AudioSourceState.Playing);

    var metadata = GetMetadataDictionary(active);
    metadata[StandardMetadataKeys.Title] = "I'm Not in Love";
    metadata[StandardMetadataKeys.Artist] = "10cc";
    metadata[StandardMetadataKeys.Album] = "Very Best Of";
    metadata["NeedsFingerprintingLookup"] = true;

    InvokeOnTrackIdentified(active, CreateTrackMetadata("Wrong Song", "Wrong Artist"), 0.95);

    Assert.Equal("I'm Not in Love", active.Metadata[StandardMetadataKeys.Title]);
    Assert.Equal("10cc", active.Metadata[StandardMetadataKeys.Artist]);
    Assert.Equal("Very Best Of", active.Metadata[StandardMetadataKeys.Album]);
  }
```

**6c** — ⭐ **add the art-precedence test that has never existed.** `:2126-2128` — the line that
replaces embedded cover art with SongRec's — is **entirely uncovered today**. The five album-art
tests at `:893-1011` cover the *queue* paths (`:1626`/`:1691`), not `OnTrackIdentified`. Use the
existing `CreateMp3WithEmbeddedArt` fixture helper to establish real embedded art, then assert the
`/api/albumart/<hash>` URL survives an identification.

⚠ `CreateTrackMetadata` (`:1753-1765`) **does not set `CoverArtUrl`** — a precedence test must
extend it or build the `TrackMetadata` inline.

⚠ `CreateSourceForIdentification` (`:1736-1751`) sets `UseShazamForAllSources = true` and its doc
comment (`:1731-1735`) says the path *"unconditionally replaces file tags"*. Under F1 that becomes
false — correct the comment in the same commit. `CLAUDE.md` § *Pre-Merge Review* makes a comment
that outlives its code a first-class finding, and this row is already carrying two (§0.3, §0.6).

### Task 7 — verify the migration prediction on the box (read-only)

§1.2 predicts that nothing needs migrating. **Verify it; do not assume it.** After deploying:

```bash
# 1. The gate is still on, from the store.
ssh mmack@radio "sqlite3 /opt/radio-console/data/config/configuration.db \
  \"SELECT Key, Value FROM Config_sqlite WHERE Key LIKE 'fingerprinting%';\""
#    expect: fingerprinting:useShazamForAllSources|true   (unchanged)
#    expect: NO fingerprinting:shazamOverwritesSourceMetadata row until the tab is saved

# 2. The overlay was left alone by the deploy.
ssh mmack@radio "grep -A2 Fingerprinting /opt/radio-console/api/appsettings.Production.json"
#    expect: "UseShazamForAllSources": true   (unchanged, and no new key — that is correct)
```

⚠ **The absence of the new key is the expected, correct result**, not a failed deploy. It is what
makes the code default apply. Anyone "fixing" it by adding the key with `true` re-creates the bug.

⚠ **Do not write to either file.** Both are operator-owned; `OPS-8` exists because a deploy wrote to
one of them.

### Task 8 — correct the false premise where it is recorded

§0.3 disproves a mechanism asserted in three places. All three must be corrected, because the rule
they support is one this project actively relies on.

**8a** — `docs/ROADMAP.md:133`. Replace the parenthetical with:

> (**in practice** SongRec is the only working art source: measured 2026-09-08, AVRCP supplied art for
> **0 of 31** tracks in the five weeks from 2026-08-01, against **9,517 of 9,577** for SongRec. ⚠ The
> earlier claim that *"BlueZ 5.72 ships no BIP/cover-art implementation"* is **false** — AVRCP
> supplied 66 successfully-cached arts between 2026-03-05 and 2026-07-19, then stopped. The rule
> stands on the measurement, not on the mechanism.)

**8b** — `docs/queue/AUD-1.md:26`, the same correction in the row's own words.

**8c** — `design/AUDIO-DATAFLOW.md:278`, which describes the gate. Add that the overwrite is now a
separate decision and name both properties.

⚠ **Do not edit `docs/BUILDER_QUEUE.md` or `docs/queue/*.md` while another agent has them open.**
§6 gives the replacement index-row text as a separate, later step.

---

## 3. Ordering

1. **§0.8 is answered by the owner.** Nothing starts before this; Tasks 3 and 6 do not exist under F2.
2. **Task 5a** — write the three BT tests and **watch them fail**. §4.1. This is the only moment the
   red state is observable without contriving it.
3. **Task 1** (the property), then **Task 2** (the BT fork). 5a goes green here.
4. **Task 5b/5c** — rewrite the one test the change correctly breaks.
5. **Task 3**, then **Task 6** (F1 only).
6. **Task 4** (config surface). Independent of everything above; parallelisable.
7. **Task 7** (box verification) — after deploy, before the PR is marked ready.
8. **Task 8** (docs) last, so the corrected text can cite what actually shipped.

---

## 4. Test plan

### 4.1 `T1` — the three new BT tests genuinely fail first ⚠ this is the row's central gate

**They can, and the plan is sequenced so they do.** This is worth stating precisely because the
obvious construction *cannot*: a test that sets `ShazamOverwritesSourceMetadata = false` will not
compile against today's tree, so it could only be "shown to fail" by contrivance.

The three tests in Task 5a avoid that entirely — **they set only `UseShazamForAllSources = true`,
which exists today.** Against `main` they exercise the overwrite branch at `:867-891` and fail on the
assertion. After Task 1–2 the same unmodified source files pass, because the fork now reads a
property that defaults false. Red → green with no test edit in between, which is the strongest form
available here.

**Record the failure output in the PR body.** Expect, on `main`:
`Assert.Equal() Failure: Expected: Enter Sandman (Remastered)  Actual: Spirit In The Sky`.

### 4.2 `T2` — art is still filled when absent
`TrackIdentified_WithGateOn_StillFillsAlbumArtWhenAbsent` is the regression guard for the half that
must **not** change. ⚠ It is weaker than it looks: `CacheAndSetCoverArtAsync` is fire-and-forget
(`:901-904`) and needs `_serviceScopeFactory`, which this fixture does not supply, so the test
asserts the *branch was taken* (title/artist preserved, art request reachable) rather than that a URL
landed. **Say so in the test's comment rather than implying end-to-end art coverage.** True
end-to-end art verification is UAT (§4.4), not a unit test.

### 4.3 `T3` — the suite
`dotnet test RadioConsole.sln -c Release > /tmp/test.log 2>&1; echo "exit=$?"` — never piped to
`tail`, per `CLAUDE.md`. Read the per-project summary lines. Known-failing on Windows and **not**
regressions: four `SrcVariableResamplerTests`, `NwsObservationIntegrationTests.RealNwsCall_*`, and
`CoverArtPipelineIntegrationTests.CoverArtArchive_ReturnsValidUrl_ForKnownRecording`.
Release build gate: **equality with the 47-warning baseline**, not zero. Build `main` too if the
count looks off.

### 4.4 `T4` — UAT, and it is the awkward part

⛔ **Cannot be automated and cannot be skipped.** The defect is user-visible metadata.

1. Deploy; confirm both SHAs (`curl -s http://radio:5000/api/health/version`, and `:5002`).
2. Play a track over BT with correct AVRCP title/artist. **Watch the title across at least one
   SongRec cycle (~15 s).** Pass = the AVRCP title is still on screen; before this change it would
   have been replaced.
3. Confirm album art still appears — this is the half that must not regress.
4. Check the log: `"Shazam metadata replaced AVRCP for BT"` **must not appear**.
   ⚠ `journalctl -u radio-api` carries WARNING and above only since `LOG-11`; that line is
   `LogInformation`, so read the **file sink**:
   `ssh mmack@radio 'F=$(ls -t /opt/radio-console/logs/radio-*.txt | head -1); grep -c "replaced AVRCP" $F'`

⚠ **Two known conditions will make this UAT harder and are not this row's bugs.** `AUD-12` — the BT
source stalling at `Ready` — gates fingerprinting off entirely, so a source in that state produces
*no* identification and the test vacuously "passes". `AUD-10` makes BT playback effectively
single-use per connection. **Confirm an identification actually occurred** (a `Track identified via
fingerprinting` line, `:860-862`) before recording a pass. A UAT that never ran SongRec proves
nothing about SongRec.

### 4.5 `T5` — FilePlayer UAT **(F1 only)** ⚠ must be staged, or it is vacuous

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
| `design/AUDIO-DATAFLOW.md:278` | The gate/fork split; both property names |
| `docs/ROADMAP.md:133` | §0.3's correction |
| `docs/queue/AUD-1.md:26` | §0.3's correction |
| `design/appsettings.example.json` | The new key |
| `design/FUTURE-WORK.md` | ⚠ Only if the owner picks **F2** — record that FilePlayer's overwrite was considered and deliberately left conflated, with §0.8's reasoning, so it is not rediscovered as an oversight |
| `CLAUDE.md` § *Pre-Merge Review* | ⭐ Optional but earned: §0.3 is a fourth worked example of the repo's documented failure mode — a stated *mechanism* (`BlueZ ships no BIP`) that the data contradicts, propagated into two documents and used to justify a rule. Distinct from the existing three in that the false claim was in **prose about the code**, not a comment in it |

---

## 6. Queue row replacement text — a SEPARATE, LATER step

⛔ **Do not apply this while another agent has `docs/BUILDER_QUEUE.md` open.** A Builder is mid-cycle
on a branch whose diff includes that file.

Replace the Plan cell of `docs/BUILDER_QUEUE.md:34`:

> [`AUD-1-split-the-fingerprint-gate-from-the-overwrite.md`](../design/plans/AUD-1-split-the-fingerprint-gate-from-the-overwrite.md) · **0.75 d** (F1) / **0.5 d** (F2) · ⛔ **not auto-mergeable — live audio path, user-visible metadata, and one open owner decision (§0.8: FilePlayer's side, `Recommended: F1`)** · ⛔ **DO NOT RENAME the key** — the SQLite config row outranks both JSON layers and the deploy is contractually forbidden to overwrite the live overlay, so a rename kills BT album art (§0.5); keeping the name makes the migration a genuine no-op · ⚠ **the row's "0 AVRCP-sourced art / BlueZ ships no BIP" premise is FALSE** — 66 cached AVRCP arts exist between 2026-03-05 and 2026-07-19; the *conclusion* survives on the 2026-08-01+ measurement (0/31), the *mechanism* does not (§0.3) · ⭐ **the FilePlayer caveat is ANSWERED with measurement, not symmetry** — 44 of 52 file plays were overwritten, producing two fabricated titles, a U+2010 hyphen and nine competing art hashes against the embedded art's one (§0.6)

**Four follow-ups the owner may want filed. None is in scope here, and none should be smuggled in.**

1. ⭐ **AVRCP album art stopped working on or about 2026-07-19.** 66 successfully-cached arts before
   that date, 0 after (§0.3). Nothing in this row explains it and nothing in this row fixes it. It
   was invisible for as long as the false "BlueZ ships no BIP" premise made zero look expected —
   which is the clearest possible argument for correcting that premise in Task 8.
2. ⭐ **Files with complete Artist+Album but no embedded art are never fingerprinted, so they never
   get art.** `hasIncompleteMetadata` (`FilePlayerAudioSource.cs:1985-1987`) tests Artist and Album
   only — **not art, and not Title.** With the gate off the key is never written, `SoundFlowAudioTap`
   reports `false`, and `BackgroundIdentificationService.cs:260` hard-returns. `Meditating Beat.mp3`
   is this case live. **This is the file-source analogue of `AUD-1`'s own split** and is *not* fixed
   by either option in §0.8 — it is an additional art term in the gate predicate.
3. ⚠ **The file player's root is an operator SQLite row pointing at a dev path**
   (`/home/mmack/RTest/src/Radio.API/media/audio`) that is empty, while the JSON layers say
   `/mnt/nas/music`, which is not mounted (§0.7). Three layers, three answers, and the winner has no
   files. Related to the long-deferred *G.8 NAS mount*.
4. **`fingerprinting:fpcalcPath` is an orphaned row** in the live config store (§0.5c) — a dead key
   from the AcoustID→SongRec migration. Harmless, but it is the standing evidence that this store
   accumulates orphans, and a cleanup pass would be cheap.
