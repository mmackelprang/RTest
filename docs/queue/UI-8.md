# `UI-8` — two glow tokens are consumed and never declared, so three shipped glows render as nothing

[← Builder Queue index](../BUILDER_QUEUE.md)

🟡 **P2.** Filed 2026-09-08. Surfaced by the `UX-1` Planner, then measured directly — the row below
is verification, not relay, and it found **more than was reported to me**.

## The defect

`src/Radio.Web/wwwroot/css/design-system.css` declares its glow tokens in `:root`:

| Token | Declared | Consumers |
|---|---|---|
| `--accent-glow` | ✅ `:74` | `:375`, `:387`, `:704`, `:5078`, `:5788` |
| `--accent-glow-strong` | ✅ `:75` | `:962` |
| `--signal-amber-glow` | ✅ `:88` | `:298`, `:531`, `:735`, `:750`, `:782`, `:5236`, `:5352` |
| **`--signal-green-glow`** | ❌ **never** | **`:5186`, `:5425`** |
| **`--signal-red-glow`** | ❌ **never** | **`:5432`** |

An undeclared custom property makes the whole declaration invalid at computed-value time, so each
of those three `box-shadow`s **resolves to nothing**. They look correct in source and render absent.

The most visible consequence is on the phone call buttons, which were plainly designed as a pair:

```css
.phone-btn.btn-answer  { box-shadow: 0 0 24px var(--signal-green-glow); }   /* :5425 — no glow */
.phone-btn.btn-hangup  { box-shadow: 0 0 24px var(--signal-red-glow);   }   /* :5432 — no glow */
```

Both are dead. `.btn-answer` sits next to amber-glowing siblings that *do* work, so the surface is
inconsistent rather than uniformly flat.

## ⚠ This was already known, partly, and deliberately deferred — read `:5374-5381` first

The `ENC-12` author found it and wrote it down accurately:

> *"No box-shadow / glow. The obvious flourish would be `0 0 6px var(--signal-red-glow)` to match
> `.nav-badge` — that token is CONSUMED further down this file but never DECLARED in `:root`, so it
> would render as nothing while looking correct in the source, and declaring it here would silently
> change an unrelated shipped component."*

That reasoning is **correct and the deferral was right** — declaring the token is a *visible* change
to a component nobody asked to change. This row exists because the finding was recorded in a comment
and never filed, so it has been sitting in the file as known-broken with no owner.

⚠ **But the comment is also incomplete, and that is the reason to re-derive rather than trust it:**
it names only `--signal-red-glow` and only one consumer. **`--signal-green-glow` is a second
undeclared token with two consumers**, one of which (`:5186`) is nowhere near the code the comment
describes. A fix scoped to the comment would leave two thirds of the defect in place.

## The decision this row needs — it is a design question, not a bug fix

Both directions are visible changes and neither is obviously right:

1. **Declare the two tokens.** Three glows switch on. Matches the evident design intent and the
   amber precedent, but changes three shipped components' appearance at once.
2. **Delete the three `box-shadow` declarations.** Codifies today's *actual* appearance and makes
   the intent honest, at the cost of the pairing `.btn-answer`/`.btn-hangup` clearly wanted.

**Get a Designer answer before planning.** `UX-1`'s handoff is the recent precedent for how a colour
value gets decided here, and the same person should say whether these glows belong.

## Verification, and the guard worth adding

A CSS-text test asserting **every `var(--token)` consumed in the file resolves to a declaration in
`:root`** would have caught this at the time, and is the same lint family as `LogSafetyLintTests`.

⚠ **A lint whose green state is "zero violations" proves nothing on its own** — pair it with a
positive control that asserts the scanner finds a deliberately-undeclared token, so the two
assertions fail in opposite directions. `TEST-2`'s plan works this through in detail; reuse its
reasoning rather than re-deriving it.

Note this guard would also have caught `fingerprinting:fpcalcPath`-style orphaning in spirit: a
reference whose target was renamed or never created.

## Not in scope

- `--accent-glow`, `--accent-glow-strong` and `--signal-amber-glow` are all declared and fine.
- ⚠ **No theme-parity work.** This app has exactly one `:root`, `color-scheme: dark` at `:57`, and
  Radzen pinned to `material-dark-base.css`. A light-theme variant would be **unreachable CSS** —
  which is the same defect class as the one this row is fixing.

---

## ⭐ DESIGN GATE DISCHARGED 2026-09-08 — and the question dissolved rather than being answered

