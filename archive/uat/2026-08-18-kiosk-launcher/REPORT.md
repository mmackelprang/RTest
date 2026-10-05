# UAT — `KIOSK-2`, three desktop icons and one repair-and-open action

**Box:** `ssh mmack@radio` — Intel N100, Ubuntu 24.04, GNOME 46 **Wayland**, 1920x720 touchscreen.
**Date:** 2026-08-18. **Branch:** `feat/kiosk-desktop-launcher`.
**Plan:** [`../../superpowers/plans/2026-08-18-kiosk-desktop-launcher.md`](../../superpowers/plans/2026-08-18-kiosk-desktop-launcher.md) § Part B ·
**Probes:** [`PROBES.md`](PROBES.md) (Part A) · [`PROBES-B.md`](PROBES-B.md) (this row).

`setup-kiosk.sh` was run on the box from `/tmp/kiosk-src` **four times** across this pass (initial,
after each of two fix rounds, then a final clean run). Every result below is read from the
installed copies under `/usr/local/bin`, not from the checkout.

---

## How this was tested without a screenshot

The plan and the queue both record that this box cannot be screenshotted — Shell's screenshot API
and `GetWindows` are `AccessDenied` on GNOME 46, and `gnome-screenshot` / `grim` / `scrot` are
absent — and concluded two checks would need the owner's eyes. **One of those two was retired by
finding a better instrument.**

`python3-gi` + `Atspi` are installed, and AT-SPI exposes every zenity dialog's **widget tree, the
rendered text of each label, screen extents in pixels, and a working `click` action** — on
Wayland, with no screenshot tool. That is how the button heights below were measured, how the
dialog copy was read back verbatim, and how `Try again` / `Open anyway` / `Cancel` were actually
pressed rather than reasoned about.

**What still needs the owner's eyes: the icon's legibility at the panel, and nothing else.**
The 56 px button question is now a measurement (see § The 56 px buttons).

---

## Results — the silent path (the common case)

| # | What | Result |
|---|---|---|
| T13 | Everything healthy -> **no dialog at all** | **PASS** — all four `online`, `TIER=silent`; a real run opened the kiosk and raised nothing |
| T14 | Double tap absorbed | **PASS** — two runs 200 ms apart; the second produced **0 bytes of output** and exited at the `flock`. One `radio-kiosk.service`, 4 established connections |
| T15 | GV blackout trough is never reported | **PASS by direct test; the live condition did not occur** — see below, it matters |
| T16 | A dead GV session **is** reported | **PASS** (direct test) — `>1200`, absent, null and unparseable all -> `needsignin` / `TIER=warning` |
| T17 | Bridge down -> repairable | **PASS** — `--dry-run` prints `WOULD-START VOICE` only when the bridge process is absent; the ensure script is *invoked*, never edited |

### T15 — the trough did not happen, so it could not be observed

17 samples over ~16 minutes spanning **two complete refresh cycles**: every sample read
`VOICE=online` / `TIER=silent`, and the **maximum `psidtsAgeSeconds` observed was 477**. The
counter reset at ~480-500 both times, i.e. the bridge is currently refreshing on a **~10-minute**
cadence, not the ~20-minute one the design was written against — so the appliance never entered
the 660-1200 window at all during the observation.

The trough behaviour is therefore verified by **direct threshold testing**, not by live
observation. 17 synthetic bodies were classified through the shipped `classify_voice`:

```
age=0 / 310 / 659            -> online       (healthy window)
age=660 / 900 / 1200         -> online       (the trough — SILENT, as designed)
age=1201 / 99999             -> needsignin
field absent / null / empty  -> needsignin
{"available":true,"degraded":false,"cookiesValid":true,"psidtsAgeSeconds":707}   -> online
{"available":false,"degraded":true,"cookiesValid":false,"psidtsAgeSeconds":100}  -> online
bridge process absent        -> offline      (regardless of psidts)
```

The second-to-last line is the **exact body captured during a confirmed outage**, where all three
liar fields read healthy. The last two together are the proof that the launcher reads
`psidtsAgeSeconds` and nothing else.

**Do not "correct" the `>1200` threshold to match the observed ~500 reset.** A shorter cadence
makes `>1200` more conservative, which is safe; tightening it to today's observation would
misreport a dead session the moment the cadence returns to 20 minutes.

## Results — repair and failure paths

