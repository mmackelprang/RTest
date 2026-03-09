-- Prevent WirePlumber from auto-linking bluez_input nodes to the default sink.
-- Radio Console captures BT audio via PipeWire native stream and routes it
-- through its own mixer. Auto-linking causes duplicate audio that bypasses
-- volume/mute controls.
bluez_monitor.rules = bluez_monitor.rules or {}
table.insert(bluez_monitor.rules, {
  matches = {
    {
      { "node.name", "matches", "bluez_input.*" },
    },
  },
  apply_properties = {
    ["node.autoconnect"] = false,
  },
})
