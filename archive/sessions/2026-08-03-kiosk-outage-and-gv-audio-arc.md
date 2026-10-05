# Session state — 2026-08-03

**Session:** Coordinator. Started as "recover state, what's next"; became a live-outage response plus the
design phase of a new three-feature arc.
**Owner status:** away from the hardware until ~**2026-08-10**. Box is network-reachable; the panel is not
physically touchable.

---

## ⚠ Read this first — the live box is half-broken and one part is not remotely fixable

| | State |
|---|---|
| **Radio Console kiosk** | ✅ **Rendering.** Fixed this session. |
| **`/phone` GV surface** | ❌ **Dark since the 2026-08-02 boot.** Not remotely fixable. |
| **Keyring modal + GNOME OSK** | ⚠ **Still overlaid, still grabbing input.** Panel renders but cannot be touched. |

**Root cause of all three:** GDM auto-login never unlocks the login keyring, so every browser that asks
gnome-keyring for it gets a modal *"Authentication required"* dialog raised by gnome-shell.

**Why the modal cannot be cleared remotely:** `Dismiss()` on both live prompt objects times out, as does a
`Locked` read on the `login` collection. `gnome-keyring-daemon` is alive but **serialized behind the displayed
dialog** — it will not service any call until the dialog is answered, and the dialog can only be answered
through it.

**Standing constraint adopted this session:** do **not** restart `gnome-keyring-daemon` or anything else that
could drop the link. The box is on **WiFi** (`enp1s0` unavailable); risking the only link on an unattended
machine is a worse trade than a modal.

### What to do when back at the box

1. **Clear the modal** — click `Cancel` (measured at **(856, 468)**), or answer it.
2. **Fix the root cause permanently** — `pam_gnome_keyring.so` in the GDM stack (auto-unlock on autologin), or
   an empty-password login keyring. Either fixes **every** browser at once and **costs no Google session**.
3. **Then** re-authenticate Google Voice if needed, and the `/phone` surface returns.

### ⚠ Do NOT "fix" the GV browser with `--password-store=basic`

This was tried on RotaryPhone's browser this session, with owner authorization, and **reverted**. All 45
cookies in that profile are **`v11`** — encrypted with the keyring-derived key. The flag makes that key
unobtainable, so Chrome **discards them**. Measured: **45 `v11` → 16 `v10`**, every Google session cookie
gone, browser logged out on "Verify it's you." Scripts and cookie DB were restored and sha256-verified;
**nothing was left changed on RotaryPhone's side.**

Snap-Chromium is immune only because snap forces `basic` from first run, so that profile is `v10` throughout
and never had a key to lose. The flag is safe on Radio Console's kiosk (no session to lose); it is **not** safe
on the GV profile without a planned physical re-login.

---

## Open PRs

| PR | Contents | Gate remaining |
|---|---|---|
| **#463** | `--password-store=basic` on all three kiosk launch paths; corrected `CLAUDE.md` deployment section; full OSK/Wayland UAT report | **Code review.** Live-verified but unreviewed — deliberately not merged. |
| **#464** | ADR-029 + Amendment 1, the Designer handoff, and the `INTEGRATIONS.md` ducking correction | Review. No production code. |

`main` also has **2 unpushed local commits**, one of which is the Designer's handoff (`79413f7`) — committed
straight to `main`, which the owner's rules permit for preparatory design docs.

---

## The arc: three owner-requested `/phone` features

**A** — voicemail plays through the console's audio engine (ducking, real output chain), not the browser.
**B** — a text message gets a play button that speaks it via TTS.
**C** — freeform compose replaced by canned replies.

**Design phase is COMPLETE. Planner is the next dispatch.**

- Architecture: `design/decisions/2026-08-03-gv-audio-through-engine.md` (ADR-029 + Amendment 1)
- Design: `docs/design-handoffs/HANDOFF-phone-console-audio-and-canned-replies.md`

**The mechanism:** A and B are the same problem, so they get one seam — `IEventPlaybackService`, beside
`IAnnouncementService` (that one stays fire-and-forget for *unattended* announcements; the new one owns
*attended* playback). Speech carries the **literal text**; voicemail carries a **`(kind, id, duration)`
reference** — a caller-supplied URL would be an SSRF primitive. Radio.API caches to disk, which makes seek a
local-file operation and **closes carried risk #3** (the voicemail auth seam) as a side effect.

### Owner decisions made this session

| Decision | Answer |
|---|---|
| Voicemail audio at rest | **Cache enabled** — bounded LRU under `./data/gvmedia/` |
| TTS engine for message speech | **Follows the currently selected engine** (ESpeak / Google / **Azure**), not pinned |
| Console as *initial* text sender | **Out of scope.** Reply-only; new-recipient flow is removed, not redesigned |
| Composer during a failed load | **Disabled with a stated reason** — not hidden, not left live |
| OS-level on-screen keyboard | **Rejected** — keep the custom in-app JS keyboard |

### Canned replies

