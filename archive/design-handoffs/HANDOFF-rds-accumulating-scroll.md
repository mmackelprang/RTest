# HANDOFF — RDS RadioText accumulating rolling scroll

**Component:** `src/Radio.Web/Components/Shared/RadioControlPanel.razor` (the existing `.rcp-rds-rt` row beneath the frequency well). A small companion file is recommended — see §10.
**Surface:** Home → Radio source → Radio Control Panel → RT line (the dim one-line strip immediately below the frequency display).
**Status:** `[PENDING REVIEW]` — ready for Planner / Builder.
**Relationship to existing handoffs:**
- **Follows** `docs/design-handoffs/design_handoff_radio_controller/` (token system, mono typography, `.rcp-rds-rt` chrome).
- **Extends** `IMPLEMENTATION.md` §P1·1 step 4 — that step explicitly deferred the marquee variant to future work: *"Scroll horizontally with `text-overflow: ellipsis` for now; a marquee variant is a future-work item, not part of this pass."* This spec is that future-work item.
- **Deviates** from no existing handoff. The RDS card (PS / PTY) above the frequency well stays exactly as designed; only the RT line below changes behavior.

---

## 1. Problem + context

Today the RT line uses the API's `RadioStateDto.RdsRadioText` field directly — whatever the SDR's `RdsDecoder` last confirmed (typically the last full 64-char Group 2A/2B assembly). When a station broadcasts a *rotating* RadioText — split across multiple RT messages, separated by ~30–60s — each new full message replaces the prior one in place via a server-driven `RadioStateChanged` SignalR push. From the user's seat that looks like: "a chunk appears for a while, then a different chunk replaces it, then the cycle repeats." The user wants the opposite — accumulate the chunks into one continuously-scrolling string with a sensible cap, so the RT line behaves like a broadcast ticker rather than a slideshow.

### Current-state data flow

```
RTLSDRCore.RadioReceiver
   └─ RdsDecoder.TryAssembleRadioText() ─ confirms a full 64-char RT message
        └─ exposes RadioText (string?)
SDRRadioAudioSource.RdsRadioText ── reads it on each poll
Radio.API.AudioStateUpdateService (500 ms loop)
   └─ HasRadioStateChanged → previous.RdsRadioText != current.RdsRadioText
        └─ SignalR "RadioStateChanged" → RadioStateDto.RdsRadioText
Radio.Web.RadioControlPanel
   └─ <div class="rcp-rds-rt">@_radioState.RdsRadioText</div>   ← REPLACEMENT, no history
```

Each new confirmed RT message overwrites the prior — the on-screen string is whatever the *latest* RT was, ellipsised if longer than the well. There is no accumulation, no scroll, no inter-chunk separator.

---

## 2. Data flow model — where accumulation happens

**Recommendation: accumulation is client-side, in the Razor component.** The API stays stateless per-client; the SignalR contract (`RadioStateDto.RdsRadioText` = "the latest confirmed RT message") is unchanged.

### Why client-side, not server-side

| Concern | Client-side (chosen) | Server-side |
|---|---|---|
| Per-client buffer cap | Each browser keeps its own buffer; perfect | Single server buffer shared across all clients — fine in the single-user console case, awkward if a phone + the console both open at once. |
| Scroll-speed / max-size config | Read once from `IOptionsMonitor<RdsScrollOptions>` on mount, render in JS | Forces SignalR + DTO churn to push config changes. |
| Station-change reset | Already have `previous.Band/Frequency` deltas client-side | Needs new "RDS buffer reset" SignalR event. |
| Backward compat | DTO unchanged → no API/test churn beyond Web | DTO and `HasRadioStateChanged` both shift. |
| Memory | ~256 chars × 1 string = trivial in the browser | Same, but multiplied by client count. |
| Multi-display behavior | Each surface scrolls independently from its own connect time — natural | All clients see the *same* scroll position — surprising when one opens late and inherits an in-flight buffer. |

