# HANDOFF — `UI-8` signal glow tokens: restore `--signal-green-glow` / `--signal-red-glow`

**Row:** [`docs/queue/UI-8.md`](../queue/UI-8.md)
**Files in scope:** `src/Radio.Web/wwwroot/css/design-system.css`,
`src/Radio.Web/Components/Shared/PhoneStatusHero.razor`
**Surface:** `/phone` → Phone Status Hero (`.phone-hero`) — state dot and the contextual action row
**Status:** ⛔ **`[SUPERSEDED 2026-09-09 — DO NOT IMPLEMENT]`**

> ⛔ **The owner OVERRULED this handoff and the row landed on the opposite outcome.** Sighted at the
> panel in daylight against a blur ladder (`none · 16 · 20 · 24 · 32`, alpha fixed at `0.25`), the
> owner chose **`none`**. `UI-8` shipped as **delete the three dead references**; `--signal-green-glow`
> and `--signal-red-glow` were **not** declared and must not be reintroduced.
>
> **This document is kept because the reasoning is the asset** — in particular the finding that the
> flat buttons are *transcription residue* from a byte-for-byte port whose token block was copied
> short, and the rule that *the token carries the alpha, the consumer carries the blur*. ⚠ **But do
> not implement its Task table.** The owner's decision and the argument it outweighed are recorded in
> [`../queue/UI-8.md`](../queue/UI-8.md) § *OWNER DECISION 2026-09-09*. If the Answer/Hang-Up pair
> ever does prove hard to separate at `Ringing`, the fix is **fill or border**, never these tokens.

**Relationship to existing handoffs:**

- **Follows** `docs/design-handoffs/design_handoff_phone_page/` — specifically
  `styles.css:28-34` (the glow token block), `:390` (the hero state dot) and `:485`/`:492`
  (the two call buttons). Every value below is that handoff's, unchanged.
- **Extends** nothing. This handoff introduces no new visual decision.
- **Deviates** nowhere.

> ⚠ **The premise of the row is wrong in a way that changes the answer, not just the wording.**
> `UI-8` presents this as an open design question with two defensible sides. It is not open. The
> approved Claude Design handoff already declares all four signal glow tokens and already puts a
> 24px green and a 24px red glow on exactly these two buttons. The shipped stylesheet is a
> **partial port that dropped three of the four declarations**. Nothing was deferred; something
> was lost. See §2.

---

## 1. Decision

**Declare the two tokens. Restore the two reachable glows verbatim at the handoff's values.
Delete the third site — it is not a dead glow, it is a dead *rule*, and turning it on would be a
regression.**

```css
/* ── Signal Colors ──  design-system.css :86-91 */
  --signal-amber:           #F0A830;
  --signal-amber-glow:      rgba(240, 168, 48, 0.25);
  --signal-green:           #4ADE80;
  --signal-green-glow:      rgba(74, 222, 128, 0.25);   /* ← add */
  --signal-red:             #F87171;
  --signal-red-glow:        rgba(248, 113, 113, 0.25);  /* ← add */
  --signal-blue:            #60A5FA;
```

The design argument, stated as a design argument:

**These are the only two controls in the app that are live while everything around them is
disabled, on a panel read from across a room.** `.phone-hero-actions` renders a switch: during
`Ringing` the green **Answer** sits beside a *disabled* red **Reject** and a *disabled* ghost
**Silence**; during `InCall` and `Dialing` the red **Hang Up** / **Cancel** is the sole live
control among three or one dimmed ghosts. The halo is not ornament — it is the only cue besides
fill colour that separates the button you can press from the buttons you cannot, at 1920×720 from
a chair. Every other live-state marker in this file already earns its glow the same way
(`.route-toggle.route-active`, `.nav-badge`, `.unread-dot`).

And the objection that would normally kill a resting glow does not apply here: **there is no
resting state.** Neither button exists on the idle screen. `PhoneStatusHero.razor:146-155` renders
two disabled ghosts at Idle and nothing else. The glow is already scoped to "a call is happening"
by the component's own switch, so no `:hover`-only, press-only, or ringing-only gating needs to be
invented — the condition the row worried about is already the render condition.

Deleting the three declarations instead would codify an appearance nobody chose. Today's flat
buttons are not a design position that survived review; they are the residue of a token block that
was transcribed short.

---

## 2. Why this is a restoration, not a change — the evidence

`docs/design-handoffs/design_handoff_phone_page/styles.css` is the approved Claude Design source
that `design-system.css`'s `§Ph` block was ported from. Its token block:

