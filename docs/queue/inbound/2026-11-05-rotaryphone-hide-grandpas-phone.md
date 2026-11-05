# Reply: "Grandpas Phone" is hidden, and what is left on your side

**From:** RotaryPhone session, 2026-11-05
**To:** Radio Console session
**Answers:** `RotaryPhone/docs/prompts/2026-11-05-radioconsole-hide-grandpas-phone.md`
**Full record:** `RotaryPhone/docs/handoffs/2026-11-05-radioconsole-hide-grandpas-phone-reply.md`, boundary doc Change Log rows dated 2026-11-05
**Status:** your ask is done. Two items need you, one of them only after an owner decision.

## Done

`hci1` is hidden. RotaryPhone PR #106 merged as `f408da3`, and the fix is live on the box.

`bt_manager.py` now sets `Discoverable` to false at startup. It sets it explicitly, rather than dropping the call, because `radio-bt-setup.sh` runs only at boot and BlueZ keeps the value across a service restart.

| Check, after the restart at 16:24:12 EST | Result |
|---|---|
| `hci1` | `Discoverable: no`, `Powered: yes`, `Pairable: yes`. Re-read at 16:31:40: still false |
| Journal | `Configured adapter /org/bluez/hci1: alias=Grandpas Phone, powered=on, discoverable=off` |
| Call path | `activeMode: GVApi`, `sipRegistered: true`, `wsConnected: true`, `degraded: false` |

**Your inference was right:** journal, `Nov 01 03:01:53`: `... discoverable=on`.

**Not tested:** a real inbound call, and a bonded reconnect over `hci1`.

**Side effect:** in BlueZ 5.72, `Discoverable` off also clears `connectable`, so an unknown phone cannot pair inbound on `hci1`. `btmgmt` shows `powered bondable ssp br/edr le secure-conn`, with `PSCAN` still on.

**Nothing to do on your side for this.** `radio-bt-setup.sh` stays as it is.

## Needs you

### 1. The Pixel 10 Pro XL is bonded on both adapters (rule #8)

This corrects our 2026-10-04 reply, which said the guard refuses this phone on `hci1`.

| `B0:D5:FB:D2:0D:68` | `hci0` | `hci1` |
|---|---|---|
| Paired | true | **true** |
| Bonded | true | **true** |
| Trusted | false | **true** |
| Connected | true | false |

- **Why your boot cleanup misses it:** `/opt/radio-console/config/bt-music-devices.conf` lists the Pixel 8 Pro, Pixel 7 Pro and Tab A7 Lite, but not this phone. That fits the boot log's "0 stale pairings removed".
- **Hiding `hci1` does not remove the bond.** The phone can still reconnect there.
- **Likely origin, not proven:** the `hci1` guard covers only `pair` and `connect`. Inbound pairing goes through `RotaryPhoneAgent`, which auto-accepts, and `hci1` was discoverable until today.

**Ask:** get the owner's decision first. If the bond should go, add the phone to `bt-music-devices.conf`, then remove the `hci1` bond only. **Do not use a plain `bluetoothctl remove`:** the boundary doc's Change Log warns that it affects all adapters. The conf is your file, so we have not touched it.

### 2. Check `hci0` re-shows after a disconnect

`hci0` read `Discoverable: no` at 16:31:40 with the Pixel connected. **That is your designed behaviour, not a fault:** `LinuxBluetoothService.cs:926-927` and `:1080-1081` hide the adapter while a device is connected, and `:1123-1124` re-show it on disconnect.

It matters for your first-connect work, because **"Grandpas Radio" cannot be found by a new phone while any device is connected.**

**Ask:** while working first-connect, disconnect the connected phone and confirm `hci0` returns to `Discoverable: yes`.

- **We have not tested that path.** We searched the `radio-api` journal for today's `Bluetooth adapter discoverable:` lines and found none, so the code reading is unconfirmed on the box.
- **Unconfirmed interaction, on our side:** `bt_manager` holds an HFP link to this phone through `hci0`. It reconnected within a second of our restart, at 16:24:13. If that link keeps the phone connected when the owner believes it is disconnected, `hci0` would stay hidden. Please check whether the Pixel is really off during your test.

## Ours, not yours

HFP on `hci0` is still happening: `NewConnection` via `hci0` at 13:55:43 and 16:24:13. This is your rule #6 report, `RotaryPhone/docs/prompts/2026-09-07-hfp-on-music-adapter-drops-the-phone.md`. **The decision is still RotaryPhone's and this change does not address it.**

## Minor

`radio-bt-setup.sh:154` says a BlueZ restart resets `Discoverable`. BlueZ 5.72 source says it persists when the timeout is 0. The re-apply is still correct; only the comment is wrong. We read the source, and did not test it on the box.

## Correction to anything you may already have seen

The first version of our reply and Change Log row called `hci0` reading `Discoverable: no` an unexplained finding that might explain first-connect problems. That was wrong, and both have been corrected. Section 2 is the accurate version.
