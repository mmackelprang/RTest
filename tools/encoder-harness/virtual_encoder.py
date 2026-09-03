#!/usr/bin/env python3
"""
ENC-17 — virtual rotary-encoder injection harness.

Creates a virtual USB HID device that presents the RotaryUsb report descriptor and
injects real Input Report 0x01 frames, so encoder behaviour can be exercised
without a hand on the physical knobs.

This is NOT a mock. The device is a real USB device as far as the kernel, udev and
HidSharp are concerned: it enumerates on a USB bus, gets a real hidraw node, is
matched by the shipped udev rule, and is opened and read by the shipped
HidRotaryEncoderService exactly as the physical device is. A check driven by this
harness exercises the same code a knob drives.

--------------------------------------------------------------------------------
WHY NOT /dev/uhid, WHICH IS WHAT THE ENC-17 ROW SPECIFIED
--------------------------------------------------------------------------------
Because it does not work with this service, and the reason is worth recording so
nobody spends the afternoon rediscovering it.

/dev/uhid is present on the appliance and a uhid device does everything you would
expect: it creates a working /dev/hidrawN, carries the right modalias, and can be
read with cat. But it is parented to /sys/devices/virtual/misc/uhid/..., so it has
no USB ancestor -- and two things downstream need one:

  1. The shipped udev rule (deploy/common/99-rotaryusb-encoder.rules) matches
     ATTRS{idVendor}=="cafe", which resolves by walking up to a USB parent. It
     never fires, so the node stays root:root 0600 and radio-api cannot open it.
     That one is workaroundable -- chmod the node.
  2. HidSharp does not enumerate the device AT ALL. Measured on the appliance
     2026-09-03: with a uhid cafe:4005 device present and readable,
     DeviceList.Local.GetHidDevices() does not list it, so
     GetHidDevices(0xCAFE, 0x4005) returns 0 and even the DevicePath override in
     RotaryEncoderOptions cannot select it -- you cannot filter a list that never
     contained the device. HidSharp's Linux backend resolves vendor and product
     from the USB parent's sysfs attributes, and a uhid device has none.

That is not workaroundable from outside the service, so the transport moved to a
real USB gadget looped back through usbip. Everything needed is already installed
on the appliance: usbip-vudc (a virtual USB device controller), vhci-hcd (a
virtual USB host controller), libcomposite and usb_f_hid, plus the usbip and
usbipd binaries from linux-tools. Nothing was installed to make this work.

The result is higher fidelity than uhid would have been, not lower: `lsusb` shows
the device, the real udev rule grants access without help, and the disconnect that
`detach` performs is an actual USB port detach rather than a simulated one.

--------------------------------------------------------------------------------
IDENTITY AND COEXISTENCE
--------------------------------------------------------------------------------
The harness takes the REAL device's identity (VID 0xCAFE / PID 0x4005) and
requires the real device to be absent for the duration. It unbinds the real
device's USB interface on start and rebinds it on exit.

The rejected alternative was a distinct VID/PID that the service is told about
through RotaryEncoder:VendorId / :ProductId. Rejected for two reasons:

  1. It would mean UAT no longer exercises the shipped configuration. The point of
     this harness is that the bytes reach the service the way they reach it in
     production; changing the identity the service matches on moves the test off
     the production path at exactly the layer under test. Taking the real identity
     also means the real udev rule grants access, with nothing to remember.
  2. Its failure mode is persistent and silent. A config override left behind
     points the console at a device that does not exist, and the real knobs stay
     dead across reboots with nothing on screen to say why.

Two encoders answering at once is its own confusing failure, so this does not rely
on the service preferring one: with the real interface unbound there is exactly
one cafe:4005 device for FindDevice's FirstOrDefault() to return.

--------------------------------------------------------------------------------
IT CANNOT BE LEFT RUNNING
--------------------------------------------------------------------------------
  1. EOF on stdin exits, so an SSH session that ends closes the pipe and stops it.
  2. --max-seconds (default 300) is a hard watchdog.
  3. SIGINT / SIGTERM / SIGHUP exit cleanly.
  4. Teardown runs in a finally: block on every exit this process can observe --
     detach, unbind the gadget, remove the configfs tree, and rebind the real
     encoder.
  5. usbipd runs as a child with PR_SET_PDEATHSIG=SIGKILL, so the kernel kills it
     even when this process cannot clean up after itself.
  6. `--cleanup` tears down leftovers and restores the real encoder in one command.
     Starting the harness again does the same thing on its way past.

⚠ SIGKILL is the one case that leaks, and it is stated rather than glossed.
Measured on the appliance 2026-09-03: after `kill -9`, usbipd was gone as designed,
but the configfs gadget and the vhci attachment BOTH survived, so a virtual
cafe:4005 device stayed enumerated and the real encoder stayed unbound. An earlier
draft of this comment claimed losing usbipd detaches the device; it does not.

What that leak is, precisely: an **inert** device. Every report originates from a
command typed into this process, so a harness nobody is driving sends nothing --
the ENC-3 hazard of a synthetic volume source is not reachable from a leak. The
real cost is that the physical knobs stay dead until somebody runs `--cleanup`,
starts the harness again, or reboots. All three were verified to restore it.

It is deliberately not installable. There is no unit file, no autostart entry and
no daemon mode, because a synthetic volume source inside sealed furniture is
exactly the hazard ENC-3's clamps exist for. Note what the harness is when it is
not being driven: a device that sends nothing. Even a leaked one is inert, because
every report originates from a command typed into this process.

--------------------------------------------------------------------------------
WIRE PROTOCOL
--------------------------------------------------------------------------------
Read from the shipped implementation, not from a document -- design/INTEGRATIONS.md
carried a wrong 8-byte format for months. Sources of truth:

  src/Radio.Infrastructure/Platform/Input/RotaryEncoderDecoder.cs
  src/Radio.Infrastructure/Platform/Input/RotaryEncoderConfigCodec.cs

Input Report 0x01, 37 bytes on the wire (1 report ID + 36 payload). Offsets below
are BUFFER offsets, i.e. payload offset + 1:

    [0]       report ID 0x01
    [1..16]   int32 x 4, little-endian -- clamped positions
    [17]      uint8 -- button bitmask, bit n = encoder n
    [18]      uint8 -- active acceleration tiers, 2 bits per encoder
    [19..20]  reserved
    [21..36]  int32 x 4, little-endian -- free-running movement accumulators

The movement accumulator is an odometer, not a delta: a running total since
power-on that keeps accruing while nothing is listening, and wraps at 32 bits
rather than saturating. The host is responsible for treating the first report of
every connection as a baseline. `detach` / `offline-turn` / `attach` exist to put
that rule under test -- see ENC-1 and the re-baseline scenario in README.md.

Configuration reports 0x02 (106-byte payload) and command reports 0x03 (2-byte
payload) are answered so the console reaches the `Configured` tier. That matters
for measurement, not just tidiness. Since ENC-16, RotaryEncoderConfigVerifier
.VolumeClampFor runs the normal 6-unit clamp for `Configured` and `Degraded` and
the tightened 2-unit clamp for `Transient`, `HardFault` and `Unknown` -- so a
harness that did not answer the read-back would leave the console on the tight
clamp and silently change what a clamp measurement means.
"""

