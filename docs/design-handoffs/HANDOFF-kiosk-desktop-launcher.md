# HANDOFF — Kiosk desktop launcher (three icons, one repair action)

**Surface:** the GNOME desktop on `radio` (`~/Desktop`) — what the owner sees when the Chrome kiosk is closed. Not a Blazor surface.
**Artifacts:** `deploy/debian-x64/kiosk/*.desktop`, `deploy/debian-x64/kiosk/setup-kiosk.sh`, plus one new launcher script and one icon asset.
**Form factor:** 1920×720 touchscreen, finger on glass, no physical keyboard, no hover, no right-click, Wayland.
**Status:** `[APPROVED 2026-08-18]` — all five §11 questions answered by the owner; see
**§12 Owner decisions**. Planned in
[`docs/superpowers/plans/2026-08-18-kiosk-desktop-launcher.md`](../superpowers/plans/2026-08-18-kiosk-desktop-launcher.md),
queued as `KIOSK-1` + `KIOSK-2`.

**Relationship to existing handoffs:**

- **Follows** `HANDOFF-phone-messages-voicemail-sms.md` §"Copy strings (consolidated)" — the house copy voice verbatim: *"plain, calm, sentence case, no exclamation marks, never blame the user; errors say what happened + what to do."*
- **Follows** `HANDOFF-bell-failure-surfacing.md` §3.4 / §3.6 — the **two-tier severity rule** (*hard failure → red with an action; transient/degraded → calm amber, explicitly **not** red*), the `Online` / `Offline` / `Unknown` reachability pill triad, the `[label][value][pill]` status-row grammar, and the *never alarm on absence of evidence* rule.
- **Follows** `HANDOFF-phone-console-audio-and-canned-replies.md` §Cross-5 — the rule that a surface **must not lean on GV's status endpoint to explain itself**, because that endpoint lies. This spec applies that rule to a second consumer (§5.3).
- **Extends** `branding/BRANDING.md` — puts the previously-unwired Anderson Console mark to work as the desktop icon (§4). First shipped use of that kit.
- **Extends** the corpus with **one new dialog channel** — zenity/GTK, outside the Blazor app. Flagged and justified in §9.
- **Deviates** from nothing. No owner decision is contradicted.

---

## 1. Problem + context

Four hand-drifted icons sit on `~/Desktop`. One is broken, one has no icon, one is dangerously over-broad, one is fine.

| # | Entry | Verified state |
|---|---|---|
| 1 | **GV Bridge** | **Broken twice over.** `Exec=systemctl --user start gv-bridge-chrome` points at the **abandoned snap-Chromium unit** (different browser, different profile, different extension) rather than the live path `~/bin/gv-bridge-ensure.sh`. It is also mode **775**; GNOME refuses to launch a group-writable `.desktop`. That mode bit is the "permission problem" the owner reports. |
| 2 | **Radio Console** | Launches, but `Icon=audio-radio` **does not exist in any installed theme**, so GNOME falls back to a generic document glyph. Its flags have also drifted from the boot path (missing `--password-store=basic`, `--ozone-platform=wayland`, the accessibility/backgrounding flags). |
| 3 | **Exit Browser** | Works, but `Exec=pkill -f chrome` **also kills the Google Voice bridge Chrome**, which nothing restarts on demand (the watchdog timer is 2-minute cadence). |
| 4 | **Shutdown System** | Works. Useful. Keep. |

**Owner's decisions, already made — this spec designs inside them:**

1. Consolidate to **three** icons; GV Bridge is absorbed into Radio Console.
2. **Radio Console** becomes the single *make-everything-right* action: validate and start whatever is down, then bring the kiosk forward.
3. When everything is healthy, **stay silent**. A dialog appears only if something had to be started, or something failed.
4. **Exit Browser** stays, scoped to the kiosk so the GV bridge survives.
5. **Shutdown System** stays as-is.

### 1.1 Drift found while speccing — Planner should fold these in

- **`deploy/debian-x64/kiosk/radio-console.desktop` in the repo is already ahead of the live box** (it has `--password-store=basic`; the live copy does not). The box was never re-run through `setup-kiosk.sh` after the Aug 11 edit. **The repo is the source of truth; the box is stale.** Whatever ships must land on the box, not just in git.
- **`setup-kiosk.sh` uses `chmod +x`, which is why entry #1 is 775.** With the default umask, `chmod +x` yields `775`, and GNOME rejects group-writable launchers. The fix is `chmod 755` — an explicit mode, not a `+x` increment. This is a **design-level acceptance criterion**: *a launcher that does not launch has no UX at all.*
- **`--remote-debugging-port=9223` on both entries is inert.** Per `CLAUDE.md`, Chrome ≥136 silently ignores it on the default user-data-dir, and this box runs 151. §7.2 proposes a change that restores it as a side effect.
- **`Comment=` is dead on this device.** It renders only as a hover tooltip, and there is no hover on a touchscreen. Keep it accurate for the app-menu/search surface, but **no affordance may depend on it** — icon and label carry everything.

---

## 2. The three entries

### 2.1 Names, comments, order

| Position | `Name=` | `Comment=` | Icon |
|---|---|---|---|
| left | `Exit to Desktop` | `Close the Radio Console screen. Leaves everything else running.` | `application-exit` (Yaru stock) |
| **centre** | **`Radio Console`** | `Open the Radio Console. Starts anything that isn't running.` | **custom — see §4** |
| right | `Shutdown System` | `Shut down the Radio Console system.` | `system-shutdown` (Yaru stock) |

**On the ordering — it is load-bearing, not cosmetic.** The desktop auto-arranges alphabetically: `Exit to Desktop` · `Radio Console` · `Shutdown System`. That puts the primary action in the centre and — the point — leaves **Exit and Shutdown non-adjacent**, with the safe, most-tapped action physically between them. A mis-aimed fingertip reaching for Exit lands on Radio Console, which is harmless.

> **Do not rename these casually.** The alphabetical order is the accidental-shutdown mitigation. Any new name must preserve `E… < R… < S…`, or the positions must be pinned explicitly (§10.1).