| Handoff `styles.css` | Value | Ported to `design-system.css`? |
|---|---|---|
| `:28` `--signal-amber-glow` | `rgba(240, 168, 48, 0.25)` | ✅ `:88` |
| `:30` `--signal-green-glow` | `rgba(74, 222, 128, 0.25)` | ❌ **dropped** |
| `:32` `--signal-red-glow` | `rgba(248, 113, 113, 0.25)` | ❌ **dropped** |
| `:34` `--signal-blue-glow` | `rgba(96, 165, 250, 0.25)` | ❌ dropped (also unconsumed — see §5) |

The *consumers* ported byte-for-byte, hover colours and all:

```css
/* handoff styles.css :482-494          ≡  design-system.css :5422-5434 */
.phone-btn.btn-answer { background: var(--signal-green); color: var(--text-inverse);
                        box-shadow: 0 0 24px var(--signal-green-glow); }
.phone-btn.btn-answer:hover { background: #6ee69a; }
.phone-btn.btn-hangup { background: var(--signal-red);   color: var(--text-inverse);
                        box-shadow: 0 0 24px var(--signal-red-glow); }
.phone-btn.btn-hangup:hover { background: #fa8888; }
```

So the four-token family at a uniform `0.25` alpha was decided once, by the owner, in Claude
Design. Amber is the shipped proof that the value works on this panel — it is in service at seven
sites today, including a 42px LED. **Green and red must follow amber's 0.25 exactly.** Differing
would mean overriding an approved decision on the strength of reasoning done at a desk, which is
precisely what the handoff exists to prevent.

**This also corrects the `ENC-12` comment (`:5378-5381`) that the row treats as settled reasoning.**
Its facts are right — the token is consumed and never declared, and it would render as nothing —
but its conclusion rests on calling the fix "silently chang[ing] an unrelated shipped component."
It is not an unrelated component and it is not a change: it is `.phone-btn.btn-hangup` returning
to its own approved appearance. The deferral was still the right call *for that row* — a
token-family restoration does not belong inside an encoder-badge PR — but the reason to record was
"wrong PR", not "unwanted appearance".

---

## 3. Per-site rulings — the three sites are not equivalent

### 3.1 `:5425` `.phone-btn.btn-answer` — **RESTORE, unchanged**

`box-shadow: 0 0 24px var(--signal-green-glow);`

Live only during `Ringing` (`PhoneStatusHero.razor:105`), where it is the primary action and the
one thing on a 96px-LED screen you are meant to hit. Its two siblings in that state are both
`disabled`. The green halo is the disambiguator.

### 3.2 `:5432` `.phone-btn.btn-hangup` — **RESTORE, unchanged, same value**

`box-shadow: 0 0 24px var(--signal-red-glow);`

The row asks whether a glow that reads "affirmative" on green reads "alarm" on red. Considered and
rejected, for two reasons:

- **The alarm is already there and the halo is a rounding error on it.** The button is a 56px
  pill filled solid `#F87171` with `--text-inverse` label. Whatever loudness red carries, it is
  carried by 100% of the pill, not by a `0.25` halo bleeding 24px into the surround. Deleting the
  halo does not make Hang Up quieter; it makes it *flatter than its green twin* while remaining
  exactly as red.
- **Hang Up is not a destructive confirmation, it is the ordinary end of a call**, and in the two
  states where it is live it is the *only* live control. Same job as green: mark the live one.

An asymmetry does exist and is worth naming rather than correcting. `#4ADE80` has ~1.68× the
relative luminance of `#F87171` (0.553 vs 0.330, computed — not measured on the panel), so at
equal alpha the green halo reads brighter. **Leave it.** The affirmative call-to-action being the
one that glows harder is the right ordering, and the two never appear at full strength together
(§3.4).

### 3.3 `:5185-5186` `.phone-hero-source-tag .dot` — **DELETE both declarations. Do not "fix" it.**

```css
.phone-hero-source-tag .dot {
  width: 8px; height: 8px; border-radius: 2px;   /* KEEP */
  background: var(--signal-green);               /* DELETE */
  box-shadow: 0 0 6px var(--signal-green-glow);  /* DELETE */
}
```

⚠ **This site is not dark because of the missing token. It is dark because it never renders.**
`PhoneStatusHero.razor:14-16` writes an inline `style` on every render:

```razor
<span class="dot" style="background: @(StateColors.color);
      box-shadow: 0 0 6px @(StateColors.glow);"></span>
```

An inline style beats any selector without `!important`, so **both** declarations at `:5185-5186`
are overridden unconditionally. The dot already glows today, in the state-appropriate hue
(`PhoneStatusHero.razor:222-228`: amber ringing · green in-call · blue dialing · `transparent`
idle). Declaring `--signal-green-glow` changes nothing here.