The single concession of client-side: a fresh page-load starts with an empty buffer until the next RT message arrives. That is identical to today's behavior — the current `.rcp-rds-rt` row is also empty until the first message after page-load — so no regression.

### New client-side data flow

```
SignalR "RadioStateChanged" ─→ RadioStateDto { RdsRadioText, Band, Frequency, RdsPi }
        │
        ▼
RdsAccumulatingScrollBuffer  (new tiny helper class in Radio.Web)
   ├─ AppendChunk(string newRt)
   │   ├─ if equals last appended chunk verbatim → no-op (dedup)
   │   ├─ if buffer.Length + separator + newRt.Length > MaxChars
   │   │     → drop oldest chars from the front (keep on whole-char boundary)
   │   └─ buffer = buffer + separator + newRt
   ├─ ResetOnTuneChange(newBand, newFreqHz, newPi)
   │   └─ if any of those differ from last seen → clear buffer
   └─ public string Text { get; }   // bound by component
        │
        ▼
RdsScrollMarquee.razor  (or inline markup + CSS — see §10)
   └─ CSS keyframe marquee, pause-on-hover, aria-live mirror
```

The Razor parent subscribes to `RadioStateChanged` exactly as today; the only added work in the handler is one call to `_buffer.AppendChunk(dto.RdsRadioText)` after `_buffer.ResetOnTuneChange(dto.Band, dto.Frequency, dto.RdsPi)`.

---

## 3. Visual mockup (ASCII)

The card it lives inside (frequency well, signal meter, controls, etc.) is unchanged from the radio controller handoff. Only the strip immediately below the well changes.

### State A — buffer fits the well (no scroll, static centered)

```
┌───── frequency well (existing, untouched) ─────┐
│                                                │
│            98.5 MHz                            │
│                                                │
│  STEP 0.1 MHz                       STEREO     │
└────────────────────────────────────────────────┘
                  ────────── RT line (this spec) ──────────
            WUNC · Morning Edition · NPR
                  ─────────────────────────────────────────
```

- Width matches `.rcp-rds-rt` (max-width 420 px, same as the frequency well).
- Single line, mono 11 px, `var(--text-low)` (same as today).
- Centered horizontally because total text width ≤ container width.

### State B — buffer exceeds the well (scrolling marquee, default)

```
┌───── frequency well (existing, untouched) ─────┐
│                                                │
│            98.5 MHz                            │
│                                                │
│  STEP 0.1 MHz                       STEREO     │
└────────────────────────────────────────────────┘
   …Brunch · WUNC · Morning Edition · NPR · The Da──┐  scrolls right-to-left
   └── fades on left edge ──        ── fades on right edge ──┘
```

- Scroll direction: right-to-left (standard ticker convention).
- Speed: 40 px/s default (configurable — §5).
- Edge fade gradients (8 px each side, `linear-gradient(to right, var(--bg-color), transparent)`) so characters appear and disappear smoothly rather than getting clipped at a hard edge.
- Inter-chunk separator: ` • ` (space, U+2022 BULLET, space). Reads as a clear chunk boundary without being noisy.

### State C — hover / focus (pause-on-hover)

```
   …Brunch · WUNC · Morning Edition · NPR · The Da──┐
   └── scroll paused at current offset                │
       cursor: default; subtle outline on the strip  │
                                                      │
```

- On hover (mouse) or keyboard focus (Tab onto the strip), CSS `animation-play-state: paused`.
- 1 px outline (`var(--text-low)` at 30 % opacity) appears on hover to signal "this is interactive."
- On mouseleave / blur, scroll resumes from the paused offset (CSS animation auto-resumes; no JS state needed).

### State D — station change (buffer cleared)

```
   ┌── user tunes from 98.5 → 91.5 MHz ──┐
   │                                     │
   ▼                                     │
   (RT line is empty / hidden)           │
   …                                     │
   ▼   first RT chunk arrives (~20s later)
   WUNC · Morning Edition
   ▼   second chunk arrives (~45s later)
   WUNC · Morning Edition · NPR News
```

