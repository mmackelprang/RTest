# WirePlumber rule: BT A2DP source exclusivity (no auto-route to default sink)

> **For Claude:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` to implement this plan task-by-task.

**Goal:** Stop PipeWire's `module-stream-restore` from auto-routing the BT phone's A2DP source stream directly to the default sink (the local soundbar). When Radio.API is the only consumer of BT audio, there should be exactly ONE path: `phone → bluez_input → Radio.API → output(s)`. The rogue parallel path `phone → default sink` is what's causing the dual-audio Mark hears AND the comb-filter "underwater" artifact that masked Path D's true effectiveness.

**Source research**: [`docs/research/2026-05-22-bt-dual-routing-investigation.md`](../research/2026-05-22-bt-dual-routing-investigation.md) — confirms the dual-path via `pactl list sink-inputs` showing both `application.name="Radio.API"` (the designed path) AND `media.name="Pixel 10 Pro XL"` (the rogue path) feeding the same sink.

**Architecture**: WirePlumber rule that intercepts BT-A2DP-source-derived streams at routing time. The rule sets `node.passive=true` (or similar) on streams whose `node.target` would otherwise resolve to the system's default sink, preventing auto-routing. Radio.API's capture node is a SEPARATE graph endpoint (bluez_input → application stream) and is not affected by this rule.

**Tech Stack**: WirePlumber Lua config (the canonical mechanism per existing project pattern at `/etc/wireplumber/bluetooth.lua.d/`). Linux-only operator change; no .NET code touched.

**Addresses**:
- "Audio coming from both the soundbar AND Cast device on startup" (Mark's report, 2026-05-22)
- Path D measurement noise — once the rogue path is silenced, Path D's true effectiveness becomes measurable

---

## Task 0: Confirm the dual-path is still active (operator action — Mark, not Builder)

Before any change, capture the current sink-input list to confirm the dual-routing is reproducible:

```bash
ssh mmack@radio "pactl list sink-inputs | grep -E 'Sink Input #|application.name|media.name|client'"
```

Expected: at least two sink-inputs feeding the same sink — one for `application.name="Radio.API"` and one for `media.name` matching a paired BT device.

If the second one is absent: PipeWire's stream-restore hasn't saved a route for the current BT device yet (e.g., factory-fresh PipeWire state). The fix is still valid as a preventive measure but the symptom won't reproduce locally until the device gets paired AND the user once-routes to the sink manually.

---

## Task 1: WirePlumber rule — refuse auto-route for BT A2DP source streams

**Files:**
- Create: `deploy/common/wireplumber/51-bt-a2dp-no-default-route.lua` (or similar — match existing project convention)
- Modify: `deploy/Deploy-ToLinux.ps1` (or the wireplumber install path within it) to scp the rule into `/etc/wireplumber/wireplumber.conf.d/` on radio

**Step 1:** Audit current WP config layout on radio:

```bash
ssh mmack@radio "ls -la /etc/wireplumber/ /etc/wireplumber/main.lua.d/ /etc/wireplumber/wireplumber.conf.d/ 2>&1 | head -30"
```

The rule's destination depends on whether radio runs WirePlumber's old (0.4.x — Lua) or new (0.5.x — script) config model. Older Ubuntu N100 likely 0.4.x → `/etc/wireplumber/main.lua.d/`. Verify with `wireplumber --version` on the box.

**Step 2:** Author the rule. For WP 0.4.x (Lua):

```lua
-- /etc/wireplumber/main.lua.d/51-bt-a2dp-no-default-route.lua
--
-- Prevent PipeWire's stream-restore module from auto-routing BT A2DP source
-- streams to the system's default sink. Radio Console is the intended consumer
-- of BT audio (via its capture node graph endpoint, which is unaffected by
-- this rule). Without this, PipeWire creates a parallel direct-playback path
-- from BT → default sink that causes dual-audio + comb-filter artifacts.
--
-- See docs/research/2026-05-22-bt-dual-routing-investigation.md for the
-- diagnostic that motivated this rule.

