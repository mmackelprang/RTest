# HANDOFF — Incoming-call banner: a full-panel overlay with the caller, Ignore, and touch-to-close

**Row:** `PHN-11` · **Surface:** layout-level overlay on every route (MainLayout) **and** on `/sleep` (the same two-host shape as `EncoderHud`)
**Author:** Designer · **Date:** 2026-10-02 · **Consumer:** Planner / Builder · **Status:** draft, awaiting owner review

> **Owner, 2026-10-02 (verbatim):** "Note that I would like to have a large aesthetically pleasing banner overlay when a call comes in along with the announcement that shows the caller on the radio console.  It can cover most of the screen, and be closed by a touch, phone answer, or call cancelled."
>
> **Owner, same day (verbatim):** "for PHN-11 - there should be an 'Ignore' button on the overlay that will allow the user to cancell the call from the touchscreen."

No Claude Design package exists for this surface. It is built from the phone page handoff's Ringing vocabulary and the existing tokens; nothing here needs a new token.

### Follows, extends, deviates

| Handoff / prior work | Relation | Detail |
|---|---|---|
| `design_handoff_phone_page/README.md` :99-109, :215 (hero: "Incoming Call" label, amber Ringing, `ring-pulse` 1.2 s) | **follows** | Amber is the ringing colour; the pulse is the existing `ringPulse` keyframe (`design-system.css:6973-6977`), unchanged. |
| same, hero action buttons (`.phone-btn.btn-hangup`, `design-system.css:6704-6708`) | **follows** | Ignore is the red end-call pill, enlarged. |
| `UI-8-signal-glow-tokens.md` / `design-system.css:163-167` (owner chose **no glow** on red/green call buttons) | **follows** | Ignore has **no glow**. The only glow on the banner is amber (`--signal-amber-glow`). |
| `EncoderHud` two-host pattern (MainLayout + `Sleep.razor`) | **follows** | Same hosting; the banner sits above the sleep screen. |
| `HANDOFF-bell-failure-surfacing.md` (during-ring assertive strip, deferred to a backend event) | **extends (note only)** | When that event ships, the banner is the natural place for its during-ring line. Not in scope here. |
| Phone hero's disabled **Reject** button (`PhoneStatusHero.razor:108-111`) | **deviates in wording, at the owner's direction** | The owner named the action **Ignore**. The hero is not changed by this row (see Open questions, Q2). |

**No new tokens.** Everything uses `design-system.css:64-231`.

---

## 1. Summary of decisions

