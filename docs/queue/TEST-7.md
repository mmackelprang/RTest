# TEST-7 — A `TimeProvider` seam for `NowPlayingPanel`'s two hardcoded debounce timers.

> Queue dossier for row **`TEST-7`** of [`BUILDER_QUEUE.md`](../BUILDER_QUEUE.md).
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
| Plan | [`design/plans/TEST-7-timeprovider-seam-for-nowplayingpanel.md`](../../design/plans/TEST-7-timeprovider-seam-for-nowplayingpanel.md) |
| Spec / handoff | [punch list §4.6 `TEST-7`](../HANDOFF-GA-PUNCH-LIST.md) |
| Depends on | — |
| Branch | `fix/test-7-nowplayingpanel-timeprovider-seam` |

## Detail

**A `TimeProvider` seam for `NowPlayingPanel`'s two hardcoded debounce timers.**

📌 **Promoted from a note in § Documented fast-follows on that note's own instruction** (*"Planner: this is a row, not a note."*), 2026-09-03. Three tests in `tests/Radio.Web.Tests/Components/Shared/NowPlayingPanelVolumeDebounceTests.cs` — `VolumeDrag_PersistsThePreferenceOnce` (`:129`), `VolumeDrag_PersistsTheFinalValue` (`:165`) and `SeparatedVolumeChanges_EachPersist` (`:192`) — sequence `await Task.Delay(1500)` against `NowPlayingPanel`'s own **300 ms** `System.Threading.Timer` (`src/Radio.Web/Components/Shared/NowPlayingPanel.razor:1054-1078`) with **no rendezvous**.

⚠ **Be precise about which assertion is at risk.** `Assert.Equal(88d, pending)` is **safe** — `_pendingVolumePreference` is assigned synchronously inside `QueueVolumePreferenceSave` before the awaited call returns, so starvation cannot break it. It is **`Assert.Equal(1, CountConfigWrites(handler))`** that is unsafe, and it fails in **both** directions: **undershoot to 0** if the timer callback plus the *two* HTTP hops it makes (`ConfigurationApiService.cs:83-97` GETs before it POSTs) do not all drain inside 1500 ms, and **overshoot to 2** if a stall inserts >300 ms between the un-slept setup invokes.

**Same shape as `TEST-4`, lower flake rate** — a 5x margin (1500/300) rather than 3x on a 20 ms poll — and the overshoot mode is the one `TEST-4` did not have.

**This one needs production code to change, which is why it was not folded into `TEST-4`:** `VolumePreferenceDebounce` is `private static readonly` and the timer is a raw `System.Threading.Timer`, so `NowPlayingPanel` must take an injectable `TimeProvider` first.

**The house idiom already exists in `Radio.Web`** (`EncoderHudService.cs:32,45-48`) and `FakeTimeProvider` is already used in this same test project (`EncoderHudServiceTests`, `SleepTests`).

⚠ **Advancing a fake clock is NOT sufficient on its own** — the callback is `async void` over two awaited HTTP hops, so the fix still needs a completion rendezvous before asserting on the write count.

**Also pull in the panel's second hardcoded timer** (`_gainDebounceTimer`, 200 ms, `:891-909`) — same exposure, same seam. Sibling `VolumeDrag_StillAppliesEveryTickToTheAudioEngine` (`:150`) is genuinely safe and needs no change.

⚠ **PLANNED 2026-09-05, and the seam is NOT the one the row assumed.** `EncoderHudService`'s idiom is an optional **constructor** parameter, and a Blazor component has no constructor — the renderer activates it parameterlessly. `@inject TimeProvider` is the obvious substitute and is wrong twice: it is a *required* resolve against a container that registers `TimeProvider` **nowhere, in either host, deliberately** (`AudioServiceExtensions.cs:414-415`, `:458-459`, `:494-496`, with a standing check at `EncoderHudServiceTests.cs:466-485`), and it leaves `Clock` **null on a bare `new NowPlayingPanel()`** — which is exactly how this row's own test file builds the panel (`:78`). The plan lands `internal TimeProvider Clock { get; set; } = TimeProvider.System;` instead: zero DI change, zero fixture change, and `Radio.Web.csproj:31` already grants the test project `InternalsVisibleTo` under a comment blessing this use. Plan §6.3 carries the `@inject` variant verbatim if the owner prefers it. ⚠ **A THIRD raw timer in the same file is deliberately out of scope** — `_nowPlayingPollTimer` (`:507`, created `:542-546`, 60 s/60 s) is armed in `OnInitializedAsync`, the one path all **45** bUnit renders of the panel and of `Home` traverse; filed in `design/FUTURE-WORK.md`. ⚠ **The gain timer has NO tests today** — a repo-wide grep for `OnGainSliderChanged` / `SetSourceGainAsync` under `tests/` returns nothing — so its six tests are new coverage, not a repair. **Found while planning and NOT fixed here:** `AudioApiService.cs:236` (`{gain:F2}`) and `:96` build URLs with **current-culture** number formatting, so a comma-decimal locale emits `/api/audio/sourcegain/FilePlayer/0,25`; the gain tests therefore assert on `_pendingGainValue`, never on the request path. **Measurable outcome for the PR body:** the three racing tests sleep **6.0 s** today (1.5 + 1.5 + 3.0) and none after, and the determinism gate is `TEST-4`'s — 200/200 under CPU saturation, not one green run.
