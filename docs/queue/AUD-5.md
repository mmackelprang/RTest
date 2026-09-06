# AUD-5 — A Cast connection that is no longer current can persist its volume as the system master volume.

> Queue dossier for row **`AUD-5`** of [`BUILDER_QUEUE.md`](../BUILDER_QUEUE.md).
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
| Plan | [`AUD-5-stale-cast-volume-persists-as-master.md`](../../design/plans/AUD-5-stale-cast-volume-persists-as-master.md) · **both halves, and the plan says which is load-bearing:** a generation re-check before the fire (closes the race, narrows but does not empty the window) **and** the subscriber ignoring `IsInitialSync` (removes the harm, no timing dependence). **0.5 d.** ⚠ Task 1 — the forensic log read — must run **before** the code lands: Task 3 retires the `"Synced volume from Cast device … initial: True"` message the row tells you to grep for. |
| Spec / handoff | _no spec doc — the diagnosis is in this row_ · **provenance: PR #473's pre-merge review**, which found it while checking whether #473's own comment was true · the mechanism is now documented in-tree at [`GoogleCastOutput.cs:95-104`](../../src/Radio.Infrastructure/Audio/Outputs/GoogleCastOutput.cs) · origin commit `b420edc` (2026-02-11) |
| Depends on | — _(no row dependency; claimable now. **⚠ Touches `GoogleCastOutput.cs`, which PR #473 (`0870410`) reflowed: its header comment grew by exactly +66 lines, so EVERY anchor below `:32` in that file moved +66.** The citations in **this** row are already post-#473 and verified; any citation copied from the **`AUD-3`** row is pre-#473 and is not. Also touches `AudioStateUpdateService.cs`, which no other row claims. **No file overlap with `AUD-1`, `AUD-2` or `AUD-4`**, so it can run alongside any of them.)_ |
| Branch | `fix/cast-initial-volume-sync-generation-check` |

## Detail

**A Cast connection that is no longer current can persist its volume as the system master volume.**

**User-visible, silently persistent, and cheap to fix — but ⚠ the obvious fix is the wrong one, so read the "do not widen the lock" note before writing a line.**

**The mechanism is verified end-to-end against `main` @ `0870410`; unlike `AUD-2` this row needs no investigation phase, and unlike most of the tranche it must not be re-brainstormed.** `GoogleCastOutput.SyncInitialVolumeAsync` (`src/Radio.Infrastructure/Audio/Outputs/GoogleCastOutput.cs:1543`) is a **read** of the Cast device's status, not a command — but its **success path fires `CastVolumeChanged`** (`:1568-1573`). That event has **exactly one subscriber in the whole solution**: `AudioStateUpdateService.OnCastVolumeChanged` (`src/Radio.API/Services/AudioStateUpdateService.cs:819`; subscribed `:118`, unsubscribed `:935` — grep-verified, no other handler anywhere in `src/` or `tests/`), which **unconditionally sets `AudioManager.MasterVolume`** (`:831`) and `IsMuted` (`:839`).

**And that setter PERSISTS.** `AudioManager.MasterVolume`'s setter (`src/Radio.Infrastructure/Audio/Services/AudioManager.cs:74-82`) calls `_preferencePersistence?.ScheduleVolumePersist()` at `:80`, which debounces 500 ms and writes to the config store (`AudioPreferencePersistence.cs:46-62`).

**So a stale event does not merely move a slider — it rewrites saved state that survives a restart.**

**`IsInitialSync` exists on the event args (`GoogleCastOutput.cs:1829`) and is NEVER BRANCHED ON:** its only two uses in the entire codebase are string interpolation into log messages (`AudioStateUpdateService.cs:834`, `:842`). An initial *status read* is therefore treated identically to the user turning the knob on the Chromecast.

**The window.** `SyncInitialVolumeAsync` has a **single** call site — `ConnectAsync:634` — and it runs **after** `TryPublishConnectionAsync` (`:614`) has already validated the connection generation, so at the moment of the call the connection genuinely *is* current. The exposure opens across the `await receiverChannel.GetChromecastStatusAsync()` network round-trip at `:1555`: **nothing re-checks `_connectionGeneration` between the status response arriving and the event fire at `:1568`.** Four sites can bump the generation inside that window — a newer connect's claim (`:539`), `DisconnectAsync` (`:712`), `InitializeAsync` (`:248`) and `DisposeAsync` (`:1720`) — after which the volume of a connection that **has already been superseded** is written to master volume and persisted.

**⚠ THE OBVIOUS FIX IS BOTH WRONG AND RISKY — stated first because someone will otherwise reach for it. Do NOT widen `_lifecycleLock` to cover `SyncInitialVolumeAsync`.** The exposure is the **event fire**, not the field read, so widening the lock does not close it — and it would hold the lock across a SharpCaster network call, which is exactly the hang `AUD-3`'s fix was built to avoid. The file says so in as many words at `GoogleCastOutput.cs:49-53`: a connect can park for tens of seconds inside calls that do not observe cancellation, and a teardown queueing behind it converts a data race into a **multi-second hang on the output picker**.

**The correct shape is a generation re-check immediately before the fire** — the same check `TryPublishConnectionAsync` already performs at `:675`, applied one step later.

**It is cheap to plumb: `myGeneration` is already in scope at the call site** (captured at `ConnectAsync:539`, still live at `:634`), so this is a parameter plus a short await-free critical section, not new machinery.

**Second half, and arguably the better one: branch on `IsInitialSync` so a status read is not treated as a user command at all.** The flag was built for precisely this distinction and has never been used for anything but logging.

**Decide deliberately and say which way in the plan** — "re-check the generation," "never sync master volume from an initial read," or both; they are separable, and only the first is strictly required to close the race.

**⚠ THIS PREDATES PR #468. It is NOT a regression from the Cast synchronization work and must NOT be "fixed" by unpicking it.** All three links — the event fire in `SyncInitialVolumeAsync`, the `IsInitialSync` flag, and `OnCastVolumeChanged`'s unconditional `MasterVolume` write — landed **together** in commit **`b420edc`, 2026-02-11** (*"feat: Fix Cast audio streaming and audio pipeline optimizations"*, whose own message reads *"Add bidirectional volume sync between app and Cast device"*) — **six months before #468.** Verified two ways: `git log -S "IsInitialSync" -- src/` returns `b420edc` and nothing else, and `git merge-base --is-ancestor b420edc 8b1ce0a` confirms the ordering.

**#468 created the generation counter this fix will use; it did not create the hole.**

**⚠ RECORDED SO IT IS NOT RE-INVESTIGATED: the three unsynchronized sibling reads in this file are NOT this bug, and were already assessed.** `SyncInitialVolumeAsync` (`:1545`), `SetCastVolumeAsync` (`:1632`) and `SetCastMuteAsync` (`:1657`) each null-check `_client` and then dereference it on a second, separate read.

**Assessed and found safe** on three grounds: `_client` is **never set back to null** once assigned (exactly two assignment sites, `InitializeAsync` and `TryPublishConnectionAsync`, both non-null; `DisconnectAsync` and `DisposeAsync` deliberately leave it set), every dereference sits inside a `try`/`catch`, and the client-identity race is **self-neutralizing** because `ConnectAsync` disconnects any old client.

**PR #473 wrote that reasoning into the file itself** — `GoogleCastOutput.cs:32-104`, specifically the PRECONDITION block at `:63-81` — so it is on the record and needs no re-deriving.

**This row's defect is DOWNSTREAM of those reads, not in them:** the read yields a perfectly valid client; what goes wrong is that its *result* is published as a user-authored volume change after the connection stopped being current.

**🔵 A HYPOTHESIS TO CHECK — deliberately NOT stated as a diagnosis, and the row does not depend on it.** The owner has been observing volume behaving oddly on this system, and the 2026-08-10 journal carries master-volume values ratcheting down through `0.03` / `0.02` / `0.01` / `6.85E-09`.

**Those particular values are attributable to a slider drag during the crash investigation and are NOT evidence for this row — do not cite them as a repro.** But a superseded Cast connection that can write *and persist* master volume is a plausible source of *"my volume changed by itself"* reports in general, and that is worth **checking rather than assuming**.

**The forensic signal already exists and costs nothing to look for:** `AudioStateUpdateService.cs:832-834` logs `"Synced volume from Cast device: {Volume:P0} (initial: {IsInitial})"` on every write. Grep journald for that line with `initial: True`, correlate against Cast connect/teardown churn and against play/volume history, and the hypothesis confirms or dies in one pass.

**⚠ Do NOT let a negative journald result close this row** — the mechanism is real whether or not it has yet fired in production, and the fix is worth shipping either way.

**Test gap, stated because it is total:** `CastVolumeChanged` and `SyncInitialVolumeAsync` have **zero test coverage anywhere in the solution** (grep-verified across `tests/`). `GoogleCastOutputConcurrencyTests.cs` covers the publish race only — its two tests (`TeardownDuringConnect_DoesNotCorruptTheInFlightConnect:35`, `ConnectThatSucceedsButLostTheRace_DiscardsItselfAndStaysUsable:97`) stop **before** the volume sync.

**Note the existing seam does not reach this window:** `ConnectRaceHookForTests` (`:120`) fires at receiver resolution, which is **too early** — a test for this must interleave a teardown during the *status read*, i.e. after publish. Whether that justifies a **fourth** `internal` seam is a real question and it is the one `TEST-2` now owns as a pattern — **check `TEST-2` first, and if you do add a seam, record it there.**

_**Anchors re-verified 2026-09-05 against `main` @ `656f58e6`. Every `GoogleCastOutput.cs` citation above is byte-exact. Every `AudioStateUpdateService.cs` citation has DRIFTED and is corrected here:** subscribe `:118` → **`:121`**, handler `:819` → **`:851`**, `MasterVolume` write `:831` → **`:863`**, log `:832-834` → **`:864-866`**, `IsMuted` write `:839` → **`:871`**, unsubscribe `:935` → **`:967`**. **And the forensic instruction is stale:** `"Synced volume from Cast device"` is logged at **Information** from `Radio.API`, whose console sink has been Warning-restricted since `LOG-11` (2026-09-02) — so it is in the **file sink**, `/opt/radio-console/logs/radio-*.txt`, and **not** in `journalctl -u radio-api`. Also: the row calls the `MasterVolume` write "unconditional"; it is guarded by `Math.Abs(current - e.Volume) > 0.01f` at `:861`, which checks nothing about the connection — the conclusion stands, the word does not, and a test asserting "never written" must use a value more than 0.01 away or it passes vacuously._