from __future__ import annotations

import argparse
import ctypes
import errno
import json
import os
import re
import select
import shutil
import signal
import struct
import subprocess
import sys
import threading
import time

# ---------------------------------------------------------------------------
# The RotaryUsb report descriptor
# ---------------------------------------------------------------------------
#
# Captured verbatim from the live device on 2026-09-03:
#   cat /sys/class/hidraw/hidraw3/device/report_descriptor | xxd -p
#
# Using the real bytes rather than a hand-written equivalent is deliberate. The
# service derives its movement-accumulator feature detection from the device's max
# input report length (RotaryEncoderDecoder.BeginConnection), which the kernel
# computes from this descriptor. A descriptor that merely "looks right" could
# report a different maximum and silently put the service on the legacy parse.
# `--check-descriptor` compares this against the live device.
REPORT_DESCRIPTOR_HEX = (
    "0600ff0901a10185010902150026ff0075089510810209031500250175019504810275019504"
    "81030904150026ff007508950381020909150026ff0075089510810285020905150026ff0075"
    "08956a81020906150026ff007508956a910285030907150026ff00750895029102850409"
    "08150026ff00750895388102c0"
)

REPORT_ID_POSITIONS = 0x01
REPORT_ID_CONFIG = 0x02
REPORT_ID_COMMAND = 0x03
REPORT_ID_DIAGNOSTICS = 0x04

POSITIONS_REPORT_SIZE = 37       # 1 + 36
CONFIG_REPORT_SIZE = 107         # 1 + 106
COMMAND_REPORT_SIZE = 3          # 1 + 2
CONFIG_PAYLOAD_SIZE = 106

# The largest report in either direction, and therefore the gadget's report_length.
# This is also what the service logs as "Encoder report length 107 bytes": HidSharp's
# GetMaxInputReportLength() is the maximum across all input report IDs, and the
# config read-back is bigger than the positions report.
MAX_REPORT_SIZE = 107

ENCODER_COUNT = 4

# RotaryEncoderCommand (src/Radio.Core/Configuration/RotaryEncoderDeviceConfig.cs)
CMD_SAVE_CONFIG = 0x01
CMD_RESET_DEFAULTS = 0x02
CMD_RESET_POSITIONS = 0x03
CMD_READ_CONFIG = 0x04
CMD_RESET_DIAGNOSTICS = 0x05

CMD_NAMES = {
    CMD_SAVE_CONFIG: "SaveConfig",
    CMD_RESET_DEFAULTS: "ResetDefaults",
    CMD_RESET_POSITIONS: "ResetPositions",
    CMD_READ_CONFIG: "ReadConfig",
    CMD_RESET_DIAGNOSTICS: "ResetDiagnostics",
}

ENCODER_NAMES = ("VOLUME", "SOURCE", "PRESETS", "TUNING")

GADGET_ROOT = "/sys/kernel/config/usb_gadget"
GADGET_NAME = "enc17"
UDC_NAME = "usbip-vudc.0"

INT32_MIN = -(2 ** 31)
INT32_MAX = 2 ** 31 - 1


def _wrap_int32(value: int) -> int:
    """Wrap to a signed 32-bit value the way the device's accumulator does.

    The accumulator wraps rather than saturating, and the host's decoder relies on
    that: it subtracts unchecked so two's-complement gives the correct signed delta
    straight across the boundary. A harness that saturated here would make the wrap
    case untestable.
    """
    return ((value - INT32_MIN) % (2 ** 32)) + INT32_MIN


# ---------------------------------------------------------------------------
# Frame construction -- pure, no I/O, exercised by `selftest`
# ---------------------------------------------------------------------------


def build_positions_report(positions, buttons_mask, accumulators, tiers_byte=0):
    """Build a 37-byte Input Report 0x01.

    `positions` and `accumulators` are 4-element sequences of signed 32-bit ints;
    `buttons_mask` is a bitmask where bit n is encoder n.
    """
    if len(positions) != ENCODER_COUNT or len(accumulators) != ENCODER_COUNT:
        raise ValueError("positions and accumulators must both have 4 entries")
    if not 0 <= buttons_mask <= 0xFF:
        raise ValueError("buttons_mask must fit in a byte")

    buf = bytearray(POSITIONS_REPORT_SIZE)
    buf[0] = REPORT_ID_POSITIONS
    for i in range(ENCODER_COUNT):
        struct.pack_into("<i", buf, 1 + (i * 4), _wrap_int32(positions[i]))
    buf[17] = buttons_mask
    buf[18] = tiers_byte & 0xFF
    # buf[19..20] reserved, left zero.
    for i in range(ENCODER_COUNT):
        struct.pack_into("<i", buf, 21 + (i * 4), _wrap_int32(accumulators[i]))
    return bytes(buf)


def build_config_report(payload: bytes) -> bytes:
    """Build a 107-byte Input Report 0x02 from a 106-byte config payload."""
    if len(payload) != CONFIG_PAYLOAD_SIZE:
        raise ValueError(f"config payload must be {CONFIG_PAYLOAD_SIZE} bytes, got {len(payload)}")
    return bytes([REPORT_ID_CONFIG]) + payload


def default_config_payload() -> bytes:
    """A plausible factory-defaults config payload.

    Only ever sent if the host asks for a read-back before it has pushed anything,
    which the shipped boot sequence does not do. It exists so the harness answers
    rather than hangs, and it deliberately does NOT match the host's designed
    config -- a harness that echoed the desired config it was never given would
    manufacture a passing verification.

    Layout (RotaryEncoderConfigCodec): version, global flags, then 4 x 26-byte
    encoder blocks of min/max/step (int32 x3), wrap, reverse, then 3 tiers of
    (uint16 threshold_ms, uint16 multiplier).
    """
    buf = bytearray(CONFIG_PAYLOAD_SIZE)
    buf[0] = 1          # version
    buf[1] = 0          # global flags: 4 steps/detent
    for i in range(ENCODER_COUNT):
        off = 2 + (i * 26)
        struct.pack_into("<iii", buf, off, 0, 100, 1)   # min, max, step_size
        buf[off + 12] = 0                                # wrap
        buf[off + 13] = 0                                # reverse
        # The factory tiers measured on this hardware on 2026-09-02.
        for t, (threshold, mult) in enumerate(((150, 5), (80, 15), (40, 50))):
            struct.pack_into("<HH", buf, off + 14 + (t * 4), threshold, mult)
    return bytes(buf)


