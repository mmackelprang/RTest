# ENC-11 is blocked: the encoder firmware drops every host-to-device report

**Status:** ✅ `[FIXED AND FLASHED 2026-09-02 — RotaryUsb #11. Kept as the record of what it was.]`
**Was:** blocked `ENC-11`, and through O10 also `ENC-8`, `ENC-12` and `ENC-14`.

## Resolution

Fixed in [RotaryUsb #11](https://github.com/mmackelprang/RotaryUsb/pull/11) by normalising the two
delivery paths at the top of `tud_hid_set_report_cb`, flashed, and verified on the device:

```
write 03 04 00        (Read config)
-> report ids seen: {2: 1, 4: 51}
-> CONFIG REPORT RECEIVED 107 bytes
```

One Input Report `0x02` where previously there were none. Decoded through the `ENC-2` codec's
layout, which validates both sides at once:

```
version 1 | steps/detent 4
enc1..4:  min=0 max=100 step=1 wrap=0 reverse=0
          tiers = (150ms -> x5), (80ms -> x15), (40ms -> x50)
```

⚠ **That x50 is the factory default this project has been warning about, now measured rather than
inferred.** With `max=100` and `step=1`, tier 3 means two fast detents cross the entire range — "one
detent from silence to full" is real, and all four encoders are currently in that state. It is
exactly what `ENC-11` exists to overwrite.

A note on what the verification does and does not prove. Command `0x05` (Reset diagnostics) was also
attempted and came back **inconclusive** — the flash power-cycled the device, so the counters were
already zero and there was nothing to reset. That is not a gap: `0x04` and `0x05` are arms of the
same `report_id == 0x03` branch, so reaching the branch is what was in doubt and that is proven.

The original diagnosis follows, kept because the failure mode is worth recognising again: a device
that accepts every write and silently ignores it looks identical, from the host, to one that is
working.
**Affected firmware:** the **C++** build (`firmware-cpp/main_generic_hid.cpp`), which is what the
appliance runs. The CircuitPython build is very likely unaffected - see below.

## Symptom

Command `0x04` (Read config) produces no Input Report `0x02`. Investigating that showed the problem
is broader: **no host-to-device report reaches the firmware at all.**

Proven with a command whose effect is observable and harmless - `0x05`, Reset diagnostics:

```
before  edges: [256, 638, 200, 300]   detents: [24, 45, 22, 30]
  write 03 05 00   (3 bytes accepted)
after   edges: [256, 638, 200, 300]   detents: [24, 45, 22, 30]
COMMAND WAS IGNORED
```

Counters are monotonic and would have zeroed. They did not move.

## Root cause

The device declares **two** interrupt endpoints:

```
bEndpointAddress 0x01  EP 1 OUT   Interrupt, 64 bytes
bEndpointAddress 0x81  EP 1 IN    Interrupt, 64 bytes
```

Because an interrupt **OUT** endpoint exists, Linux `hidraw` `write()` delivers output reports over
that endpoint rather than as a `SET_REPORT` control transfer. TinyUSB handles the two paths
differently:

| Path | `tud_hid_set_report_cb` receives |
|---|---|
| `SET_REPORT` control | `report_id` = the real ID, buffer = payload |
| Interrupt OUT endpoint | **`report_id` = 0**, buffer = **report ID followed by payload** |

`main_generic_hid.cpp` dispatches solely on the parameter:

```c
if (report_id == 0x02 && bufsize >= FULL_CONFIG_SIZE) {        // :747
} else if (report_id == 0x03 && bufsize >= 2) {                 // :761
    uint8_t command = buffer[0];
}
```

With `report_id == 0`, both branches are skipped and the report is dropped silently. There is no
`report_id == 0` handling and no `buffer[0]` unwrapping anywhere in the file.

## Why this is bigger than "read config does not work"

**Config push (`0x02`) is dropped by the same code path.** `ENC-11`'s whole premise - push config,
then verify by read-back - cannot work in either direction. The punch list's warning that *"bad
config is silently rejected, so the host MUST read back and verify"* understates it: config is not
rejected, it is **never received**.

The device therefore runs whatever is in its flash regardless of what the host sends, which is the
exact condition `ENC-11` exists to eliminate - factory defaults put volume acceleration at x50, one
detent from silence to full.

## The fix, in the firmware repo

The canonical TinyUSB normalisation, at the top of `tud_hid_set_report_cb`:

```c
// Reports arriving on the interrupt OUT endpoint carry report_id == 0 and put the real
// report ID in the first byte. SET_REPORT control transfers populate report_id instead.
// Normalise both into the same shape before dispatching.
if (report_id == 0 && bufsize > 0) {
    report_id = buffer[0];
    buffer++;
    bufsize--;
}
```

The `bufsize` adjustment matters as much as the ID: the existing guards are `bufsize >=
FULL_CONFIG_SIZE` and `bufsize >= 2`, and an unadjusted length is one byte too large in both.

**The CircuitPython build should be checked but is likely fine** - it uses
`hid_device.get_last_received_report(2)` / `(3)`, so CircuitPython resolves the report ID at its own
layer rather than relying on a callback parameter.

A regression test belongs with the fix. `tests/test_descriptor_parity.py` already asserts the two
firmwares' descriptors are byte-identical, so that a host written against one works against the
other - which is exactly the guarantee this defect breaks in practice.

## What is NOT affected

The host side is correct. `RotaryEncoderConfigCodec` produces the right bytes at the right offsets
(13 tests, offsets pinned against INTEGRATION.md section 4), and `write()` reports success because
the kernel accepts and delivers the transfer.

Input reports are fine in both builds: report `0x01` (positions, buttons, movement) and `0x04`
(diagnostics) arrive normally, which is why `ENC-1` works today.

## Cross-repo note

The fix lives in `D:/prj/RotaryUsb`, a separate repository. Recorded here rather than committed
there, because that repo is outside this project's trust boundary and a firmware change wants its own
review and a flash.
