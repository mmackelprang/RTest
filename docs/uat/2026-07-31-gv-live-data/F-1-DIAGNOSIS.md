# F-1 root-cause diagnosis — silent conversation-body load failure

**Date:** 2026-07-31
**Investigator:** debugger pass following the UAT in [`REPORT.md`](REPORT.md) finding **F-1**
**Method:** server-side `journalctl` on `radio` (`radio-web`, `rotary-phone`), read-only `curl` against
`localhost:5004`, and source reads across both repos. **No production code was changed.**

---

## Executive answer

F-1 is **three independent defects**, not one. Two belong to RotaryPhone, one to Radio Console.
The UAT's candidate explanation — Google Voice throttling — is **falsified**.

| # | Defect | Owner | Confidence |
|---|---|---|---|
| **A** | GV cookie/PSIDTS goes stale ~11 min after each refresh, but refresh only runs every ~20 min → deterministic 9-minute auth blackouts → HTTP 502 | **RotaryPhone** | **Confirmed** |
| **B** | Thread ids containing `/` (all GV group/MMS threads) are never decoded from `%2F` → HTTP 200 with `messages: []` | **RotaryPhone** | **Confirmed** |
| **C** | Our client maps every failure to an empty list; the conversation pane has no error branch at all | **Radio Console** | **Confirmed** |

---

## Defect A — periodic auth blackout (the "general flakiness")

The gvbridge per-thread fetch returns a real **HTTP 502**, logged server-side.

`journalctl -u radio-web`:

```
[13:17:33 ERR] GvBridgeApiService: Failed to get GV SMS thread t.32665
System.Net.Http.HttpRequestException: Response status code does not indicate success: 502 (Bad Gateway).
   at Radio.Web.Services.ApiClients.GvBridgeApiService.GetSmsThreadMessagesAsync(...) ...GvBridgeApiService.cs:line 209
```

Upstream cause, `journalctl -u rotary-phone`: `api2thread/list returned Unauthorized for folder Sms`
— **271 occurrences today**, zero `TooManyRequests`, one `BadGateway`.

The pattern is a clean **20-minute square wave**, not rate-dependent degradation. Transition
timestamps (healthy → dead → healthy):

```
12:00:56 OK   12:11:00 401   12:21:01 OK   12:31:05 401   12:40:06 OK
12:52:10 401  13:00:11 OK    13:11:15 401  13:20:16 OK    13:31:20 401
13:40:21 OK   13:51:25 401   14:00:26 OK   14:12:30 401   14:20:31 OK
14:31:35 401  14:40:35 OK    15:00:02 OK   15:11:44 401
```

Mechanism, captured at a boundary:

```
14:59:40 WRN api2thread/list returned Unauthorized for folder Sms
15:00:01 INF Cookies saved to data/gv-cookies.enc
15:00:02 INF GV adapter re-activated with new cookies
15:00:02 INF CDP cookie refresh: 20 cookies extracted and activated
15:00:34 INF Listed 149 recent SMS messages          <- healthy again
15:11:44 WRN api2thread/list returned Unauthorized    <- 11m42s later, stale again
```

**Google's PSIDTS is good for ~11 minutes; RotaryPhone's CDP refresh fires every ~20 minutes;
there is no reactive refresh on 401.** Result: a guaranteed ~9-minute dead zone every 20 minutes.

Config discrepancy worth their attention: `/opt/rotary-phone/appsettings*.json` declares
`"CookieRefreshIntervalMinutes": 5` (default at
`RotaryPhoneController.GVBridge/Models/GVBridgeConfig.cs:23`) while observed cadence is 20 minutes.

### Throttling is falsified — three independent ways

1. The 60-second background poller runs at a **constant** rate and shows the identical on/off
   pattern — failure tracks wall-clock, not request volume.
2. The upstream status is `Unauthorized` (401), **never 429**.
3. Recovery happens at fixed 20-minute boundaries, not after a variable cooldown.

**11 of 11** `radio-web` 502s fall inside a dead window — perfect correlation:

| Time | Thread | Dead window |
|---|---|---|
| 12:13:00 | `t.+19192308923` | 12:11–12:20 ✓ |
| 12:54:01 | (thread *list*) | 12:52–13:00 ✓ |
| 13:13:12 | `g.Group Message.yL8g8…` | 13:11–13:20 ✓ |
| 13:17:33 | `t.32665` | 13:11–13:20 ✓ |
| 13:32:32 | `g.Group Message.d5Mri…` | 13:31–13:40 ✓ |
| 13:34:42 | `t.39041` | 13:31–13:40 ✓ |
| 13:39:17 | `t.+19199304719` | 13:31–13:40 ✓ |
| 14:32:12–14:32:32 | `t.51789`, `t.+13362039432` ×2, `t.+16627480199` | 14:31–14:40 ✓ |

This also explains the UAT's confusing timing: the "75s cooldown" that worked simply crossed a
20-minute boundary, and "the very next two failed" were still inside the same dead window.

### Bonus finding — `/api/gvbridge/status` is dishonest during a blackout

Measured at 15:13:03, while both SMS endpoints returned 502:

```json
{"available":true,"cookiesValid":true,"psidtsAgeSeconds":781,"degraded":false,"throttledUntil":null}
```

Because of this, our `GvStatus.IsAvailable` stays true and the "Google Voice is reconnecting"
banner (`src/Radio.Web/Components/Pages/PhoneMessagesPanel.razor:14-20`) never fires during the
exact window it exists for.

---

## Defect B — the MMS correlation is REAL, and it is not about MMS

**Confirmed by direct reproduction.** During a fully healthy window (15:06:48), curling gvbridge
with the *exact* escaping our client produces:

```
t.32665                                       HTTP 200  messages=2
t.%2B18019208129                              HTTP 200  messages=4
g.Group%20Message.d5Mri%2FNrDUQgXNXNQehOfw    HTTP 200  messages=0   <-- silent empty
g.Group%20Message.yL8g8JjuyR7Z57d9BxRW%2FQ    HTTP 200  messages=0   <-- silent empty
```

Server log for those same four requests names the cause:

```
[15:06:48 INF] Listed 2 SMS for thread t.32665 (of 149 parsed)
[15:06:48 INF] Listed 0 SMS for thread g.Group Message.d5Mri%2FNrDUQgXNXNQehOfw (of 149 parsed)
```

**The `%20` was decoded to a space, but the `%2F` was not.** Kestrel deliberately leaves `%2F`
encoded in the path so it cannot forge a segment boundary, so the route value keeps the literal
`%2F`. RotaryPhone then does an exact string compare against the real id, which fails —
`RotaryPhoneController.GVBridge/Clients/GvSmsClient.cs` (`ListMessagesAsync`):

```csharp
var forThread = all.Where(m => m.ThreadId == threadId).ToList();
```

Zero matches → `Succeeded: true` with an empty list → `GvSmsController.GetThreadMessages` returns
**200 + `messages: []`**.

This is a *200-with-empty* of exactly the class the `PositionalGvThreadParser` fix (`627b928`) was
meant to eliminate. RotaryPhone's honest-status guards (`ShapeIsSane`, the `Succeeded` flag) do not
catch it because the fetch and parse both genuinely succeeded — only the **filter** matched nothing.

### The predicate is "thread id contains `/`", not "is MMS"

GV group threads are `g.Group Message.<base64url>`, and the base64url alphabet includes `/`. Group
threads are the MMS threads. Both MMS threads in the live top-20 are group threads
(`g.Group Message.d5Mri/NrDUQgXNXNQehOfw` = Mary Carmen Wiser,
`g.Group Message.yL8g8JjuyR7Z57d9BxRW/Q` = Darlann Romney). Confirmed against the live list.

Because Defect A and Defect B were both active, these two threads were hit by *both* — every other
thread only had to dodge a 9-minute window, while these two were structurally unreadable **100% of
the time**. That is precisely why they were the only two that never rendered.

Note this is separate from the *cosmetic* MMS sender prefix in UAT finding G-8
(`+1XXXXXXXXXX - <text>`), which is real GV payload and remains GV-7's design concern.