# ---------------------------------------------------------------------------
# Real-device bind / unbind
# ---------------------------------------------------------------------------


def find_real_encoder_interface(vid=0xCAFE, pid=0x4005):
    """Return the USB interface name of the real encoder, e.g. '3-2.3:1.0', or None.

    Walks /sys/bus/hid/devices looking for a HID device whose id matches, then finds
    the USB interface directory in its sysfs path. Discovered rather than hardcoded
    so this keeps working if the encoder is moved to another port.

    Deliberately skips anything under the virtual USB host controller, so the
    harness's own gadget is never mistaken for the real device during teardown.
    """
    hid_root = "/sys/bus/hid/devices"
    if not os.path.isdir(hid_root):
        return None
    want = f"{vid:04X}:{pid:04X}"
    for name in sorted(os.listdir(hid_root)):
        if want not in name.upper():
            continue
        real = os.path.realpath(os.path.join(hid_root, name))
        if "vhci_hcd" in real:
            continue
        # .../usb3/3-2/3-2.3/3-2.3:1.0/0003:CAFE:4005.0016 -> want the ':1.0' part
        for part in reversed(real.split(os.sep)):
            if re.fullmatch(r"\d+-[\d.]+:\d+\.\d+", part):
                return part
    return None


def find_encoder_interface_in_usb_tree(vid=0xCAFE, pid=0x4005):
    """Find the encoder's USB interface by walking the USB tree rather than the HID tree.

    Needed by --cleanup specifically: after a hard kill the real device is still
    unbound, so it has no /sys/bus/hid entry for find_real_encoder_interface to find,
    but the USB device itself is still enumerated and still carries idVendor/idProduct.

    Returns something like '3-2.3:1.0', or None.
    """
    root = "/sys/bus/usb/devices"
    if not os.path.isdir(root):
        return None
    entries = sorted(os.listdir(root))
    for name in entries:
        if ":" in name:
            continue                       # that is an interface, we want the device
        if "vhci_hcd" in os.path.realpath(os.path.join(root, name)):
            continue                       # the harness's own gadget, not the real one
        try:
            with open(os.path.join(root, name, "idVendor")) as fh:
                if int(fh.read().strip(), 16) != vid:
                    continue
            with open(os.path.join(root, name, "idProduct")) as fh:
                if int(fh.read().strip(), 16) != pid:
                    continue
        except (OSError, ValueError):
            continue
        for entry in entries:
            if entry.startswith(name + ":"):
                return entry
    return None


def _usbhid_write(action: str, interface: str) -> bool:
    try:
        with open(f"/sys/bus/usb/drivers/usbhid/{action}", "w") as fh:
            fh.write(interface)
        return True
    except OSError as exc:
        # Rebinding something already bound, or unbinding something already gone, is
        # not a failure worth aborting for -- report it and carry on.
        print(f"  [{action}] {interface}: {exc}", file=sys.stderr)
        return False


def unbind_real_device(interface: str) -> bool:
    print(f"Unbinding the real encoder USB interface {interface} ...", file=sys.stderr)
    ok = _usbhid_write("unbind", interface)
    if ok:
        print("  unbound.", file=sys.stderr)
    return ok


def rebind_real_device(interface: str) -> bool:
    print(f"Rebinding the real encoder USB interface {interface} ...", file=sys.stderr)
    ok = _usbhid_write("bind", interface)
    print("  rebound." if ok else "  REBIND FAILED -- see the recovery line above.",
          file=sys.stderr)
    return ok


# ---------------------------------------------------------------------------
# The virtual device: a USB HID gadget looped back through usbip
# ---------------------------------------------------------------------------


def _run(args, check=False, quiet=True):
    result = subprocess.run(args, capture_output=True, text=True)
    if not quiet and result.stdout.strip():
        print(f"  {result.stdout.strip()}", file=sys.stderr)
    if check and result.returncode != 0:
        raise RuntimeError(f"{' '.join(args)} failed: {result.stderr.strip()}")
    return result


def _write_sysfs(path, value):
    with open(path, "w") as fh:
        fh.write(value)


def _set_pdeathsig():
    """Ask the kernel to SIGKILL this child when its parent dies.

    usbipd is a separate process, so a finally: block cannot reach it if the harness
    is SIGKILLed; PR_SET_PDEATHSIG can, because the kernel delivers it. That stops a
    stray daemon outliving the run and holding port 3240 against the next one.

    ⚠ It does NOT tear the device down. Measured 2026-09-03: after `kill -9` on the
    harness, usbipd was gone and the vhci attachment and configfs gadget both
    remained. Use `--cleanup` for that.
    """
    PR_SET_PDEATHSIG = 1
    libc = ctypes.CDLL("libc.so.6", use_errno=True)
    libc.prctl(PR_SET_PDEATHSIG, signal.SIGKILL)


def our_vhci_ports():
    """Return the vhci port numbers carrying THIS harness's gadget.

    `usbip port` prints a numbered header per attached device followed by an
    indented detail line naming the remote, e.g.

        Port 00: <Port in Use> at High Speed(480Mbps)
               unknown vendor : unknown product (cafe:4005)
               3-1 -> usbip://127.0.0.1:3240/usbip-vudc.0

    so a port is ours when its block mentions our UDC. Matching rather than
    detaching everything matters because `usbip detach` is not scoped: an earlier
    revision tore down every attached port on the box while its own comment claimed
    it only took our own, which would silently rip out a concurrent operator's
    device. Nothing else on this appliance is known to use usbip today; that is a
    fact about the box, not a property of the code, so the code is scoped.
    """
    result = _run(["usbip", "port"])
    ports, current = [], None
    for line in result.stdout.splitlines():
        header = re.match(r"^Port (\d+):", line.strip())
        if header:
            current = header.group(1)
            continue
        if current is not None and UDC_NAME in line:
            ports.append(current)
            current = None
    return ports


