# `AUD-94` — AM and shortwave on this tuner: direct sampling or an upconverter, or say so in the panel (owner decision)

[← Builder Queue index](../BUILDER_QUEUE.md)

⛔ **ON HOLD 2026-10-02 BY OWNER RULING.** Owner: *"#4 - keep as it is. Once I have the new hardware, we'll execute the roadmap item."* So: **AM and SW stay exactly as they are** — option 1 (dim the pills, `UNAVAILABLE`, a toast instead of switching band; the preset-bar spec's PR 4) is **declined for now** and is not to be built. Options 2–4 become the roadmap's *All-band reception* item ([`archive/roadmaps/ROADMAP.md`](../../archive/roadmaps/ROADMAP.md) § Queued; hardware spec in [`docs/known-issues-and-future-work.md`](../known-issues-and-future-work.md) § All-band reception), executed once the owner has the hardware. Revisit the PR 4 UI then, if any band is still unreceivable. The two "still open" notes below were true until this ruling.

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

## 2026-10-01 — option 1's UI is designed and ready to build once this is decided

The Designer's preset-bar spec
([`design-handoffs/2026-10-01-radio-presets-bar-and-band-colour.md`](../../archive/design-handoffs/2026-10-01-radio-presets-bar-and-band-colour.md)
§6, §7, and its PR 4 in §10) recommends option 1 as **"shown disabled with the reason"**:

- **Band pill:** dimmed to 0.4 opacity (the project's disabled convention), with the range sub-label
  replaced by **`UNAVAILABLE`**.
- **Tap:** does **not** switch band; it shows a toast with the reason the BAND view already shows (the
  band map's `UnavailableReason`, rendered at `VisualizerPanel.razor:153-155`; the spec's example is
  *"The AM Broadcast band (530–1,710 kHz) is below this tuner's 24 MHz lower limit."*).
- **AM/SW preset cards** in the new bar (`UI-20`): dimmed the same way, meta `… · unavailable`, tap shows
  the same toast.
- **Hiding is rejected** (options 2-4 would bring the bands back, and hiding orphans AM/SW presets);
  **keep-as-is is rejected** (it tunes into silence without saying why).

⛔ **The owner has NOT decided this.** It was the spec's §9 Q4 and was left open on 2026-10-01. No row was
minted for the UI; it belongs to this row once decided. `UI-20` deliberately leaves AM and SW behaving
exactly as today, just in one row.

⚠ **Data gap for the Planner:** the band list the panel reads (`RadioBandModel`, projected by
`RadioBandService.cs:35-48` from `BandPresets`) has **no "receivable" flag**. Only the band-map data knows
(`Mappable`, `UnavailableReason`, from `AUD-91`). Where the band button gets that from — the band list,
the band-map endpoint, or a shared source both read — is a Planner / Architect question, not a design one.

## 2026-10-01 — hardware deferred; the full hardware spec is on the roadmap

**Owner:** *"We're not going to change hardware now. There are not many AM stations nearby."* Asked
whether the current hardware and software would capture or play decent AM/SW stations, the answer
(measured, see above) is no: the R820T cannot lock below ~24 MHz, so no AM or SW station is receivable at
any strength. **Owner:** *"Spec out what HW I would need to make all the bands work, and add that to the
roadmap."* → the spec is [`docs/known-issues-and-future-work.md` § All-band reception](../known-issues-and-future-work.md)
(options A: one HF-capable RTL-SDR Blog dongle — V4 discontinued, V4 Lite expected to keep HF, verify at
purchase; B: a second dongle permanently behind an HF upconverter; plus an HF/MW antenna) and the roadmap
entry is [`archive/roadmaps/ROADMAP.md`](../../archive/roadmaps/ROADMAP.md) § Queued → *All-band reception*. Options 2–4 above are
therefore deferred until the owner buys hardware. **Still open:** option 1 (show AM/SW as unavailable in
the panel) — the owner has not ruled on it.

## Verification

For option 1: the panel shows AM/SW as unavailable on this tuner, and nothing tunes into silence
without saying why. For options 2-4: a known local AM station is heard on the box, and the `AUD-91` BAND
view's AM map shows it as a peak (that view would then need an AM channel plan, 119 channels at 10 kHz).

## Related

- `AUD-91` — the band-aware sweep and BAND view, whose feasibility pass measured this.
- `AUD-76` — the FM band map (*"AM/shortwave: no direct-sampling support, so FM is the target"*).
- [`UI-20`](../../archive/queue/UI-20.md) — the preset bar and one-row bands; leaves AM/SW as they are until this is decided.