**On renaming `Exit Browser` → `Exit to Desktop`:** the old name is now wrong in a way that matters. After this change the action no longer exits *browsers* — leaving the GV bridge alive is the entire fix — and "browser" is a word the appliance otherwise never says. `Exit to Desktop` names the outcome from the owner's seat, is unambiguously not a power-off, and keeps the sort key. **Owner's call** (§11 Q1); if they'd rather keep the familiar label, `Exit Browser` also sorts correctly and nothing else in this spec changes.

### 2.2 Icon rendering size

Desktop icons must be fingertip targets. Set the desktop-icon size to **large**:

```
org.gnome.shell.extensions.ding icon-size 'large'
```

At `large` the icon tile is ~96px with its label — comfortably past `--touch-min` (48px) and `--touch-preferred` (56px), and legible from across the room, which is the stated glanceability bar for every other surface in this app. Builder to confirm the schema id on GNOME 46 and fall back to `standard` if `large` overflows the 720px height with four rows of grid.

### 2.3 What happens to the GV Bridge entry

Deleted from `~/Desktop` **and** from `~/.local/share/applications`. Its function moves inside Radio Console (§5). No replacement entry, no hidden entry.

This also closes an ambiguity the install audit already flagged — `design/plans/IAC-PRISTINE-INSTALL-AUDIT.md` §7: *"two different Chrome setups coexist… Clarify which path is canonical before scripting it."* **Canonical is `~/bin/gv-bridge-ensure.sh`** (google-chrome + `~/.config/gv-bridge-chrome` + `/opt/rotary-phone/ChromeExtension`). The snap-Chromium `gv-bridge-chrome` unit is dead and the last thing referencing it is the entry being deleted.

> **Cross-repo note.** The bridge script, profile, and extension are **RotaryPhone-owned**. Radio Console is only *calling* `~/bin/gv-bridge-ensure.sh`, not owning or modifying it. Per `feedback_boundary_doc_protocol`, record the new caller in `RADIO-CONSOLE-BT-AUDIO-BOUNDARY.md`'s Change Log so the RotaryPhone session knows a second consumer now depends on that script's path and exit code.

---

## 3. Visual mockup — the desktop

```
┌────────────────────────────────────────────────────────────────────────────────┐  1920
│                                                                                │
│                                                                                │
│    ┌──────┐              ┌──────────┐              ┌──────┐                    │
│    │ ⇥▯   │              │  ▓▓▓▓▓▓  │              │  ⏻   │                    │
│    │      │              │ ◔ ║║║ ▬▬ │              │      │                    │
│    └──────┘              └──────────┘              └──────┘                    │
│  Exit to Desktop          Radio Console          Shutdown System               │  720
│   (Yaru, flat)          (walnut/cream/brass)       (Yaru, red)                 │
│                                                                                │
│         ╰──────── the one warm, branded tile is the primary action ────────╯   │
└────────────────────────────────────────────────────────────────────────────────┘
```

**Why the three are deliberately *not* a matched set.** The obvious move is an icon family — one plinth, three semantic colours. Rejected. On this desktop the safety property that matters is that **the thing you tap constantly looks nothing like the thing that powers off the box.** A matched family makes all three siblings and lets Shutdown blend in. One richly-coloured branded tile flanked by two flat monochrome system glyphs reads correctly at a glance and at distance, costs two fewer assets, and reuses Yaru's own red power semantics for the destructive action. Distinctness is doing the work colour-coding would have done, for less.

---

## 4. The Radio Console icon

### 4.1 The asset already exists — use it

`Icon=audio-radio` names an icon that is in no installed theme. The fix is not to hunt for a Yaru substitute.

**`D:\prj\RTest\RTest\branding\favicon.svg` is the icon.** It is a 256×256 flat-geometric mark of exactly the thing this appliance is: a cream tabletop radio face on a walnut rounded tile — round tuning dial with the needle at upper-right, three vertical speaker-grille bars, two knobs, a brass plinth bar, and a brass gable roofline arcing over the top. `branding/BRANDING.md` describes it as *"A tabletop radio front — tuning dial, speaker grille, two knobs, brass trim — under a gable line: the console in the house it belongs to. Warm cream-on-walnut, deliberately un-digital for a project whose UI is Blazor but whose soul is 1940."*

It has never shipped anywhere. This is the right first use.

**Why this and not a Yaru name.** The brief invited the argument that a stock icon is the better engineering trade. It is not, here, and the reason is specific rather than sentimental:

- Yaru's nearest candidates are `audio-speaker-*` and `audio-card`. Both read as *"sound settings"* — a system-preferences affordance. This icon is not a settings shortcut; it is **the appliance itself**, tapped many times a day by someone who thinks of it as Grandpa's radio.
- The engineering cost of "shipping an asset" here is **zero net new design work** — the file exists, is 853 bytes, three colours, no gradients, no shadows, and already has raster siblings (§4.3).
- Distinctness from the two flanking system glyphs is the accidental-tap mitigation (§3). A third generic system glyph would forfeit it.

### 4.2 Why the walnut palette is correct *here*, despite the dark UI

The repo carries two visual identities: **Command Surface** (`#0D0D0F` + electric cyan `#5CD4E8` + amber `#F0A830` — the shipped in-app language) and **Anderson Console** (walnut `#5C3A21` / cream `#F3E9DC` / brass `#C9A227` — the `branding/` mark). They clash if mixed inside one screen.

They are not being mixed. **The desktop icon does not render inside the app** — it renders on a GNOME wallpaper, beside Yaru system glyphs, in a context the design system has no jurisdiction over. The brand mark is purpose-built as a standalone rounded tile, which is exactly the shape that context wants. Command Surface tokens would be wrong here: `#0D0D0F` on a desktop background makes an invisible icon.

> **Explicitly out of scope: the in-app favicon.** `App.razor:9` currently inlines a cyan Material `radio` glyph. Whether that should also become the Anderson mark is a real question with a real trade-off, and it is **a separate decision this spec does not make and must not be read as making**. Raised as §11 Q4.

### 4.3 Formats, sizes, install path

Canonical is the SVG. Rasters are insurance against a librsvg/DING rendering surprise, not the primary path.