**Decision: DECLARE both tokens, RESTORE the two reachable glows, DELETE the third site.**
Handoff: [`../design-handoffs/UI-8-signal-glow-tokens.md`](../design-handoffs/UI-8-signal-glow-tokens.md).

### ⛔ This was never an open design question — it is a transcription defect

`docs/design-handoffs/design_handoff_phone_page/styles.css` is the **approved source the whole `§Ph`
block was ported from.** It declares **all four** signal glow tokens at `:28-34` — amber, green, red,
blue, every one at `0.25` — and puts `0 0 24px var(--signal-green-glow)` / `0 0 24px
var(--signal-red-glow)` on **exactly these two buttons** at `:485`/`:492`.

**The consumers ported byte-for-byte, hovers included. Only the token block was transcribed short:
amber survived, three declarations were dropped.**

⭐ **So today's flat buttons are not a position that survived review — they are residue.** Restoring is
not a visible *change*; it is `.btn-answer` and `.btn-hangup` returning to their own approved
appearance. That is the argument for restoring rather than deleting, and it is stronger than either
option this row originally posed.

### Values, and the durable rule underneath

```css
--signal-green-glow: rgba(74, 222, 128, 0.25);
--signal-red-glow:   rgba(248, 113, 113, 0.25);
```

**Amber's alpha exactly** — the handoff already decided the family at one alpha, and amber is the
shipped proof it works on this panel.

⭐ **The rule, currently only implicit and worth writing down: the TOKEN carries the alpha, the
CONSUMER carries the blur.** Amber ships at 6/8/12 px off one token; green and red join at 24 px
because the emitter is a 56 px pill, not an 8 px badge. ⚠ **If it reads hot at night, cut the blur at
the site — never the alpha, which is shared.**

### ⚠ Three corrections to this row as originally filed

1. **It is TWO glows, not three, and `:5186` must be DELETED rather than fixed.**
   `PhoneStatusHero.razor:14-16` writes an inline `style` setting both `background` and `box-shadow` on
   that dot **on every render**, and inline beats any selector without `!important` — so `:5185-5186`
   are overridden unconditionally and **the dot already glows today**, in the state hue. Worse, the
   rule's hard-coded `--signal-green` is a **mockup fossil** predating the component's four-state colour
   logic: making it reachable would paint the dot **green during an amber `Ringing` state.**
2. **The "pair" never renders as a pair.** `Ringing` is the only state with both on screen, and there
   the red button is `disabled` and labelled **Reject**; `.phone-btn:disabled { opacity: 0.35 }`
   composites the shadow too, landing it at ~**0.09 effective alpha**. Red is live and glowing only in
   `InCall`/`Dialing`, where it is the *sole* live control. **So the affirmative-vs-alarm question never
   has to be answered.**
3. **There is no permanent glow to worry about.** `PhoneStatusHero.razor:102-156` renders **neither**
   button at `Idle`, so the component already scopes this to "a call is happening."

### Hover: no change, deliberately

**It is a touchscreen.** Chrome applies `:hover` on tap and leaves it stuck until the next touch
elsewhere, so a hover-brightened glow would persist *after* the call is answered and the button has
been replaced. `:active { transform: scale(0.96) }` already gives unambiguous press feedback, and a
brighter halo would need `*-strong` variants — four new tokens for a state this hardware barely has.

### On the `ENC-12` deferral

**Its facts are right and its conclusion is not.** *"Declaring it here would silently change an
unrelated shipped component"* — it is not unrelated, and it is not a change. The deferral was still
correct **for that PR**; the reason to record was *"wrong PR"*, not *"unwanted appearance."*

### ⚠ What is NOT established — this gates the merge

**No green or red glow has ever rendered on this panel**, and there is **no shipped precedent for a
24 px resting glow at any hue** — amber's largest is 12 px, and every `≥20px` box-shadow in the file is
transient `:active` feedback. Off-box the green edge computes to ~ΔG 25/255, a ~3.8× linear-luminance
step, and it is **chromatic rather than a neutral near-black ramp**, so daylight should be the *easier*
condition here — the opposite of `UX-1`.

**Confidence: HIGH on declaring the tokens; MODERATE on 24 px being right in a dark room.** §7 of the
handoff gates merge on a two-condition sitting and names the failure lever (**cut the blur, not the
alpha**).

### ⭐ Free finding for the token-declaration lint this row proposed

