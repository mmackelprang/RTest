# PROBES — Part B (`KIOSK-2`) additions

**Box:** `ssh mmack@radio`. **Read:** 2026-08-18 by Builder, off the live box.
Companion to [`PROBES.md`](PROBES.md), which pinned Part A's four unknowns and deliberately left
these for this row ("Not probed, and why"). **Nothing in `PROBES.md` was re-probed.**

---

## 1. ⚠ The `--icon-name` constraint is REAL, but its consequence is not what it looked like

`PROBES.md` §2 recorded `--icon-name` as dropped in zenity 4, and concluded that per-tier
iconography must come from the built-in `--info` / `--warning` / `--error` types, "which take a
single button" — forcing a choice between the right glyph and two live buttons.

**Measured: that trade-off does not exist.** zenity 4.0.1 accepts **`--extra-button`** on the
stock dialog types, so `--error` renders its red glyph *and* two buttons:

```
--error --ok-label="Try again" --extra-button="Open anyway"
  dialog | 'Error' | (0, 0, 888, 209)
    push button | 'Try again'   | (0, 0, 445, 56)
    push button | 'Open anyway' | (0, 0, 444, 56)
```

`--warning --ok-label="Show the sign-in" --extra-button="Not now"` behaves identically.
**Handoff §6.1's tier→dialog-type mapping therefore ships exactly as designed** — no glyph is
traded, no button is dropped, and no new design decision was required. `--icon-name` was only ever
the *plan's* mechanism; the *design* always specified the glyph as coming from the dialog type.

Button order is also the mockup's order (`Try again` left, `Open anyway` right). `--question` with
`--cancel-label` reverses them, which is a second reason to prefer the stock types.

**`--icon` exists in zenity 4's `--help-all` and is a red herring: it is silently ignored.**
The `image` node is byte-identically present with `--icon=dialog-error`, with a nonexistent icon
name, and with no `--icon` at all — the glyph comes from the dialog *type*, nothing else.

### Exit-code contract (measured by driving the buttons, not from the man page)

| Button | rc | stdout |
|---|---|---|
| `--ok-label` button | `0` | empty |
| `--extra-button` | `1` | the button's label |
| dismissed / ESC / closed | `1` | empty |
| `--timeout` expiry | `5` | empty |

## 2. 56 px buttons — ACHIEVED and MEASURED, not left to a fingertip

GTK 4 default is **44 px** (measured), under the 48 px touch floor. The plan's CSS
(`min-height: 56px` alone) **overshoots to 76 px** because GTK 4 adds ~20 px of vertical padding
on top of `min-height`. Zeroing the vertical padding lands it exactly:

```css
button { min-height: 56px; min-width: 200px; padding-top: 0; padding-bottom: 0; }
```
```
push button | 'Try again' | (0, 0, 444, 56)      <- exactly --touch-preferred
```

The file must be **`gtk-4.0/gtk.css`** (zenity 4.0.1 links `libgtk-4` and `libadwaita-1`),
loaded by pointing `XDG_CONFIG_HOME` at the private config dir. **This retires one of the two
"owner's eyes" checks** — button height is now an objective measurement.

**The 24 px inter-button gap is NOT achievable and is not shipped.** GTK 4's message-dialog action
area lays buttons out full-width with a 1 px separator; three selector variants
(`.dialog-action-box separator`, `.dialog-vbox separator`, `messagedialog .dialog-action-area
separator`) all left it at 1 px. **The gap's stated intent is met by a wider margin than the spec
asked for**: each button is ~444 px wide, so the aim point sits ~222 px from the seam, versus
~100 px in the spec's 200 px-wide button model. Recorded for the owner rather than worked around.

## 3. The dialogs can be driven headlessly — AT-SPI works

`python3-gi` + `Atspi` are present and expose the zenity widget tree with **screen extents** and a
working **`click` action**, on Wayland, with no screenshot tool. This is how §2's button heights
were measured and how the `Try again` / `Open anyway` wiring is verified end to end. It is a
general capability for this box, which `CLAUDE.md` previously recorded as having no remote UI
driving at all.

## 4. Tooling presence (the plan's open list)

| Tool | Present | Consequence |
|---|---|---|
| `flock` | ✅ `/usr/bin/flock` | single-instance guard ships as specced; no PID-file fallback needed |
| `jq` | ✅ | present, but the launcher still parses `psidtsAgeSeconds` with `grep` so it carries no dependency |
| `desktop-file-validate` | ✅ | validation runs |
| `rsvg-convert` / `inkscape` / `convert` | ❌ **all absent** | **no rasteriser on the box.** See §5 |

## 5. Icon assets — the SVG is sufficient, and the PNG set is trimmed on evidence

`librsvg2-common` is installed and gdk-pixbuf carries a working SVG loader
(`libpixbufloader-svg.so`), so an `Icon=` pointing at an **absolute `.svg` path` resolves and
renders. Shipping `radio-console.svg` + a copy of the existing `branding/icon-512.png`.
**The plan's 256 px and 128 px renders are NOT shipped** — nothing on the box or in the repo can
rasterise them, and fabricating them was the only alternative. Not a gap: the SVG is the asset
GNOME actually uses.

## 6. ⚠ Desktop icon ordering — the safety property was NOT holding

Plan §10.1 flagged that hand-dragged positions may be persisted. **They are, and all four entries
carried one:**

```
GV-Bridge.desktop          metadata::nautilus-icon-position: 1789,148
radio-shutdown.desktop     metadata::nautilus-icon-position: 1789,263
radio-exit-browser.desktop metadata::nautilus-icon-position: 1789,378
radio-console.desktop      metadata::nautilus-icon-position: 1789,492
```

`org.gnome.shell.extensions.ding keep-arranged` is **`false`**, so DING honours those positions and
`arrangeorder 'NAME'` never applies. **The live column order was `GV-Bridge · Shutdown System ·
Exit Browser · Radio Console` — i.e. `Shutdown` and `Exit` were ADJACENT and `Radio Console` sat at
the far end.** That is the exact adjacency handoff §2.1 designs against, and it was live.

So clearing those positions is load-bearing for the accidental-shutdown mitigation, not cosmetics.
`setup-kiosk.sh` now unsets the key on every entry it installs.

`icon-size` was `'standard'`; set to `'large'` (§2.2). Three entries at ~130 px per cell occupy
~390 px of the 720 px height, so `large` cannot overflow.

## 7. Live state at probe time (context for the UAT numbers)

`radio-api`, `radio-web`, `rotary-phone.service` all `active`; GV bridge 13 processes;
`radio-kiosk.service` active with 6 established connections to `:5002`;
`psidtsAgeSeconds: 400` (healthy window, `< 660`).

`~/bin/gv-bridge-ensure.sh` is the only one of the three candidate paths that exists — the other
two (`/usr/local/bin`, `/opt/rotary-phone/bin`) are absent, which is precisely why the launcher
resolves it from a list at runtime instead of hard-coding.