| File | Source | Install path | Purpose |
|---|---|---|---|
| `radio-console.svg` | `branding/favicon.svg` | `~/.local/share/icons/radio-console/radio-console.svg` | **canonical**, referenced by absolute path |
| `radio-console-512.png` | `branding/icon-512.png` | same directory | fallback / high-DPI |
| `radio-console-256.png` | render from SVG | same directory | fallback |
| `radio-console-128.png` | render from SVG | same directory | fallback |

`~/.local/share/icons/` does not exist on the box; `setup-kiosk.sh` creates it (`mkdir -p`). The directory is user-owned and outside `/opt/radio-console/`, so **it survives deploys** — `Deploy-ToLinux.ps1` wipes `api/` and `web/`, which this is not.

**Source of truth is the repo.** Copy the four files into `deploy/debian-x64/kiosk/icons/` and have `setup-kiosk.sh` install them, exactly as it already does for the `.desktop` files. Nothing should be hand-placed on the box.

### 4.4 How the entry references it — absolute path, deliberately

```ini
Icon=/home/mmack/.local/share/icons/radio-console/radio-console.svg
```

**Absolute path, not a theme name.** The Desktop Entry Spec permits either. Theme-name resolution (`Icon=radio-console` + install into `hicolor/scalable/apps/` + `gtk-update-icon-cache`) is the "proper" route for a distributed application, and it is the route that just failed: `Icon=audio-radio` is a theme name that silently resolved to nothing, and the owner got a document glyph with no error anywhere. On a single-appliance box with one known user, **an absolute path cannot fail to resolve and cannot be invalidated by a stale icon cache.** That is the better engineering trade here.

Cost of the choice: the path hard-codes `mmack`. `setup-kiosk.sh` already parameterises `KIOSK_USER`, so it must **generate** the `Icon=` line rather than copy a literal — the `.desktop` files in the repo carry a `@ICON_DIR@` placeholder that the script substitutes. Flagged for Planner; it is the one place this decision costs something.

*Optional, additive:* also install to `~/.local/share/icons/hicolor/scalable/apps/radio-console.svg` and run `gtk-update-icon-cache`, so the app-menu and dock pick it up by name too. Nice-to-have; the absolute path is what the desktop entry uses either way.

### 4.5 Rendering acceptance

*Verified visually from `branding/icon-512.png` while writing this spec: the mark reads unmistakably as a radio, and its warm walnut tile is strongly distinct from flat Yaru system glyphs — which is the §3 distinctness argument confirmed rather than assumed.*

- Legible as a radio at 48px (the smallest DING size) — verify the three grille bars and the two knobs do not merge into blobs. If they soften at 48px that is acceptable; `large` (96px) is the shipping size.
- No detail relies on the brass gable line surviving downscale; it is the topmost 15% of the tile and may soften.
- The tile's own `rx=56` corner radius (22%) is retained. Do not add a second plinth, drop shadow, or glow — the mark is flat by design and Yaru's neighbours are flat.

---

## 5. Radio Console — the repair-and-open action

### 5.1 The rule that makes silence possible

The brief's silence requirement contains a trap, and getting it wrong inverts the whole design:

> The desktop is only visible **when the kiosk is closed**. So the kiosk browser is *always* down when this icon is tapped. If "we started the kiosk browser" counts as *"had to start something,"* the dialog fires **every single time** and the silent path never happens.

**Rule: the kiosk browser is the *subject* of the action, not a *reported component*.** Starting it is the point of the tap, not a repair. It never appears in the status list and never triggers the dialog.

Its *failure* is still reportable — if Chrome will not launch at all, that is an error worth saying. But its routine cold start is invisible, by design.

The four **reported** components are the background dependencies the owner did not ask about but which need to be right:

| Label (90px, mono uppercase) | Value column (jargon lives here) | What it is |
|---|---|---|
| `AUDIO` | `radio-api` | the audio engine |
| `CONSOLE` | `radio-web` | the UI server on :5002 |
| `PHONE` | `rotary-phone` | the GV/SIP service on :5004 |
| `VOICE` | `Google Voice bridge` | the bridge Chrome at voice.google.com |

Order is dependency order, which is also start order, which is also the corpus's *identity/health first* ordering (`HANDOFF-bell-failure-surfacing.md` §3.8).

### 5.2 Health probes — liveness, and evidence, not a probe field

Two rules, both learned the hard way in this repo:

**(a) `systemctl is-active` is not health.** Per the repo's own lesson — *"a health field derived from a probe rather than from 'did the last real call return data' will report healthy straight through an outage"* — each check must include a real request that returns real data.

| Component | Check | Healthy when |
|---|---|---|
| `AUDIO` | `systemctl is-active radio-api` **and** `GET :5000/api/health/version` | HTTP 200 with a body |
| `CONSOLE` | `systemctl is-active radio-web` **and** `GET :5002/` | HTTP 200 |
| `PHONE` | `GET :5004/api/gvbridge/status` | responds at all (any 2xx) |
| `VOICE` | bridge Chrome process present **and** the `psidts` rule in §5.3 | see §5.3 |

**(b) Probe budget: 2 seconds, all four in parallel.** The healthy path must feel like the icon simply opened the kiosk. Anything slower and the owner taps again.

### 5.3 The Google Voice check — the single most important constraint in this spec

`design/INTEGRATIONS.md` and two independent UAT passes established, and `HANDOFF-phone-console-audio-and-canned-replies.md` §Cross-5 encodes as a design rule:

> **`available`, `degraded`, and `cookiesValid` on `/api/gvbridge/status` are liars.** `{"available": true, "degraded": false, "cookiesValid": true, "psidtsAgeSeconds": 707}` was captured while both SMS endpoints were returning hard 502s. **`psidtsAgeSeconds` is the only honest field**, and it is a live blackout clock: **`< 660` healthy · `660–1200` blackout · resets at ~1200.** GV auth is dead roughly **9 minutes in every 20**.

**If this launcher naively asks "is Google Voice OK?", it will answer "no" on roughly 45% of taps.** The dialog would fire on a coin-flip, every time, for a condition that fixes itself in under ten minutes and that the launcher cannot do anything about. Within a fortnight the owner would be dismissing it unread — and then the dialog is worthless on the day something is actually broken. **A status surface that cries wolf is worse than no status surface**, and this one has a documented 45% wolf rate waiting for it.