Six, fixed order, never reordered: `Yes` · `No` · `OK` · `Thanks.` · `Call me when you can.` · `Love you.`
Two taps to send (the second tap is the safety — there is no undo). Six rather than eight is a **geometry**
constraint at 1920×720, arithmetic shown in the handoff.

---

## ⚠ The TTS-engine question — settled, and the memory layer is WRONG about it

**This will be re-litigated tomorrow if not read.** Injected memory observations from this session claim
*"users can select engines via SourcesController endpoints, their choice persists in TTSPreferences"* and
prescribe wiring message speech to `TTSPreferences.LastEngine`. **Both claims are false**, and following that
prescription would silently break the owner's requirement.

**Decisive evidence, verified twice this session** (Architect, then re-verified directly):

```
grep -rn "LastEngine" --include=*.cs --include=*.razor src/
→ src/Radio.Core/Configuration/TTSPreferences.cs:17   (its own declaration — that is ALL)
```

**`TTSPreferences.LastEngine` has zero readers and zero writers.** Worse, its class binds
`SectionName = "TTS"` — *the same section* `TTSOptions` binds — and the deployed `TTS` block has no
`LastEngine` key, so it is permanently `"ESpeak"`, and `PreferencesPersistenceService:99` writes that default
back every save period. **Wiring message speech to it would re-pin espeak-ng — precisely the behavior the
owner reversed.**

**The correct resolution: `TTS:DefaultEngine`.**
- `TTSFactory.cs:71` — `var engine = parameters?.Engine ?? ParseEngine(opts.DefaultEngine)` off an
  `IOptionsMonitor<TTSOptions>`. This is the **only** engine-resolution site in `src/`.
- The control the user actually operates is `SystemConfigPage.razor:678`, bound to `_ttsConfig.DefaultEngine`.
  (`:885`'s `_selectedTTSEngine` is a page-local, never-persisted workbench control — that is the transient
  thing the memory observations are describing.)

**Trap Planner must encode:** `TTSParameters.Engine` is **non-nullable** with an `ESpeak` initializer
(`ITTSFactory.cs:87`), so `:71`'s `??` fires only when `parameters` is *entirely* null. Since these requests
carry a `VoiceId`, "just pass null parameters" breaks the moment a voice is attached and **silently selects
ESpeak**. `EventPlaybackService` must resolve and pass the engine **explicitly**.

**Engine-unavailable: fail with a stated reason, never substitute.** ESpeak→cloud fallback would send a
private SMS body to a party the owner did not select; cloud→ESpeak hides a one-place-fixable misconfiguration
behind a mystery voice.

---

## Defects discovered this session (none part of the arc, none fixed)

1. **The ducking priority system arbitrates nothing.** `DuckingService` is binary and reference-counted — the
   first event ducks the primary to a fixed 20%, every subsequent concurrent event changes nothing
   (`DuckingService.cs:138-143`). `GetActiveEventsByPriority` / `StopAllDuckingAsync` have **zero non-test
   callers**, so every `SetPriority` call in the tree is decorative. `INTEGRATIONS.md` claimed otherwise and is
   corrected in PR #464. ADR-029 introduces the first load-bearing use.
2. **`psidtsAgeSeconds` degrades to "time since service start"** when no cookie was ever read. A total auth
   failure therefore reads as a stale-but-plausible number instead of an outage — **this is why the GV surface
   was dark for 35 hours unnoticed.** The queue's ordering notes lean on this field as a blackout clock
   (`<660` healthy / `660–1200` blackout); that reading is only valid once a session exists. A
   `cookiesValid`-based check would have caught it.
3. **`.spinner` has no visual properties at all** (`design-system.css:1209` sets only `animation`) — every
   buffering and sending state on `/phone` is currently invisible. Prerequisite for the arc.
4. **Kiosk CDP on `:9223` is dead** — Chrome ≥136 silently ignores the flag on a default user-data-dir; this
   box runs 151. `radio-refresh-browser` is broken for the same reason. Needs a non-default `--user-data-dir`.
   **Held back deliberately** this session so the keyring restart had one unambiguous variable.
5. **`xset -dpms` is a hard no-op** (X server has no DPMS extension under this session), and
   `power idle-dim` is `true` — the wall panel still dims on idle. The `disable-dpms.desktop` autostart entry
   `setup-kiosk.sh` claims to create does not exist on the box.
6. **`--window-position` is a no-op under Wayland.** RotaryPhone's "off-screen" browser is not off-screen;
   visibility is decided purely by stacking order, i.e. which browser restarted most recently.
7. **`DetectAvailableEngines:247` tests only for non-empty**, while `GenerateGoogleTTSAsync:383` also rejects
   an unsubstituted `${secret:` tag — **which `appsettings.json:173` ships**. Azure checks for the tag in
   neither place. And `AvailableEngines` is cached for the process lifetime (`:54`), so a key fixed in System
   Config stays "unavailable" until restart.