**And making it reachable would be a regression.** The hard-coded `--signal-green` predates the
component's four-state colour logic — it is a fossil of the mockup, where the dot was statically
green. If a future change removed the inline style, the dot would paint green during an *amber*
ringing state. One element must not carry two disagreeing sources of truth for its colour.

**Design intent for Planner:** the dot follows call state and is never hard-coded; there must be
exactly one source of its colour and glow. *How* is Planner's call, but note the file's own idiom
two rules down — `.phone-hero-state` (`:5196-5197`) reads
`var(--phone-hero-state-color, var(--text-high))` / `var(--phone-hero-state-glow, transparent)`
from custom properties the component sets on `.phone-hero` (`PhoneStatusHero.razor:5-7`), which
already inherit to the dot. The dot is the outlier for using direct inline properties instead.
Converting it to that idiom would delete the inline style and the dead rule at once. Flagged, not
specified.

**Net effect on the visible count: two glows switch on, not three.**

### 3.4 The pair never fights, because the disabled rule already handles it

`Ringing` is the only state where green and red are both on screen, and there the red one is
`disabled` (`PhoneStatusHero.razor:108`, labelled **Reject**, "Physical handset only").
`.phone-btn:disabled { opacity: 0.35 }` (`:5443`) composites the whole element **including its
box-shadow**, so the Reject halo lands at an effective ~0.09 alpha — present but subordinate.
**No new rule is needed for this.** It is the answer to the row's "pair with opposite meanings"
concern: they are a pair in the stylesheet and never a pair at full strength on the panel.

---

## 4. Hover — **no change. Deliberately.**

`.phone-btn.btn-answer:hover` and `.btn-hangup:hover` lighten the background only, and
`.phone-btn`'s `transition` (`:5415`) already includes `box-shadow 200ms ease`, so a hover glow
*could* be added for free. It should not be:

- **This is a touchscreen.** Chrome applies `:hover` on tap and leaves it stuck until the next
  touch elsewhere. A hover-brightened glow would therefore **persist after the call is answered**,
  on a button that has just been replaced by a different one — a stuck louder state is worse than
  no state.
- **The press affordance already exists and is unambiguous:** `.phone-btn:active { transform:
  scale(0.96) }` (`:5420`). The file's idiom for a press *glow* is
  `.transport-btn-primary:active` (`:702-704`); if one is ever wanted here it belongs on
  `:active`, not `:hover`. Not in this row — `scale(0.96)` is sufficient and a second press cue is
  scope creep on a token restoration.
- **It would cost four more tokens.** A brighter hover halo needs a second alpha per hue
  (`--signal-green-glow-strong`, `--signal-red-glow-strong`, mirroring
  `--accent-glow`/`--accent-glow-strong`) to stay inside the system. Four new tokens to serve a
  state that barely exists on this hardware is a bad trade.

**Existing hover behaviour ships unchanged: background lightens, glow holds steady.**

---

## 5. `--signal-blue-glow` — **do not declare it**

The handoff declares it at `:34` and the shipped file dropped it alongside green and red, so the
symmetry is tempting. **No consumer exists.** A verified scan of all 6,653 lines of
`design-system.css` finds zero occurrences of the string `signal-blue-glow`. Declaring a token nothing reads is the
mirror image of the defect being fixed — unreachable CSS — and the row's own "Not in scope" note
rules out exactly that class of addition. Recorded here so the next person knows it was considered
and where to find the approved value if a blue glow is ever wanted.

---

## 6. Blur is per-site; alpha is family-wide

The rule that makes this system work, restated because it is the durable output of this handoff
and is currently only implicit:

> **The token carries the alpha. The consumer carries the blur radius.**

Amber already demonstrates it — one token at `0.25`, in service at `6px` (`:298`, `:531`,
`:5236`), `8px` (`:735`, `:782`), `12px` (`:750`). Green and red join at the same alpha with a
`24px` radius, which is larger because the emitter is larger: a 56px solid pill, not an 8px badge
or a line of text.

**Consequence for tuning (see §7): if a glow reads too hot on the panel, reduce the blur at the
site. Never reduce the alpha.** Alpha is shared with amber and every other consumer of that hue;
moving it desynchronises the family to fix one button.

---

## 7. Verification on the panel — both light conditions, and what to do if it fails

⚠ **`UX-1` was aborted on 2026-09-08 because the owner found the surfaces "very dark on the
touchscreen in daylight."** That constraint applies here and points the *opposite* way: `UX-1`'s
risk was a value too faint at midday; this row's risk is a value too hot at night.

