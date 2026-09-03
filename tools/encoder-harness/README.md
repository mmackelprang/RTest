# ENC-17 — virtual rotary-encoder injection harness

Drive the console's rotary encoders from a shell, without a hand on the physical knobs.

The harness creates a **real USB HID device** presenting the RotaryUsb identity and report
descriptor, and injects genuine Input Report `0x01` frames. The shipped
`HidRotaryEncoderService` opens it, decodes it and acts on it exactly as it does for the
physical device — same udev rule, same HidSharp enumeration, same decoder, same
`ENC-1` accumulator semantics. It is not a mock, and nothing in the console is modified,
reconfigured or restarted to accommodate it.

---

## Quick start

```bash
scp tools/encoder-harness/virtual_encoder.py mmack@radio:/tmp/
ssh mmack@radio
sudo python3 /tmp/virtual_encoder.py            # interactive; type commands, Ctrl-D to stop
```

Scripted, which is the usual form:

```bash
ssh mmack@radio "sudo python3 /tmp/virtual_encoder.py \
  -c 'turn 0 3' -c 'sleep 1000' -c 'hold 0 900'"
```

Anything the harness prints on stdout is its own transcript; `[gadget]` lines on stderr are
the device's lifecycle.

---

## Commands

| Command | What it does |
|---|---|
| `turn <enc> <detents> [mult]` | Turn an encoder. Negative detents reverse. `[mult]` stands in for a firmware acceleration tier. |
| `offline-turn <enc> <detents> [mult]` | Advance the accumulator **without** emitting — the knob turning while nothing is listening. |
| `press <enc>` / `release <enc>` | Button down / up, held until you say otherwise. |
| `tap <enc> [ms]` | Press, hold (default 80 ms), release. A short press. |
| `hold <enc> <ms>` | Press, hold, release. Use > 600 for a long press. |
| `idle <seconds> [hz]` | Silence for `<seconds>` (what the real device does at rest). With `[hz]`, re-emit the *unchanged* report at that rate to prove repeats produce no movement. |
| `detach` | Unplug — a real USB port detach. |
| `attach [settle]` | Plug back in, wait `[settle]`s (default 8) for the service to reconnect, then send the baseline. |
| `sleep <ms>` / `send` / `status` / `echo <text>` / `help` / `quit` | Utilities. |

Encoders are `0 = VOLUME`, `1 = SOURCE`, `2 = PRESETS`, `3 = TUNING`.

Flags worth knowing: `--selftest` (frame checks, no root, runs on any OS), `--check-descriptor`
(compare the embedded descriptor against the live device), `--cleanup` (tear down leftovers
after a hard kill), `--settle-seconds`, `--max-seconds`, `--no-unbind`.

---

## The re-baseline scenario, which is what this was built for

`ENC-1`'s rule, in Designer's words: *"Turn a knob ~50 detents while unplugged, then replug:
volume does not jump."* The device's movement accumulator is free-running, so a host that
differenced against its last remembered value would deliver the entire outage as one delta —
on the volume knob. Before this harness there was no way to test it without unplugging USB
by hand and turning a knob fifty times.

```bash
sudo python3 /tmp/virtual_encoder.py \
  -c 'turn 0 1' \
  -c 'detach' \
  -c 'offline-turn 0 50' \
  -c 'attach'
```

Read the volume from `curl -s http://localhost:5000/api/audio/volume` before the `detach`
and after the `attach`. It must be identical.

**Measured on the appliance 2026-09-03: 0.52 before, 0.52 after, zero jump.** Unclamped that
would have been +100 points — silence to full.

---

## Two design decisions, both load-bearing

### It takes the real device's identity, and unbinds the real device

The harness runs as `cafe:4005`, the same VID/PID as the physical encoder, and unbinds the
real device's USB interface for the duration (rebinding it on exit).

The alternative — a distinct VID/PID that the service is told about through
`RotaryEncoder:VendorId` / `:ProductId` — was rejected for two reasons. It would mean UAT no
longer exercises the shipped configuration, at exactly the layer under test; and its failure
mode is persistent and silent, because an override left behind points the console at a device
that does not exist and the real knobs stay dead across reboots with nothing on screen to say
why. Taking the real identity also means the shipped udev rule grants access with nothing to
remember.

Two encoders answering at once is its own confusing failure, so this does not rely on the
service preferring one: with the real interface unbound there is exactly one `cafe:4005`
device for `FindDevice`'s `FirstOrDefault()` to return.

### It cannot be left running

1. EOF on stdin exits, so an SSH session that ends stops it.
2. `--max-seconds` (default 300) is a hard watchdog.
3. `SIGINT` / `SIGTERM` / `SIGHUP` exit cleanly.
4. Teardown runs in a `finally:` on every exit this process can observe — detach, unbind the
   gadget, remove the configfs tree, rebind the real encoder.
5. `usbipd` runs as a child with `PR_SET_PDEATHSIG=SIGKILL`, so the kernel kills it even when
   the harness cannot clean up after itself.
6. `--cleanup` restores everything in one command, and starting the harness again does the same
   on its way past.

⚠ **`SIGKILL` leaks, and it is worth being exact about what.** Measured 2026-09-03: after
`kill -9`, usbipd was gone as designed, but the configfs gadget and the vhci attachment **both
survived** — a virtual `cafe:4005` device stayed enumerated and the real encoder stayed unbound.
An earlier draft of this document claimed losing usbipd detaches the device. It does not.

What survives is an **inert** device: every report originates from a command typed into the
process, so a harness nobody is driving sends nothing, and the `ENC-3` hazard of a synthetic
volume source is not reachable from a leak. The real cost is that the physical knobs stay dead
until someone recovers. All three recoveries were verified: `--cleanup`, starting the harness
again, and a reboot.