| Decision | Choice | Why | What the declined option gives up |
|---|---|---|---|
| **Sleep and a dark panel** | A call **lights the panel and shows the banner without waking the console** (music stays parked in Standby; Ambient keeps playing). On close the sleep screen is reported visible again. | A ring at night should show who is calling, not start the music. The Web can only light the panel by reporting the sleep screen hidden, which it can do while the banner covers it. | **Declined: wake the console.** Would resume parked audio under the announcement, at night, for a call the person may ignore. **Cost of the choice:** after a call during **deep sleep** the panel stays lit on the sleep clock (box has `PanelOffAfterMinutes = 0`, so ENC-22 never turns it off again) until someone sleeps it again. The Web cannot restore deep-dark without an API change (Q3). |
| **Touch vs announcement** | A touch on the banner closes **only the banner**. It does not silence the announcement or the ring sound, and does not affect the call. | The announcement is the API's, played independently; the call belongs to RotaryPhone. Closing a picture must not have side effects the person did not ask for. | **Declined: touch also silences.** Would give a one-tap "quiet" — but that is a different action with its own endpoint, and conflating it makes "close" sometimes do more than close. |
| **Second call while the banner is up** | (a) Caller-ID update for the same call: **upgrade the caller line in place**, no re-entry, no re-announce. (b) New call after the first ended: **a fresh banner**, even if the last one was touched away. (c) Ring on another `phoneId`: **one banner, showing the newest ringing call**; when it ends, fall back to any other still ringing. | Without a call id, "same call" = same `phoneId` with no `Idle` in between. Dismissal is per call, not sticky. Two simultaneous rings are theoretical; stacking UI for them is not worth its cost. | **Declined: stacked/split banners for (c).** Would show both callers at once, at the price of a layout nobody will see in practice. |
| **Ignore with the capability OFF** | **Visible but disabled**, with a one-line reason under it. The whole Ignore column stays **inert** to touch. | The owner asked for the button and will look for it at UAT; hiding it reads as "not built". Precedent: the hero already shows a disabled Reject. Inert column so a tap on the dead button is not taken as "close" and mistaken for "it worked". | **Declined: hidden.** Cleaner screen, no dead control on an urgent surface — but the owner cannot tell "not wired yet" from "forgotten". |
| **Ignore confirm** | **No confirm.** One tap. | Ringing lasts tens of seconds; a second tap eats that window. Ignoring is low-cost and recoverable (the call can be returned; the caller reaches voicemail/hears a busy). The inert column already guards against near-misses. | **Declined: confirm step.** Protects against an accidental tap, at the cost of a slower, more fiddly action on the one surface that exists to be fast. |
| **Exit after answer / end** | A **600 ms** labelled beat ("ANSWERED" green / "CALL ENDED" grey), then a 200 ms fade. A touch closes with no beat. | Confirms *why* the banner left — useful when the call was answered on the handset across the room. 0.8 s total: no lingering. | **Declined: just close.** Quicker, but the banner vanishing is ambiguous between "answered" and "they hung up". |
| **Encoder HUD over the banner** | **The HUD wins** (renders above the banner). | Someone turning VOLUME during a ring is probably turning the announcement down; the readout must be visible. | **Declined: banner covers the HUD.** Knob turns would act blind. |
| **Scrim** | Near-opaque, **no backdrop blur**. | What is behind does not matter during a ring, and blur over a live visualizer costs GPU on a box where load correlates with audio distortion — while the announcement is playing. | **Declined: blur(12px) like the HUD.** Slightly richer look; real cost at the worst moment. |

---

## 2. Layout (1920 × 720)

