-- Disable HFP Hands-Free role so RotaryPhone hfp_monitor.py can register
-- the Profile1 agent and handle incoming call detection.
-- A2DP, HSP, and other profiles remain active.
bluez_monitor.properties["bluez5.roles"] = "[ a2dp_sink a2dp_source bap_sink bap_source hsp_hs hsp_ag hfp_ag ]"