def teardown_gadget(verbose=True):
    """Remove every piece of harness state, in dependency order.

    Safe to run when nothing is set up -- each step tolerates its target being
    absent. This is both the normal teardown path and what `--cleanup` calls after
    a hard kill.
    """
    def log(message):
        if verbose:
            print(f"[gadget] {message}", file=sys.stderr, flush=True)

    # 1. Detach the vhci ports carrying our gadget, and only those.
    for port in our_vhci_ports():
        _run(["usbip", "detach", "-p", port])
        log(f"detached vhci port {port}")

    # 2. Stop any usbipd we started. Matched on the device-mode argument so a
    #    usbipd somebody else is running for another purpose is left alone.
    _run(["pkill", "-f", f"usbipd --device {UDC_NAME}"])

    # 3. Unbind and dismantle the configfs gadget, deepest first.
    gadget = os.path.join(GADGET_ROOT, GADGET_NAME)
    if os.path.isdir(gadget):
        try:
            _write_sysfs(os.path.join(gadget, "UDC"), "\n")
        except OSError:
            pass
        for path in (
            os.path.join(gadget, "configs/c.1/hid.usb0"),
        ):
            try:
                os.unlink(path)
            except OSError:
                pass
        for path in (
            os.path.join(gadget, "configs/c.1/strings/0x409"),
            os.path.join(gadget, "configs/c.1"),
            os.path.join(gadget, "functions/hid.usb0"),
            os.path.join(gadget, "strings/0x409"),
            gadget,
        ):
            try:
                os.rmdir(path)
            except OSError:
                pass
        log("configfs gadget removed" if not os.path.isdir(gadget)
            else f"WARNING: {gadget} could not be fully removed")