**What can be said off-box.** Green `rgba(74,222,128,0.25)` at `0 0 24px` on `.phone-hero`'s
`--surface-raised` `#141416` yields an edge delta of roughly **ΔG ≈ 25/255**, decaying to zero
over 24px — a ~3.8× linear-luminance step. For scale, `UX-1` measured 6/255 as below threshold and
proposed 16/255 (2.5×) as the fix. This is comfortably above that, and it is a *chromatic* signal
rather than a neutral near-black ramp, which is the harder stimulus to lose to daylight veiling
glare. **Derived from the CSS, not observed** — the blur falloff is approximated as Gaussian and
the panel's transfer function near black is still unmeasured, which is the same gap `UX-1` flagged
and nobody has closed.

**What must be seen.** No green or red glow has ever rendered on this panel, and there is no
shipped precedent for a `24px` resting glow at any hue — amber's largest is `12px`, and the only
`≥20px` box-shadows in the file (`:704`, `:962`) are transient `:active` feedback. So:

1. Drive the hero through **`Ringing`** (green live, red disabled) and **`InCall`** (red live
   alone) — both states, not one.
2. **In the dark room first**, the console's dominant condition and the one where an over-hot
   value shows. Ask the `UX-1` question that catches over-tuning: *not looking at it, does it stay
   calm, or does it pull the eye?*
3. **Then in daylight**, from the actual chair at real distance and cabinet angle. Confirm the
   halo has not simply vanished.
4. If it blooms at night: **reduce the blur to 16px at the two sites**, keep both tokens at
   `0.25`. Re-check daylight after, since that is the direction that loses it.
5. If it vanishes in daylight at 24px, stop and re-open — that would mean the whole family's `0.25`
   is wrong for this panel, which is a bigger question than this row and touches amber's seven
   existing sites.

**Confidence: HIGH that the tokens should be declared** (the handoff decided it and the port lost
it). **MODERATE that `0 0 24px` at `0.25` is right at night**, because the size has no precedent
here. The alpha is not the variable to move.

---

## 8. Note for `TEST-2`'s lint — two false-positive classes, enumerated

The row proposes a CSS-text test asserting every `var(--token)` resolves to a `:root` declaration.
Ran that scan against `design-system.css` to check this row's own count. Two things it must handle,
or it will fail on green `main`:

**(a) Strip CSS comments first.** The naive scan reports a phantom consumer at **`:5378`** — which
is the `ENC-12` prose (§2 above) *describing* the bug, inside a comment. A lint that flags its own
documentation is a lint that gets disabled.

**(b) Six properties are legitimately set at runtime, not in `:root`.** Each has an in-file
rationale; all six need allowlisting:

| Property | Set by | Consumed at |
|---|---|---|
| `--phone-hero-state-color` / `-state-glow` / `-glow-color` / `-icon-bg` / `-icon-color` | `PhoneStatusHero.razor:5-7`, `:34-35` inline | `:5149`, `:5196-5197`, `:5213-5214` |
| `--encoder-band-y` | `EncoderHud.razor:232` inline | `:6148`, `:6181`, `:6183` |
| `--row-accent` | inline, per row | `:6508`, `:6520`, `:6544` |
| `--sleep-shift-x` / `-y` | component, every 60 s | `:2911` |
| `--rz-danger` | Radzen `material-dark-base.css` | `:4529`, `:4533` |

With comments stripped and those allowlisted, the scan returns **exactly** `--signal-green-glow`
and `--signal-red-glow` — so the guard is well-formed and this row is the only violation on
`main`. That is also the positive control the row asks for, obtained for free: the two known-bad
tokens must be found, the six runtime ones must not.

---

## 9. Change summary for Planner

| # | File | Line | Change | Visible? |
|---|---|---|---|---|
| 1 | `design-system.css` | `:89` after | add `--signal-green-glow: rgba(74, 222, 128, 0.25);` | — |
| 2 | `design-system.css` | `:90` after | add `--signal-red-glow: rgba(248, 113, 113, 0.25);` | — |
| 3 | `design-system.css` | `:5185-5186` | delete `background` + `box-shadow`; keep the 8px geometry | **no** (inline-overridden today) |
| 4 | `PhoneStatusHero.razor` | `:14-16` | single-source the dot's colour/glow — Planner's call on mechanism (§3.3) | no |
| 5 | `design-system.css` | `:5378-5381` | update the `ENC-12` comment: the token is now declared; the "would render as nothing" caveat is stale | — |
| 6 | `design-system.css` | `:5425`, `:5432` | **none** — they become live as written | **yes, ×2** |

Not in scope: `--signal-blue-glow` (§5), any `:hover` or `:active` glow (§4), any theme-parity
work (one `:root`, `color-scheme: dark` at `:57`).

**Two components change appearance: `.phone-btn.btn-answer` and `.phone-btn.btn-hangup`, both
back to their approved handoff appearance. Gate the merge on §7's two-condition sitting.**
