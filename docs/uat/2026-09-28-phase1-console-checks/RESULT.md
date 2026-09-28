# Phase 1 console checks — results, 2026-09-28 (in progress)

Run by the owner at the cabinet against [`CHECKLIST.md`](CHECKLIST.md). Box verified at `4adfbe9`
before the sitting, then `a4d52f3` after the mid-sitting RDS head fix (#684). Station for 1.1:
Rock 92, 92.3 FM, PI `0x70DB` → call sign WKRR.

| # | Check | Result |
|---|---|---|
| 1.1 | RDS scroll: no jump on RT update or PS flip | ⛔ → ✅ **FAIL, fixed in the sitting, then PASS.** First pass: *"once the entire string has rolled by, the beginning of the next cycle will have the first few characters replaced a couple of times before settling down"* — *"'Rock 92' is overwritten by 'The Cars'"*. Cause: Rock 92 rolls its **PS** through artist/title every ~3 s (`Rock 92` → `Cars` → `Good` → `Times` → `Roll`, every logged page clean), and the head leads each cycle. #684 binds the head to the PI call sign. After deploy: *"The scrolling is now fixed."* ⭐ The decoder fixes (`AUD-69`/`AUD-70`) are confirmed: no hybrid page in the log window. |
| 1.1a | Speed alternates on RT updates (`AUD-64`) | ✅ **Not observed** — *"I don't see a speed change with the scrolling."* `AUD-64` stays filed (the mechanism is in the code) but has no visible symptom on this station; P2, not a GA item. |
| 1.1b | D-B: rolling PS vs call sign | ✅ **Decided by the observation** — call sign, shipped as #684. Owner note: *"The station name always scrolls with the text when visible — it's not separate from it."* That is the single-row design (`RdsCard_RendersAsSingleRow_PSAndRtShareOneLine`); whether the name should instead be pinned and only the RT scroll is an **open product question**, not a defect. |
| 1.2 | BT: Pause on the console | ⛔ **FAIL — `AUD-40` CONFIRMED.** *"Pausing from the phone works, pausing from the console doesn't pause the music in BT mode."* Exactly the row's prediction: the console path sets `State = Paused` and never pauses the mixer component; the phone path is different (`OnPlaybackStatusChanged`). |
| 1.2+ | BT volume, observed alongside | ⚠ **Two findings, recorded against `AUD-47`.** (a) *"Changing volume on my phone changes audible volume on the console, but doesn't change the slider"* — consistent with `AUD-47`: `OnTransportPropertiesChanged` raises `VolumeChanged` and **nothing subscribes**. (b) *"Changing the slider by hand on the console changes volume on the soundbar, but doesn't affect the phone"* — the slider is the console's master (local output), which is behaving as built; whether it should also drive the phone via AVRCP absolute volume is a **product question**. |
| 1.2+ | BT disconnect | ✅ *"Disconnecting BT from the phone drops the album art and artist info immediately."* Expected: the source is gone. Recorded so nobody files it. |
| 1.3–1.9 | | _not yet run_ |