table.insert(default_policy.policy.roles, {
  ["match"] = {
    {
      ["node.name"] = "matches", "bluez_input.*",      -- any BT-source node
      ["media.class"] = "Audio/Source",                 -- A2DP source class
    },
  },
  ["actions"] = {
    ["update-props"] = {
      ["node.target"] = "null",                         -- refuse target sink
      ["node.dont-reconnect"] = true,                   -- don't auto-link to default sink
      ["session.suspend-timeout-seconds"] = 0,
    },
  },
})
```

Alternative for WP 0.5.x (TOML/JSON-conf): same intent, different syntax. The plan-execution Builder will adapt to the runtime version found in Step 1.

**Step 3:** Adapt the deploy script to push the rule on every deploy. In `deploy/Deploy-ToLinux.ps1`, add a step (around where the existing systemd unit files are scp'd):

```powershell
# Push WirePlumber rules
Write-Host "  Syncing WirePlumber rules..."
scp deploy/common/wireplumber/*.lua "${SshTarget}:/tmp/" 2>&1
ssh $SshTarget "sudo mv /tmp/51-bt-a2dp-no-default-route.lua /etc/wireplumber/main.lua.d/ && sudo systemctl restart --user wireplumber || true"
```

Note: `systemctl --user restart wireplumber` may need to run as the actual `mmack` user; if `sudo` over SSH doesn't have access to the user-mode systemd, fall back to `pkill -HUP wireplumber` (signals the daemon to reload).

**Step 4:** Build verification — N/A (config-only change).

**Step 5:** Commit:

```bash
git add deploy/common/wireplumber/51-bt-a2dp-no-default-route.lua deploy/Deploy-ToLinux.ps1
git commit -m "fix(audio): WP rule preventing BT A2DP source from auto-routing to default sink"
```

---

## Task 2: Clear pre-existing stream-restore decisions for known BT devices (one-time)

The WP rule prevents NEW auto-routing decisions. Already-saved stream-restore entries from prior sessions still exist in `~/.local/state/pipewire/stream-restore` or similar. Clear them so the rule takes effect for existing BT devices.

**Step 1:** Operator action on radio (Mark, after deploy):

```bash
ssh mmack@radio "rm -f ~/.local/state/pipewire/stream-restore && pkill -HUP wireplumber"
```

(Builder documents this; doesn't execute.)

---

## Task 3: Deploy + verify acceptance criteria (operator action — Mark, post-merge)

**Step 1:** Deploy:

```bash
pwsh.exe -NoProfile -Command "& './deploy/Deploy-ToLinux.ps1' -TargetHost radio -Runtime linux-x64"
```

**Step 2:** Verify the WP rule is active. Reconnect the BT phone, then:

```bash
ssh mmack@radio "pactl list sink-inputs"
```

**Success criterion** (primary):
- ONLY `application.name="Radio.API"` sink-input should be present
- NO `media.name="Pixel ..."` or other BT-device sink-input on the soundbar's sink

**Step 3:** Subjective verification:
- Disconnect Cast. Confirm soundbar is silent (Radio.API muted local because Cast was previously selected; this state should persist).
- Or: ensure no Cast is selected. Audio should flow normally through Radio.API → soundbar (Path A only).

**Step 4:** Re-run Path D UAT under the now-single-path condition:

```bash
ssh mmack@radio bash /tmp/path_d_uat.sh
```

Expected after Part 1 lands AND Path D is given a fair test:
- `D1 comp reduction ratio` should drop to ≤0.1 (the resampler can finally do its job without competing against direct-routing)
- `D2 underrun events/hour` should be 0
- `D3 residual |ppm|` should drop near 0
- Subjective: "underwater" gone

If Path D's metrics still don't pass: that's a real Path D problem worth deeper investigation (SincMedium quality, closed-loop ratio control). But likely the metrics now reflect actual Radio.API behavior.

---

## Out of scope

- **Universal stream-restore disabling.** This plan only narrows the rule to BT A2DP source streams. Other audio (file player, system sounds) keeps stream-restore functionality.
- **A second BT device.** If Mark pairs a different phone, the rule applies to it too (matches any `bluez_input.*`). No per-device customization needed.
- **The "Out" pill stub** — separate plan at `docs/plans/2026-05-22-output-picker-ui.md`.
- **WirePlumber 0.5.x TOML config migration.** If radio is on 0.4.x today and upgrades to 0.5.x in a future Ubuntu release, this rule will need re-authoring in the new format. Note in the file's docstring so a future maintainer sees the version dependency.
- **Path D quality-mode tuning.** Pending Part 1's measurement-cleanup before any quality-mode escalation is justified.