It is deliberately **not installable**: no unit file, no autostart, no daemon mode. A synthetic
volume source inside sealed furniture is the hazard `ENC-3`'s clamps exist for. Note also what
the harness is when nobody is driving it: a device that sends nothing. Every report originates
from a command typed into the process, so even a leaked one is inert.

**After a hard kill**, run `sudo python3 /tmp/virtual_encoder.py --cleanup`. A reboot or a
physical replug also restores the real encoder.

---

## Why not `/dev/uhid`, which the ENC-17 row specified

Because it does not work with this service, and the reason is worth recording so nobody spends
an afternoon rediscovering it.

`/dev/uhid` is present on the appliance and a uhid device does everything you would expect: it
creates a working `/dev/hidrawN`, carries the right modalias, and can be read with `cat`. But
it is parented to `/sys/devices/virtual/misc/uhid/...`, so it has **no USB ancestor** — and two
things downstream need one:

1. The shipped udev rule (`SUBSYSTEM=="hidraw", ATTRS{idVendor}=="cafe"`) resolves by walking up
   to a USB parent, so it never fires and the node stays `root:root 0600`. Workaroundable —
   `chmod` the node.
2. **HidSharp does not enumerate the device at all.** Measured 2026-09-03: with a uhid
   `cafe:4005` device present and readable by `mmack`, `DeviceList.Local.GetHidDevices()` did
   not list it, so `GetHidDevices(0xCAFE, 0x4005)` returned 0 — and even the `DevicePath`
   override cannot help, because you cannot filter a list that never contained the device.
   HidSharp's Linux backend resolves vendor and product from the USB parent's sysfs attributes.

That second one is not workaroundable from outside the service, so the transport is a real USB
gadget looped back through usbip. Everything it needs was already installed: `usbip-vudc`,
`vhci-hcd`, `libcomposite`, `usb_f_hid`, and the `usbip` / `usbipd` binaries from
`linux-tools`. **Nothing was installed on the appliance to make this work**, and `dummy_hcd`
— the more usual virtual UDC — is *not* available here (`linux-modules-extra` has no package
for this kernel).

The result is higher fidelity than uhid would have been: `lsusb` shows the device, the real
udev rule grants access unaided, and `detach` is an actual USB port detach rather than a
simulated one.

---

## How the pieces fit

```
virtual_encoder.py (root, foreground)
  ├─ unbinds the real encoder's USB interface        (rebound on exit)
  ├─ modprobe libcomposite / usbip-vudc / vhci-hcd   (already present on the box)
  ├─ configfs gadget: cafe:4005 + the real 125-byte report descriptor
  ├─ binds the gadget to usbip-vudc.0  →  /dev/hidg0
  ├─ usbipd --device usbip-vudc.0                    (child, PR_SET_PDEATHSIG)
  └─ usbip attach -r 127.0.0.1                       → a real USB device on vhci_hcd
                                                       → udev rule fires → plugdev 0660
                                                       → HidSharp enumerates it
                                                       → HidRotaryEncoderService opens it
  writes 37-byte report 0x01 to /dev/hidg0   → the service's read loop
  reads  output reports from /dev/hidg0      → answers config 0x02 / command 0x03
```

The harness answers the configuration handshake so the console reaches the **`Configured`**
tier. That matters for measurement, not tidiness: since `ENC-16`, `VolumeClampFor` runs the
normal 6-unit clamp for `Configured` and `Degraded` and the tightened 2-unit clamp for
`Transient`, `HardFault` and `Unknown`, so a harness that did not answer would silently change
what a clamp measurement means.

---

## The report descriptor and the golden vectors

`REPORT_DESCRIPTOR_HEX` is the live device's descriptor, captured verbatim
(`cat /sys/class/hidraw/hidraw3/device/report_descriptor | xxd -p`). Using the real bytes
rather than a hand-written equivalent is deliberate: the service derives its
movement-accumulator feature detection from the device's max input report length, which the
kernel computes from this descriptor. Run `--check-descriptor` on the box to confirm the
firmware has not changed it.

`report-vectors.json` holds golden frames read by **two** things:

- `virtual_encoder.py --selftest`, which builds them, and
- `tests/Radio.Infrastructure.Tests/Platform/Input/VirtualEncoderHarnessProtocolTests.cs`,
  which decodes them with the shipped `RotaryEncoderDecoder`.

One artifact, two readers: if the harness's byte layout drifts from the decoder's, one of them
fails. That matters here specifically — `design/INTEGRATIONS.md` documented a **wrong 8-byte**
encoder report format for months and nothing mechanical caught it.

---

## What this does and does not cover

**Covered, and measured on the appliance 2026-09-03:** the service connects to the virtual
device and reports `Encoder report length 107 bytes (movement accumulators: true)`; the
configuration push verifies `Configured` on attempt 1; turns move volume at 2% per unit; the
`ENC-3` per-event clamp holds at ±6 units (±12 points) against 20- and 50-detent single
events; the `ENC-4` HUD renders left-anchored at the index band; a > 600 ms hold synthesises a
long press with the progress ring while a 200 ms hold does not; and `ENC-1`'s re-baseline rule
holds across a real USB disconnect.

**Not covered.** The harness cannot tell you how a knob *feels* — detent weight, acceleration
ramp, whether a spin *sounds* right. `ENC-3`'s deferred volume ramp says so explicitly, and the
`ENC-17` row says it too: **this does not replace the owner's own hand on the panel.** It also
does not exercise the firmware: acceleration tiers, `step_size` and detent density are the
device's, and the harness simply asserts a movement value where the real device would compute
one. `[mult]` on `turn` approximates a tier; it does not reproduce one.
