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