- The instant the band or frequency changes (or the PI code changes — see §6), the buffer is cleared and the RT line collapses to empty (`display: none`).
- It reappears as soon as the first new RT message arrives for the new station.
- This matches the existing "hidden when empty" behavior of `.rcp-rds-rt`.

### State E — no RDS / no RT

```
┌───── frequency well ─────┐
│      540 kHz             │
│  STEP 10 kHz             │
└──────────────────────────┘
              (no RT row rendered)
```

Same as today — when `_buffer.Text` is empty, render nothing. No "—" placeholder, no "No RT" copy. Silence is the correct affordance for "this station does not broadcast RT."

---

## 4. Scroll animation — CSS keyframes, not JS

**Recommendation: pure CSS `@keyframes` + `animation` on a `transform: translateX(...)` track.** Justification:

- **Performance:** CSS `transform` runs on the GPU compositor thread. JS rAF-driven `translateX` runs on the main thread, contending with Blazor's render loop and the visualization hub. On the Intel N100, the audio pipeline is already sensitive to main-thread contention (see CLAUDE.md auto-memory: "Audio distortion correlates with SSH activity"). Anything we can keep off the main thread is a win.
- **Pause-on-hover precision:** CSS `animation-play-state: paused` honors the current keyframe offset exactly — no drift, no recomputation. Resume is automatic on un-hover.
- **No SignalR coupling:** Animation is purely a render concern; the SignalR push only updates the *text*, not the scroll position.

### Skeleton (Builder will polish)

```html
<div class="rcp-rds-rt-scroll"
     tabindex="0"
     aria-live="polite"
     aria-atomic="true"
     title="@_buffer.Text">
  <div class="rcp-rds-rt-track"
       style="--scroll-duration: @ScrollDurationSeconds.ToString("F1")s;">
    @_buffer.Text
  </div>
</div>
```

```css
.rcp-rds-rt-scroll {
  position: relative;
  overflow: hidden;
  width: 100%;
  max-width: 420px;
  height: 1.6em;       /* matches existing .rcp-rds-rt line-height */
  /* edge fade gradients */
  mask-image: linear-gradient(
    to right,
    transparent 0,
    black 8px,
    black calc(100% - 8px),
    transparent 100%
  );
  -webkit-mask-image: linear-gradient(
    to right, transparent 0, black 8px,
    black calc(100% - 8px), transparent 100%
  );
}

.rcp-rds-rt-track {
  display: inline-block;
  white-space: nowrap;
  font-family: var(--font-mono);
  font-size: 11px;
  color: var(--text-low);
  letter-spacing: 0.04em;
  /* Translate from "fully off the right edge" to "fully off the left edge" */
  animation: rds-rt-scroll var(--scroll-duration) linear infinite;
}

@keyframes rds-rt-scroll {
  from { transform: translateX(100%); }
  to   { transform: translateX(-100%); }
}

.rcp-rds-rt-scroll:hover .rcp-rds-rt-track,
.rcp-rds-rt-scroll:focus .rcp-rds-rt-track,
.rcp-rds-rt-scroll:focus-within .rcp-rds-rt-track {
  animation-play-state: paused;
}

/* Fits-in-well variant — no animation, just centered text */
.rcp-rds-rt-scroll.is-static .rcp-rds-rt-track {
  animation: none;
  transform: none;
  display: block;
  text-align: center;
}
```

### How `--scroll-duration` is computed

Duration must derive from text length so the *speed* (px/s) stays constant regardless of buffer length:

```
durationSeconds = (containerWidthPx + textWidthPx) / scrollSpeedPxPerSec
```

The component measures `textWidthPx` via a one-shot `JS interop` call (`getBoundingClientRect().width`) **only when the buffer changes** (rare — every RT message, ~30–60 s on a typical station). `containerWidthPx` is the well max-width (420 px hard-coded today; a more robust read of `offsetWidth` is fine but not required for v1). The "is-static" CSS branch is enabled in the component when `textWidthPx <= containerWidthPx`.

