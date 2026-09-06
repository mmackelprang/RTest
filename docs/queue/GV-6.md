# GV-6 — ASSESSED AGAINST `D31` 2026-09-05 AND UNAFFECTED — claim it as written.

> Queue dossier for row **`GV-6`** of [`BUILDER_QUEUE.md`](../BUILDER_QUEUE.md).
> The detail below was moved verbatim out of that row's Item cell on 2026-09-06; only
> whitespace, the table's `\|` escapes and docs-relative link prefixes changed.
>
> ⚠ **Directional words in the prose were written when every row shared one file.**
> *above*, *below* and *this file* may now point across files — most often at
> [`BUILDER_QUEUE_ARCHIVE.md`](../BUILDER_QUEUE_ARCHIVE.md) or a sibling in this
> directory. They were left verbatim rather than reworded, which would be a content edit.

| Field | Value |
|---|---|
| Status | 📋 |
| Plan | _plan TBD (small)_ |
| Spec / handoff | [ADR-024 §3.3](../../design/decisions/2026-06-20-gv-mark-read-durable-readstate.md) |
| Depends on | **GV-4** |
| Branch | `fix/gv-markread-dark-409` |

## Detail

✅ **ASSESSED AGAINST `D31` 2026-09-05 AND UNAFFECTED — claim it as written.** Mark-read is a **different feature behind a different flag**: `RotaryPhone:Gv:MarkReadEnabled` (`appsettings.json:22`, read at `GvBridgeApiService.cs:142`/`:322`), whose own doc-comment at `:131` calls it *"distinct"* from send. Different routes, and a different `409` code (`markread_disabled`, not `send_disabled`), so nothing here needs the taxonomy parked `GV-5` was to build.

**Marking a thread read is not replying to it** — it is the read surface recording that reading happened, which is exactly what `D31` says this console is for.

⛔ **Do not park this because `GV-5` was parked.** **Distinguish `409 markread_disabled` from a genuine mark-read failure.** ADR-024 §3.3 (amended 2026-07-31) documents the dark-state `409` RotaryPhone returns when their `GVBridge:EnableMarkRead=false`. Our client maps **every** non-2xx to `null`, so "the feature is switched off" is indistinguishable from "GV is unreachable." Degrades acceptably today — no crash, no wrong badge, next list fetch is authoritative — so this is **diagnostic quality, not correctness**. Fix: in `GvBridgeApiService`'s two mark methods, detect `409` + `markread_disabled` and treat it as a distinct outcome — log **once** at Warning (not per call, to avoid journald churn on the N100) and latch a flag that suppresses further calls until restart, rather than re-POSTing on every mark.

**No user-visible affordance** — mark-read is silent-by-design (ADR-024 §6); this is for operators. Matters in exactly one state: config skew where ours is on and theirs is off.