8. **`/sleep` uses `@layout EmptyLayout`** (`Sleep.razor:2`), so the new topbar transport chip is absent
   there — **and the console navigates itself to `/sleep` on idle** via `idle-dimmer.js` and a server-pushed
   `SleepStateChanged`. A voicemail could play on a surface with no stop control, with no user action. ADR-029
   §7.5 takes the safe position (entering `/sleep` stops attended playback); the alternative is routed to the
   sleep arc as §14 Q8.
9. **`PhoneIntegration:Enabled` is `false` and always has been** — `git log -S'"PhoneIntegration"'` returns
   exactly one commit (`8d2a2ab`), never flipped; `appsettings.Production.json` has no override. ADR-029's
   ducking thresholds were re-anchored off it.
10. **`PhoneDevTray.razor:58-61`** serves digits with no `type`/`inputmode`/`data-keyboard`, so the in-app
    keyboard opens in **qwerty** mode. One-line fix; worth sweeping other digit fields.

---

## The keyboard question — closed

**An OS-level OSK is NOT viable on this box.** Mechanically established, not inferred:

> **Chrome 151 on Wayland never issues `zwp_text_input_v3.enable()`** when a web-page input receives focus —
> **zero** occurrences across six input types, *including* with `--enable-wayland-ime
> --wayland-text-input-version=3`.

Without `enable()` the compositor is never told a text field is focused, and GNOME's OSK is driven by exactly
that signal. Chrome also never sends `set_content_type`, so the `type`/`inputmode` hygiene question is **moot
for an OS OSK**. Not a box misconfiguration — `screen-keyboard-enabled` was already `true`, the OSK works, and
Mutter advertises the protocol.

**`onboard` is settled too — the repo is right by accident, wrong in its reason.** `setup-kiosk.sh:116-117`
says it "doesn't work on Wayland." It runs fine (pid 2354, visibly rendering); it simply has **no protocol
route to deliver a keystroke** (wayland socket only, no XTEST; Mutter advertises neither
`zwp_virtual_keyboard_manager_v1` nor `zwp_input_method_manager_v2`). A decorative keyboard eating a third of
a 720px panel.

**Follow-ups:** close `IAC-PRISTINE-INSTALL-AUDIT.md:307` as *"system OSK removed; in-app keyboard is the only
workable option"*; remove `onboard` from `deploy/provision/packages.sh:88` and delete its autostart. The real
work on the custom keyboard is that it has **zero test coverage** across ~12 surfaces.

Full evidence: `docs/uat/2026-08-03-osk-wayland-viability/REPORT.md`.

---

## Cross-repo — filed, needs RotaryPhone

`D:\prj\RotaryPhone\docs\prompts\radioconsole-keyring-modal-blocks-both-services-request.md`

Asks for the **root-cause** fix (PAM auto-unlock / empty-password keyring), explicitly warns off the naive
one-flag ask with the `v11` cookie evidence, and flags two smaller things: their watchdog units
(`gv-bridge-watchdog.service`/`.timer`) are **inactive and disabled** and did not survive the 08-02 reboot,
and `--window-position` is a no-op for them too.

---

## Queue state — unchanged, nothing claimed this session

Open rows: **GV-5** 📋 (composer decision now answered — plumbing survives canned replies intact, but its
freeform-composer chunk should likely be **deferred into the canned-reply PR** rather than built and
replaced; Planner's call), **GV-6** 📋, **GV-7** 📋, **GV-9** 📋, **GV-10** 📋, **OPS-1** 📋, **UX-1** 📋.

**⚠ GV-5 gained a constraint from ADR-029 §11:** canned responses **invalidate a probability assumption** in
ADR-028 §4.4. Drawing replies from six fixed strings makes *"two identical sends to the same counterparty
inside 120s"* ordinary rather than rare, and the poller's re-surfaced copy always falls through to the fuzzy
tier. `OutboundSmsReconciler` must match **one-to-one**, with a regression test. Neither
`OutboundSmsReconciler` nor `GvCounterparty` exists in code yet, so this lands inside GV-5 rather than as a
retrofit.

**⚠ GV-8's `M-1` verification is now blocked on PHYSICAL access, not merely the owner's return** — there is no
GV data flowing to observe. It needs a real inbound SMS to land on an open-but-failed thread.

**UX-1** (skeleton shimmer) and **GV-7** remain gated on a Designer answer.

---

## Next actions, in order

1. **Review + merge PR #463** (kiosk fix) and **#464** (design docs).
2. **Dispatch Planner** on the A/B/C arc — ADR-029 and the handoff are both ready. ADR §14 lists 6 open
   questions; none block planning. Planner's added scope per the Architect: §9.2-§9.4, §7.3, §7.5, §8.1, §11.5.
3. **When back at the box:** clear the modal, apply the PAM/keyring root-cause fix, confirm GV re-auth, then
   close GV-8's `M-1`.
4. **Consider** the held-back CDP fix (non-default `--user-data-dir`) — it restores remote UI driving, which
   would have made much of this session cheaper.