This is the *one* JS interop point. Everything else is pure CSS. If Builder wants to avoid even that interop, an acceptable approximation is `durationSeconds = max(8, charCount * 0.18)` — empirically ~5.5 chars/s at 11 px mono, slightly slower than 40 px/s but eliminates the measurement round-trip. Either approach is fine; Builder picks based on what's cleaner inside `RadioControlPanel`.

---

## 5. Configuration model

Three new keys live under the existing **Radio** config tab in System Config (the same tab that holds `DefaultFMFrequencyMHz`, `ScanStopThreshold`, etc. — see `SystemConfigPage.razor:352-412`). Adding them here, not under Audio or a new RDS tab, keeps the discovery story consistent — "anything about how the radio reads / behaves lives under Radio."

| Key (SQLite `Config_sqlite`)              | Type | Default | Validation | UI label                            |
|-------------------------------------------|------|---------|------------|-------------------------------------|
| `Radio:Rds:RtBufferMaxChars`              | int  | **256** | 64 – 2048  | "RDS ticker max length (chars)"     |
| `Radio:Rds:RtScrollSpeedPxPerSec`         | int  | **40**  | 10 – 200   | "RDS scroll speed (px/s)"           |
| `Radio:Rds:RtChunkSeparator`              | string | `" • "` | 1 – 8 chars | "Chunk separator"                  |

### Why 256 chars?

A single Group 2A RadioText message is 64 chars. 256 chars = exactly **4 messages × 64 chars** *minus inter-chunk separators*, which after the ` • ` (3 chars) separator works out to ~ 3.7 full messages before the oldest drops off the front. On a station that rotates "song · artist · station ID · DJ name · weather tagline" every ~30 s, this gives the user roughly a 2-minute scrolling history — long enough to catch context they missed, short enough that the scroll cycle completes in a comfortable 17 s at 40 px/s + 11 px mono. Doubling to 512 made the scroll cycle feel sluggish (~33 s) in mental simulation; halving to 128 lost the "history" value the user asked for. 256 is the Goldilocks point.

### Why 40 px/s?

40 px/s × ~7 px/char (11 px mono with 0.04em letter-spacing) = ~5.7 chars/s — roughly the upper end of comfortable reading speed for short ticker text. Old-school news tickers run 60–80 px/s; broadcast captions run 30–40 px/s. We split the difference toward the captions end because the user is also looking at the frequency / meter / etc.; the RT line is peripheral, not primary, so it should read without forcing the user to track it actively.

### Why is the separator configurable?

Mostly so a future "I'd actually prefer a pipe" preference doesn't need a code change. Validation should reject empty, > 8 chars, and the control characters that would break the marquee (`\n`, `\r`, `\t`). No locale-aware defaulting.

### Where the keys land in System Config UI

Append a 12-column row to the Radio tab, after the existing `DefaultDeviceVolume` row and before the Save button:

```
┌────────── Radio tab (existing rows above) ──────────┐
│ [Default Device] [Default FM Hz]  [Default AM kHz]  │
│ [FM Step]        [AM Step]                          │
│ [Min FM]   [Max FM]   [Min AM]   [Max AM]           │
│ [Scan Threshold]  [Scan Delay]   [Default Vol]      │
├─────────────────────────────────────────────────────┤  ← new sub-heading
│ RDS RADIOTEXT TICKER                                │  (mono uppercase label,
│                                                     │   same style as section
│ [Max length]   [Scroll speed]   [Separator]         │   dividers elsewhere)
├─────────────────────────────────────────────────────┤
│ [Save Radio Settings]                               │
└─────────────────────────────────────────────────────┘
```

