-- /etc/wireplumber/main.lua.d/41-disable-bt-input-restore-target.lua
--
-- Prevent WirePlumber's restore-stream script from restoring a saved routing
-- target (and saved volume) for BT A2DP source nodes. Radio Console is the
-- intended consumer of BT audio: it captures the bluez_input node directly via
-- a PipeWire native stream (`radio-bt-stream`) and routes through its own
-- mixer / outputs.
--
-- Without this rule, restore-stream remembers a per-stream target (keyed by
-- media.name e.g. "Pixel 10 Pro XL (codec aptX HD)") and on every subsequent
-- BT connect writes a `target.node` metadata entry that policy-node.lua then
-- uses to auto-link the BT source straight to the default sink (local
-- soundbar). The result is a parallel audio path that runs alongside the
-- designed `phone -> bluez_input -> Radio.API -> output(s)` flow, producing
-- dual playback + a comb-filter "underwater" artifact (the two paths have
-- different latencies). See
-- docs/research/2026-05-22-bt-dual-routing-investigation.md for the
-- diagnostic that motivated this rule.
--
-- The companion rule at bluetooth.lua.d/90-disable-bt-input-autolink.lua sets
-- `node.autoconnect = false` on the same nodes, which stops the policy
-- module's *default* auto-link behaviour but does not stop restore-stream
-- from writing `target.node` metadata. Both rules are needed.
--
-- Radio.API's capture is unaffected: it is a separate `pw_stream`
-- (`radio-bt-stream`) that links its own input ports to the bluez_input
-- output ports independently of restore-stream.
--
-- Scope: matches only `bluez_input.*` nodes. File-player, system sounds,
-- Chromium, etc. keep restore-stream functionality. HFP/voice profiles on
-- hci1 (RotaryPhone's adapter) are unaffected: they expose nodes named
-- `bluez_output.*` or HSP/HFP variants, not `bluez_input.*`.
--
-- WirePlumber version note: written for WP 0.4.x (Lua-based config). If the
-- runtime moves to WP 0.5.x (JSON/TOML config), this rule needs to be
-- re-authored with the equivalent `wireplumber.settings` shape under
-- `node.restore-target` / `node.restore-props`.

stream_defaults.rules = stream_defaults.rules or {}
table.insert(stream_defaults.rules, {
  matches = {
    {
      { "node.name", "matches", "bluez_input.*" },
    },
  },
  apply_properties = {
    -- Stop restore-stream from writing target.node metadata for this node
    -- on connect. Without target.node, policy-node.lua leaves the node
    -- unlinked (consistent with node.autoconnect = false).
    ["state.restore-target"] = false,
    -- Also stop restoring the saved channel volume. The saved value for
    -- this BT device may be a leftover from a manual once-routing decision
    -- (e.g. user once dragged the stream onto the soundbar with volume at
    -- 30%). If the rule above is ever bypassed, we do not want a stale
    -- volume to bleed into the real sink.
    ["state.restore-props"] = false,
  },
})
