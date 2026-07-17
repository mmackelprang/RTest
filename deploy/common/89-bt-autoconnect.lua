-- Enable auto-connect for BT A2DP profiles.
-- Without this, WirePlumber may leave profile = 'off' when PipeWire 1.0.7
-- reports api.bluez5.connection = 'disconnected' (known quirk).
bluez_monitor.rules = bluez_monitor.rules or {}
table.insert(bluez_monitor.rules, 1, {
  matches = {
    {
      { "device.name", "matches", "bluez_card.*" },
    },
  },
  apply_properties = {
    ["bluez5.auto-connect"] = "[ a2dp_sink a2dp_source hfp_ag hsp_ag ]",
  },
})
