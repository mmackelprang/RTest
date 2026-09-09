# `UX-1` shimmer variants — harness, and an aborted first sitting

> ⛔ **CORRECTION, 2026-09-08 (night sitting) — the sentence below was wrong about the harness, and
> it is the one sentence in this file a reader would act on.** ~~"The harness works and is reusable;
> the *conditions* were wrong."~~ **Both halves failed.** The conditions were indeed wrong, but the
> harness was *also* broken: its V1–V4 concentrate the highlight into a band spanning ~0.32 of the
> element width, and that band is **absent from the element for a substantial part of every cycle**.
> It could not compare geometry against amplitude; it compared a faint-but-continuous shimmer against
> an intermittent one. ⚠ **The duty-cycle figures in [`NIGHT-SITTING.md`](NIGHT-SITTING.md) § 2 are
> swapped and their arithmetic is wrong** — it counts one tile, but `background-repeat` defaults to
> `repeat`, so the 2W tile recurs and the band crosses **twice** per cycle. Measured in Chromium
> while `UX-1` shipped: the band is on the element **~1.01 s** and off it **~0.49 s**, not the other
> way round. ⭐ **The real mechanism is `ease`** — the animation declares no timing function, so the
> sweep decelerates to **2.1 %/s against a median of 221.7 %/s** (a ~100× stall), producing a
> **~390 ms** dead pause at each cycle boundary and **~110 ms** mid-cycle, which a narrow band sits
> out entirely and the shipping full-width ramp does not.
> ⛔ **Do not reuse `index.html` as an instrument** — it now carries
> a banner saying so. `UX-1` was decided on the **v2** ladder (shipping geometry, five highlight
> values), reconstructible from [`NIGHT-SITTING.md`](NIGHT-SITTING.md) § *"The v2 harness"*.
> Everything else in this file — the aborted sitting, and the finding that the plan's dark-room-only
> gate was an incomplete specification — stands, and led directly to the daylight sitting.

**Status: DEFERRED to a night sitting.** ~~The harness works and is reusable;~~ the *conditions* were
wrong.

## What happened, 2026-09-08 ~03:45 EDT

The five-variant harness was served on the appliance and opened on the console via the kiosk's CDP
endpoint (`:9223`). The owner looked at it **in daylight** and stopped the test:

> "the graphics are very dark on the touchscreen (although I'm looking at it in the daylight) we
> should probably defer this test until nighttime."

The console was restored to `http://localhost:5002` and the temporary server stopped. No
configuration was changed and the kiosk was not restarted.

## ⭐ The aborted sitting is itself a finding, and it changes the row

**`UX-1`'s plan specifies the deciding gate as the owner's eye on the panel *in a dark room*.** That
is now known to be an incomplete specification, because **this console is also used in daylight**,
and in daylight the skeleton surface reads as very dark — which is exactly the condition under which
a low-amplitude shimmer disappears most completely.

Three consequences the plan did not account for:

1. **The A/B must be run in both conditions, or the winner is only proven for one.** A value chosen
   at night in a dark room may be invisible at midday, and that is the same defect this row exists to
   fix, merely relocated.
2. **It shifts the argument toward the brighter candidates.** If one value must serve both, the
   dimmer end of the ladder (V1 at 26, V2 at 31) is less likely to survive daylight. ⚠ **This is a
   hypothesis, not a result** — nobody has yet compared any variant in daylight, and the whole point
   of the row is that reasoning about these amplitudes has been wrong twice already.
3. **There may be a larger question behind it.** "Very dark on the touchscreen in daylight" is an
   observation about the *theme*, not only the skeleton. Whether the appliance's dark surfaces are
   right for a daylit room is a separate design question, out of scope here, and worth its own row if
   the owner sees it again.

## The harness

`index.html` in this directory. Five columns, animating in step:

| Variant | Geometry | Highlight | Notes |
|---|---|---|---|
| **V0** | full-width ramp, `background-size: 200%` | `#1A1A1D` (26) | **ships today, byte-for-byte** |
| **V1** | narrow band | `#1A1A1D` (26) | geometry only — isolates what geometry alone buys |
| **V2** | narrow band | `#1F1F22` (31) | ⚠ collides with `--surface-separator` |
| **V3** | narrow band | `#242429` (36) | the Designer's seed |
| **V4** | narrow band | `#282830` (40) | brightest candidate |

Base surface is `--surface-raised` `#141416` (20,20,22) and the page ground is `--surface-base`
`#0D0D0F`, both lifted from `design-system.css` so the contrast being judged is the real one.

**The only variable between V1–V4 is the highlight value.** The geometry change is identical across
them: the same 1.5 s sweep, with the highlight concentrated into a band spanning ~16% of the element
width instead of ramping across the full width. That is what takes the slope from **0.034 levels/px**
(today) to roughly **0.33** at V3.

**Controls:** *Labels hidden* (default — judge blind) and *Shuffle order* (re-randomises columns and
restarts every animation in step, so position bias cannot decide it).

## Running it again

```bash
# on the box
mkdir -p /tmp/ux1 && cp <this dir>/index.html /tmp/ux1/
cd /tmp/ux1 && (setsid nohup python3 -m http.server 8099 --bind 0.0.0.0 >/dev/null 2>&1 </dev/null &)
curl -sf -X PUT "http://localhost:9223/json/new?http://localhost:8099/"   # opens it on the panel
```

To restore the console afterwards, close that target and stop the server:

```bash
curl -sf http://localhost:9223/json/list   # find the :8099 target id
curl -sf "http://localhost:9223/json/close/<id>"
pkill -f "http.server 8099"
```

⚠ The kiosk's CDP on `:9223` only works because it runs on its own `--user-data-dir`; Chrome ≥136
ignores `--remote-debugging-port` on the default profile. **`:9224` is the Google Voice bridge and is
RotaryPhone's, not ours** — do not drive it, and never widen a kill to `pkill -f chrome`.

## What this does NOT establish

- **No variant has been judged.** The sitting was aborted before any comparison was made.
- **Nothing about the panel's transfer function** at code values 20–40, which the plan named as the
  crux and which remains unmeasured.
- **Nothing about daylight amplitude** beyond the owner's one qualitative remark.