### We cannot fix this client-side — both workarounds tested and rejected

- **Double-escape (`%252F`)** → server sees `g.Group%20Message.d5Mri%2FNrDUQgXNXNQehOfw` (now the
  `%20` is literal too) → still 0 messages.
- **Raw `/`** → the extra path segment misses the API route entirely and falls through to
  RotaryPhone's SPA fallback, returning **`index.html` with HTTP 200**. Our `GetFromJsonAsync`
  would throw on the content type → null → same silent empty.

---

## Defect C — our client turns any failure into "empty"

Confirmed by source. The full chain:

1. **`src/Radio.Web/Services/ApiClients/GvBridgeApiService.cs:204-218`** — `GetSmsThreadMessagesAsync`
   wraps `GetFromJsonAsync` in `catch (Exception) { log; return null; }`. `GetFromJsonAsync` calls
   `EnsureSuccessStatusCode()`, so **every non-2xx, timeout, and deserialization error collapses to
   `null`** with no status distinction. Same class of bug as GV-6.

2. **`src/Radio.Web/Components/Pages/PhonePage.razor:631-632`** — the load-bearing line:

   ```csharp
   var messages = await GvBridgeApi.GetSmsThreadMessagesAsync(threadId);
   _openThreadMessages = messages?.Messages.ToList() ?? new();
   ```

   `?? new()` converts "the load failed" into "the conversation is empty." After this line, a 502
   and a genuinely empty thread are **byte-identical state**. No `_openThreadError` field exists.

3. **`src/Radio.Web/Components/Pages/PhoneMessagesPanel.razor:184-191`** — hosts `PhoneTextsPanel`
   and passes **neither `Loading` nor `Error`**, even though both parameters exist
   (`PhoneTextsPanel.razor:172-173`). They default to `false`.

4. **`src/Radio.Web/Components/Pages/PhoneTextsPanel.razor:36-68`** — the conversation `msg-list` has
   exactly three branches: skeleton (`Messages == null && Loading`), empty
   (`Messages != null && Count == 0` → **"Start the conversation below."**), list. **There is no
   error branch.** Because `Loading` is never passed, the skeleton branch is dead code in this
   hosting — which is why the UAT saw no spinner either.

**Causal chain:** gvbridge 502 (or 200-with-empty for group threads) → `GetFromJsonAsync` throws /
returns empty → `catch` → `null` → `?? new()` → `Messages.Count == 0` →
`PhoneTextsPanel.razor:44` matches → "Start the conversation below." **Nothing in the chain is
capable of expressing "failed."**

### Why F-6 works and F-1 does not

The thread list keeps a dedicated error flag and does **not** coalesce it.
`PhonePage.razor:595-616` sets `_threadsError = true` when the fetch returns null and nothing is
cached (and toasts "Couldn't refresh / Showing the last update" when a cached list exists), and
`PhoneMessagesPanel.razor:110-117` renders `cloud_off` + `Retry` off that flag. The conversation
path has no counterpart to any of it.

That contrast is the fix template — the work is mostly copying an existing, already-designed
pattern down one level.

Side note this explains: the thread list "stayed healthy throughout" partly because of that
keep-last-good-list behaviour, not because the list endpoint was immune. It 502'd too (12:54:01),
which is what produced F-6's error state.

---

## Ownership split

### Radio Console owns (this repo — independently valuable, unblocks UAT, ship regardless)

- Stop mapping failure to empty. `GvBridgeApiService.GetSmsThreadMessagesAsync` must distinguish
  outcomes (result type / status), and `PhonePage.razor:632` must set an error flag instead of
  `?? new()`.
- Add `_openThreadError` / `_openThreadLoading` page state and pass `Loading` + `Error` through
  `PhoneMessagesPanel.razor:184-191` — the parameters already exist and are already wired for the
  thread list.
- Add the error branch to `PhoneTextsPanel.razor:36-68`, reusing the exact F-6 pattern from
  `PhoneMessagesPanel.razor:110-117` (`cloud_off` + "Couldn't load messages." + `Retry`). The string
  is already specified in `docs/design-handoffs/HANDOFF-phone-dark-theme-and-scrollbars.md:310`.
