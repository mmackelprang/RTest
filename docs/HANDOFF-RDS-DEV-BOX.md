# HANDOFF — RDS fixes: what is on `main`, what to do on the dev box

**Written 2026-09-27, paused by the owner mid-arc.** The RDS fixes are merged; nothing has been
deployed or looked at on the panel. The owner is picking this up on the dev box. This document
is the whole pick-up point: the sections below are in the order the work happens.

## 0. State of the tree

| | |
|---|---|
| `main` | `0aa7c6f` — [#674](https://github.com/mmackelprang/RTest/pull/674), squash of four commits |
| Working branch | `claude/bt-cast-fm-sdr-review-flm1x1`, re-pointed to `0aa7c6f`, nothing unpushed |
| Box (`radio`) | **not deployed** — still whatever `/api/health/version` says, which predates #674 |
| CI on #674 | `build` was **queued** on the self-hosted runner at merge time; check the run before trusting it |
| Rows closed | `AUD-63`, `AUD-69`, `AUD-70`, `AUD-60` (struck in `HANDOFF-GA-PUNCH-LIST.md` §4.2 / §5) |
| Rows open from the RDS review | `AUD-64`, `AUD-66`, `AUD-67`, `AUD-68`, `AUD-71`, `AUD-72` — all P2, §5 |

The review that produced the rows is `HANDOFF-BT-CAST-SDR-REVIEW.md` (section *"The FM RDS
follow-up"*); it has the root-cause narrative. This document does not repeat it.

## 1. Gates on the dev box before deploying

The fixes were gated in a Linux container, not on Windows, so run the real gates once from a fresh
pull of `main`:

```powershell
git fetch origin main; git checkout main; git pull
dotnet build RadioConsole.sln -c Release --no-incremental > $env:TEMP\build.log 2>&1; echo "exit=$LASTEXITCODE"
Select-String -Path $env:TEMP\build.log -Pattern '^\s+\d+ Warning\(s\)'
```

- Expect **47 warnings, 0 errors** on Windows (the container measured 33 because the Windows
  target does not build on Linux — that difference is the WinRT project, not a change).
- `dotnet test RadioConsole.sln -c Release` — read the per-project `Passed!`/`Failed!` lines, never
  the pipe's exit code. The known-failing set is in `CLAUDE.md`. One more to know about:
  `BluetoothAutoSwitchServiceTests.NodeArrivesAfterProbe_SwitchesViaEvent` sleeps 500 ms on a wall
  clock (`tests/Radio.Infrastructure.Tests/Audio/Services/BluetoothAutoSwitchServiceTests.cs:261`)
  and failed once under load in the container, then passed 3/3 alone. It is not touched by #674.
  If it fails on the dev box too, it is a `TEST` row (the `TEST-4` shape), not a reason to stop.

## 2. Deploy, and prove the deploy

```powershell
./deploy/Deploy-ToLinux.ps1          # defaults: -TargetHost radio -Runtime linux-x64
```

Then, before anyone looks at the panel (CLAUDE.md: *merged is not deployed*):

```bash
curl -s http://radio:5000/api/health/version | grep -o '"gitShaShort":"[^"]*"'   # API
curl -s http://radio:5002/api/health/version | grep -o '"gitShaShort":"[^"]*"'   # Web
git log --oneline -1 origin/main                                                  # must be 0aa7c6f
```

The web half matters more than usual here: `AUD-63` is CSS + JS + Razor. `Radio.Web` sends
`Cache-Control: no-cache` on static assets since `OPS-5`, so the kiosk revalidates `rds-marquee.js`
and `design-system.css` on the next load, and the deploy relaunches the kiosk. If the ticker looks
unchanged, confirm the browser is on the new module before suspecting the code:

```js
// via CDP on :9223 (kiosk profile), any page:
const m = await import('/js/rds-marquee.js'); Object.keys(m)        // must include _debugState
m._debugState(1)                                                     // new shape: headWidth, bodyCharWidth, headText, bodyText
```

The old module's `_debugState` reported `charWidth`; the new one reports `headWidth`,
`bodyCharWidth`, `headText` and `bodyText`. That is the quickest "is the new JS loaded" check.

## 3. What to watch on the panel (acceptance)

Tune an RDS-rich station, ideally one that rolls its PS. All of these are CDP on `:9223`.

1. **The jump is gone.** Poll every 200 ms for a few minutes:
   ```js
   const m = await import('/js/rds-marquee.js');
   setInterval(() => { const s = m._debugState(1); if (s) console.log(Date.now(), s.offset.toFixed(0), s.headText, s.bodyText.length); }, 200);
   ```
   `offset` should climb monotonically at the configured px/s and wrap from `trackWidth` to
   `-containerWidth` once per pass. **It must no longer drop to 0** on a RadioText update (that was
   `AUD-63`(1)) or on a PS page change (`AUD-63`(2)). Instance id is 1 unless the card has
   remounted; if `_debugState(1)` is null, try 2, 3 — ids are per mount.
2. **Head flips in place.** On a rolling-PS station `headText` changes page to page while
   `bodyText` and the offset continue smoothly. A visible sideways lurch of the RadioText on a page
   flip means the head-width compensation is wrong — `rds-marquee.js:253` `update()`.
3. **Speed step (`AUD-64`, not fixed).** With a full buffer you will still see the scroll rate
   alternate 40 ↔ 60 px/s on successive RT updates (`s.speed` in the poll). That is the one
   jerk-adjacent thing left; it is `AUD-64`, an afternoon, `RdsScrollSpeedPolicy.cs:40`.
4. **No fragments in the log.** Over the same window, from the file sink (Information lines are
   not in journald for `radio-api`):
   ```bash
   ssh mmack@radio 'F=$(ls -t /opt/radio-console/logs/radio-*.txt | head -1); grep -E "RDS: (Station name|Radio Text)" $F | tail -40'
   ```
   Every `Station name` should be a real page of the station's PS (no `LICKIGH`-style mixes); every
   `Radio Text` a real title, never two titles spliced.
5. **Reduced motion.** `matchMedia('(prefers-reduced-motion: reduce)').matches` once — if true the
   strip is static by design and none of the above applies.

## 4. Two decisions the fix leaves to you

- **What the head shows.** It is still the *live rolling PS*: it now changes in place without moving
  the body, which is how a car radio behaves. `AUD-63`'s row suggested anchoring it on
  `RdsStationNameStable` (the PI call sign) instead, so the head never changes. That is a one-line
  change in `RadioControlPanel.razor` (the `StationName=` binding on `<RdsCard>`) and a product
  choice, not a bug. Decide after watching a rolling station.
- **PS confirmation latency.** A page now needs **two clean consecutive in-order cycles** to be
  displayed (`RdsDecoder.cs:104`, `PsConfirmThreshold`). Under heavy block loss the head goes
  *stale* (keeps the last confirmed page) rather than *wrong*. If a station's pages visibly lag or
  skip, that is the trade-off working as designed; lowering the threshold to 1 re-admits any hybrid
  the station itself transmits (it cannot re-admit the decoder-made ones — those are gone by
  construction).

## 5. If something is wrong, where it lives

| Symptom | Look at |
|---|---|
| Body lurches on RT update | `src/Radio.Web/wwwroot/js/rds-marquee.js:253` `update()` — trim priced at `prevBodyCharWidth`; `MarqueeTextDiff.cs` classification of the body |
| Body lurches on PS flip | same `update()` — `offset += headWidth - prevHeadWidth`; CSS `.rcp-rds-rt-head/.rcp-rds-rt-body` (`display:inline-block; white-space:pre`) in `design-system.css` |
| Text on screen is stale vs the SR mirror / title | `RdsScrollMarquee.razor:167` `OnAfterRenderAsync` — the engine owns the span text once attached; a swallowed interop exception leaves the spans until the next update |
| Two ticker instances / dispose leaks | `RdsScrollMarquee.razor` `_interopGate` (`:114`) serialises renders; `dispose` on unmount |
| Station name never appears | `RdsDecoder.cs:648` `ProcessGroup0PS` — cycle mask abandons on any out-of-order segment; `:556` a group with a bad block is dropped whole |
| Station name wrong | should now be impossible without the station sending it; capture `RDS: Station name` lines and the group-0 sequence and file it |
| RadioText never completes | `RadioTextAssembler.cs:214` `ReceiveByte` (control codes other than 0x0A/0x0B/0x0D are dropped), `:306` `TryConfirm` (a slot older than the current wave does not count) |
| RadioText hybrid | `RadioTextAssembler.cs:294` `OpenChangeWave` — should be impossible; file it with the 2A group sequence |

The unit tests that pin each of these: `tests/RTLSDRCore.Tests/RdsDecoderTests.cs` (*PS Cycle
Integrity*), `RadioTextAssemblerTests.cs` (*In-place changes*), and
`tests/Radio.Web.Tests/Components/Shared/RdsScrollMarqueeTests.cs` (*Head / body split*). The
browser-level check that was run is not checked in; it was a Playwright script loading the real
module and stylesheet and asserting a body glyph's `getBoundingClientRect().left` before and after
each `update()` mode — easy to recreate if a regression needs pinning at that level.

## 6. What is left after acceptance

Build order, unchanged from the review handoff:

1. `AUD-64` — hysteresis on the catch-up speed (engage 75 %, release ~50 %), or drop the policy.
   The one still-visible artifact.
2. `AUD-66`, `AUD-67` — layout shift on tune / RT arrival; touch freezes the ticker. Independent,
   an afternoon each.
3. `AUD-68` — (2) re-entrancy is closed by #674's `_interopGate`; (1) cancel/recreate on every
   `update` still applies to `'swap'` and `'speed'`; (3) `RdsRelevantChanged` per circuit remains.
4. `AUD-71`, `AUD-72` — ride with whichever PR next touches `RdsDecoder.cs`.

Nothing is queued in `BUILDER_QUEUE.md` for any of these; the next free `AUD` number is `AUD-73`
(re-derive from both documents before claiming it).