class VirtualEncoder:
    """A USB HID gadget presenting the RotaryUsb identity and report descriptor.

    Owns the configfs gadget, the usbipd child, the vhci attachment, the /dev/hidgN
    endpoint, and the per-encoder position/accumulator state that survives a
    `detach` so `offline-turn` can advance it while the host is not listening.
    """

    def __init__(self, name, vid, pid, uniq, descriptor, verbose=True):
        self.name = name
        self.vid = vid
        self.pid = pid
        self.uniq = uniq
        self.descriptor = descriptor
        self.verbose = verbose

        self.fd = None
        self._usbipd = None
        self._write_lock = threading.Lock()
        self._reader = None
        self._stop = threading.Event()
        self._attached = False
        self._gadget_built = False

        # Device state. Deliberately survives detach/attach: that is the whole point
        # of the re-baseline scenario -- the accumulator keeps accruing while nothing
        # is listening, exactly as the real device's does.
        self.positions = [0] * ENCODER_COUNT
        self.accumulators = [0] * ENCODER_COUNT
        self.buttons = 0
        self.pushed_config = None

        self.hidraw_node = None
        self.stats = {"reports_sent": 0, "outputs_received": 0, "config_readbacks": 0}

    # -- setup -------------------------------------------------------------

    def open(self):
        """Load modules, build the gadget, bind it, and export it over usbip."""
        for module in ("libcomposite", "usbip-vudc", "vhci-hcd"):
            result = _run(["modprobe", module])
            if result.returncode != 0:
                raise SystemExit(f"modprobe {module} failed: {result.stderr.strip()}")
        self._log("modules loaded (libcomposite, usbip-vudc, vhci-hcd)")

        if not os.path.isdir("/sys/class/udc") or UDC_NAME not in os.listdir("/sys/class/udc"):
            raise SystemExit(
                f"{UDC_NAME} did not appear under /sys/class/udc. "
                "usbip-vudc loaded but did not create a controller.")

        self._build_gadget()

        self._usbipd = subprocess.Popen(
            ["usbipd", "--device", UDC_NAME],
            stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
            preexec_fn=_set_pdeathsig)
        time.sleep(1.0)
        if self._usbipd.poll() is not None:
            raise SystemExit("usbipd exited immediately; is another usbipd already on port 3240?")
        self._log(f"usbipd running (pid {self._usbipd.pid}, dies with this process)")

        self.fd = os.open("/dev/hidg0", os.O_RDWR)
        self._reader = threading.Thread(target=self._read_loop, name="hidg-reader", daemon=True)
        self._reader.start()

    def _build_gadget(self):
        gadget = os.path.join(GADGET_ROOT, GADGET_NAME)
        if os.path.isdir(gadget):
            self._log("a previous gadget was still present; removing it first")
            teardown_gadget(verbose=self.verbose)

        os.makedirs(gadget, exist_ok=True)
        _write_sysfs(os.path.join(gadget, "idVendor"), f"0x{self.vid:04x}")
        _write_sysfs(os.path.join(gadget, "idProduct"), f"0x{self.pid:04x}")
        _write_sysfs(os.path.join(gadget, "bcdUSB"), "0x0200")
        _write_sysfs(os.path.join(gadget, "bcdDevice"), "0x0100")

        os.makedirs(os.path.join(gadget, "strings/0x409"), exist_ok=True)
        _write_sysfs(os.path.join(gadget, "strings/0x409/serialnumber"), self.uniq)
        _write_sysfs(os.path.join(gadget, "strings/0x409/manufacturer"), "RotaryUsb")
        _write_sysfs(os.path.join(gadget, "strings/0x409/product"), self.name)

        function = os.path.join(gadget, "functions/hid.usb0")
        os.makedirs(function, exist_ok=True)
        _write_sysfs(os.path.join(function, "protocol"), "0")
        _write_sysfs(os.path.join(function, "subclass"), "0")
        _write_sysfs(os.path.join(function, "report_length"), str(MAX_REPORT_SIZE))
        with open(os.path.join(function, "report_desc"), "wb") as fh:
            fh.write(self.descriptor)

        config = os.path.join(gadget, "configs/c.1")
        os.makedirs(os.path.join(config, "strings/0x409"), exist_ok=True)
        _write_sysfs(os.path.join(config, "strings/0x409/configuration"), "RotaryUsb")
        link = os.path.join(config, "hid.usb0")
        if not os.path.islink(link):
            os.symlink(function, link)

        _write_sysfs(os.path.join(gadget, "UDC"), UDC_NAME)
        self._gadget_built = True
        time.sleep(0.5)
        self._log(f"gadget bound to {UDC_NAME} as {self.vid:04x}:{self.pid:04x}")

    # -- lifecycle ---------------------------------------------------------

    def attach(self):
        """Plug the virtual device in: attach the usbip export to the local vhci bus."""
        if self._attached:
            return
        result = _run(["usbip", "attach", "-r", "127.0.0.1", "-d", UDC_NAME])
        if result.returncode != 0:
            raise RuntimeError(f"usbip attach failed: {result.stderr.strip()}")
        self._attached = True
        # Enumeration, driver bind, hidraw creation and the udev rule all have to
        # happen before the service can see it.
        self._settle_node()
        self._log("attached (the host now sees a USB device)")

    def detach(self):
        """Unplug the virtual device but keep the accumulator state.

        This is a real USB port detach, not a simulated one: the host's read loop
        sees the stream break and must re-baseline on the next connect, while the
        accumulators here keep their values so `offline-turn` can advance them in
        between.
        """
        if not self._attached:
            return
        for port in our_vhci_ports():
            _run(["usbip", "detach", "-p", port])
        self._attached = False
        self.hidraw_node = None
        self._log("detached (accumulators retained)")

    def close(self):
        """Tear everything down, running every step even if an earlier one fails.

        Each step is guarded individually and on purpose. This is the only thing that
        removes the gadget, and main()'s finally: block calls it immediately before
        rebinding the real encoder -- so an exception escaping here would skip the
        remaining teardown AND the rebind, leaving the box worse off than a step that
        simply failed. A teardown that gives up halfway is the failure mode this
        method exists to prevent, so it reports problems rather than propagating them.
        """
        def step(what, fn):
            try:
                fn()
            except Exception as exc:
                self._log(f"teardown step '{what}' failed: {exc}")

        step("detach", lambda: self.detach() if self._attached else None)

        # Stop and join the reader before closing the fd, so it is not parked in
        # select() on a descriptor about to be closed and reused. The join has a
        # timeout, so this narrows the window rather than closing it: a reader inside
        # _on_command's write when teardown starts can still outlive the join. That is
        # survivable -- _read_loop catches its own exceptions and would log rather than
        # crash -- and it is why the fd close below is guarded too.
        self._stop.set()
        if self._reader is not None and self._reader.is_alive():
            step("join reader", lambda: self._reader.join(timeout=1.0))

        def close_fd():
            if self.fd is not None:
                try:
                    os.close(self.fd)
                finally:
                    self.fd = None
        step("close /dev/hidg0", close_fd)

        def stop_usbipd():
            if self._usbipd is not None and self._usbipd.poll() is None:
                self._usbipd.terminate()
                try:
                    self._usbipd.wait(timeout=3)
                except subprocess.TimeoutExpired:
                    self._usbipd.kill()
        step("stop usbipd", stop_usbipd)

        step("remove gadget", lambda: teardown_gadget(verbose=self.verbose))

    # -- the hidraw node ---------------------------------------------------

    def _settle_node(self, timeout=6.0):
        """Wait for the kernel to create our hidraw node and udev to grant access.

        Unlike the uhid attempt this needs no permission fix-up: the gadget has a
        real USB parent carrying idVendor/idProduct, so the shipped udev rule
        (SUBSYSTEM=="hidraw", ATTRS{idVendor}=="cafe", ATTRS{idProduct}=="4005")
        matches it and grants plugdev 0660 by itself. Waiting for that group to
        appear is therefore also a check that the rule fired.
        """
        deadline = time.time() + timeout
        # uevent carries HID_ID=<bus>:<8-hex vendor>:<8-hex product>, e.g.
        # "0003:0000CAFE:00004005". Matching the 4-hex form finds nothing.
        want = f"{self.vid:08X}:{self.pid:08X}"
        while time.time() < deadline:
            for entry in sorted(os.listdir("/sys/class/hidraw")):
                try:
                    with open(f"/sys/class/hidraw/{entry}/device/uevent") as fh:
                        text = fh.read()
                except OSError:
                    continue
                if want not in text.upper():
                    continue
                if "vhci_hcd" not in os.path.realpath(f"/sys/class/hidraw/{entry}/device"):
                    continue          # that is the real device, not ours
                node = f"/dev/{entry}"
                try:
                    mode = os.stat(node).st_mode & 0o777
                except OSError:
                    continue
                if mode & 0o060:
                    self.hidraw_node = node
                    self._log(f"hidraw node: {node} (mode {mode:o}, udev rule fired)")
                    return node
            time.sleep(0.1)
        self._log("WARNING: our hidraw node did not appear group-readable within "
                  f"{timeout}s; the service may not be able to open it")
        return None

    # -- the reader thread -------------------------------------------------

    def _read_loop(self):
        """Answer the host's configuration traffic.

        Runs on its own thread because the host's boot push writes three reports and
        then blocks waiting for a 0x02 read-back with a 2 s timeout, while the main
        thread is parked on stdin. Missing that deadline is not cosmetic. An
        unanswered read-back reaches Classify as a null mismatch list, which is
        Transient for the first two attempts and HardFault once the three-attempt
        budget is spent -- and both of those run the tightened 2-unit volume clamp
        rather than the normal 6, which would quietly change what a clamp
        measurement means.
        """
        while not self._stop.is_set():
            # Snapshot the descriptor: close() clears self.fd from another thread,
            # and re-reading the field between the select and the read is how this
            # loop ended up passing None to os.read on shutdown.
            fd = self.fd
            if fd is None:
                return
            try:
                ready, _, _ = select.select([fd], [], [], 0.2)
            except (OSError, ValueError):
                return
            if not ready:
                continue
            try:
                data = os.read(fd, MAX_REPORT_SIZE * 2)
            except (OSError, TypeError, ValueError) as exc:
                if isinstance(exc, OSError) and exc.errno in (errno.EINTR, errno.EAGAIN):
                    continue
                return
            try:
                self._on_host_report(data)
            except Exception as exc:                      # never kill the reader
                self._log(f"reader error: {exc}")

    def _on_host_report(self, report: bytes):
        self.stats["outputs_received"] += 1
        if not report:
            return
        report_id = report[0]

        if report_id == REPORT_ID_CONFIG:
            if len(report) >= CONFIG_REPORT_SIZE:
                self.pushed_config = bytes(report[1:1 + CONFIG_PAYLOAD_SIZE])
                self._log("host pushed a configuration (106 bytes)")
            else:
                self._log(f"host pushed a short configuration ({len(report)} bytes) -- ignored")
            return

        if report_id == REPORT_ID_COMMAND:
            self._on_command(report[1] if len(report) > 1 else 0)
            return

        self._log(f"host sent an unrecognised report 0x{report_id:02x} ({len(report)} bytes)")

    def _on_command(self, command):
        self._log(f"host command: {CMD_NAMES.get(command, f'0x{command:02x}')}")

        if command == CMD_RESET_POSITIONS:
            # Firmware semantics: positions reset, accumulators untouched. Getting
            # this wrong in the harness would hide the very bug ENC-1 guards.
            self.positions = [0] * ENCODER_COUNT
        elif command == CMD_RESET_DEFAULTS:
            self.positions = [0] * ENCODER_COUNT
            self.pushed_config = default_config_payload()
        elif command == CMD_READ_CONFIG:
            payload = self.pushed_config
            if payload is None:
                self._log("  (no config pushed yet -- answering with factory defaults)")
                payload = default_config_payload()
            self.send_raw(build_config_report(payload))
            self.stats["config_readbacks"] += 1
        elif command in (CMD_SAVE_CONFIG, CMD_RESET_DIAGNOSTICS):
            pass          # accepted; the harness has no flash to persist to

    # -- emitting ----------------------------------------------------------

    def build_report(self) -> bytes:
        return build_positions_report(self.positions, self.buttons, self.accumulators)

    def send_raw(self, report: bytes):
        if not self._attached:
            raise RuntimeError("device is detached; `attach` first")
        with self._write_lock:
            if self.fd is None:
                raise RuntimeError("the gadget endpoint is closed")
            os.write(self.fd, report)
        self.stats["reports_sent"] += 1

    def send_state(self):
        self.send_raw(self.build_report())

    # -- gestures ----------------------------------------------------------

    def turn(self, index, detents, multiplier=1, emit=True):
        """Advance one encoder's accumulator and (optionally) report it.

        `multiplier` stands in for the firmware's acceleration tier: real movement is
        detents x step_size x tier_multiplier, and the designed config uses
        step_size 1, so a slow turn is one movement unit per detent.

        `emit=False` is the `offline-turn` case -- the knob moved while nothing was
        listening. That is not a hypothetical: the accumulator is free-running, so it
        is what actually happens across a disconnect.
        """
        movement = detents * multiplier
        self.accumulators[index] = _wrap_int32(self.accumulators[index] + movement)
        self.positions[index] = _wrap_int32(self.positions[index] + movement)
        if emit:
            self.send_state()

    def set_button(self, index, pressed):
        mask = 1 << index
        self.buttons = (self.buttons | mask) if pressed else (self.buttons & ~mask)
        self.send_state()

    # -- logging -----------------------------------------------------------

    def _log(self, message):
        if self.verbose:
            print(f"[gadget] {message}", file=sys.stderr, flush=True)