- Fix UAT finding **F-2** while in there: the empty copy must only appear for a genuine empty.
- **Cannot** fix Defect B. Do not attempt a client-side escaping workaround — both were tested and
  neither works.

### RotaryPhone owns (cross-repo handoff — two separate items)

- **B1 (do first — cheap, high value):** decode the `threadId` route value in
  `GvSmsController.GetThreadMessages` and `MarkThreadRead`
  (`src/RotaryPhoneController.GVBridge/Api/GvSmsController.cs`) — e.g. `Uri.UnescapeDataString(threadId)`
  — or move the id to a query parameter. Without this, group/MMS conversations are permanently
  unreadable no matter what we ship. Their honest-status guards cannot see it, so it also warrants a
  per-thread sanity check (thread present in the list but 0 messages parsed = suspicious).
- **B2 (the real availability fix):** align GV cookie refresh with the observed ~11-minute PSIDTS
  lifetime, add a reactive refresh-and-retry on the first 401, and make `/api/gvbridge/status`
  report `available:false` / `degraded:true` when `api2thread/list` is returning 401.

**Neither side's fix subsumes the other.** Ours makes the failure honest and recoverable; theirs
makes it rare.

---

## Scope-affecting notes

- **Each thread open costs 2–3 upstream Google calls, not 1.** `GvSmsController.MarkThreadRead`
  re-lists to resolve the thread (`ListThreadsAsync(count: 100)`) and again to enumerate message ids
  (`ListMessagesAsync(count: 200)`), on top of `GetThreadMessages`. Confirmed in the logs — two
  `Unauthorized` lines per user click. `EnableMarkRead: true` is set in deployed production config.
  If anyone later suspects rate pressure, this amplification is where to look.
- **Per-thread messages are derived by filtering the whole SMS folder list**
  (`GvSmsClient.ListMessagesAsync`), not by a per-thread Google endpoint. Consequences: (a) a thread
  outside the fetched window silently yields 0 messages — another 200-with-empty path worth a guard;
  (b) this is a plausible mechanism for UAT finding **F-5** (bubble text ending in a literal `...`),
  since folder-list entries carry snippets rather than full bodies. **F-5 is unproven** — worth one
  curl once B1 lands.
- **No test covers the failure path.** `tests/Radio.Web.Tests/Services/GvBridgeApiServiceVoicemailSmsTests.cs:81`
  only asserts the happy path for `GetSmsThreadMessagesAsync`. Any fix should add a non-2xx case
  there plus a bUnit case asserting the conversation pane renders the error state, not the empty state.
- **Monitoring is server-side only.** The two probes are
  `journalctl -u radio-web | grep 'Failed to get GV SMS thread'` and
  `journalctl -u rotary-phone | grep 'api2thread/list returned'`. The box is an Intel N100 — keep
  these bounded with `--since`, do not tail.
- **Retesting requires window awareness.** Any future UAT of this surface must record wall-clock time
  and check it against the 20-minute cycle, or results will look random again. Until B2 lands, test
  in the first ~10 minutes after a `CDP cookie refresh` log line.

---

## Key files

**Radio Console:** `src/Radio.Web/Components/Pages/PhonePage.razor`,
`src/Radio.Web/Components/Pages/PhoneMessagesPanel.razor`,
`src/Radio.Web/Components/Pages/PhoneTextsPanel.razor`,
`src/Radio.Web/Services/ApiClients/GvBridgeApiService.cs`,
`tests/Radio.Web.Tests/Services/GvBridgeApiServiceVoicemailSmsTests.cs`

**RotaryPhone:** `src/RotaryPhoneController.GVBridge/Api/GvSmsController.cs`,
`src/RotaryPhoneController.GVBridge/Clients/GvSmsClient.cs`,
`src/RotaryPhoneController.GVBridge/Clients/GvThreadClient.cs`,
`src/RotaryPhoneController.GVBridge/Models/GVBridgeConfig.cs`
