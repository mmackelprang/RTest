# `AUD-17` — AVRCP album art has never worked: we read the wrong attribute from the wrong interface

[← Builder Queue index](../BUILDER_QUEUE.md)

🟡 **P2.** Filed 2026-09-08, then **substantially rewritten the same night after both of its original
claims were measured and found false.** The retraction is kept in full at the bottom, because the way
it went wrong is the useful part.

## The defect

`src/Radio.Infrastructure/Platform/Bluetooth/LinuxBluetoothService.cs:2761-2765`:

```csharp
// MPRIS/BlueZ exposes album art via "ArtUrl" or "mpris:artUrl"
var artUrl = GetString(track, "ArtUrl");
if (string.IsNullOrEmpty(artUrl)) artUrl = GetString(track, "mpris:artUrl");
```

The `track` dictionary comes from a proxy created at `:2545` against **`org.bluez.MediaPlayer1`**
(`Linux/BluezInterfaces.cs:49`, `:102`). **BlueZ publishes the cover-art attribute on that interface
as `ImgHandle`.** `ArtUrl` and `mpris:artUrl` are *MPRIS* names — synthesised by BlueZ's separate
`tools/mpris-proxy`, which resolves `ImgHandle` over OBEX BIP and republishes it. **We do not run
mpris-proxy.**

So both reads always return empty, `BluetoothPlaybackMetadata.AlbumArtUrl` is always null, the branch
at `BluetoothAudioSource.cs:814` never fires, and **`CacheAvrcpArtAsync` (`:926`) has never
executed.** This is dead-on-arrival code, not a regression.

⚠ **The comment is the defect.** It conflates MPRIS with BlueZ, and the whole file does — `:339` and
`:358` log *"No MPRIS media player attached"* about a BlueZ interface. This is exactly the
over-claiming-comment failure mode `CLAUDE.md` § *Pre-Merge Review* exists for: it made a dead path
look live for months and sent two separate rows chasing the wrong mechanism.

## The measurement, taken read-only on `radio` 2026-09-08

`/opt/radio-console/data/fingerprints/fingerprints.db`, `sqlite3 -readonly`:

| Source | rows | `/api/albumart/` | `file://` | `http` |
|---|---|---|---|---|
| Shazam | 43,405 | 43,013 | **0** | 69 |
| Manual | 818 | 69 | **0** | 8 |
| Avrcp | 713 | 66 | **0** | 0 |
| FileTag | 162 | 146 | **0** | 0 |
| AcoustID | 112 | 0 | **0** | 93 |

⭐ **`file://` is zero across all 45,210 rows, every source.** `PlayHistoryTracker.cs:733-737` writes
the raw `e.AlbumArtUrl` unfiltered at row creation, so if AVRCP had ever supplied *any* URL it would
appear here. It never has. That is the empirical confirmation, independent of reading the code.

## ⚠ `Source` records the TITLE's provenance, not the art's

`TrackMetadata.Source` is set from title/artist provenance.
`BluetoothAudioSource.UpdateRecentPlayHistoryCoverArtAsync` (`:1070-1112`) writes `CoverArtUrl` via
`Track with { CoverArtUrl = … }` and **never touches `Source`**. Its only live caller is the SongRec
path (`CacheAndSetCoverArtUrlAsync:1056` ← `OnTrackIdentified:884`).

Of the 66 `Avrcp` rows carrying art: **26 share a filename with a `Shazam` row**, 1 with `Manual`,
39 appear only on `Avrcp` rows. Filenames are content-addressed — `AlbumArtCacheService.cs:46`/`:80`
compute `ComputeHash(imageData)` — so **a shared filename means byte-identical image data.** The 39
are consistent with SongRec art landing on the AVRCP-titled row without a separate `Shazam` row ever
being created.

## Scope questions for the plan

1. **Is fixing it even wanted?** `ImgHandle` is an OBEX BIP handle, not a URL. Resolving it needs a
   BIP client and **BlueZ 5.72 ships none** — the AVRCP cover-art patches landed upstream ~Sept 2024.
   So the options are (a) implement BIP retrieval, (b) **delete the dead path** and document that BT
   art comes from fingerprinting, or (c) upgrade BlueZ. **(b) is probably right**: SongRec already
   supplies art on ~99% of rows, and dead code that looks live has now cost two rows.
2. **Whatever is chosen, fix the comment and the log strings.** A path that stays must stop claiming
   MPRIS; a path that goes must not leave `:339`/`:358` describing a player that was never attached.