# ---------------------------------------------------------------------------
# Command interpreter
# ---------------------------------------------------------------------------

HELP = """\
Commands (one per line on stdin; '#' starts a comment):

  turn <enc> <detents> [mult]   Turn an encoder. Negative detents turn the other way.
                                [mult] stands in for a firmware acceleration tier.
  offline-turn <enc> <detents> [mult]
                                Advance the accumulator WITHOUT emitting a report --
                                the knob turning while nothing is listening.
  press <enc>                   Button down.
  release <enc>                 Button up.
  tap <enc> [ms]                Press, hold [ms] (default 80), release. A short press.
  hold <enc> <ms>               Press, hold <ms>, release. Use >600 for a long press.
  idle <seconds> [hz]           Stay silent for <seconds> (what the real device does
                                when nothing moves). With [hz], re-emit the unchanged
                                report at that rate instead, to prove repeated
                                identical reports produce no movement.
  detach                        Unplug the virtual device (a real USB port detach).
  attach [settle]               Plug it back in, wait [settle]s (default 8) for the
                                service to reconnect, then send the baseline report
                                carrying the accrued accumulator. Accumulators survive
                                the unplug -- that is the point.
  sleep <ms>                    Wait.
  send                          Emit the current state as one report.
  status                        Print internal state.
  echo <text>                   Print text (useful for marking up a transcript).
  help                          This text.
  quit                          Exit (also happens on EOF).

Encoders: 0 = VOLUME, 1 = SOURCE, 2 = PRESETS, 3 = TUNING
"""


class Harness:
    def __init__(self, device: VirtualEncoder, out=sys.stdout):
        self.device = device
        self.out = out
        self.should_quit = False

    def say(self, message):
        print(message, file=self.out, flush=True)

    def run_line(self, line: str):
        line = line.split("#", 1)[0].strip()
        if not line:
            return
        parts = line.split()
        verb, args = parts[0].lower(), parts[1:]
        handler = getattr(self, f"_cmd_{verb.replace('-', '_')}", None)
        if handler is None:
            self.say(f"? unknown command: {verb} (try `help`)")
            return
        try:
            handler(args)
        except (ValueError, IndexError) as exc:
            self.say(f"! {verb}: {exc}")
        except RuntimeError as exc:
            self.say(f"! {verb}: {exc}")

    # -- helpers -----------------------------------------------------------

    @staticmethod
    def _encoder(token):
        index = int(token)
        if not 0 <= index < ENCODER_COUNT:
            raise ValueError(f"encoder index must be 0-3, got {index}")
        return index

    # -- commands ----------------------------------------------------------

    def _cmd_turn(self, args):
        index = self._encoder(args[0])
        detents = int(args[1])
        mult = int(args[2]) if len(args) > 2 else 1
        self.device.turn(index, detents, mult)
        self.say(f"turn {ENCODER_NAMES[index]} {detents:+d} (movement {detents * mult:+d}, "
                 f"accumulator now {self.device.accumulators[index]})")

    def _cmd_offline_turn(self, args):
        index = self._encoder(args[0])
        detents = int(args[1])
        mult = int(args[2]) if len(args) > 2 else 1
        self.device.turn(index, detents, mult, emit=False)
        self.say(f"offline-turn {ENCODER_NAMES[index]} {detents:+d} (not reported; "
                 f"accumulator now {self.device.accumulators[index]})")

    def _cmd_press(self, args):
        index = self._encoder(args[0])
        self.device.set_button(index, True)
        self.say(f"press {ENCODER_NAMES[index]}")

    def _cmd_release(self, args):
        index = self._encoder(args[0])
        self.device.set_button(index, False)
        self.say(f"release {ENCODER_NAMES[index]}")

    def _cmd_tap(self, args):
        index = self._encoder(args[0])
        ms = int(args[1]) if len(args) > 1 else 80
        self._do_hold(index, ms, "tap")

    def _cmd_hold(self, args):
        index = self._encoder(args[0])
        ms = int(args[1])
        self._do_hold(index, ms, "hold")

    def _do_hold(self, index, ms, label):
        self.device.set_button(index, True)
        time.sleep(ms / 1000.0)
        self.device.set_button(index, False)
        kind = "long press" if ms >= 600 else "short press"
        self.say(f"{label} {ENCODER_NAMES[index]} {ms}ms ({kind})")

    def _cmd_idle(self, args):
        seconds = float(args[0])
        hz = float(args[1]) if len(args) > 1 else 0.0
        if hz <= 0:
            # Silence IS the idle state. The device transmits only when the report
            # contents change, and the service treats a quiet device as idle rather
            # than disconnected.
            time.sleep(seconds)
            self.say(f"idle {seconds}s (silent, as the real device is)")
            return
        interval = 1.0 / hz
        deadline = time.time() + seconds
        count = 0
        while time.time() < deadline:
            self.device.send_state()
            count += 1
            time.sleep(interval)
        self.say(f"idle {seconds}s ({count} unchanged reports at {hz}Hz -- deltas should all be 0)")

    def _cmd_detach(self, args):
        self.device.detach()
        self.say("detached -- the host should see a disconnect")

    def _cmd_attach(self, args):
        settle = float(args[0]) if args else 8.0
        self.device.attach()
        # Wait for the service to notice the replug and re-open the device before
        # sending anything. Its reconnect loop backs off (2s, doubling to 15s), so a
        # report sent the instant the port comes back has no reader and is dropped.
        if settle > 0:
            time.sleep(settle)
        # This report carries whatever the accumulator reached while unplugged, and it
        # is the one the host must absorb as a baseline rather than act on. It IS
        # Designer's test: "turn a knob ~50 detents while unplugged, then replug:
        # volume does not jump."
        self.device.send_state()
        time.sleep(0.3)
        self.say(f"attached (settled {settle}s; baseline carrying accumulator "
                 f"{self.device.accumulators} sent -- the host must discard it)")

    def _cmd_sleep(self, args):
        ms = float(args[0])
        time.sleep(ms / 1000.0)
        self.say(f"slept {ms}ms")

    def _cmd_send(self, args):
        self.device.send_state()
        self.say("sent one report")

    def _cmd_status(self, args):
        d = self.device
        self.say(f"attached={d._attached} node={d.hidraw_node} buttons=0x{d.buttons:02x}")
        for i in range(ENCODER_COUNT):
            self.say(f"  [{i}] {ENCODER_NAMES[i]:<8} position={d.positions[i]:<12} "
                     f"accumulator={d.accumulators[i]}")
        self.say(f"  reports_sent={d.stats['reports_sent']} "
                 f"host_reports={d.stats['outputs_received']} "
                 f"config_readbacks={d.stats['config_readbacks']} "
                 f"config_pushed={'yes' if d.pushed_config else 'no'}")

    def _cmd_echo(self, args):
        self.say(" ".join(args))

    def _cmd_help(self, args):
        self.say(HELP)

    def _cmd_quit(self, args):
        self.should_quit = True
        self.say("quit")