| # | What | Result |
|---|---|---|
| T18 | Variant A (info) | **PASS** — `radio-web` stopped, then one tap: `--dry-run` predicted `WOULD-START CONSOLE`, the run started it (`active`), the kiosk came up (**4 established connections**), and the dialog read *"The console wasn't running — it's started now."* with `CONSOLE radio-web · started just now [ Online ]`. Auto-closed inside the 10 s timeout |
| T19 | Variant C (error) | **PASS** — all four variants rendered and read back verbatim; transcript below |
| T20 | Retry is not a silent no-op | **PASS** — `Try again` pressed via AT-SPI produced a **second complete run** (the start-failure line appears twice in the log, followed by a fresh dialog), confirming `exec 9>&-` released the `flock` across `exec "$0"` |
| T21 | Hard failure holds the kiosk back | **PASS** — with a component that could not start, at t+12 s the dialog was up and the kiosk was `inactive`, **0 processes, 0 connections**. `Open anyway` then launched it (`active`, 2 connections) |
| T22 | Dialog is not buried by the fullscreen kiosk | **PASS** — with the kiosk fullscreen, zenity mapping took `ACTIVE` off the kiosk window; the default `RADIO_DIALOG_DELAY=2` ships |
| T23 | Exit spares the bridge | **PASS** — kiosk **10 -> 0** processes while the GV bridge stayed at **13 -> 13** and its CDP on `:9224` stayed alive |
| T24 | Shutdown confirm never auto-proceeds | **PASS** — left untouched for **90 s** (GNOME's own confirm powers off at 60 s) and the dialog was still waiting; `Cancel` dismissed it and `uptime -s` was unchanged at `2026-08-16 03:00:33` |
| T25 | Icon resolves | **PASS** (mechanically) — `Icon=/home/mmack/.local/share/icons/radio-console/radio-console.svg`, present, 853 bytes. **Legibility at the panel is the owner's call** |
| T26 | Name order holds | **PASS** — `Exit to Desktop` < `Radio Console` < `Shutdown System` |
| T27 | No unsubstituted placeholders | **PASS** — zero `@...@` across the three entries, the autostart entry and both `/usr/local/bin` launchers |
| T28 | Entries validate | **PASS** — `desktop-file-validate` silent on all four |
| T12 | Installer is idempotent | **PASS** — four consecutive runs, identical output, no errors, three entries at mode `755` each time |

### The four dialog variants, read back off the glass

Captured from the accessibility tree of the actually-rendered dialogs:

```
A  (info, --timeout=10)   Two services weren't running — they're started now.
                          AUDIO     radio-api                          [ Online ]
                          CONSOLE   radio-web · started just now       [ Online ]
                          PHONE     rotary-phone · started just now    [ Online ]
                          VOICE     Google Voice bridge                [ Online ]
                          [ OK ]                                        828 x 58

B1 (warning)              The audio engine is still starting — give it a minute.
                          AUDIO     radio-api · not answering yet      [ Starting ]
                          ...
                          If sound is still missing in a minute, open the console again.
                          [ Continue ]                                  828 x 58

B2 (warning)              Google Voice needs you to sign in again.
                          You'll need a keyboard — the on-screen one can't type into Google's page.
                          ...
                          VOICE     Google Voice bridge · session expired [ Needs sign-in ]
                          [ OK ]                                        828 x 58

C  (error)                The audio engine didn't start.
                          The console will open, but there'll be no sound.
                          AUDIO     radio-api · didn't start           [ Offline ]
                          ...
                          [ Try again ] 414 x 58    [ Open anyway ] 413 x 58
```

Copy matches handoff §6.4 / §6.5 / §6.6 verbatim. The B2 row runs past the value column and pushes
its pill right — **that is what the approved §6.5 mockup also shows**, so it is not drift.

The T21 run produced a genuine failure dialog rather than a canned state:

```
The phone service didn't start.
PHONE     radio-nonexistent-test · didn't start   [ Offline ]
[ Try again ]   [ Open anyway ]
```

Per-component copy is distinct per §6.6, and the unit name — the one place jargon is allowed — is
the only technical string on the dialog. No exit codes, no `systemctl` output and no curl errors
reached the glass.

---

## The 56 px buttons, measured rather than judged

| Configuration | Measured button height |
|---|---|
| GTK 4 default, no override | **44 px** — under the 48 px touch floor |
| the plan's CSS (`min-height: 56px` alone) | **76 px** — GTK 4 adds ~20 px vertical padding on top |
| shipped CSS (`min-height: 56px` + zeroed vertical padding), bare zenity | **56 px** |
| **shipped CSS through the shipped scripts (`GTK_THEME=Yaru-dark`)** | **58 px** — the theme's button border adds 1 px top and bottom |

**58 px ships.** It clears the 48 px floor and exceeds the 56 px `--touch-preferred` target. Both
numbers are recorded in `gtk-touch/gtk-4.0/gtk.css` so the next person does not read 56 there,
measure 58 here, and go hunting a bug that is not there.

**The 24 px inter-button gap is not shipped, and could not be.** GTK 4's message-dialog action area
lays buttons out full-width separated by a 1 px hairline; three selector variants were tried and
all left it at 1 px. The gap's stated intent — a fingertip aiming at one button must not land on
its neighbour — is met by a wider margin than the spec asked for: each button is ~444 px wide, so
the aim point sits ~222 px from the seam against ~100 px in the spec's 200 px-button model.
**Flagged for the owner rather than quietly worked around.**

---

## Two things this pass found that the plan had wrong

**1. The accidental-shutdown mitigation was not actually being enforced.** Plan §10.1 allowed for
persisted icon positions and prescribed clearing them. Clearing them *works* — and then DING writes
fresh positions moments later, so the one-shot unset races a writer that always wins; the attribute
was back on all three entries immediately after a clean install. `keep-arranged true` is the setting
that makes DING auto-arrange and ignore stored positions, and it is now set alongside
`arrangeorder 'NAME'`. Both are set explicitly, because `arrangeorder` was **already** `'NAME'` on
this box and had never applied — `keep-arranged` being `false` is exactly why.

Worth recording: **the live layout before this row had `Shutdown System` and `Exit Browser`
adjacent**, with `Radio Console` at the far end — precisely the arrangement handoff §2.1 is designed
to prevent. It was not a hypothetical risk.

The sort *direction* was not stable across installs (one run put `Exit` at the top, the next
`Shutdown`). It does not matter, and that is the durable point: `Radio Console` is the middle of
three, and **the middle of a three-element sort is the same element whichever way the sort runs**.
It sat between the two "leaving" actions in every observation.

**2. `--icon-name` being dropped from zenity 4 cost nothing.** `PROBES.md` recorded the removal and
framed it as a forced choice between the right glyph and two live buttons. zenity 4 accepts
`--extra-button` on the stock dialog types, so `--error --ok-label="Try again"
--extra-button="Open anyway"` renders the red glyph **and** both buttons, in the mockup's own
left-to-right order. **Handoff §6.1's tier->type mapping ships exactly as designed** — no glyph
traded, no button dropped, no design decision required. (`--icon` still parses in zenity 4 and is
silently ignored: the image node is byte-identical with a valid icon name, a nonexistent one, and
none at all.)

---

## Gates

- `dotnet build --configuration Release` — **exit 0, 0 errors**.
- `bash -n` clean on all five shipped scripts.
- `dotnet test --configuration Release` — **5 pre-existing failures, none attributable to this
  branch.** No file under `src/` or `tests/` is touched by it, and **all five reproduce identically
  on `main`**:
  - 4 x `SrcVariableResamplerTests` — fail inside `PipeWireNative.src_new`, a native
    `libsamplerate` P/Invoke that does not resolve on a Windows dev machine.
  - 1 x `NwsObservationIntegrationTests.RealNwsCall_ReturnsForecast_WithCurrentObservation` — makes
    a **real external NWS API call**; the `Category=Integration` class CI already excludes.

  This is the false-signal problem `TEST-1` exists to fix; recorded here rather than waved past.

## The GV bridge, which this row must not break

Counted at every step — after the installer, after `radio-kiosk-exit`, after each repair run, after
the induced hard failure, and at the end: **13 processes throughout, with CDP on `:9224` answering.**
The bridge's script, timers, profile and extension were never written, installed, edited or killed;
the launcher only invokes `~/bin/gv-bridge-ensure.sh` and reads its exit code.

One incident recorded rather than hidden: an early exploratory probe ran the ensure script over SSH
with its stdout inherited by the Chrome it spawns, which wedged that SSH session for 30 minutes.
**The bridge itself was unharmed** (13 processes before and after; all services active). It is also
why `start_voice()` now wraps that call in `timeout 20` — an unbounded call into a script this repo
does not own could otherwise wedge the launcher with the `--no-cancel` progress dialog on screen and
the single-instance lock still held, leaving a dead icon on a keyboard-less panel until a reboot.

## Left for the owner

1. **Icon legibility at the panel** (handoff §4.5) — do the three grille bars and two knobs stay
   distinct at the shipping size? If they muddy, route back to the Designer for a simplified variant
   rather than hand-editing the brand mark.
2. **The 24 px button gap**, which is not achievable in GTK 4 — see above for what ships instead.