3. **Does anything else read MPRIS names off a BlueZ interface?** The conflation is file-wide; check
   before assuming this is the only site.

## Verification

Mostly static — the claim is about which string is read from which interface. To confirm live, with
the phone connected and playing:

```
busctl --system introspect org.bluez /org/bluez/hci0/dev_<MAC>/player0
```

Expect **`ImgHandle` present and `ArtUrl` absent** in the `Track` dict. ⚠ Use `bluetoothctl select
78:20:51:F5:FB:A7` first — `hci0` is the music adapter and the default may be wrong with two
adapters.

⚠ Needs the owner's phone. **Not auto-mergeable** if the path is changed rather than deleted.

---

## ⛔ Retracted: the original row claimed a July regression. Both claims were false.

Filed 2026-09-08 asserting *"AVRCP album art worked for four and a half months and stopped around
2026-07-19."* Measured the same night; neither half survived.

**Claim 1 — "66 successfully-cached AVRCP arts."** False. `Source` is the title's provenance, not the
art's (above). 26 of the 66 are provably byte-identical SongRec images.

**Claim 2 — "stopped around 2026-07-19."** Not supported. The monthly rate declines smoothly:

| Month | rows | with art | rate |
|---|---|---|---|
| 2026-03 | 522 | 58 | 11.1% |
| 2026-05 | 115 | 6 | 5.2% |
| 2026-07 | 45 | 2 | 4.4% |
| 2026-08 | 16 | 0 | — |
| 2026-09 | 15 | 0 | — |

**0-of-16 against a 4.4% base rate is p ≈ 0.49.** ⚠ **And the boundary was post hoc** — chosen as the
date of the *last success*, then tested for "zeros after it". Every series has a last success
followed only by zeros; that construction cannot fail.

⛔ **Consequence: do NOT "correct" `docs/queue/AUD-1.md:26` or `docs/ROADMAP.md:133` as the original
row instructed.** Their *conclusion* — AVRCP cannot supply album art on this box — is **right**. Only
their stated *mechanism* (BlueZ 5.72 ships no BIP) is incomplete: the binding constraint is our own
attribute name, and BIP is a second blocker behind it. Amend the mechanism; do not reverse the
conclusion.

⭐ **The lesson, and it cuts both ways.** A measurement read through a false premise **confirms the
premise instead of contradicting it.** `AUD-1` said "AVRCP can never supply art", so a zero looked
expected. This row then said "66 arts disprove that", so a decline looked like a regression. Both
readings came from not checking what the column actually records.

---

## ⛔ 2026-09-10 — **ART IS NOW APPEARING IN BT MODE, AND THIS ROW IS *NOT* FIXED.** Read this before closing it.

The owner observed album art appearing during Bluetooth playback and asked whether it comes from
fingerprinting. **It does.** Measured on the live box, same session:

```
Identified track: 'Heart and Soul' by 'Huey Lewis & The News' (confidence: 80 %, source: "SongRec", coverArt: /api/albumart/0f924e4c2dd0504e.jpg)
Cover art found for 'Heart and Soul' by 'Huey Lewis & The News': /api/albumart/0f924e4c2dd0504e.jpg
```

That is the exact URL the panel was displaying, and its source is **SongRec**. ⛔ **Zero
`CacheAvrcpArt` lines appear in the log. The AVRCP cover-art path still has never executed** — which
is precisely what this row is about.

## ⭐ THE TRAP: A WORKING SYMPTOM THAT READS AS A FIXED ROW

**Art appearing in BT mode looks exactly like this row resolving itself.** It has not. What is
working is the **fingerprinting** path; what this row tracks — `LinuxBluetoothService` reading MPRIS
names (`ArtUrl` / `mpris:artUrl`) off an `org.bluez.MediaPlayer1` proxy that publishes **`ImgHandle`**
— is **still dead and has never run**.

⛔ **DO NOT CLOSE THIS ROW ON THE OBSERVATION THAT ART APPEARS.** The correct test is whether
`CacheAvrcpArtAsync` executes, not whether an image reaches the panel. ⚠ **They are different
subsystems that populate the same UI element**, which is the whole reason this is easy to get wrong.

⭐ **Consequence worth stating plainly: this row is only observable when fingerprinting FAILS.** With
SongRec succeeding on ~80 % confidence, the AVRCP path is masked on every track it identifies. **Any
future "AVRCP art works now" sighting must be checked against the log before it is believed.**