# ---------------------------------------------------------------------------
# selftest -- runs anywhere, needs neither root nor a device
# ---------------------------------------------------------------------------


def load_vectors():
    path = os.path.join(os.path.dirname(os.path.abspath(__file__)), "report-vectors.json")
    with open(path) as fh:
        return json.load(fh)


def selftest():
    """Check frame construction against the shared golden vectors.

    The same file is read by
    tests/Radio.Infrastructure.Tests/Platform/Input/VirtualEncoderHarnessProtocolTests.cs,
    which decodes these exact frames with the shipped RotaryEncoderDecoder. That is
    what stops this harness and the decoder drifting apart: one artifact, two
    readers, and a C# test that fails if either side moves.
    """
    failures = []

    def check(name, actual, expected):
        if actual != expected:
            failures.append(f"{name}:\n  expected {expected}\n  actual   {actual}")
        else:
            print(f"  ok  {name}")

    print("Frame construction vs report-vectors.json:")
    vectors = load_vectors()
    for vector in vectors["positionReports"]:
        frame = build_positions_report(
            vector["positions"],
            vector["buttonsMask"],
            vector["accumulators"],
            vector.get("tiersByte", 0),
        )
        check(vector["name"], frame.hex(), vector["hex"].lower())

    print("Structural invariants:")
    frame = build_positions_report([0, 0, 0, 0], 0, [0, 0, 0, 0])
    check("positions report is 37 bytes", len(frame), POSITIONS_REPORT_SIZE)
    check("report id is 0x01", frame[0], REPORT_ID_POSITIONS)
    check("config report is 107 bytes", len(build_config_report(default_config_payload())),
          CONFIG_REPORT_SIZE)
    check("descriptor is 125 bytes", len(bytes.fromhex(REPORT_DESCRIPTOR_HEX)), 125)

    print("Accumulator wrap (the decoder subtracts unchecked; we must wrap to match):")
    check("INT32_MAX + 1 wraps to INT32_MIN", _wrap_int32(INT32_MAX + 1), INT32_MIN)
    check("INT32_MIN - 1 wraps to INT32_MAX", _wrap_int32(INT32_MIN - 1), INT32_MAX)

    print("Button bitmask:")
    for i in range(ENCODER_COUNT):
        f = build_positions_report([0] * 4, 1 << i, [0] * 4)
        check(f"encoder {i} sets bit {i}", f[17], 1 << i)

    print("Config round-trip offsets:")
    payload = default_config_payload()
    check("config payload is 106 bytes", len(payload), CONFIG_PAYLOAD_SIZE)
    check("version at payload[0]", payload[0], 1)
    check("step_size of encoder 0 at payload[10..13]",
          struct.unpack_from("<i", payload, 2 + 8)[0], 1)

    print()
    if failures:
        print(f"FAILED ({len(failures)}):")
        for failure in failures:
            print("  " + failure.replace("\n", "\n  "))
        return 1
    print("selftest: all checks passed")
    return 0


def compare_descriptor():
    """Compare the embedded descriptor against the live device's, if present."""
    for entry in sorted(os.listdir("/sys/class/hidraw")):
        path = f"/sys/class/hidraw/{entry}/device/report_descriptor"
        uevent = f"/sys/class/hidraw/{entry}/device/uevent"
        try:
            with open(uevent) as fh:
                if "CAFE:00004005" not in fh.read().upper():
                    continue
            with open(path, "rb") as fh:
                live = fh.read()
        except OSError:
            continue
        embedded = bytes.fromhex(REPORT_DESCRIPTOR_HEX)
        if live == embedded:
            print(f"descriptor matches the live device at {entry} ({len(live)} bytes)")
            return 0
        print(f"DESCRIPTOR MISMATCH against {entry}:")
        print(f"  live     ({len(live)} bytes): {live.hex()}")
        print(f"  embedded ({len(embedded)} bytes): {embedded.hex()}")
        print("The firmware's descriptor has changed. Update REPORT_DESCRIPTOR_HEX.")
        return 1
    print("No live cafe:4005 device found to compare against.")
    return 0


# ---------------------------------------------------------------------------
# main
# ---------------------------------------------------------------------------