The three controls are `RadzenNumeric` × 2 + `RadzenTextBox` × 1, each Size=12 SizeMD=4. Save is the existing button; the `_radioConfig` POCO grows three properties to match the new keys. The SQLite bridge (PR #298, see auto-memory) propagates the change to `IOptionsMonitor<RdsScrollOptions>` automatically.

---

## 6. Edge cases

### a. Duplicate-chunk deduplication

The `RdsDecoder` re-fires the same confirmed RT string when the station holds the same RadioText for many cycles. Without dedup, the buffer would grow as `"WUNC NEWS • WUNC NEWS • WUNC NEWS • ..."`. **Rule:** if `newChunk == _lastAppendedChunk` (exact string equality, after trim), no-op the append. The decoder already trims via `.Trim()` before exposing `RadioText`, so straight `==` is enough.

A second rule for the rotating case where the *same* chunk re-enters the rotation after several others have intervened (`A → B → C → A`): **allow** the re-append. The user's intent is "see the rolling history," and seeing A reappear after B and C *is* part of the history.

### b. Station-change reset

`RadioStateChanged` carries `Band`, `Frequency`, and `RdsPi`. Clear the buffer when *any* of:
- `Band` differs from the last seen value, OR
- `Frequency` differs by more than 0.001 Hz (matches `HasRadioStateChanged` tolerance), OR
- `RdsPi` differs (and the new value is non-null — a transient null-during-tune doesn't count).

Three independent signals because each catches a different miss case: PI alone misses non-RDS bands (AM, WB) where it's always null; frequency alone misses sub-channel changes; band alone misses in-band tunes. All three together cover the union without false-positives.

The dedup tracker (`_lastAppendedChunk`) MUST also reset on station change. Otherwise the first RT message on the new station would be silently dropped if it happened to match the last message on the prior station.

### c. No-RDS state

Buffer is empty → component returns empty markup. Same as State E in the mockup. Do **not** render a placeholder like "—" or "No RT" — the surrounding frequency well already conveys "this is a radio, you're tuned, there's just no RDS here."

### d. Very long single chunk (longer than `MaxChars`)

A 64-char RT message is well under the 256-char default, but if a future config caps `MaxChars` below the chunk size (or a station broadcasts a chunk via some non-standard extension), the buffer would consist of a partial chunk only. **Rule:** truncate the *new* chunk to `MaxChars` characters (taking the *last* `MaxChars` so the most-recent text wins), then replace the entire buffer with the truncated chunk. The buffer never holds more than `MaxChars` characters total at any time.

### e. Buffer overflow during normal append

When `buffer.Length + separator.Length + newChunk.Length > MaxChars`, drop characters from the *front* of the buffer until the new total ≤ `MaxChars`. Drop on a whole-char boundary (no half-graphemes — string indexing in C# is fine for the ASCII RDS character set, but use `Char.IsHighSurrogate` defensively in case future RDS+ extensions surface non-ASCII). After dropping, also strip any leading separator fragments so the buffer never starts with ` • `.

### f. RT message arrives during a scroll cycle

The CSS animation restarts when `--scroll-duration` changes (CSS animations don't smoothly retarget their duration). The visible effect is the text "jumps" back to the right edge and starts scrolling again. **Accept this** for v1 — it happens at most once per minute on most stations and the visual disruption is brief. If user feedback later asks for smoother appends, a JS-driven scroll position with explicit append-without-restart logic is a future-work item; do not premature-optimize.

### g. Scroll speed change while scrolling

Same behavior — CSS animation restarts. Config changes are rare (the user adjusts once and leaves it), so this is a non-issue.

---

## 7. Accessibility

Marquees have a deserved bad reputation with screen readers and motion-sensitive users. Handling both:

### Screen-reader behavior
- The visible scrolling track is `aria-hidden="true"`.
- An invisible mirror element with `aria-live="polite"` and `aria-atomic="true"` carries the *full* buffer text as a single static string. When the buffer updates, the SR announces the new content politely (not interrupting the user). The mirror is positioned off-screen via the standard `position: absolute; left: -10000px; width: 1px; height: 1px;` sr-only pattern (or whichever sr-only utility class the project already uses — search `wwwroot/css/` for `.sr-only` first; if none, define one).
- The mirror updates **debounced** at 1 Hz at most. RT messages rarely arrive faster, but the debounce prevents a flood if a noisy decoder repeatedly fires the same string.

### Motion-sensitivity
- Honor `@media (prefers-reduced-motion: reduce)` — disable the marquee animation entirely and fall back to a horizontal scroll bar (`overflow-x: auto`). The user can swipe / scroll the strip manually to read the buffer. The static centered layout (State A) still applies when the buffer fits.

```css
@media (prefers-reduced-motion: reduce) {
  .rcp-rds-rt-track {
    animation: none;
    transform: none;
    overflow-x: auto;
    scrollbar-width: thin;
  }
}
```

### Keyboard
- The scroll container is `tabindex="0"` so keyboard users can land on it and pause-on-focus works.
- Arrow keys do nothing today. (A future "hold arrow to manually scroll" is a future-work item.)
- `Esc` while focused does nothing (no popover to dismiss).

### Color contrast
No change — same `var(--text-low)` on the same dark background as the existing `.rcp-rds-rt`. Already AA-compliant at 11 px per the radio controller handoff.

---

## 8. Open questions for the user

Defaults below were chosen pragmatically — please confirm or override:

1. **256-char max default** — comfortable middle, ~4 full RT messages. Acceptable, or do you want more (say 512) for longer history at the cost of slower scroll cycles?
2. **40 px/s scroll speed** — closer to broadcast-caption pace than news-ticker pace. Acceptable, or faster (closer to 60–80 px/s)?
3. **` • ` (bullet) separator** — visually clean, monospace-friendly. Alternatives: ` | ` (pipe), `  ·  ` (middle dot wider gaps), ` — ` (em dash). Your call.
4. **Right-to-left scroll direction** — standard ticker convention. Confirm, or do you prefer LTR (which reads as "older content scrolls in from the right, newer drops off the left" — uncommon but defensible if you think of it as "new chunks land on screen and march toward history").
5. **Pause-on-hover/focus** — recommended on for desktop readability. Disable for touch-only? (Touch hover behavior is browser-dependent; on a touchscreen, a long-press would be more natural — but this surface is desktop-primary at 1920×720.) Default: enabled for all input modes.
6. **Per-station-saved buffer** — if you tune away and come back, the buffer is empty until new RT messages arrive. We could persist the last-seen buffer per PI in SQLite and re-hydrate on return. Out of scope unless you specifically want it (see §9 — recommend keeping it out of v1).
7. **Clear-buffer trigger on PI change** — the rule is "clear if Band OR Frequency OR (PI is non-null AND PI changed)." Edge case: a station temporarily loses RDS lock (PI goes null), then re-acquires with the same PI value — the rule correctly does NOT clear. Confirm this matches your mental model.
8. **Static-fit threshold** — when buffer width ≤ container width, show static centered (no scroll). Alternative: always scroll, even short text, for visual consistency. Default: static when it fits — feels less twitchy on stations with a single short tagline.

---

## 9. Out of scope (do not build in this PR)

Explicitly NOT in scope — flag any of these to the user if they come up:

1. **PS / PTY / PI display changes.** The `RdsCard.razor` (station name + PTY chip above the frequency well) is unchanged. This spec touches only the RT row below.
2. **Per-station persisted buffer history.** Re-hydrating the buffer from SQLite when re-tuning to a previously-heard PI. Tempting but adds DB churn for marginal value; gather user feedback after v1.
3. **Exporting / copying the RDS log.** A "click to copy buffer" or "view full RT history for this session" affordance. Not asked for; skip.
4. **AM / WB / SW RT support.** AM stations don't broadcast RDS; weather stations carry SAME messages, not RDS. This spec is FM-only by virtue of where RT exists. The component should still gracefully empty itself on non-FM bands (the buffer clear on band-change already handles this).
5. **Multi-line wrap.** Some receivers split RT into two visible lines. We stay one-line scrolling — the radio controller handoff was explicit about the single-line RT row, and a wrapped layout would conflict with the frequency well's chrome.
6. **Now-Playing extraction.** Parsing "Artist - Title" out of RT for the fingerprinting pipeline is a separate (and longer) workstream. Don't touch the existing `RdsRadioText` consumers in `BackgroundIdentificationService` / `PlayHistoryTracker`.
7. **Animation-frame-perfect smooth appends.** As noted in §6.f, appends during a scroll cycle visibly restart the animation. Don't paper over this with JS unless v1 user feedback specifically asks.
8. **Localized number/date formatting inside RT.** RT is opaque ASCII to us; we don't parse or transform its contents beyond dedup and accumulation.

---

## 10. File-touch summary (for Planner / Builder)

This is informational, not prescriptive — Planner picks the actual structure.

- **New / changed (Web):**
  - `src/Radio.Web/Services/Rds/RdsAccumulatingScrollBuffer.cs` — small POCO helper (≤ ~80 LOC) with `AppendChunk`, `ResetOnTuneChange`, `Text` properties. Unit-testable in isolation; recommend a `Radio.Web.Tests/Services/Rds/RdsAccumulatingScrollBufferTests.cs` covering: dedup, overflow truncation, station-change reset, separator handling, long-single-chunk truncation.
  - `src/Radio.Web/Components/Shared/RdsScrollMarquee.razor` (new tiny component, ~50 LOC + scoped CSS) OR inline markup directly in `RadioControlPanel.razor` if Planner prefers fewer files. Either works; the component option is cleaner if a unit test wants to render it standalone.
  - `src/Radio.Web/Components/Shared/RadioControlPanel.razor` — replace the existing `<div class="rcp-rds-rt">…</div>` block (lines 96–99) with the new component / markup; wire `_buffer` into the existing `HandleRadioStateChanged` handler.
  - `src/Radio.Web/Components/Pages/SystemConfigPage.razor` — append three new RadzenNumeric/TextBox controls under the Radio tab; extend `_radioConfig` model with `RtBufferMaxChars`, `RtScrollSpeedPxPerSec`, `RtChunkSeparator`.
  - `src/Radio.Web/Models/ApiModels.cs` (or wherever `RadioConfigDto` lives) — add the three properties so the SystemConfigPage bind targets exist.
- **New / changed (API):**
  - The config keys flow through the existing SQLite bridge (`Configuration/Bridge/SqliteConfigurationProvider.cs`); no API endpoint change required — `IOptionsMonitor<RdsScrollOptions>` reads them directly on the Web side.
  - Define `RdsScrollOptions` in `src/Radio.Web/Models/RdsScrollOptions.cs` (`MaxChars`, `ScrollSpeedPxPerSec`, `ChunkSeparator`) and register via `services.Configure<RdsScrollOptions>(configuration.GetSection("Radio:Rds"))` in `Program.cs`.
- **No changes:**
  - `RTLSDRCore.DSP.RdsDecoder` — unchanged. The decoder already exposes the data; this is purely a presentation-layer change.
  - `Radio.API.Services.AudioStateUpdateService` — unchanged. The SignalR contract (`RdsRadioText` = latest confirmed message) is unchanged.
  - `Radio.Web.Components.Shared.RdsCard` — unchanged. PS/PTY still render above the frequency well exactly as designed.

---

## Hand-off summary for Planner / Builder

Replace the one-line ellipsised RT row beneath the frequency well with an accumulating, configurable, smooth-scrolling ticker. Accumulation lives client-side in a small POCO buffer (256 chars default, ` • ` separator default, dedup verbatim repeats, clear on band/frequency/PI change). Scroll is CSS keyframes on `translateX` (no JS rAF), pauses on hover/focus, honors `prefers-reduced-motion` with a manual-scroll fallback, mirrors content to an `aria-live="polite"` SR-only element. Three new config keys land under the existing Radio tab in System Config — defaults are calibrated for ~17 s scroll cycles holding ~2 minutes of rotating history. No API contract change, no decoder change, no PS/PTY card change.
