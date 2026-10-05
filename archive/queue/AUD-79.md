# `AUD-79` — Cast audio alternated 21 ms of music with 21 ms of silence ("underwater")

[← Builder Queue index](../../docs/BUILDER_QUEUE.md)

🟠 **P1.** Filed and fixed 2026-09-29 from an owner report: *"I'm playing BT cast to my Office Speaker and I'm hearing occasional distortion ('underwater' sounding audio)."* Only while casting; the radio casts cleanly; the phone was next to the box; long-standing.

## What was measured (appliance, build `34c458e`, Bluetooth → Cast `Office speaker`, `StreamingMode: DirectChannel`)

- The Cast sender was healthy: ~10 chunks/s of ~29 KB (100 ms of base64 PCM), 0 send errors.
- Recording the Cast tap itself (`http://localhost:8080/stream/audio`, the same `TappedOutputStream` the DirectChannel sender reads) for 20 s gave **38.7 s of audio**; reading it for 15 s measured **exactly 2.00× real time**.
- The content was clean (no byte-identical repeats, L/R correlation +0.3…+0.98) but, sample-exact, **alternated 1,024 frames of audio with 1,024 frames of digital zeros** (70 zero runs of exactly 1,024 frames in 3 s). Played back, that is music chopped into 21 ms slices with 21 ms holes: the "underwater" sound.
- The tap's WRITER measured **0.967× real time** (908 × 4,096-byte writes in 20.0 s) while `pw-top` showed the graph clocked by the analog sink at a locked 48 kHz — so samples were being lost between the mixer and the tap.

## Two defects, compounding

1. **`BufferedTapModifier` dropped whole batches.** One flush buffer and an in-progress flag: a 2,048-sample batch that filled while the previous flush had not yet *run* on the ThreadPool was discarded. The pool had 21 ms per flush; with Bluetooth's extra load it missed about 1 in 30. That starved the tap (and the visualizer and diagnostic capture, which share the base).
2. **`TappedOutputStream.ReadForReader` spliced silence.** Whenever a reader caught up, it returned 4,096 bytes of zeros *without advancing the reader* — meant as keep-alive for a paused source, but it fired on every momentary catch-up. With the writer slow, the Cast reader (1 s head start) caught up within ~30 s and from then on got silence between every real block.

Why the radio casts cleanly was not measured; the likely answer is that without Bluetooth's load the pool rarely misses the 21 ms window, so the writer keeps pace and the reader never catches up.

## Fix

- `BufferedTapModifier`: 16 pooled batch buffers and a queue drained in order by one work item; a batch is dropped only when all 16 are in flight (≈ 340 ms stall), counted in `DroppedBatches`.
- `TappedOutputStream`: a caught-up reader with an active writer gets `WaitForData` and the reader waits (2 ms polls) for the next block; keep-alive silence only after the writer has been idle `KeepAliveAfterIdle` = 250 ms. Clock injectable through an internal overload.

## Verification

Tests: stalled consumer — 5 batches all delivered in order, 0 dropped; 20 batches — 16 delivered, 4 counted; caught-up reader waits and returns real data; idle writer yields keep-alive silence (fake clock). On the box: re-record the tap during Bluetooth — rate ≈ 1.00×, no 1,024-frame zero runs; owner listens to a Bluetooth cast.