def main(argv=None):
    parser = argparse.ArgumentParser(
        description="ENC-17 virtual rotary-encoder injection harness (USB HID gadget over usbip).",
        epilog=HELP,
        formatter_class=argparse.RawDescriptionHelpFormatter,
    )
    parser.add_argument("--selftest", action="store_true",
                        help="check frame construction against the golden vectors and exit "
                             "(no root, no device, runs on any OS)")
    parser.add_argument("--check-descriptor", action="store_true",
                        help="compare the embedded report descriptor against the live device")
    parser.add_argument("--cleanup", action="store_true",
                        help="tear down leftover harness state from a hard kill, then exit")
    parser.add_argument("--script", metavar="FILE",
                        help="run commands from FILE instead of stdin")
    parser.add_argument("--command", "-c", action="append", metavar="CMD",
                        help="run one command; repeatable; implies exit when done")
    parser.add_argument("--max-seconds", type=float, default=300.0,
                        help="hard watchdog; exit after this long no matter what (default 300)")
    parser.add_argument("--settle-seconds", type=float, default=8.0,
                        help="how long to wait after attaching for the service to connect and "
                             "verify its configuration (default 8)")
    parser.add_argument("--vid", type=lambda v: int(v, 0), default=0xCAFE)
    parser.add_argument("--pid", type=lambda v: int(v, 0), default=0x4005)
    parser.add_argument("--name", default="RotaryUsb Rotary Encoder Generic HID")
    parser.add_argument("--uniq", default="ENC17VIRT")
    parser.add_argument("--no-unbind", action="store_true",
                        help="do not unbind/rebind the real device (use when it is already absent)")
    parser.add_argument("--quiet", action="store_true", help="suppress [gadget] chatter")
    args = parser.parse_args(argv)

    if args.selftest:
        return selftest()
    if args.check_descriptor:
        return compare_descriptor()

    if os.geteuid() != 0:
        print("This needs root (configfs, modprobe, usbip). Re-run with sudo.", file=sys.stderr)
        return 1

    if args.cleanup:
        teardown_gadget(verbose=True)
        # After a hard kill the real device is still unbound, so it has no HID entry.
        # Look it up in the USB tree instead and rebind exactly that one interface --
        # an earlier revision brute-forced every interface on the box, which poked
        # unrelated hardware and printed a wall of errors to say so.
        if find_real_encoder_interface(args.vid, args.pid) is not None:
            print("The real encoder is already bound; nothing to restore.", file=sys.stderr)
        else:
            interface = find_encoder_interface_in_usb_tree(args.vid, args.pid)
            if interface is None:
                print(f"No {args.vid:04x}:{args.pid:04x} USB device found to rebind. If the "
                      "encoder should be present, replug it or reboot.", file=sys.stderr)
            else:
                rebind_real_device(interface)
        print("cleanup complete", file=sys.stderr)
        return 0

    for tool in ("usbip", "usbipd", "modprobe"):
        if shutil.which(tool) is None:
            print(f"Required tool '{tool}' is not on PATH.", file=sys.stderr)
            return 1

    descriptor = bytes.fromhex(REPORT_DESCRIPTOR_HEX)

    # Fall back to the USB tree when the HID lookup finds nothing. The case that matters
    # is starting up after a previous run was hard-killed: the real encoder is still
    # unbound, so it has no /sys/bus/hid entry, and without this fallback `interface`
    # would be None and the exit path would not restore it -- the harness would leave the
    # box exactly as badly off as it found it.
    interface = None
    if not args.no_unbind:
        interface = (find_real_encoder_interface(args.vid, args.pid)
                     or find_encoder_interface_in_usb_tree(args.vid, args.pid))

    print("=" * 74, file=sys.stderr)
    print("ENC-17 harness. If this process is killed without cleaning up, run:", file=sys.stderr)
    print(f"    sudo python3 {os.path.abspath(__file__)} --cleanup", file=sys.stderr)
    if interface:
        print("The real encoder is about to be unbound so the virtual one is unambiguous.",
              file=sys.stderr)
        print("To restore it by hand:", file=sys.stderr)
        print(f"    echo -n '{interface}' | sudo tee /sys/bus/usb/drivers/usbhid/bind",
              file=sys.stderr)
        print("A reboot or a physical replug also restores it.", file=sys.stderr)
    elif not args.no_unbind:
        print("No real cafe:4005 device found to unbind -- continuing.", file=sys.stderr)
    print("=" * 74, file=sys.stderr)

    device = VirtualEncoder(args.name, args.vid, args.pid, args.uniq, descriptor,
                            verbose=not args.quiet)
    harness = Harness(device)

    deadline = time.time() + args.max_seconds
    stop_reason = "stdin closed"

    def _on_signal(signum, _frame):
        nonlocal stop_reason
        stop_reason = f"signal {signal.Signals(signum).name}"
        harness.should_quit = True

    for sig in (signal.SIGINT, signal.SIGTERM, signal.SIGHUP):
        signal.signal(sig, _on_signal)

    try:
        if interface and find_real_encoder_interface(args.vid, args.pid) is not None:
            unbind_real_device(interface)
        elif interface:
            print(f"The real encoder ({interface}) is already unbound; leaving it that way "
                  "and restoring it on exit.", file=sys.stderr)
        if interface:
            # Let the service's reconnect loop notice the real device left before the
            # virtual one appears, so the transition is a clean disconnect-then-connect
            # rather than a race between two cafe:4005 devices.
            time.sleep(1.0)

        device.open()
        device.attach()

        if args.settle_seconds > 0:
            harness.say(f"waiting {args.settle_seconds}s for the service to connect and "
                        "verify its configuration")
            time.sleep(args.settle_seconds)

        # Send the connection's baseline AFTER the settle, not before it, and this
        # ordering is load-bearing rather than tidy.
        #
        # The host absorbs the first report of every connection as a baseline and
        # produces no movement from it (ENC-1). A report written to /dev/hidg0 before
        # the service has opened the hidraw node goes nowhere -- there is no reader to
        # queue it for -- so a baseline sent at attach time is simply lost, and the
        # operator's FIRST REAL GESTURE silently becomes the baseline instead. Measured
        # on the appliance 2026-09-03: with the baseline sent at attach, `turn 0 1`
        # moved volume by 0 points and every later turn was correct, which reads as a
        # dropped first detent rather than as the harness's own sequencing.
        device.send_state()
        time.sleep(0.3)
        harness.say("ready (baseline sent; the next gesture produces real movement)")

        if args.command:
            for command in args.command:
                if harness.should_quit or time.time() > deadline:
                    break
                harness.run_line(command)
            stop_reason = "commands finished"
        elif args.script:
            with open(args.script) as fh:
                for line in fh:
                    if harness.should_quit or time.time() > deadline:
                        break
                    harness.run_line(line)
            stop_reason = "script finished"
        else:
            while not harness.should_quit:
                remaining = deadline - time.time()
                if remaining <= 0:
                    stop_reason = f"--max-seconds ({args.max_seconds}) watchdog"
                    break
                ready, _, _ = select.select([sys.stdin], [], [], min(remaining, 0.5))
                if not ready:
                    continue
                line = sys.stdin.readline()
                if line == "":
                    stop_reason = "stdin closed"
                    break
                harness.run_line(line)
    finally:
        harness.say(f"shutting down ({stop_reason})")
        device.close()
        if interface:
            time.sleep(0.5)
            rebind_real_device(interface)

    return 0


if __name__ == "__main__":
    sys.exit(main())