- **Scrim:** `position: fixed; inset: 0;` full viewport, `background: rgba(5, 5, 7, 0.88)` (the sleep screen's `#050507` at 88%). No `backdrop-filter`.
- **Card:** 1760 × 600 px, centred (insets **80 px** left/right, **60 px** top/bottom). `background: var(--surface-raised)`; `border: 1px solid rgba(240, 168, 48, 0.30)` (amber hairline, the `.btn-warn` recipe at `design-system.css:6956`); `border-radius: 24px`; `box-shadow: 0 0 64px var(--signal-amber-glow), 0 16px 48px rgba(0,0,0,0.6)`. A radial glow behind the caller disc, top-left: `radial-gradient(circle at 220px 260px, rgba(240,168,48,0.15), transparent 480px)` (the hero's `glowBg` value, `PhoneStatusHero.razor:224`).
- **Card padding:** 56 px. Inner box 1648 × 488 (x 136–1784, y 116–604).
- **Three columns**, `gap: 56px`, vertically centred in the area above the hint:

| Column | Width | Content |
|---|---|---|
| Caller disc | 240 px | Halo + monogram/glyph (§4) |
| Text | flex 1 (≈ 952 px) | Header, caller primary, caller secondary |
| Ignore zone | 400 px | 1 px left divider `--surface-separator`, then the button + sub-label, centred. **The whole 400 × 488 px column is the Ignore zone** (§6). |

- **Hint line:** bottom of the inner box, spanning the disc + text columns only (x 136–1328), 28 px tall, left-aligned with the text column (x 432).

```
 x0                                                                                     x1920
 ┌──────────────────────────────────────── scrim rgba(5,5,7,.88) ──────────────────────────────┐ y0
 │   ┌─ card 1760×600, r24, amber hairline, amber outer glow ─────────────────────────────┐   │ y60
 │   │                                                              │                      │   │
 │   │     .-''''-.        INCOMING CALL          (Orbitron 32 amber)│                      │   │
 │   │   .'  ____  '.                                                │   ┌──────────────┐   │   │
 │   │  :  /    \  :     Carol Anderson           (Inter 72 high)    │   │  ☎  IGNORE   │   │   │ button
 │   │  :  | CA |  :                                                 │   │              │   │   │ 320×128
 │   │  :  \____/  :     (919) 555-0142           (Mono 32 medium)   │   └──────────────┘   │   │
 │   │   '.      .'                                                  │     Ends the call    │   │
 │   │     '-..-'   halo 240, disc 176                               │                      │   │
 │   │                                                               │   Ignore zone 400    │   │
 │   │  Touch anywhere to close. The call keeps ringing. (Inter 18)  │   (inert to close)   │   │
 │   └───────────────────────────────────────────────────────────────┴──────────────────────┘   │ y660
 └─────────────────────────────────────────────────────────────────────────────────────────────┘ y720
       x80  x136   x376 x432                                  x1328 x1384               x1784 x1840
```

**Stacking (z-index):**

| Layer | z | Note |
|---|---|---|
| Top bar | 1100 | covered |
| Sleep screen, kiosk entry dialogs, preset menu, queue confirm | 9999 | covered |
| Gain popover anchor | 10000 | covered. (There is **no** gain-popover backdrop at 9999 — `design-system.css:7456-7464` corrects that.) |
| Virtual keyboard | 10001 | covered — an incoming call covers an open text entry; both are intact underneath when the banner closes |
| **Incoming-call banner (scrim + card)** | **10002** | new |
| Encoder HUD card + selector overlay | **above the banner while it is up** | Planner's choice of mechanism (e.g. 10003 scoped to "banner open"). Must not disturb the HUD/gain-popover tie documented at `design-system.css:7456-7464`. |

On `/phone`, the banner covers the page's own Ringing hero. That is fine: the hero is the long-lived view, the banner the interrupt; after a touch-close the hero is there with the same caller.

---

## 3. Typography and colour

| Element | Font | Size / weight | Colour | Other |
|---|---|---|---|---|
| Header | `--font-display` (Orbitron) | 32 px / 700, line-height 40 px | `--signal-amber` | letter-spacing 0.18em, uppercase, `text-shadow: 0 0 20px rgba(240,168,48,0.50)` (hero value) |
| Caller primary (name or "Unknown caller") | `--font-body` | 72 px / 600, line-height 1.1 | `--text-high` | §5 for wrapping |
| Caller primary (number only) | `--font-mono` | 72 px / 600, line-height 1.1 | `--text-high` | `font-variant-numeric: tabular-nums`, letter-spacing 1px, never wraps |
| Caller secondary | `--font-mono` (number) / `--font-body` ("No caller ID") | 32 px / 500, line-height 40 px | `--text-medium` | tabular-nums for numbers |
| Gap header → primary → secondary | | 20 px / 12 px | | |
| Monogram initials | `--font-display` | 64 px / 700 | `--signal-amber` | letter-spacing 4px |
| Glyph (number / unknown) | Material icon | 88 px | `--signal-amber` | |
| Ignore label | `--font-body` | 28 px / 700 | `--text-inverse` on `--signal-red` | uppercase, letter-spacing 0.08em; icon `call_end` 40 px |
| Ignore sub-label / reason / error | `--font-body` | 18 px / 500, line-height 24 px | `--text-medium`; error `--signal-red` | centred under the button, max 2 lines, width 320 px |
| Hint line | `--font-body` | 18 px / 500 | `--text-medium` | |

Contrast (approximate, on `--surface-raised` `#141416`): `--text-high` ≈ 17:1, `--text-medium` ≈ 9:1, `--signal-amber` ≈ 9:1, `--signal-red` ≈ 6:1; Ignore label `--text-inverse` on `--signal-red` ≈ 7:1. All pass AA at these sizes.

---

## 4. Caller line cases

| Case | Trigger | Disc | Primary | Secondary |
|---|---|---|---|---|
| **Contact** | Number known and the Web's lookup (PBAP contacts via API, then RotaryPhone manual contacts) returns a name | Monogram: 176 px circle, `background: rgba(240,168,48,0.12)`, `border: 2px solid var(--signal-amber)`, initials centred | Contact name | Formatted number |
| **Number only** | Number known, lookup missed (or still running) | Same circle, glyph `person` | Formatted number (mono) | none (the line is removed, not blank) |
| **Unknown caller** | No `IncomingCall` for this ring, or number is `"Unknown"` / empty | Same circle, glyph `no_accounts` | `Unknown caller` | `No caller ID` |

**Initials:** from the contact name. If the name contains a comma (`Anderson, Carol`), use first letter after the comma + first letter before it (`CA`); otherwise first letter of the first and last words (`Carol Anderson` → `CA`). One word → one letter. Uppercase. If the first character of the name is not a letter (e.g. `+1 Work`, an emoji), fall back to the `person` glyph.

**Number formatting:** strip non-digits; 10 digits, or 11 starting with `1` → `(555) 013-7424`. Anything else (international, short codes, 7 digits) is shown **exactly as received**.

**Upgrade rule:** within one call the caller line only ever gains information (unknown → number → name). A later `IncomingCall` carrying `"Unknown"` never replaces a known number. Each upgrade is a 200 ms crossfade of the text column and disc content (`--anim-ease-standard`); the card does not move or re-enter.

---

## 5. Long names

- Primary wraps at word boundaries to **at most 2 lines**, then ellipsis (`-webkit-line-clamp: 2`). A single unbroken token wider than the column breaks anywhere (`overflow-wrap: anywhere`).
- The text column is ≈ 952 px: about 22–24 characters per line at 72 px Inter, so 2 lines hold ≈ 45 characters.
- Numbers (primary or secondary) never wrap; they fit at 72 px with room to spare for any realistic length. If a received non-US string exceeds the column, ellipsis at the end.
- The vertical budget holds the 2-line case: 40 + 20 + 2 × 80 + 12 + 40 = 272 px, inside the 460 px above the hint.

---

## 6. Interaction

**Touch zones.**

| Zone | Touch does |
|---|---|
| **Ignore button** (320 × 128 px) | Capability ON, ringing: declines the call (§7 Declining). Otherwise nothing. |
| **Rest of the Ignore zone** (the 400 × 488 px right column, including the sub-label and padding around the button) | **Nothing**, always. A near-miss on Ignore must not close the banner — closing removes the only on-screen way to ignore, and a closed banner after tapping toward Ignore reads as "it worked" while the phone keeps ringing. |
| **Everywhere else** — rest of the card **and** the scrim outside it | Closes the banner (§7 Dismissed). On `/sleep` the touch must stop at the banner: it does **not** reach the sleep screen's wake handler (`Sleep.razor:47`). |

**How "touch to close" is communicated:** the hint line, always visible while ringing: `Touch anywhere to close. The call keeps ringing.` The second sentence is load-bearing: it says closing is not ignoring.

**Ignore button:** 320 × 128 px, `border-radius: 64px`, `background: var(--signal-red)`, no border, **no glow, no shadow** (UI-8). Pressed: `transform: scale(0.96)` (the `.phone-btn:active` idiom). It is visually unambiguous from the close area by colour (the only solid-filled element on the card), size, its own divided column, and its sub-label.

**Knobs:** the banner does not intercept encoder input. Knob turns and presses behave exactly as they do without it; their HUD renders above the banner. (On `/sleep`, a VOLUME turn wakes the console per ENC-25 — see Q4.) If a knob action navigates (`/sleep` → Home on wake), the banner **persists across the navigation** without replaying its entry.

**Keyboard (dev use):** `Escape` closes the banner like a touch. No other keys.

---

## 7. States

| State | Header | Disc | Ignore column | Hint line | Exit |
|---|---|---|---|---|---|
| **Ringing** (number known or not) | `INCOMING CALL` amber | Halo pulses | Enabled (capability ON) or disabled + reason (OFF) | Shown | — |
| **Name resolving** (≤ ~1 s) | same | Glyph `person` | same | Shown | Upgrades in place (§4). **No** "Looking up…" text: it would flash for under a second. |
| **Declining** (Ignore pressed, capability ON) | same | Halo keeps pulsing | Button: spinner (existing `.spinner`, 32 px, `--text-inverse`) + `ENDING CALL`, full opacity, not pressable. Sub-label hidden. | **Hidden**, and touch-to-close is **suspended** (the outcome must be seen). | Ends on `Idle` (→ Call ended), on request failure (→ Decline failed), or **5 s** after the request was sent with no `Idle` (→ Decline failed). |
| **Decline failed** | same | Pulsing | Button back to `IGNORE`, enabled. Sub-label slot shows the error in `--signal-red`, `role="alert"`. | Shown; touch-to-close active again | Error clears on the next Ignore press. A caller-line upgrade does not clear it. |
| **Dismissed** (touch / Escape) | — | — | — | — | No beat. Fade + scale out, 200 ms. Banner stays closed for **this call** (caller-ID updates do not reopen it). |
| **Answered** (`InCall`) | `ANSWERED` in `--signal-green`, text-shadow removed | Halo stops, disc border and content `--signal-green` | Fades out 150 ms | Hidden | Hold 600 ms, then fade 200 ms. |
| **Call ended** (`Idle`: caller hung up, ring timeout, Ignore succeeded, or the 90 s safety net) | `CALL ENDED` in `--text-medium`, no shadow | Halo stops, disc border and content `--text-medium` | Fades out 150 ms | Hidden | Hold 600 ms, then fade 200 ms. |

A touch during an exit hold closes immediately. A new `Ringing` on the same `phoneId` during an exit hold cancels the exit and shows a fresh Ringing banner (case b).

**Capability OFF:** button rendered as normal but at `opacity: 0.35` (house `.phone-btn:disabled`), native `disabled`. Sub-label slot shows the reason instead of `Ends the call`. Zone inert.

---

## 8. Motion

| Motion | Spec | Reduced motion (`prefers-reduced-motion: reduce`) |
|---|---|---|
| **Entry** | Scrim opacity 0 → 1, 200 ms `--anim-ease-decelerate`. Card opacity 0 → 1 and scale 0.96 → 1, 300 ms `--anim-ease-emphasized`, starting together. | Appears instantly. |
| **Ringing pulse** | **One** pulsing element: the 240 px halo behind the disc (a circle filled `radial-gradient(circle, rgba(240,168,48,0.35), transparent 70%)`), running the existing `ringPulse` (opacity 0.45 ↔ 1, scale 1 ↔ 1.04, 1.2 s ease-in-out infinite). Header and card do not pulse. | **Static**: `animation: none; opacity: 1; transform: none` on the halo. Must be explicit: the global rule at `design-system.css:2037-2043` would otherwise run the keyframe once and freeze it on its last frame, **opacity 0.45** — a dim halo. |
| **Caller upgrade** | 200 ms crossfade, `--anim-ease-standard` | Instant swap |
| **Answered / ended beat** | Colour change instant; Ignore column opacity → 0 over 150 ms; hold 600 ms | Same timings for the hold (it is information, not motion); fades instant |
| **Exit** | Scrim + card opacity → 0, card scale 1 → 0.98, 200 ms `--anim-ease-accelerate`; unmount after | Instant |

---

## 9. Sleep and dark-panel behaviour

| Console state when the ring arrives | What happens | On close |
|---|---|---|
| **Awake** (MainLayout) | Banner over the current page. The 5-minute idle dim (brightness 0.3) is **lifted to full** while the banner is up. | Idle dim resumes under its normal rules (the closing touch counts as activity). |
| **Ambient** (`/sleep`, audio playing) | Banner over the sleep screen. Audio continues (the API ducks it for the announcement as it does today). Console **not** woken. | Sleep screen is underneath, unchanged. |
| **Standby** (`/sleep`, audio parked) | Banner over the sleep screen. **Audio stays parked.** Console not woken. | Same. |
| **Panel dark** (ENC-22 off-timer, or ENC-23 deep sleep) | The Web reports the sleep screen as not showing, which lights the panel; the banner is what it shows. Console not woken. | Sleep screen reported visible again. Panel then follows ENC-22's off-timer. **On the box (`PanelOffAfterMinutes = 0`) that means it stays lit on the sleep clock** until slept again (Q3). |

A touch on the banner while on `/sleep` closes **only the banner**; it never wakes the console. While the panel is dark the touchscreen is off the USB bus, so the first touch that can reach the banner is necessarily after it has lit.

---

## 10. Copy

| Where | String |
|---|---|
| Header, ringing | `INCOMING CALL` |
| Header, answered | `ANSWERED` |
| Header, ended | `CALL ENDED` |
| Primary, contact | `<contact name>` |
| Secondary, contact | `(919) 555-0142` (formatted per §4) |
| Primary, number only | `(919) 555-0142` / as received |
| Primary, unknown | `Unknown caller` |
| Secondary, unknown | `No caller ID` |
| Hint line | `Touch anywhere to close. The call keeps ringing.` |
| Ignore button | `IGNORE` |
| Ignore sub-label (capability ON) | `Ends the call` |
| Ignore in flight | `ENDING CALL` (with spinner) |
| Decline failed | `Couldn't end the call. Try again.` |
| Ignore disabled reason (capability OFF) | `Not available yet. Answer and hang up on the phone, or let it ring.` |
| Ignore `aria-label`, ON | `Ignore call from <name / formatted number / unknown caller>` |
| Ignore `aria-label`, OFF | `Ignore call, not available yet` |
| Live announcement (§11) | `Incoming call from <name>` / `Incoming call from (919) 555-0142` / `Incoming call from unknown caller` |

The word **Reject** does not appear on the banner.

---

## 11. Accessibility

- **Container:** `role="dialog"`, `aria-modal="true"`, `aria-labelledby` → header, `aria-describedby` → hint line.
- **Announcement:** one **persistent, visually hidden** element in the layout, `role="alert"` (assertive), empty until a call; the banner writes `Incoming call from …` into it **once per call** on show. Caller-ID upgrades do **not** re-announce on the alert; they update a sibling persistent `role="status"` (polite) region with the new text. Answered / ended text goes to the polite region. Both regions must exist before their text does (inserting a live region with its text is announced unreliably — the house rule from the band-strip handoff §6).
- **Errors:** the decline-failed line renders as `role="alert"` in the sub-label slot.
- **Focus:** no programmatic focus move (touch kiosk; avoids a stray focus ring). Ignore is a native `<button>` and reachable by Tab; `Escape` closes.
- **Decorative:** halo, disc glyph and divider are `aria-hidden="true"`; the monogram initials are `aria-hidden` (the name is read from the primary line).
- **Colour is never the only cue:** answered/ended states change the header word, not just the colour; Ignore is distinguished by fill, size, column and label.
- AT-SPI check for the Tester: the dialog, header text, caller text, `IGNORE` button and its extents (320 × 128) are all inspectable with the kiosk's `--force-renderer-accessibility`.

---

## 12. Open questions for the owner

1. **Answer from the banner?** The `/phone` hero has an Answer button wired to an existing answer endpoint. You asked for Ignore only; adding Answer is easy but raises "where does the audio go" when answered from the console. **Recommended: not in this row.**
2. **"Reject" on the Phone page hero vs "Ignore" here.** The hero's disabled button says Reject. When the decline endpoint lands, should the hero's button become **Ignore** too, so one action has one name? **Recommended: yes, in the row that enables it.**
3. **Deep sleep after a call.** With `PanelOffAfterMinutes = 0`, a call during deep sleep leaves the panel lit on the sleep clock until you sleep it again. Acceptable, or file an API row so the panel can return to dark on its own? **Recommended: accept for now; file the row if it bothers you in the first week.**
4. **VOLUME during a ring on the sleep screen** wakes the console today (ENC-25), which resumes music under the announcement. Keep that, or should a turn while the banner is up only change volume? (This spec does not change knob behaviour.)