The Designer ran the proposed scan. **It needs two fixes or it fails on green `main`:**

1. ⚠ **Strip comments first.** The naive version flags a phantom consumer at `:5378` — which is the
   `ENC-12` prose *describing the bug*. **A lint that flags its own documentation gets disabled.**
2. **Allowlist six properties legitimately set at runtime** — `--phone-hero-*` (×5, inline),
   `--encoder-band-y`, `--row-accent`, `--sleep-shift-x/y`, and `--rz-danger` from the Radzen theme.

With both handled the scan returns **exactly the two tokens** — this row is the only violation on
`main`, and **the six runtime properties are the must-not-fire half of the positive control.**

---

## ⭐ OWNER DECISION 2026-09-09 — **no glow.** The Designer's recommendation is OVERRULED.

**Sighted in daylight on the appliance**, on a harness showing both buttons at shipping geometry with
a blur ladder — `none · 16 · 20 · 24 · 32 px`, alpha fixed at `0.25`. **The owner chose `none`.**

So this row resolves as **delete the dead references**, not *declare the tokens*. The row was filed as
"enable or remove"; it lands on remove.

### Why this is a strong decision rather than a rejection of the design work

⭐ **The no-glow state is what has been on that panel all along.** The tokens were never declared, so
every reference already resolves to nothing — an undefined `var()` makes the declaration invalid at
computed-value time and `box-shadow` falls back to `none`. **The owner has been living with exactly
this appearance for months and is choosing it from use**, where the handoff was reasoning from a
design that never shipped.

⛔ **No regression is possible from here**, which is a stronger position than the argument it
displaces.

### What is overruled, recorded rather than smoothed

The Designer's case was that the glow is a **functional cue, not ornament** — *"the only visual
separator of interactive from disabled buttons on a panel read from a chair at 1920×720"* — and
specifically that at **Ringing** the Answer (active) and Hang Up (disabled) buttons render together
and need separating.

**That argument is not refuted; it is outweighed by lived experience of the actual panel.** If the
Answer/Hang-Up pair ever does prove hard to separate at Ringing, the fix is a **fill or border**
change, not a resurrection of these tokens — reopening them would re-import the dual-source problem
§ *three corrections* describes at `:5186`.

### Implementation — verified 2026-09-09

Exactly **three** live consumers, plus one phantom:

| Line | What | Action |
|---|---|---|
| `:5186` | `box-shadow: 0 0 6px var(--signal-green-glow)` — source-tag dot | **delete** (Designer already ruled delete: inline-overridden, enabling creates a dual-source regression) |
| `:5378` | ⚠ **a COMMENT** — the `ENC-12` prose describing the bug | **leave**; it is not a consumer |
| `:5425` | `0 0 24px var(--signal-green-glow)` — `.btn-answer` | **delete** |
| `:5432` | `0 0 24px var(--signal-red-glow)` — `.btn-hangup` | **delete** |

**Zero references in any `.razor` or `.cs`** — confirmed repo-wide. ⛔ **Do NOT declare
`--signal-green-glow` / `--signal-red-glow`.** After the deletions they have no consumers and must not
exist.

### ⭐ Three consequences that change this row's shape

1. ⛔ **The two-condition sighting gate is DISCHARGED — and the dark-room half is CANCELLED.** The
   edit is a **literal zero-visual-change** cleanup: `box-shadow` already computes to `none`, and will
   continue to. **There is nothing to look at in either lighting condition.** Do not schedule a night
   sitting for this row; a UAT claiming to have observed the change would be false.
2. ✅ **Now AUTO-MERGEABLE.** CSS-only, no hardware, no live-audio path, no visual delta. Previously
   gated on a sighting that no longer has a subject.
3. ⭐ **The proposed token-declaration lint now passes with ZERO violations** — these two tokens were
   the file's only ones. ⚠ The lint still needs its two documented fixes before it can be trusted:
   **strip comments first** (or `:5378` above flags as a phantom consumer — *a lint that flags its own
   documentation gets disabled*) and **allowlist the six runtime-set properties**.

### Verification

⚠ **A screenshot diff is the honest gate here, and it must show NO change.** Assert the computed
`box-shadow` on `.btn-answer` / `.btn-hangup` is `none` **both before and after** — that is the whole
claim. ⛔ **A test asserting the glow is absent proves nothing**, since it is absent today; the
meaningful assertion is that **no token reference remains** and the lint returns clean.
