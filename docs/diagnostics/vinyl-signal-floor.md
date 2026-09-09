# Vinyl signal floor — is the turntable actually feeding the box?

Measured 2026-09-09 on `radio`, both states, minutes apart, same device and same command.

## The numbers

| State | Peak | dBFS | RMS |
|---|---|---|---|
| **Record playing, needle down** | 16112 / 32767 (49.2%) | **-6.2** | 2941.9 |
| **Needle UP (cue lever raised)** | 164 / 32767 (0.5%) | **-46.0** | 82.5 |
| **Gap** | ×98 | **39.8 dB** | ×36 |

⭐ **A ~40 dB gap is wide enough to be unambiguous.** Above roughly **-20 dBFS** is real signal;
anything near **-46 dBFS** is an empty input and **the fault is upstream of the box.**

## Why this exists

⛔ **`-46 dBFS` is the NOISE FLOOR of the dongle, not a weak signal.** On the day this was measured
the raised cue lever was mistaken for a fault, and the `-46 dBFS` reading was very nearly diagnosed as
*"the phono signal is ~40 dB too low — probably no phono preamp in the chain"*, which would have sent
the owner shopping for hardware that was already present and working. **The measurement was true; the
conclusion drawn from it was not, because nobody had established whether a record was playing.**

⭐ **The general rule this box keeps re-earning: a mechanism running is not the mechanism working.**
`isActive: true` from the devices API is not proof audio is flowing, `Capture: Status: Running` in
`/proc/asound` is not proof of signal, and a correctly-wired `pw-link` graph is not proof of samples.
**Measure the samples.**

## The recipe

```bash
# 1. Find the capture device
pactl list short sources | grep -i usb

# 2. Capture 5 s off it (safe while radio-api is also reading it)
timeout 5 parecord --device=alsa_input.usb-Generic_USB_Microphone_IM20000001-00.analog-stereo \
  --file-format=wav --rate=48000 --channels=2 --format=s16le /tmp/probe.wav

# 3. Measure peak and RMS
python3 - <<'PY'
import wave, struct, math
w=wave.open('/tmp/probe.wav','rb'); d=w.readframes(w.getnframes())
s=struct.unpack('<%dh'%(len(d)//2), d)
peak=max(abs(x) for x in s); rms=(sum(x*x for x in s)/len(s))**0.5
print(f"peak={peak}/32767 ({peak/32767*100:.1f}% FS, {20*math.log10(peak/32767):.1f} dBFS) rms={rms:.1f}")
PY
```

## ⚠ The device is named "USB Microphone" and is NOT a microphone

`lsusb` reports `2034:0105 Generic USB Microphone`; ALSA calls card 1 `Microphone`. **It is the phono
capture input.** The giveaway is that it is **stereo (2ch)** — a real microphone would be mono. Do not
"correct" this by looking for a differently-named device; there is only one USB audio input on the box.

## ⚠ The Vinyl source is USB, not Bluetooth

`/api/sources` lists a **Bluetooth device named "Turntable"** as `isPaired: true, isConnected: true`
**with an empty address**, while BlueZ reports nothing connected and PipeWire has no `bluez` node at
all. ⛔ **That entry is phantom state and must not be followed** — see [`AUD-25`](../queue/AUD-25.md).
`pw-link -l` confirms the real path: `alsa_input.usb-Generic_USB_Microphone…:capture_FL/FR →
Radio.API:input_FL/FR`.

## Album art on vinyl

Vinyl carries **no metadata protocol** — no AVRCP, no tags. **Acoustic fingerprinting is the only
route** to artist/album/art, and it runs on a ~15 s interval.

⭐ **Validate the instrument before blaming the record.** A positive control takes seconds and
distinguishes *"this album is not in the catalogue"* from *"fingerprinting is silently dead"*:

```bash
songrec audio-file-to-recognized-song "/opt/radio-console/media/audio/02 We're Ready.mp3"
#  -> matched "Boston - We're Ready" on 2026-09-09
```

⚠ **A passing control proves SongRec + network + Shazam work on a CLEAN DIGITAL FILE. It does not
prove vinyl-sourced audio fingerprints as reliably.** Surface noise, the RIAA curve, and especially
**turntable speed error** degrade matching — Shazam fingerprints are pitch-sensitive, and a stretched
belt running 1–3% off can defeat a match on even a famous record. **The decisive test is playing a
well-known record**: if that matches, an unmatched obscure album is correct behaviour and there is
nothing to fix.