⚠ **And `AUD-1` is the reason it stays masked**: `UseShazamForAllSources: true` makes SongRec run on
BT and *replace* AVRCP metadata — confirmed live the same day. **If `AUD-1`'s split lands, the
relative visibility of this row changes**, and a re-test then is worth more than one now.

---

## ⭐ Feasibility settled 2026-09-29 (read-only, on the box) — the phone offers no cover art to BlueZ

The row's first question was *"whether to fix it at all"*. Measured with the owner's Pixel 10 Pro XL connected over `hci0`:

- `busctl get-property … /player0 org.bluez.MediaPlayer1 Track` → `Title`, `TrackNumber`, `NumberOfTracks`, `Duration`, `Album`, `Item`, `Artist`. **No `ImgHandle`.**
- `MediaPlayer1` properties: `Browsable Device Equalizer Name Playlist Position Repeat Scan Searchable Shuffle Status Subtype Track Type`. **No `ObexPort`** (how BlueZ exposes the AVRCP cover-art OBEX channel).
- BlueZ **5.72**; `obexd` running as `/usr/libexec/bluetooth/obexd` (no `-E`), exposing only generic `org.bluez.obex.Client1`. `mpris-proxy` and `obexctl` are installed but unused.

So reading the right attribute would still read nothing: the cover-art channel is never negotiated. Making it negotiate means enabling BlueZ's experimental AVRCP cover-art support (`main.conf` `Experimental = true` and `obexd -E`) — a system-wide change to a Bluetooth stack RotaryPhone shares, so it goes through `RADIO-CONSOLE-BT-AUDIO-BOUNDARY.md` first, and even then the phone must support it.

## Owner decision — ✅ answered 2026-09-30: A (see the last section)

| Option | What it means |
|---|---|
| **A (recommended) — close the dead path** | Remove the never-firing `ArtUrl`/`mpris:artUrl` read and `CacheAvrcpArtAsync`'s unreachable branch; fix the "MPRIS" comments and log lines (`:339`, `:358`) that describe a BlueZ interface. BT art keeps coming from SongRec, which works (every BT track on 2026-09-29 got art ~15 s in; see `AUD-77` for the one straddled-sample miss). |
| B — experiment | Enable BlueZ experimental cover art on the box behind the boundary-doc protocol, confirm the Pixel then publishes `ImgHandle`/`ObexPort`, and only then implement a BIP fetch. |

## 📝 2026-09-30 — owner: "Bluetooth album art passes" (via song recognition)

Owner 2026-09-30 ([`RETURN-CHECKLIST.md`](../uat/RETURN-CHECKLIST.md) evening batch): *"Bluetooth album art passes."* The art arrives through song recognition (SongRec), not AVRCP — the trap described above — so **this does not close the row.** The A/B decision above (A: close the dead AVRCP cover-art path; B: experiment with BlueZ's experimental cover art) is still pending.

## ✅ 2026-09-30 — owner ruling: option A, close the dead AVRCP cover-art path

Owner 2026-09-30 ([`RETURN-CHECKLIST.md`](../uat/RETURN-CHECKLIST.md) evening batch §D): *"AUD-17 recommendation is fine."* That is **option A**: close the dead AVRCP cover-art code path. BT album art continues to come from song recognition (SongRec), which the owner passed the same day. Option B (BlueZ experimental cover art) is declined; nothing goes to the boundary doc.

**The row is now buildable as a small removal PR, sequenced with Phase 2i (confirm-or-close) in [`HANDOFF-GA-CLOSEOUT.md`](../HANDOFF-GA-CLOSEOUT.md).** Scope, from option A above:

1. Remove the never-firing `ArtUrl` / `mpris:artUrl` read off the `org.bluez.MediaPlayer1` proxy (`LinuxBluetoothService.cs:2761-2765` as of 2026-09-08 — re-anchor by symbol before editing) and `CacheAvrcpArtAsync`'s unreachable branch.
2. Fix the comments and the `:339` / `:358` log strings that say "MPRIS" about a BlueZ interface.
3. Do not touch the SongRec art path, and ⛔ do not "correct" `queue/AUD-1.md:26` or `ROADMAP.md:133` (see the row's spec cell).

**Verification:** the Release build stays at the host's baseline (`--no-incremental`); the suite is green apart from the known-failing set; after deploy, BT art still arrives via song recognition on a played track (the owner's 2026-09-30 pass is the reference). Deletion only, no new behaviour — ⚠ still confirm on the box that BT art appears, because the removed code shares a file with the live AVRCP metadata path.

## ✅🔬 2026-09-30 — shipped (Builder, Phase 2i): the dead read is gone; the consumer was not dead

**Removed:** `LinuxBluetoothService.UpdateMetadata`'s `ArtUrl` / `mpris:artUrl` read. In its place is
a comment saying why Linux reads no art: `org.bluez.MediaPlayer1` offers only the OBEX handle
`ImgHandle`; BlueZ exposed neither `ImgHandle` nor `ObexPort` on the box (2026-09-29, experimental cover art off — whether the phone would offer it is untested); and the owner
declined option B. **Linux now never sets `BluetoothPlaybackMetadata.AlbumArtUrl`**, and the property's
new XML doc says so. The two `"No MPRIS media player attached"` warnings (formerly `:339`/`:358`, now
`:491`/`:510`) name `org.bluez.MediaPlayer1` instead. `FingerprintingOptions`' remark, which quoted
the MPRIS names, is corrected too.

### ⚠ The row's premise was half wrong: `CacheAvrcpArtAsync` was not unreachable

It was unreachable **on Linux**, but it is a platform-neutral consumer of `AlbumArtUrl`, and two other
producers feed it:
- `WindowsMediaSessionWatcher`, from the SMTC thumbnail;
- `MockBluetoothService`, through which `AUD-1`'s *"source-supplied art is never replaced by an
  identification"* tests (`Aud1_AvrcpArt_RestoredFromCache_IsStillNotReplaced`,
  `Aud1_ArtAlreadyInPlace_IsNotReplaced`, `MetadataChanged_WithHttpsArtUrl_*`,
  `MetadataChanged_WithFileSchemeArtUrl_*`) run.

