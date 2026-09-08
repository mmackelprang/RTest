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
