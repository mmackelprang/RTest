# `AUD-94` — AM and shortwave on this tuner: direct sampling or an upconverter, or say so in the panel (owner decision)

[← Builder Queue index](../BUILDER_QUEUE.md)

🟢 **P3, decision row.** Filed 2026-10-01 by the `AUD-91` Builder, from that row's box feasibility
measurements. **The reception results below are MEASURED on `radio`; the hardware options are from
the RTL-SDR's documented behaviour, not tried here.**

## What was measured (`AUD-91` dossier, § *Feasibility, measured on the box*)

- The dongle is a generic RTL2832U (`0bda:2838`) with a **Rafael Micro R820T** tuner (`Found Rafael
  Micro R820T tuner` in the `radio-api` journal on every receiver open). The library is Ubuntu's stock
  osmocom `librtlsdr2 2.0.1`.
- **AM (530-1710 kHz): nothing.** `signalStrength 0` / `rssiDbu -60` at 600, 1000, 1400 and 1710 kHz.
  librtlsdr logs `[R82XX] PLL not locked!` on every tune.
- **SW (1.6-30 MHz): nothing below ~24 MHz.** 5, 10 and 15 MHz read `0` with `PLL not locked!`. 23 and
  25 MHz read `0` with no warning. Only 27-29 MHz read the noise level of a working tuner.
- The tune **does not fail**: `SetFrequency` returns success and the radio plays (muted, here) a silent
  or noise channel. The radio control panel still offers AM and SW as if they worked.

`AUD-91` made the BAND view say plainly that AM and SW cannot be scanned on this tuner, and disabled Scan
for them. It did **not** change the radio itself. Selecting AM or SW in the panel still tunes into
silence.

## The options

1. **Accept and say so in the panel.** Mark AM and SW as not receivable on this hardware, either
   hidden or shown disabled with the reason. This needs no hardware. It is the honest minimum.
2. **Direct sampling (Q-branch).** librtlsdr exposes `rtlsdr_set_direct_sampling`. RTLSDRCore has no
   P/Invoke for it (`RtlSdrDevice.cs`, P/Invoke region). On a generic R820T dongle the Q-branch input is
   not wired to the antenna connector, so it needs a **hardware mod** (an HF antenna soldered to the
   RTL2832U's Q pins) or a dongle that has it built in (RTL-SDR Blog V3). The software also needs a
   different capture rate and DC handling below 24 MHz, and per-band tuner switching.
3. **An HF upconverter** (e.g. a 125 MHz upconverter) between the antenna and the dongle. The
   receiver would tune `f + LO` for AM/SW and switch the converter in or out. This needs hardware in the
   cabinet and a frequency offset in `RadioReceiver`.
4. **A different dongle** with HF built in (RTL-SDR Blog V4, whose R828D path covers HF through its
   own upconverter with the Blog fork of librtlsdr). This swaps the box's hardware and library.

Options 2-4 change the cabinet's hardware. That is the owner's call. Option 1 can be built on its own,
and is worth doing whatever is decided about the others.

## Verification

For option 1: the panel shows AM/SW as unavailable on this tuner, and nothing tunes into silence
without saying why. For options 2-4: a known local AM station is heard on the box, and the `AUD-91` BAND
view's AM map shows it as a peak (that view would then need an AM channel plan, 119 channels at 10 kHz).

## Related

- `AUD-91` — the band-aware sweep and BAND view, whose feasibility pass measured this.
- `AUD-76` — the FM band map (*"AM/shortwave: no direct-sampling support, so FM is the target"*).