Deleting it would have removed the only producer of source-supplied art. That would leave `AUD-1`'s
source-art rule, in the fingerprinting fill path this row was told not to touch, as constant-false
code with no tests. So it is **kept, renamed `CacheSourceSuppliedArtAsync`**, and its comments and log
lines now say it never runs on the appliance. The fill path (`OnTrackIdentified`,
`CacheAndSetCoverArtUrlAsync`) received comment edits only.

**Found along the way → [`AUD-88`](AUD-88.md):** on Windows the consumer silently drops the SMTC art. The
watcher hands on an already-local `/api/albumart/...` path, and `SaveFromUrlAsync` (whose `HttpClient`
has no `BaseAddress`) returns null for it. This is dev-host only.

### Verification

- Release build `--no-incremental`: **46 warnings, 0 errors** (Windows baseline, unchanged). The suite is
  green apart from the six known `SrcVariableResamplerTests`.
- **No new test.** The removed read is inside a private method on a D-Bus proxy path that has no seam,
  and the kept consumer's behaviour is unchanged and already covered by the tests named above.
- **After deploy (agent):** the post-deploy evidence is recorded in the next section once it is taken.
- **Owner check left:** on a Bluetooth track, album art still appears about 15 s in (via song
  recognition). The owner's 2026-09-30 *"Bluetooth album art passes"* is the reference. The removed code
  shared a file with the live AVRCP metadata path, which is why this check is still worth doing.

## 🔬 2026-09-30 — after deploy (agent, box `radio`)

Measured by the batch B session that merged and deployed [#742](https://github.com/mmackelprang/RTest/pull/742)
(squash `a86349f`); recorded here by the next Builder in the same batch.

- **Deploy:** `Deploy-ToLinux.ps1` printed `Verified: API/Web is running commit a86349f` for both services
  and `Kiosk is live`. `systemctl show radio-api radio-web -p NRestarts` was **0 before and 0 after**. The deploy's own stop/start resets that counter, so "0 after" covers the time since the 19:07 EDT start, not the deploy window.
  Re-read 2026-09-30 ~19:20 EDT: `/api/health/version` still reports `a86349f`, `NRestarts=0` on both units.
- **`/api/bluetooth/status`:** byte-identical before and after the deploy.
- **`/api/sources`:** the same five primary sources before and after (`Radio`, `Vinyl`, `FilePlayer`,
  `GenericUSB`, `Bluetooth`).
- **Start-up Warnings:** the set logged after this deploy's start is a subset of the previous deploy's.
- **Not exercised:** no phone was connected, so no Bluetooth track played and album art was not checked.

**Owner check still open (the row stays ✅🔬):** play a Bluetooth track from the phone. Album art should
still appear about 15 s in, through song recognition.