**The rule: the launcher checks liveness, never auth.** It answers *"is the process running and is the port answering,"* not *"is Google happy."* Transient auth decay belongs to the in-app `/phone` banner, which already owns it.

| `psidtsAgeSeconds` | Reading | `VOICE` row |
|---|---|---|
| `< 660` | healthy window | `Online` — silent |
| `660 – 1200` | **normal blackout trough** | `Online` — **silent.** This is the appliance working as designed. Never report it. |
| `> 1200`, or field absent/null | the refresh cycle itself has stopped | `Needs sign-in` (amber) — see §6.5 |
| endpoint unreachable | the *phone service* is down, not GV | that is the `PHONE` row, not this one |

The `> 1200` test is what separates a genuine dead session from a routine trough, and it does it **with a single stateless probe** — the counter resets at ~1200 in every healthy cycle, so a value beyond it means the refresh never fired. No timestamp file, no history, no new state. (A persisted *unhealthy-since* stamp would be more robust still; raised as §11 Q3, deliberately not designed here because it crosses into Architect's lane.)

Separately and independently, if the bridge Chrome **process** is absent, `VOICE` is `Offline` and is repairable by running `~/bin/gv-bridge-ensure.sh`.

### 5.4 Sequence

```
tap
 │
 ├─ probe all four in parallel ....................... ≤ 2s
 │
 ├─ ALL HEALTHY ──► launch kiosk ──► exit. No dialog. No toast. Nothing.
 │
 └─ SOMETHING DOWN
      ├─ show progress dialog (§6.2)
      ├─ start what's down, in dependency order, waiting for health after each
      ├─ close progress dialog
      │
      ├─ nothing hard-failed ──► launch kiosk FIRST ──► then show report dialog
      │                          (Wayland: last-launched wins the stack, so the
      │                           dialog must come second or Chrome buries it)
      │
      └─ something hard-failed ──► HOLD the kiosk ──► show error dialog
                                   (the owner chooses: Try again / Open anyway)
```

**Two ordering rules that are easy to get backwards:**

1. **On the success path, the kiosk launches *before* the dialog.** `--window-position` is a no-op under Wayland and stacking order is decided by launch order. A dialog raised before Chrome would be swallowed by the fullscreen kiosk and the owner would see nothing.
2. **On the error path, the kiosk is held back.** This is the only place in this spec that deliberately adds friction, and the reason is specific: the error dialog carries the only explanation of what is wrong, plus the retry. Letting a fullscreen kiosk race it risks burying it.

### 5.5 Timeouts

| Phase | Budget | On expiry |
|---|---|---|
| initial probe, all four parallel | 2s | treat non-responders as down |
| `radio-api` → healthy | 20s | amber `Starting` |
| `radio-web` → healthy | 15s | amber `Starting` |
| `rotary-phone` → healthy | 15s | amber `Starting` |
| GV bridge Chrome → process up | 20s | amber `Starting` |
| whole run, ceiling | 60s | stop starting, report whatever is true |

A component that started but has not answered inside its budget is **amber, never red** — per the two-tier rule, it is a transient condition that may still resolve. Red is reserved for *the start command itself failed*.

### 5.6 Single-instance guard

Tapping twice must not produce two kiosk Chromes. A tap that appears to do nothing gets tapped again — the corpus states this directly: *"A tap that produces no visible change reads as a broken button on a touch panel."* With `unclutter -idle 3` and touch input there is **no cursor feedback at all**, so a second tap during the 1–3s cold start is likely, not hypothetical.

Requirement: if a kiosk Chrome is already running, do not spawn another — bring the existing one forward. If a repair run is already in flight, the second tap is absorbed silently.

Mechanism is Planner's call and carries Wayland risk (§10.2).

---

## 6. The dialog

### 6.1 Tier → dialog type

zenity's three stock dialog types map cleanly onto the corpus's severity tiers. **No new severity vocabulary is invented.**

| Condition present | zenity type | Stock icon | Tier |
|---|---|---|---|
| any `Could not start` | `--error` | red | hard failure → red **with an action** |
| else any `Starting` or `Needs sign-in` | `--warning` | amber | degraded/transient → calm amber, **not red** |
| else (everything we touched came up) | `--info` | blue/info | informational success |

### 6.2 Progress dialog (repair path only)

Shown only once a repair is known to be needed — never on the healthy path.

```
┌──────────────────────────────────────────────────────────────┐
│  Radio Console                                               │
│                                                              │
│    Starting the audio engine…                                │
│                                                              │
│    ▓▓▓▓▓▓▓▓░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░         │
│                                                              │
└──────────────────────────────────────────────────────────────┘
```

- `--progress --pulsate --auto-close --no-cancel`, width 760.
- Text updates per component: `Starting the audio engine…` → `Starting the console…` → `Starting the phone service…` → `Starting the Google Voice link…`
- **No cancel button.** Half-started is a worse state than either end, and there is no touch-safe way to unwind it.
- Single ellipsis character `…`, never three dots (corpus convention, `HANDOFF-stop-casting-menu-item.md` §4).
- Closes before the report dialog opens.

**Why a progress dialog at all, when the brief asks for minimum friction:** a repair can take 20–45 seconds. Twenty seconds of a motionless desktop after a tap is indistinguishable from a dead icon, and produces exactly the double-tap this design is trying to prevent. It appears **only** on the rare repair path; the common path never sees it.

### 6.3 Status list — the row grammar

The report dialog **always lists all four components**, not just the broken one. When a dialog appears unbidden the owner's first question is *"how bad is it?"*, and a one-line dialog naming a single failure cannot answer it. Four quiet rows with one coloured row answers it in a glance.

Grammar is the corpus's status row (`HANDOFF-bell-failure-surfacing.md` §3.6), transposed to monospaced text:

```
[90px mono-uppercase plain label]  [1fr technical value · what we did]  [auto state pill]
```

| Row state | Pill | Colour | Value-column suffix |
|---|---|---|---|
| was already running | `Online` | `--signal-green` `#4ADE80` | *(none)* |
| we started it, it came up | `Online` | `--signal-green` `#4ADE80` | `· started just now` |
| started, not answering yet | `Starting` | `--signal-amber` `#F0A830` | `· not answering yet` |
| start command failed | `Offline` | `--signal-red` `#F87171` | `· didn't start` |
| GV session genuinely dead | `Needs sign-in` | `--signal-amber` `#F0A830` | `· session expired` |

Labels and values dim (`--text-low` `#4B5563` for the value column) so the eye goes to the pills. Colour is carried by Pango `<span foreground="…">` using the **existing** `--signal-*` hex values — no new tokens, per the corpus's standing *zero new tokens* discipline.

> **New label flagged: `Starting`.** The established reachability triad is `Online` / `Offline` / `Unknown`. None fits "running but not answering yet": `Unknown` would be a lie (we have evidence — the process exists), and `Offline` would be red for a transient condition the tier rule forbids reddening. `Starting` is the minimum honest addition. `Needs sign-in` is likewise new, for the same reason — the corpus expresses GV auth decay in prose, which does not fit a pill column.

### 6.4 Variant A — repaired (info)

```
┌────────────────────────────────────────────────────────────────────────────┐
│  Radio Console                                                             │
│                                                                            │
│   Two services weren't running — they're started now.                      │
│                                                                            │
│   AUDIO      radio-api                          [ Online ]                 │
│   CONSOLE    radio-web · started just now       [ Online ]                 │
│   PHONE      rotary-phone · started just now    [ Online ]                 │
│   VOICE      Google Voice bridge                [ Online ]                 │
│                                                                            │
│                                              ┌─────────────────────┐       │
│                                              │        OK           │       │
│                                              └─────────────────────┘       │
└────────────────────────────────────────────────────────────────────────────┘
```

**Headline copy** — names the component when there is one, counts when there are several:

| Case | String |
|---|---|
| one | `The audio engine wasn't running — it's started now.` |
| one (console) | `The console wasn't running — it's started now.` |
| one (phone) | `The phone service wasn't running — it's started now.` |
| one (voice) | `The Google Voice link wasn't running — it's started now.` |
| two or more | `Two services weren't running — they're started now.` |

Sentence case, contraction, em-dash introducing the consequence, one sentence, no exclamation mark, no blame — the house voice exactly.

**Dismissal:** `--timeout=10`, auto-closes. Good news should not require a fingertip. Ten seconds reads four rows plus a headline with margin. The kiosk is already up behind it (§5.4), so the timeout costs nothing.

Button: `OK`. One button, because there is no decision to make.

### 6.5 Variant B — degraded (warning, amber)

**B1 · something is slow**

```
┌────────────────────────────────────────────────────────────────────────────┐
│  Radio Console                                                             │
│                                                                            │
│   The audio engine is still starting — give it a minute.                   │
│                                                                            │
│   AUDIO      radio-api · not answering yet      [ Starting ]               │
│   CONSOLE    radio-web                          [ Online ]                 │
│   PHONE      rotary-phone                       [ Online ]                 │
│   VOICE      Google Voice bridge                [ Online ]                 │
│                                                                            │
│                                              ┌─────────────────────┐       │
│                                              │      Continue       │       │
│                                              └─────────────────────┘       │
└────────────────────────────────────────────────────────────────────────────┘
```

Sub-line under the list, `--text-low`: `If sound is still missing in a minute, open the console again.`

**B2 · Google Voice needs a sign-in**

```
┌────────────────────────────────────────────────────────────────────────────┐
│  Radio Console                                                             │
│                                                                            │
│   Google Voice needs you to sign in again.                                 │
│   You'll need a keyboard — the on-screen one can't type into Google's page.│
│                                                                            │
│   AUDIO      radio-api                          [ Online ]                 │
│   CONSOLE    radio-web                          [ Online ]                 │
│   PHONE      rotary-phone                       [ Online ]                 │
│   VOICE      Google Voice bridge · session expired  [ Needs sign-in ]      │
│                                                                            │
│                      ┌──────────────────────┐  ┌─────────────────────┐     │
│                      │  Show the sign-in    │  │      Not now        │     │
│                      └──────────────────────┘  └─────────────────────┘     │
└────────────────────────────────────────────────────────────────────────────┘
```

**The second line is the honest part, and it is not comfortable.** The GV bridge shows a **Google** page, not this app — so the in-app JS keyboard, the only text input that works on this box, **cannot serve it**. And `docs/uat/2026-08-03-osk-wayland-viability/REPORT.md` proved the OS keyboard cannot type into Chrome at all: *"Google Chrome 151 on Wayland never issues `zwp_text_input_v3.enable()` when a web-page input receives focus… the count for `enable` is **zero**."*

**Therefore Google re-login is not achievable with finger-only input.** The corpus rule is *"copy must not imply capabilities that don't exist"* and *"every 'you can't do this here' state says why, in one short sentence."* Telling the owner to go and get a keyboard is the only truthful copy available. Papering over it would send them tapping at a page that cannot receive keystrokes.

`Show the sign-in` raises the bridge Chrome window (launch order = stacking order) so it is at least *reachable* once a keyboard is plugged in. `Not now` dismisses; everything else keeps working — voicemail and texts are the only things affected.

> **Amber, not red — deliberately, and consistent with precedent.** An expired Google session is a degraded condition needing awareness, not a hard failure of this appliance. The corpus already treats GV auth decay as the canonical amber case: *"amber, non-blocking, **NOT red**"* (`HANDOFF-phone-messages-voicemail-sms.md` §Auth-decay). Same condition, same tier.

No timeout on Variant B — it asks for a decision. One fingertip on either button dismisses it.

### 6.6 Variant C — hard failure (error, red)

```
┌────────────────────────────────────────────────────────────────────────────┐
│  Radio Console                                                             │
│                                                                            │
│   The audio engine didn't start.                                           │
│   The console will open, but there'll be no sound.                         │
│                                                                            │
│   AUDIO      radio-api · didn't start           [ Offline ]                │
│   CONSOLE    radio-web                          [ Online ]                 │
│   PHONE      rotary-phone                       [ Online ]                 │
│   VOICE      Google Voice bridge                [ Online ]                 │
│                                                                            │
│                      ┌──────────────────────┐  ┌─────────────────────┐     │
│                      │      Try again       │  │     Open anyway     │     │
│                      └──────────────────────┘  └─────────────────────┘     │
└────────────────────────────────────────────────────────────────────────────┘
```

**Per-component failure copy — four distinct failures, four distinct sentences.** The corpus is explicit that these must not be collapsed into one generic string: *"'couldn't load the recording' and 'the console can't play it' are different problems with different fixes."*

| Component | Headline | Consequence line |
|---|---|---|
| `radio-api` | `The audio engine didn't start.` | `The console will open, but there'll be no sound.` |
| `radio-web` | `The console didn't start.` | `The screen will show an error page instead.` |
| `rotary-phone` | `The phone service didn't start.` | `Calls and texts won't reach the screen.` |
| GV bridge | `The Google Voice link didn't start.` | `Voicemail and texts won't load. Calls still work.` |
| kiosk Chrome | `The console screen didn't open.` | `Try again. If it keeps failing, shut down and power back on.` |
| two or more | `Two services didn't start.` | `Some things won't work until they're running.` |

Each says *what happened* then *what it costs* — the corpus's *what happened + what to do* structure, where the "what to do" for a non-technical owner at the glass is genuinely *"know what you've lost, then retry or restart."*

**No raw exit codes, `systemctl` output, or curl errors in the dialog.** Corpus rule: *"Never show raw HTTP codes / stack traces — logs only."* The unit name in the dim value column is an identifier, not a diagnostic dump, and it is where jargon is allowed to live.

**Buttons.** `Try again` is the default — a surprising share of start failures are races that clear on a second attempt, and one retry is cheap. `Open anyway` launches the kiosk regardless, because a dead `radio-api` still leaves a fully usable UI for diagnosing it. No timeout.

### 6.7 Variant D — nothing wrong

**No dialog. No notification. No toast. No sound.** The kiosk opens; that is the entire feedback. This is the overwhelmingly common path and it must stay completely silent.

> **Rejected: a 2-second `notify-send` launch acknowledgement.** Tempting, because touch gives no cursor feedback and Chrome takes 1–3s to appear. Rejected because it violates the owner's explicit silence decision, opens a second notification channel whose GNOME behaviour on this kiosk is unverified, and solves a problem better solved by the single-instance guard (§5.6) — which makes a stray second tap harmless rather than merely unlikely.

### 6.8 Sizing, type, and touch targets

| Property | Value | Why |
|---|---|---|
| dialog width | `760px` | ~40% of 1920 — reads as a dialog, not a panel |
| dialog max height | `520px` | 720 − 520 leaves 100px top and bottom; must never touch the screen edges |
| headline | Pango `size="x-large"`, bold | glanceable from across the room, the app-wide bar |
| status rows | Pango `size="large"`, monospace | monospace so the three columns align without a table widget |
| value column | `--text-low` `#4B5563` | jargon present but recessed |
| **button height** | **56px** (`--touch-preferred`) | GTK's ~34px default is well under `--touch-min` (48px) |
| button min width | `200px` | fingertip target with margin on a 760px dialog |
| gap between buttons | `24px` (`--sp-6`) | prevents a fat-finger hitting `Open anyway` when aiming at `Try again` |

GTK's default button metrics do not meet the touch floor and **must be overridden** — the corpus treats this as non-negotiable (*"On a touch kiosk they need vertical padding to reach a 48px hit area"*). Mechanism is Planner's; §10.3 records the risk and a suggested route.

Chrome: launch zenity with `GTK_THEME=Yaru-dark` so the dialog reads as part of a dark appliance rather than a bright system popup, without touching any global GTK setting. Verify on the box; if Yaru-dark is unavailable the design still holds — the `--signal-*` pill colours clear WCAG AA on both light and dark GTK surfaces.

---

## 7. Exit to Desktop

### 7.1 No confirmation — and the reason is asymmetry, not laziness

The brief rightly flags that an accidental fingertip near a shutdown-adjacent icon is a real risk. The answer is still **no confirm**, because the two adjacent mistakes are not remotely comparable:

| Accident | Cost | Recovery |
|---|---|---|
| Exit to Desktop | kiosk closes | tap `Radio Console` — ~3 seconds, and the icon is *right there* |
| Shutdown System | box powers off mid-song | physical power button, full boot, audio stops |

An accidental Exit **self-repairs via the neighbouring icon.** Gating a cheap, instantly-reversible action behind a confirm dialog taxes every correct use to protect against a mistake that costs three seconds. The corpus already made this call for the closest analogue — *"Tap = stop. One tap, no confirm. Stopping playback is not destructive"* — and the owner's own note is that this icon works and is useful today.

**The risk is addressed by layout instead** (§2.1): alphabetical ordering puts `Radio Console` physically between `Exit to Desktop` and `Shutdown System`, so the two "leaving" actions are never neighbours, and the icons look nothing alike (§3).

### 7.2 Scoping the kill to the kiosk

`pkill -f chrome` must die. It matches every Chrome on the box, including the GV bridge — which nothing restarts on demand.

The two Chromes are distinguishable: the bridge runs `--user-data-dir=/home/mmack/.config/gv-bridge-chrome`, while the kiosk runs on the **default** profile. Matching on the *absence* of a flag is fragile.

**Recommendation: give the kiosk its own `--user-data-dir`** and match the kill on that. It is precise, it is symmetric with how the bridge is already identified, and it pays for itself twice over:

- It **restores CDP on `:9223`**. Per `CLAUDE.md`, `--remote-debugging-port` is silently ignored on the default user-data-dir since Chrome 136, which is why remote UI driving is currently dead. A non-default profile is the documented fix.
- It makes `--password-store=basic` **safe on this profile by construction**. `CLAUDE.md` warns that flag destroys `v11` cookies on an existing profile — *"measured live at 45 `v11` → 16 `v10`, destroying the Google Voice session"* — but is *"only safe on a profile that was `basic` from first run."* A brand-new kiosk profile is exactly that, and it holds no Google session to lose (it only ever visits `localhost:5002`).

This touches ops surface beyond pure UX. Flagged for Planner and, if they judge it structural, Architect. **The design requirement is only this: Exit to Desktop must close the kiosk and leave the Google Voice bridge running.** The profile route is the recommended means, not the requirement.

Post-exit there is no dialog, no toast, no confirmation. The desktop appearing is the feedback.

---

## 8. Shutdown System

**Unchanged, per the owner's decision.** One observation is recorded because it was found while speccing and the owner should have it — it is explicitly **outside** the decisions already made and needs their call before anyone acts:

> `gnome-session-quit --power-off` raises GNOME's own confirmation dialog, which carries a **60-second countdown and then powers off by itself.** On a wall panel that means an accidental tap, followed by the owner walking away, powers the box off anyway. The confirm is not a confirm; it is a delay.

A confirm that proceeds on inaction is the wrong shape for a touchscreen. If the owner wants it changed, the fix is a zenity confirm in the language of §6 that **never auto-proceeds**, followed by `systemctl poweroff`:

- Headline: `Shut down the radio?`
- Sub-line: `Music stops and the screen goes dark. You'll need the power button to start it again.`
- Buttons: `Shut down` / `Cancel`, `Cancel` focused by default, 56px tall, 24px apart.

**Not proposed as part of this spec's scope.** Raised as §11 Q2.

> **Superseded 2026-08-18 — the owner approved Q2.** The dialog described immediately above *is*
> now in scope and ships as `radio-shutdown-confirm` (plan Task B6). The sentence "not proposed as
> part of this spec's scope" was true when written and is retained for the record; it no longer
> describes what is being built.

---

## 9. New patterns flagged

The brief asked for these to be called out explicitly rather than slipped in.

| # | New thing | Why it is not reuse | Risk if wrong |
|---|---|---|---|
| 1 | **A zenity/GTK dialog channel outside the Blazor app** | The corpus's entire dialog/toast vocabulary is Radzen-in-browser. This surface runs when the browser is *closed*, so none of it is reachable. `zenity` and `notify-send` are the only tools installed (`yad`, `kdialog` absent; `xmessage` is X11-era). | A second visual language drifts from the first. Mitigated by carrying the `--signal-*` hex values and the copy voice across verbatim, and by mapping zenity's three dialog types onto the corpus's existing severity tiers rather than inventing new ones. |
| 2 | **Pill labels `Starting` and `Needs sign-in`** | The established triad is `Online`/`Offline`/`Unknown`. `Unknown` would be a lie for a process we can see running; `Offline` would redden a transient condition the tier rule forbids reddening. | Two extra words in a vocabulary of three. Contained: they appear only in this dialog and nowhere in the app. |
| 3 | **Anderson Console mark used as a shipped asset** | `branding/` has never been wired to anything. This is its first use. | Two identities now visibly coexist on the box. Justified in §4.2 — they occupy different contexts and never share a screen. Made worse if someone later half-adopts the mark inside the dark UI, which §11 Q4 exists to prevent happening by accident. |
| 4 | **The dialog reports on components, not on the action taken** | The corpus has status *cards* (`/phone` System Status) but no *"here is what I just did for you"* report. | Reads as noisy if it fires often. Contained by §5.1 (the kiosk is never a reported row) and §5.3 (GV blackouts are never reported), which together are what keep the common path silent. |

Everything else is reuse: the copy voice, the two-tier severity rule, the `[label][value][pill]` row grammar, the `--signal-*` colours, the touch-target floors, the `…` ellipsis, the *zero new tokens* discipline.

---

## 10. Implementation risks for Planner

These are places where the design is sound but the mechanism needs proving on the box. None changes what the design *is*.

**10.1 · Desktop icon ordering may not be pinnable.** §2.1 depends on alphabetical auto-arrange. GNOME 46's Desktop Icons NG can also persist hand-dragged positions, and the current four icons are already hand-drifted. Builder must establish which mode is active and either pin positions explicitly or clear the saved positions so alphabetical applies. **If neither is reliable, the fallback is still safe** — the alphabetical order is what the names already produce.

**10.2 · Single-instance / window-raise under Wayland.** `--window-position` is a no-op and `GetWindows` is `AccessDenied` on GNOME 46, so "raise the existing window" has no clean API. A `pgrep` guard that simply *declines* to launch a second instance satisfies the design requirement even if raising proves impossible. Same caveat applies to `Show the sign-in` in §6.5 — if the bridge window cannot be raised, that button should be dropped and the copy reduced to the statement alone. **Do not ship a button that does nothing.**

**10.3 · GTK button sizing.** 56px buttons need a GTK CSS override; there is no zenity flag for it. One route that scopes the change to this process alone is pointing `XDG_CONFIG_HOME` at a small config dir containing a `gtk-3.0/gtk.css` with `button { min-height: 56px; min-width: 200px; }`. Unverified — Builder to confirm. **If it cannot be made to work, say so rather than shipping 34px buttons**; the fallback is a wider dialog with fewer, larger buttons.

**10.4 · Dialog stacking over a fullscreen kiosk.** §5.4 relies on launch order winning the stack. A short delay after launching Chrome may be needed before raising zenity. Verify; if the dialog can be buried, invert to *dialog first, kiosk on dismiss* for all variants.

**10.5 · `chmod 755`, not `chmod +x`.** The mode bug that broke the GV Bridge entry is in `setup-kiosk.sh` and will recur for all three entries otherwise. Also keep the existing `gio set … metadata::trusted true`.

**10.6 · The box is stale relative to the repo.** Shipping this means re-running `setup-kiosk.sh` on the box, not just merging. The web-freshness gate in `CLAUDE.md` does not cover desktop entries — verify by reading the installed files.

---

## 11. Open questions for the owner

**Q1 · Rename `Exit Browser` → `Exit to Desktop`?** The old name is now inaccurate (it deliberately no longer exits all browsers) and "browser" is a word the appliance otherwise never uses. Both names sort correctly, so the safety ordering holds either way. *Designer recommendation: rename.* Low cost, and the current name actively misdescribes the new behaviour.

**Q2 · Replace GNOME's auto-proceeding shutdown confirm?** (§8) The owner said Shutdown stays as-is, and this spec honours that. But its 60-second countdown means an accidental tap plus walking away still powers off the box. *Designer recommendation: replace it with the §8 dialog.* Genuinely reduces risk and adds no friction to intentional use — but it is outside the stated decisions, so it needs an explicit yes.

**Q3 · Is the stateless `psidtsAgeSeconds > 1200` test good enough?** (§5.3) It distinguishes a dead GV session from a routine blackout with a single probe and no stored state, which is why it is proposed. A persisted *unhealthy-since* timestamp would be more robust against a changed refresh cadence upstream. That is a data-shape decision — **Architect's lane, not mine.** Escalate if Planner wants the sturdier version.

**Q4 · Should the in-app favicon also become the Anderson mark?** (§4.2) **Deliberately not decided here.** This spec uses the brand mark for the *desktop icon only*, where it has no dark-UI neighbours. Changing `App.razor:9` is a different question with a real trade-off — walnut/cream/brass against `#0D0D0F` Command Surface — and it deserves its own pass rather than riding along on a launcher cleanup.

**Q5 · Are the four reported components the right four?** (§5.1) They are the background dependencies of a working appliance. Anything else worth surfacing — PipeWire default sink, Bluetooth adapter state, the nightly maintenance timer? *Designer recommendation: no, keep it at four.* Each added row costs glanceability, and the dialog's job is "what did I have to fix," not "full system diagnostics." That job belongs on the in-app Diagnostics surfaces, where jargon is welcome.

---

## Hand-off summary for Planner

Three desktop entries replace four. The broken GV Bridge entry is deleted and its job folded into **Radio Console**, which becomes a repair-and-open action: probe `radio-api` / `radio-web` / `rotary-phone` / the GV bridge in parallel (≤2s), start whatever is down in dependency order, then open the kiosk. **When nothing needed fixing it shows nothing at all** — which works only because the kiosk browser itself is treated as the subject of the action rather than a reported component (§5.1), and because Google Voice's 9-minutes-in-20 auth blackout is deliberately never reported (§5.3). Get either of those wrong and the dialog fires on almost every tap.

When something *did* need fixing, one zenity dialog reports all four components using the corpus's existing `[label][value][pill]` grammar and `--signal-*` colours, in three tiers mapped onto zenity's `--info` / `--warning` / `--error`: started-for-you auto-closes in 10s, degraded waits for a tap, hard failure holds the kiosk back and offers `Try again` / `Open anyway`.

**Exit Browser** becomes **Exit to Desktop**, scoped to kill only the kiosk profile so the GV bridge survives — recommended via giving the kiosk its own `--user-data-dir`, which also restores CDP on `:9223` and makes `--password-store=basic` safe by construction. **Shutdown System** is untouched, with one flagged hazard (§8, Q2).

The **icon asset already exists**: `branding/favicon.svg`, the Anderson Console mark, unwired until now. Ship it plus PNG fallbacks to `~/.local/share/icons/radio-console/` and reference it by **absolute path** — a theme name is precisely what failed here. The other two entries keep flat Yaru stock glyphs on purpose (§3).

Three fixes are prerequisites rather than polish: `chmod 755` (not `chmod +x`) or the launchers stay unlaunchable; 56px GTK buttons or the dialog is untappable; and re-running `setup-kiosk.sh` on the box, which is stale relative to the repo.

---

## 12. Owner decisions (recorded 2026-08-18 — settled, do not re-litigate)

All five §11 questions are answered. §11 is left intact above as the Designer's record; this
section is the authority on what ships.

| §11 | Decision | Notes |
|---|---|---|
| **Q1** — rename `Exit Browser` → `Exit to Desktop` | **APPROVED** | Sort key preserved (`E… < R… < S…`), so the §2.1 safety ordering holds. |
| **Q2** — replace GNOME's auto-proceeding shutdown confirm | **APPROVED** | Overrides the earlier "Shutdown stays as-is". The §8 dialog ships and **waits indefinitely** for an explicit tap. |
| **Q3** — stateless `psidtsAgeSeconds > 1200` probe | **APPROVED**, no Architect escalation | Owner confirmed the field is real and populated on the live box: `psidtsAgeSeconds: 310`, `authBlackout: false`, `degraded: false`. |
| **Q4** — in-app favicon (`App.razor:9`) | **DEFERRED** — Designer's recommendation upheld | `--kiosk` hides the tab strip, address bar and title bar, so that favicon is **never visible on the appliance**. It does not justify carrying the dark-surface and 16–32 px legibility risks inside a launcher cleanup. Picked up later as its own item. **The desktop icon (§4) is unaffected and fully in scope.** |
| **Q5** — the four reported components | **APPROVED** | `radio-api` · `radio-web` · `rotary-phone` · GV bridge. No fifth row. |

### Scope confirmed beyond §11

- **Fix `Deploy-ToLinux.ps1`'s kiosk relaunch.** It kills the kiosk then relaunches with
  `DISPLAY=:0`, which the script itself documents as a known Wayland defect — so every deploy
  leaves the screen dead. Verified working replacement:
  `systemd-run --user --collect google-chrome --kiosk … --ozone-platform=wayland …`, which
  inherits the graphical session environment. Confirmed by **2 established connections to
  `:5002`** afterwards; during the Aug 2 outage that count was **0**, which is why it is the
  meaningful liveness check and process existence is not.
- **Drop `onboard`** from `deploy/provision/packages.sh:88` and the autostart entry, per the
  recommendation already made in `docs/uat/2026-08-03-osk-wayland-viability/REPORT.md`.
- **Close the installer-drift loop** (§1.1) — nothing has ever installed the repo's `.desktop`
  files to `~/Desktop`, which is the root cause of three separate drift instances found in one
  day. Treated as a first-class goal, not a side effect.

### Boundary reaffirmed

`gv-bridge-ensure.sh`, its watchdog timer and its nightly restart timer are **RotaryPhone-owned**
and are being brought under version control in that repo right now. Radio Console **invokes and
probes only** — it must not own, reimplement or edit bridge startup. The Change Log note for
`RADIO-CONSOLE-BT-AUDIO-BOUNDARY.md` is a commit in `D:\prj\RotaryPhone`, so it is tracked as
§ Cross-repo handoffs **#8** in `docs/BUILDER_QUEUE.md` rather than as a claimable row here.
