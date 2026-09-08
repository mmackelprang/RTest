# `AUD-13` — `USBPort: ""` matches every device, so three sources bind to whatever enumerates first

[← Builder Queue index](../BUILDER_QUEUE.md)

🟠 **P1.** Found 2026-09-06 while planning `AUD-11`, **confirmed by reading the code**, and it is
arguably worse than the row that found it: `AUD-11` at least leaves a wrong link visible in the
PipeWire graph. This one is invisible everywhere, including in the log line written to catch it.

## The defect

`USBAudioSourceBase.cs:198-203` selects a capture device by substring:

```csharp
if (device.Name.Contains(usbPort, StringComparison.OrdinalIgnoreCase) &&
    !device.Name.StartsWith("Monitor of", StringComparison.OrdinalIgnoreCase))
{
  targetDevice = device;
  break;
}
```

**`string.Contains("")` returns `true` for every string.** The shipped configuration sets
`"USBPort": ""` — `src/Radio.API/appsettings.json:51` and `:54` — so the first capture device that
is not a `"Monitor of …"` loopback matches, `break`s, and becomes the source's input.

**And the warning that exists for exactly this case never fires.** `:208-214` logs
*"Could not find USB capture device for port {USBPort}, using first available capture device"* only
when `targetDevice == null`. The empty-string match guarantees it is non-null, so the fallback path
is unreachable while the shipped config is in use. The system takes the fallback's *behaviour*
without its *warning*.

## Who is affected

All three sources deriving from `USBAudioSourceBase`: **Radio, Vinyl, GenericUSB.**

Enumeration order is not stable across reboots, deploys or device hot-plug, so which physical input
a source binds to can change without any configuration changing. On a box with a USB Microphone, a
built-in analog input and a soundbar loopback all enumerable, "first that is not a Monitor" is not a
meaningful selection.

## Why it survived

Nothing reports it. The device is chosen silently, the warning is unreachable, and the source
reports itself healthy afterwards. This is the same family as `AUD-2`, `AUD-11` and
`SoundFlowMasterMixer` — **a component reporting success while doing something other than what it
claims** — and it is the fourth instance this month. `CLAUDE.md` § *Pre-Merge Review* exists for
exactly this.

⚠ **Note the comment at `:194-196` is accurate and the code is still wrong.** It correctly explains
the `Monitor of` guard. Nothing in it is false; it simply does not describe what happens when
`usbPort` is empty. A reviewer checking the comment against the code would have found no mismatch.

## The owner decision this needs

**What should an empty `USBPort` mean?** This is a product question and the plan should not answer
it alone:

- **"Any device"** — current de-facto behaviour, but then the selection should be explicit and
  logged, not an accident of `Contains("")`.
- **"Not configured"** — refuse to bind, surface a configuration fault (`ENC-12`'s tiered
  config-fault surfacing is the established pattern for this).
- **"Default capture device"** — bind deliberately to the system default rather than to
  enumeration position.

The shipped default is `""` for both entries, so whichever answer is chosen changes behaviour on
the appliance today. That is why this is not a silent code fix.

## Scope questions for the plan

1. Guard the empty case explicitly, whichever semantic is chosen.
2. Make the selection observable — the source should be able to say which device it bound to and
   why (matched / fell back / refused). Mind the sink asymmetry in `CLAUDE.md` § *Deployment* when
   choosing levels.
3. Does anything else in the tree match a device by unguarded `Contains` on a config value? This is
   a shape, not a one-off; enumerate rather than assume.
4. `AUD-11` touches the BT capture path in the same area. Neither blocks the other, but expect
   anchors to move if both are in flight.

## Verification

Unit-testable without hardware: the selection logic takes a device list and a port string, so an
empty port against a multi-device list is a pure function test. The behavioural half — which device
is actually bound on the appliance — needs the box and should be checked against whichever semantic
the owner picks.

---

## ⭐ Owner input, 2026-09-07 — this narrows the row considerably

Two facts from the owner that the row did not have, and that change what the fix should be:

1. **The Vinyl source will ALWAYS have a USB port connector. All of its audio arrives over USB.**
2. **The Radio USB connection is DEPRECATED.** `SDRRadioAudioSource` (RTL-SDR) is the only supported
   radio path.

### What that means for the two shipped `USBPort: ""` entries

They are not two instances of one problem. They need opposite treatments:

| Entry | Treatment |
|---|---|
| `Devices/Vinyl` (`appsettings.json:54`) | **Must name a real device.** Vinyl is USB-only, so an empty port is never correct — it is a missing configuration, not a permissive default. |
| `Devices/Radio` (`:51`) | **Vestigial.** It configures a deprecated path. The question is whether the entry — and `RadioAudioSource`'s USB capture route — should be removed rather than fixed. |

### Why this makes the current behaviour worse than the row states

The row says an empty port binds to whatever enumerates first. With both entries empty, **`Radio` and
`Vinyl` match the same first non-`"Monitor of"` device** — they are aliases, not merely
unconfigured. On the appliance the candidates are the USB Microphone and the built-in analog input,
and enumeration order is not stable across reboots or hot-plug.

So today, **Vinyl is capturing whichever of those two enumerates first**, and it is masked only
because nobody has played a record. The moment the turntable is used, it is selected by ordering
rather than by configuration — and the warning written to catch exactly that case is unreachable,
because `Contains("")` guarantees `targetDevice != null` before the fallback runs.

### The decision this row was waiting on, now answerable

**Empty `USBPort` should be a configuration fault, not a match.** `ENC-12`'s tiered config-fault
surfacing is the established in-repo pattern for making that visible rather than silent.

That answer is safe *because* of fact 1: Vinyl always has a port, so refusing to bind on empty
cannot break a legitimate configuration — there is no legitimate configuration in which Vinyl's port
is empty.

⚠ **The plan should decide `Devices/Radio` separately and deliberately.** Applying the same fault to
a deprecated path would surface a fault for a source nobody uses. Removing the entry and its capture
route is probably right, but that is a deprecation, not a bug fix, and it deserves its own
justification rather than riding along with the Vinyl fix.
