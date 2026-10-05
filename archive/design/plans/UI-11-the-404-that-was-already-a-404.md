# PLAN — `UI-11` · The unmatched `/api/*` path already returns 404. The defect is the missing contract, not a SPA fallback.

> **Row:** `UI-11`, [`docs/queue/UI-11.md`](../../docs/queue/UI-11.md). 🟡 **P2.** Filed 2026-09-08.
> **Branch:** `fix/ui-11-api-404-not-spa-fallback` (unchanged — the row's branch name is still accurate).
> **Estimate:** **0.5 d**, and **an owner decision before Task 1**. §0.8 and §1 derive both.
> **Auto-mergeable on green gates — the code is. The *scope* is not.** §0.9.
> **Planned against** `main` at **`e0b84c4b`**. Every line number below was read out of the tree at that
> commit.
> **Nothing on the box was changed.** One read-only `curl` against `radio-web` supplied §0.2's
> measurement; no deploy, no restart, no config write. §7.2 lists what is measured and what is derived.

---

## 0. Read this before Task 1

### 0.1 ⚠⚠ `C-301` — THE ROW'S CENTRAL PREMISE IS FALSE

[`docs/queue/UI-11.md:10-16`](../../docs/queue/UI-11.md) says:

> `Radio.Web`'s SPA fallback catches **everything** that does not match a route, including paths under
> `/api/`. So a caller that asks for JSON at a mistyped or renamed API path gets:
> - **HTTP 200**
> - `Content-Type: text/html`
> - the SPA shell

**`Radio.Web` has no SPA fallback, has never had one, and does not return 200 for an unmatched
`/api/*` path.** It returns a bare `404` with a zero-byte body. Six independent checks, none of which
depends on any other:

| # | Check | Result |
|---|---|---|
| 1 | **Measured live** on `radio-web` | `HTTP/1.1 404 Not Found`, `Content-Length: 0`, **no `Content-Type` header at all** |
| 2 | `MapFallback` / `MapFallbackToPage` / `MapFallbackToFile` / `UseSpa` / `UseSpaStaticFiles` / `UseDefaultFiles` / `UseFileServer` / `UseStatusCodePages*` anywhere in `src/` | **0 hits** |
| 3 | `git log -S "MapFallback" --all -- src/` and the same for `UseSpa` | **0 commits.** It was not removed; it never existed |
| 4 | Any `*.html` file under `src/Radio.Web/` | **0 files.** There is no `index.html` to return — the shell is generated from `Components/App.razor` |
| 5 | Any `@page` route template containing a catch-all (`{**…}`) | **0.** Twelve plain literal routes — §0.6 enumerates them |
| 6 | `MapRazorComponents<TRootComponent>()` in .NET 10 | Registers **one endpoint per `@page` template** plus `/_framework/opaque-redirect`, `/_blazor`, `/_blazor/negotiate`, `/_blazor/disconnect/`, `/_blazor/initializers/`. **No catch-all, no fallback** |

The wire, on the appliance, today:

```
$ curl -sSD- -o/dev/null http://radio:5002/api/definitely-not-a-route
HTTP/1.1 404 Not Found
Content-Length: 0
Server: Kestrel
```

`Radio.Web` is a **.NET 8+ Blazor Web App** (`app.MapRazorComponents<App>().AddInteractiveServerRenderMode()`,
`Program.cs:644-645`). That hosting model routes by real per-page endpoints. The bug the row describes
is real, but it belongs to the **legacy .NET 6/7 model** — `app.MapFallbackToPage("/_Host")`, whose
default pattern is `{*path:nonfile}` at `Order = int.MaxValue`. Under *that* model `/api/gvsms/` has no
dot in its last segment, matches `:nonfile`, renders `_Host.cshtml` and yields exactly the reported
`200` + `text/html` + app shell. **`Radio.Web` is not on that model and never was.**

📌 **This is not a criticism of whoever filed the row.** The symptom was real, it was reported in good
faith by the other repo, and the *inference* — "a 200 with an app shell means a SPA fallback, and we
are the app with the SPA" — is the natural one. §0.2 is where it actually came from.

### 0.2 `C-302` — both incidents happened on RotaryPhone's service, not ours

The row cites two costs "on both sides of the boundary". Traced to primary sources, **both 200s were
emitted by RotaryPhone**.

**Incident 1 — the `XR-2` workaround.** The row (`UI-11.md:24-26`) calls this "**Ours**". Our own
primary record says otherwise —
[`docs/BUILDER_QUEUE_ARCHIVE.md:99`](../../docs/BUILDER_QUEUE_ARCHIVE.md):

> both double-escaping (`%252F`) and a raw `/` were tested and both fail, the latter falling through to
> **their** SPA fallback and returning `index.html` with HTTP 200.

The *attempt* was ours. The *fallback that swallowed it* was theirs — necessarily so: the route under
test was `/api/gvbridge/sms/threads/…`, which lives on **RotaryPhone.API at `radio:5004`**
(`Program.cs:336`, `phoneApiBaseUrl`), not on `Radio.Web` at `:5002`. `Radio.Web` has no `gvbridge`
route to miss.

**Incident 2 — the wasted probe.** The verbatim source is
[`docs/queue/inbound/2026-09-08-rotaryphone-incident-and-corrections.md:113-114`](../../docs/queue/inbound/2026-09-08-rotaryphone-incident-and-corrections.md):

> Note the route prefix is `/api/gvbridge/sms/` — we wasted a probe on `/api/gvsms/` and got HTTP 200
> with `index.html` back, your SPA-fallback trap biting us **in our own house**.

Same host. They were retesting `XR-2` against **their own** `/api/gvbridge/sms/…` on `:5004`,
mistyped the prefix, and their own fallback answered. "In our own house" is the phrase that settles it,
and their next message concedes the ownership directly —
[`2026-09-08-rotaryphone-bell-persistence-and-404.md:37`](../../docs/queue/inbound/2026-09-08-rotaryphone-bell-persistence-and-404.md):

> **Ours has the same hole** and we are filing it on our side too.

⭐ **The row's framing is itself an instance of the disease it names.** The row's thesis is *a success
code covering a failure*. What actually happened is a **misattribution surviving two hops**: they said
"your trap", we recorded "ours", and neither side re-derived which server sent the bytes. The
row asks for a fix to a hole that is, on our side, already closed — and the evidence that it was
already closed was sitting in our own archive at line 99 the whole time.

### 0.3 So what *is* wrong, and is the row still worth shipping?

Three things are genuinely true, and none of them is the row's headline:

1. **The 404 has no body and no `Content-Type`.** A JSON caller gets zero bytes and must infer
   everything from the status line. The row's own parenthetical — *"ideally `application/json` with a
   small problem body"* (`UI-11.md:36`) — is the part that is still unbuilt, and it is the part
   RotaryPhone actually needed: a body naming the right service would have ended their probe in one
   request instead of sending them to file a defect against us.
2. **Nothing locks the behaviour.** `Radio.Web` has **no** test asserting an unmatched `/api/*` path
   404s. It is correct by accident of the hosting model, not by contract. A future
   `UseStatusCodePagesWithReExecute("/not-found")` — which is what the **.NET 10 Blazor Web App
   template now ships**, and the first thing a Builder reading the current docs would reach for —
   silently converts every `/api/*` 404 into a `404` **with an HTML body**. That is 80% of this row's
   bug, arriving through the front door, with every existing test green. §6.1.
3. **`Routes.razor` claims a behaviour the app does not have.** §0.5.

**Verdict: worth shipping, materially smaller than the row implies, and re-scoped.** §1.

### 0.4 `C-303` — scope question answered: `Radio.API` is unaffected, and is *already* locked

The row asks (`UI-11.md:46-50`) whether `Radio.API` is affected too, and says *verify rather than
assume*. **Verified. It is not, and it already has the regression test this row would have added.**

- `Radio.API/Program.cs` calls `UseStaticFiles` **never** (grep across `src/`: the only two hits are
  `Radio.Web/Program.cs:599` and its own doc comment). `CLAUDE.md`'s claim that it serves no static
  files is accurate.
- Its pipeline is `UseCors` (dev) → `UseSerilogRequestLogging` → `UseApiMetrics` → `UseAudioStream` →
  `UseHttpsRedirection` (prod) → `UseAuthorization` → `MapControllers` → `MapHub` ×2 →
  `MapHealthChecks("/health")` (`Program.cs:155-186`). **No fallback, no static files, nothing that can
  emit HTML.**
- ⭐ **The lock already exists**, and has been green in CI since it was written:
  `tests/Radio.API.Tests/ApiTests.cs:32-43`, `ApiApplication_ReturnsNotFoundForUnknownRoute`, asserts
  `GET /api/nonexistent` → `404`.

**Services checked: two, and there are exactly two.** `WebApplication.CreateBuilder` appears at
`src/Radio.API/Program.cs:15` and `src/Radio.Web/Program.cs:13` and nowhere else in the repository. No
tool, test or deploy artifact hosts a third.

**No `Radio.API` change is in this plan.** §6.2 says why not even a cosmetic one.

### 0.5 `C-304` — `Routes.razor` has a dead `<NotFound>` fragment that is unsupported on .NET 10

`src/Radio.Web/Components/Routes.razor:6-10`:

```razor
  <NotFound>
    <LayoutView Layout="typeof(Layout.MainLayout)">
      <p role="alert">Sorry, there's nothing at this address.</p>
    </LayoutView>
  </NotFound>
```

**That string has never been served.** It does not appear in the live 404 (§0.1 check 1 — zero bytes),
and it cannot: Microsoft's own routing documentation states that Blazor Web Apps do not use the
`NotFound` parameter, that it was retained in .NET 8/9 only to avoid a breaking change — *"placing
`<NotFound>…</NotFound>` markup doesn't result in an exception, but using the markup isn't effective
either"* — and that it **"isn't supported in .NET 10 or later"**. This solution targets `net10.0`.

This is precisely the failure mode `CLAUDE.md` § *Pre-Merge Review* exists to catch: **markup that
asserts a behaviour the code does not have.** An engineer reading `Routes.razor` today will conclude
that an unknown URL renders a friendly message inside `MainLayout`. It renders zero bytes. Removing it
is a four-line, behaviour-neutral deletion and it belongs in this row, because this row is about
unmatched paths.

### 0.6 `C-305` — registration order is *not* what makes real routes win, and the row implies it is

The row (`UI-11.md:35-37`) and the queue line both say the rule must be *"registered so that real API
routes still win"*, and the brief asks where it goes *"relative to the fallback"*. **Both frame this as
a registration-order problem. It is not.** ASP.NET Core endpoint selection is by **route precedence**
then **`Order`**, never by the sequence of `Map*` calls. `MapFallback` exists exactly for this: it
stamps `Order = int.MaxValue`, so *any* non-fallback endpoint beats it regardless of where either was
registered.

This matters because a Builder reasoning from the row's model would try to place the call "before the
fallback", find there is no fallback to be before, and improvise. The correct instruction is:

> Put it wherever it reads best — **after `MapRazorComponents` (`Program.cs:644-645`)** — and rely on
> `MapFallback`'s `Order`, not on the line number.

**The routes it must not shadow, enumerated rather than assumed.** `Radio.Web` serves exactly
**fourteen** server routes:

| Kind | Routes |
|---|---|
| `/api/*` minimal-API (2) | `/api/health/version` (`Program.cs:609`), `/api/albumart/{filename}` (`Program.cs:615`) |
| `@page` components (12) | `/`, `/bare`, `/bluetooth`, `/devices`, `/Error`, `/history`, `/metrics`, `/minimal`, `/phone`, `/radio`, `/sleep`, `/system` |
| Blazor framework | `/_framework/*`, `/_blazor`, `/_blazor/negotiate`, `/_blazor/disconnect/`, `/_blazor/initializers/` |
| Static files | whatever is under `wwwroot/` + `_content/` |

Both `/api/*` routes have literal first segments and default `Order = 0`; they beat `/api/{**rest}`
twice over — on precedence (literal beats catch-all) and on order. The twelve page routes and every
framework route are **outside `/api/`** and cannot match the pattern at all.

⚠ **`/stream/audio` and `/stream/audio/mp3` are not `Radio.Web` routes.** The row's third warning is
sound in principle but its premise is off: those paths are served by `Radio.API`'s
`AudioStreamMiddleware` (`src/Radio.API/Streaming/AudioStreamMiddleware.cs:112`, wired at
`src/Radio.API/Program.cs:168`), with constants at `src/Radio.Core/Constants/ApiPaths.cs:19-20`.
`Radio.Web` has no `/stream/*` route to catch, and an `/api/`-scoped pattern could not catch one if it
did. **Scoping to `/api/` only, as the row asks, is correct — and it is correct for a second reason
the row does not give:** the *other* eleven non-`/api/` deep links are what a wider pattern would
break.

### 0.7 What the fix must not do: leak the request into a body or a log

The brief's constraint, and it is a real one here. `CLAUDE.md` is explicit that **`Radio.Web`'s Console
sink is unrestricted** — `MinimumLevel.Default: "Information"`, no `restrictedToMinimumLevel` on the
Console sink, and `radio-web.service` sets no `SyslogLevelPrefix` — so **every `Information` line in
`Radio.Web` is a journald line**, on a box where log volume correlates with audible audio distortion.
That is the whole of `PHN-5`, whose founding example is a raw phone number reaching the journal from
`PhoneHubService.cs:82`.

An unmatched `/api/*` path is **exactly the shape that carries PII here**: the paths people mistype
are `/api/gvbridge/sms/threads/<threadId>` and `/api/phone/...`, and a thread id is a phone number or a
group id. Two consequences, both binding:

1. **The handler logs nothing.** Not at `Information`, not at `Debug`. `Radio.Web` installs no
   `UseSerilogRequestLogging` (unlike `Radio.API/Program.cs:162`), so *adding no log statement* means
   genuinely nothing is written — there is no ambient request log to inherit from.
2. **The body is a compile-time constant.** No `instance` member, no `HttpContext.Request.Path`, no
   query string, no route values, no `traceId`. §2.3 uses a `const string` written with `Results.Text`
   rather than `Results.Problem`/`Results.Json` **specifically so that the exact bytes are auditable in
   source** and no serializer, `ProblemDetailsFactory` or framework extension can inject a request-derived
   field later.

### 0.8 Estimate — 0.5 d

| | |
|---|---|
| Task 1 — promote the existing host factory | 0.5 h |
| Task 2 — the test pair | 1.5 h |
| Task 3 — the terminal rule + constant body | 1 h |
| Task 4 — delete the dead `<NotFound>` | 15 min |
| Task 5 — docs | 30 min |
| Build + full suite + review + PR | 1 h |

No hardware, no audio path, no deploy required to prove it.

### 0.9 Auto-mergeable? The code, yes. The scope, no.

The row predicts auto-mergeable and **for the change itself that holds**: no hardware, no live-audio
path, no migration, no secrets, testable in both directions, and the entire runtime delta is one
endpoint that can only match paths nothing else claimed.

⛔ **But the justification the row was approved on is false (§0.1), so the scope is now a judgement
call, and the global auto-merge policy's "still pause when the change is ambiguous" clause applies to
that — not to the diff.** §1 is a decision for the owner **before Task 1**, not after the PR is open.
Once §1 is answered, the PR merges on green gates without a further check-in.

---

## 1. Decision — the owner picks the scope before Task 1

**Option A — close `UI-11` as already-correct. Ship tests only.**
Tasks 1, 2 (minus the problem-body assertions), 5. ~2 h. The 404 stays zero-byte; the behaviour gets a
regression lock; the row closes with the §0.1/§0.2 correction recorded and an outbound note to
RotaryPhone.
*Gives up:* the JSON body — so a future mistyped probe still gets a silent, bodyless 404 and still
cannot tell which of the two services it reached. That is what cost RotaryPhone a probe, and it is the
one user-visible thing the row asked for that is genuinely absent.

**Option B — `Recommended:` ship the contract. Tests, the JSON problem body, the dead-markup deletion.**
All five tasks. ~0.5 d. Turns a behaviour that is correct by accident into one that is correct by
contract, gives a JSON caller a body that names the right service, and closes §6.1's front door.
*Cost:* ~90 lines of new source and test against a defect that is not, today, producing a wrong status
code. The 404 was already a 404; this makes it a *useful* 404.

**Option C — do what the row literally says: add a SPA fallback ordering fix.**
⛔ **Not viable — there is nothing to order against.** Listed only so it is on the record as considered
and refuted, per §0.1.

**Recommendation: B.** The row's *evidence* collapsed but its *instinct* did not — a bodyless 404 on a
two-service appliance where both services expose `/api/*` is a real ambiguity, and it demonstrably
cost the other repo time this week. B is the smallest change that removes the ambiguity rather than
just proving it is not a 200.

Everything below is written for **B**. To take **A**, drop Tasks 3 and 4 and the two problem-body
assertions in Task 2 (they are marked).

---

## 2. Tasks

### Task 1 — promote the `OPS-5` host factory to `TestHelpers`

`OPS-5` already built and paid for a working `WebApplicationFactory<Program>` for `Radio.Web`, but
nested it inside the only class that used it:
`tests/Radio.Web.Tests/Configuration/StaticAssetPipelineTests.cs:62-117`. Task 2 needs the same host.
Copying 55 lines would be flagged in review, and correctly.

**Create `tests/Radio.Web.Tests/TestHelpers/RadioWebFactory.cs`** (`TestHelpers/` is this project's
existing convention — `CapturingLogger`, `HermeticTestRig`, `MockHttpHandler`, `RecordingHandler`,
`HubEventFire` all live there; there is no `TestSupport/`):

```csharp
namespace Radio.Web.Tests.TestHelpers;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

/// <summary>
/// Minimal in-process host for Radio.Web, shared by every test that needs to drive a real request
/// through the real Program.cs pipeline.
/// </summary>
/// <remarks>
/// <para>
/// Extracted verbatim from <c>StaticAssetPipelineTests.WebFactory</c> (OPS-5) when UI-11 became its
/// second consumer. Mirrors the lessons already paid for in
/// <c>Radio.API.Tests.TestSupport.CustomWebApplicationFactory</c>: hosted services removed so no
/// background poll outlives the test, and every relative storage path redirected onto a private root
/// so concurrent hosts never share a SQLite file or a DataProtection key ring.
/// </para>
/// <para>
/// xUnit constructs one instance per test class holding it as an <c>IClassFixture</c>, so two classes
/// mean two hosts. That is safe precisely because of the per-instance <see cref="Guid"/> root below —
/// do not "optimise" it into a shared static.
/// </para>
/// </remarks>
public sealed class RadioWebFactory : WebApplicationFactory<Program>
{
  private readonly string _storageRoot =
    Path.Combine(Path.GetTempPath(), "radio-web-tests", Guid.NewGuid().ToString("N"));

  protected override void ConfigureWebHost(IWebHostBuilder builder)
  {
    // Program.cs configures the static Serilog logger before the factory applies any override,
    // so replace it here or the run fills with connection-refused noise from this host.
    Log.Logger = new LoggerConfiguration().MinimumLevel.Fatal().CreateLogger();

    builder.UseEnvironment("Testing");

    builder.ConfigureAppConfiguration((_, config) =>
    {
      var data = Path.Combine(_storageRoot, "data");
      config.AddInMemoryCollection(new Dictionary<string, string?>
      {
        ["Database:RootPath"] = data,
        ["DataProtection:KeysPath"] = Path.Combine(data, "keys-web"),
      });
    });

    builder.ConfigureServices(services =>
    {
      foreach (var descriptor in services.Where(d => d.ServiceType == typeof(IHostedService)).ToList())
      {
        services.Remove(descriptor);
      }
    });
  }

  protected override void Dispose(bool disposing)
  {
    base.Dispose(disposing);

    if (!disposing)
    {
      return;
    }

    // Best-effort: a leftover temp directory is harmless, and throwing here would turn a passing
    // test into a failing one during teardown.
    try
    {
      if (Directory.Exists(_storageRoot))
      {
        Directory.Delete(_storageRoot, recursive: true);
      }
    }
    catch
    {
      // Ignored - SQLite can still hold a handle on Windows when the host tears down.
    }
  }
}
```

**Then edit `StaticAssetPipelineTests.cs` to consume it**, deleting the nested class:

- Line 21: `: IClassFixture<StaticAssetPipelineTests.WebFactory>` → `: IClassFixture<RadioWebFactory>`
- Line 23: `private readonly WebFactory _factory;` → `private readonly RadioWebFactory _factory;`
- Line 25: `public StaticAssetPipelineTests(WebFactory factory)` → `(RadioWebFactory factory)`
- Delete lines **56-117** (the `<summary>` block and the whole `public sealed class WebFactory`), leaving
  the closing brace of the outer class.
- Add `using Radio.Web.Tests.TestHelpers;` to the using block at lines 3-9.

⚠ **Tree-contention note.** A Builder is mid-cycle on `SystemConfigPage.razor` and its tests.
`StaticAssetPipelineTests.cs` is not in that set, so this should not conflict — but if it has moved on
`main` by the time this is claimed, rebase before editing rather than reapplying the line numbers
above.

**Gate:** `dotnet test --filter "FullyQualifiedName~StaticAssetPipelineTests"` — 4 tests, still green.
This task must not change any assertion.

### Task 2 — the test pair, and it must be run before Task 3

⭐ **This task is the measurement, not a formality.** §0.1 says the headline assertion is already green.
Task 2 exists to *prove that on this machine, in the test host* rather than inherit it from a `curl`
against the appliance. **Run it before writing a line of Task 3** and record the actual result in the
PR body — including, and especially, if it disagrees with §0.1.

**Create `tests/Radio.Web.Tests/Configuration/ApiNotFoundPipelineTests.cs`:**

```csharp
namespace Radio.Web.Tests.Configuration;

using System.Net;
using System.Text.Json;
using Radio.Web.Tests.TestHelpers;

/// <summary>
/// Pins the two halves of Radio.Web's unmatched-path contract against the real Program.cs pipeline.
/// </summary>
/// <remarks>
/// <para>
/// UI-11. The row was filed believing an unmatched <c>/api/*</c> path returned <c>200</c> with an SPA
/// shell. It does not, and never did — this app is a Blazor Web App
/// (<c>MapRazorComponents</c>), which routes by real per-page endpoints and has no fallback; the
/// reported symptom came from the other repo's service. See
/// <c>design/plans/UI-11-the-404-that-was-already-a-404.md</c> §0.1-0.2.
/// </para>
/// <para>
/// ⚠ <b>The pair is the point, and neither half is sufficient alone.</b> A change that 404s
/// <c>/api/*</c> by 404ing <em>everything</em> would satisfy the first group and break the console;
/// a change that serves the shell for everything would satisfy the second and reintroduce the bug the
/// row describes. Do not delete one group to make the other pass.
/// </para>
/// </remarks>
public class ApiNotFoundPipelineTests : IClassFixture<RadioWebFactory>
{
  private readonly RadioWebFactory _factory;

  public ApiNotFoundPipelineTests(RadioWebFactory factory) => _factory = factory;

  /// <summary>Every <c>@page</c> route in the app, read out of the tree at <c>e0b84c4b</c>.</summary>
  public static TheoryData<string> SpaDeepLinks() => new()
  {
    "/", "/bare", "/bluetooth", "/devices", "/Error", "/history",
    "/metrics", "/minimal", "/phone", "/radio", "/sleep", "/system",
  };

  // ─── Direction 1: an unmatched /api/* path must never look like a success ──────────────────

  [Theory]
  [InlineData("/api/definitely-not-a-route")]
  [InlineData("/api/gvsms/")]            // the exact probe that cost RotaryPhone a request
  [InlineData("/api/albumart")]          // the real route minus its required {filename}
  [InlineData("/api")]
  [InlineData("/API/Definitely/Not/A/Route")]  // routing is case-insensitive; so is the rule
  public async Task UnmatchedApiPath_Returns404_AndNeverAnHtmlPage(string path)
  {
    var response = await _factory.CreateClient().GetAsync(path);
    var body = await response.Content.ReadAsStringAsync();

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    Assert.NotEqual("text/html", response.Content.Headers.ContentType?.MediaType);
    Assert.DoesNotContain("blazor.web.js", body);
    Assert.DoesNotContain("<!DOCTYPE html>", body, StringComparison.OrdinalIgnoreCase);
  }

  // ─── The two assertions below are Option B only. Drop them for Option A. ───────────────────

  [Fact]
  public async Task UnmatchedApiPath_AnswersAJsonCallerWithAProblemBody()
  {
    var response = await _factory.CreateClient().GetAsync("/api/definitely-not-a-route");
    var body = await response.Content.ReadAsStringAsync();

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

    using var doc = JsonDocument.Parse(body);
    Assert.Equal(404, doc.RootElement.GetProperty("status").GetInt32());
    Assert.Equal("Not Found", doc.RootElement.GetProperty("title").GetString());
  }

  /// <summary>
  /// PHN-5. The paths people mistype here carry phone numbers and thread ids, and Radio.Web's Console
  /// sink is unrestricted, so nothing request-derived may reach the body or the journal.
  /// </summary>
  /// <remarks>
  /// ⚠ The <c>DoesNotContain</c> assertions below are <b>vacuously true of an empty body</b> — which is
  /// exactly what this endpoint returned before UI-11. The two assertions above them are therefore
  /// load-bearing: they prove a body exists and is well-formed, so the absence checks are absence and
  /// not emptiness. Do not reorder or remove them.
  /// </remarks>
  [Fact]
  public async Task UnmatchedApiPath_BodyEchoesNothingFromTheRequest()
  {
    const string path = "/api/gvbridge/sms/threads/8015550137?token=sekrit";

    var response = await _factory.CreateClient().GetAsync(path);
    var body = await response.Content.ReadAsStringAsync();

    Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    using (var doc = JsonDocument.Parse(body))
    {
      Assert.Equal(404, doc.RootElement.GetProperty("status").GetInt32());
    }

    Assert.DoesNotContain("8015550137", body);
    Assert.DoesNotContain("sekrit", body);
    Assert.DoesNotContain("token", body);
    Assert.DoesNotContain("threads", body);
    Assert.DoesNotContain("gvbridge", body);
  }

  // ─── Direction 2: real routes still win, and every SPA deep link still serves the shell ────

  /// <summary>
  /// Proves the terminal rule does not shadow a real <c>/api/*</c> endpoint.
  /// </summary>
  /// <remarks>
  /// <c>/api/health/version</c> is the whole proof deliberately: it is served in-process from the
  /// assembly's own build info, so it is deterministic. The other real route,
  /// <c>/api/albumart/{filename}</c>, proxies outward to Radio.API and its status therefore depends on
  /// whether a Radio.API happens to be listening on the developer's box — an unstable oracle, and a
  /// timing-dependent one. It is covered by the theory above only in its unmatched form.
  /// </remarks>
  [Fact]
  public async Task RealApiRoute_StillWins_AndIsNotShadowedByTheTerminalRule()
  {
    var response = await _factory.CreateClient().GetAsync("/api/health/version");
    var body = await response.Content.ReadAsStringAsync();

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    Assert.Contains("gitSha", body, StringComparison.OrdinalIgnoreCase);
  }

  [Theory]
  [MemberData(nameof(SpaDeepLinks))]
  public async Task SpaDeepLink_StillServesTheAppShell(string path)
  {
    var response = await _factory.CreateClient().GetAsync(path);
    var body = await response.Content.ReadAsStringAsync();

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
    Assert.Contains("_framework/blazor.web.js", body);
    Assert.Contains("<title>Radio Console</title>", body);
  }
}
```

**Why the deep-link theory is cheap and not fragile:** `App.razor:39` renders
`<Routes @rendermode="new InteractiveServerRenderMode(prerender: false)" />`. With prerendering off,
a server request renders **only `App.razor`'s static shell** — no `MainLayout`, no page component, no
service resolution beyond DataProtection for the component marker. Every one of the twelve routes
therefore returns byte-identical HTML, which is why asserting on shell markers is sound.

⚠ **If `/Error` misbehaves in the test host**, drop that one entry and note it in the PR — it is the
only route with framework-assigned meaning. Do **not** thin the list further; eleven of the twelve are
the guard.

**Expected result, to be confirmed rather than assumed:**

| Test | Predicted, before Task 3 | Why |
|---|---|---|
| `UnmatchedApiPath_Returns404_AndNeverAnHtmlPage` | 🟢 **already passes** | §0.1 — the row's headline is already true |
| `UnmatchedApiPath_AnswersAJsonCallerWithAProblemBody` | 🔴 **RED** | `JsonDocument.Parse("")` throws on a zero-byte body |
| `UnmatchedApiPath_BodyEchoesNothingFromTheRequest` | 🔴 **RED** | same — the non-vacuity assertions fail first |
| `RealApiRoute_StillWins…` | 🟢 already passes | nothing shadows it yet |
| `SpaDeepLink_StillServesTheAppShell` | 🟢 already passes | this is the guard; it must never go red |

⛔ **If the first test comes back RED — i.e. the row was right and I was wrong — stop and re-plan.**
That would mean something outside `Program.cs`, `Routes.razor` and the endpoint model is serving HTML,
and none of §0's reasoning would be safe to build on.

### Task 3 — the terminal `/api/` 404 with a constant, leak-free body *(Option B only)*

**Create `src/Radio.Web/Configuration/ApiNotFound.cs`:**

```csharp
namespace Radio.Web.Configuration;

using Microsoft.AspNetCore.Http;

/// <summary>
/// The terminal 404 for any request under <c>/api/</c> that no real endpoint claimed.
/// </summary>
/// <remarks>
/// <para>
/// ⚠ <b>This did not fix a wrong status code.</b> Measured on the appliance 2026-09-08, an unmatched
/// <c>/api/*</c> path already returned <c>404</c> — with <c>Content-Length: 0</c> and no
/// <c>Content-Type</c> at all. This app is a Blazor Web App: <c>MapRazorComponents</c> registers one
/// endpoint per <c>@page</c> template and no catch-all, so there is no SPA fallback to swallow
/// anything. The <c>200</c>-with-<c>index.html</c> symptom that prompted UI-11 came from the other
/// service on this box. What this type adds is the <em>body</em>: a JSON caller that mistypes a path
/// now learns which service it reached instead of receiving zero bytes.
/// </para>
/// <para>
/// <b>The body is a compile-time constant, and that is a security decision rather than a
/// simplification.</b> The paths callers mistype here are <c>/api/gvbridge/sms/threads/&lt;id&gt;</c> and
/// <c>/api/phone/…</c>, where a thread id is a phone number. Radio.Web's Console sink carries no
/// <c>restrictedToMinimumLevel</c> and <c>radio-web.service</c> sets no <c>SyslogLevelPrefix</c>, so
/// anything this code logged or echoed would be a journald line on a box where log volume correlates
/// with audible audio distortion — the whole of PHN-5. Hence: no log statement at any level, no
/// <c>instance</c> member, and <see cref="Results.Text(string?, string?, System.Text.Encoding?, int?)"/>
/// over <c>Results.Problem</c> or <c>Results.Json</c>, so the exact bytes on the wire are readable in
/// this file and no serializer, <c>ProblemDetailsFactory</c> or framework extension can add a
/// request-derived field to them later.
/// </para>
/// <para>
/// Registered with <c>MapFallback</c>, which stamps <c>Order = int.MaxValue</c>. Endpoint selection is
/// by route precedence then order — never by <c>Map*</c> call sequence — so both real Web-side API
/// routes (<c>/api/health/version</c>, <c>/api/albumart/{filename}</c>) win on both counts, and the
/// twelve <c>@page</c> routes cannot match an <c>/api/</c>-scoped pattern at all.
/// </para>
/// </remarks>
internal static class ApiNotFound
{
  /// <summary>RFC 9457 media type. Asserted on the wire by <c>ApiNotFoundPipelineTests</c>.</summary>
  internal const string ContentType = "application/problem+json";

  /// <summary>
  /// The entire response body, verbatim. Names both services and both real Web-side routes, because
  /// the request this exists to answer is "which of the two services did I just hit?" — the question
  /// RotaryPhone burned a probe on. It names nothing from the request itself.
  /// </summary>
  internal const string Body =
    """{"type":"about:blank","title":"Not Found","status":404,"detail":"No API route on this service matches the request path. Radio.Web (port 5002) serves only /api/health/version and /api/albumart/{filename}. The audio, radio, bluetooth, queue and configuration API is Radio.API on port 5000."}""";

  /// <summary>Handler for the terminal <c>/api/{**rest}</c> route. Takes no request input, by design.</summary>
  internal static IResult Handler() =>
    Results.Text(Body, ContentType, contentEncoding: null, statusCode: StatusCodes.Status404NotFound);
}
```

**Then edit `src/Radio.Web/Program.cs`.** Insert immediately after the `MapRazorComponents` call at
lines 644-645 (`using Radio.Web.Configuration;` is already present at line 6):

```csharp
app.MapRazorComponents<Radio.Web.Components.App>()
  .AddInteractiveServerRenderMode();

// UI-11 - a terminal 404 with a JSON problem body for any unmatched path under /api/.
//
// This is NOT an ordering fix for a SPA fallback: there is no SPA fallback in this app, and there
// never has been (`git log -S MapFallback -- src/` is empty). MapRazorComponents above registers one
// endpoint per @page template and no catch-all, so an unmatched path already 404'd - it just did so
// with zero bytes and no Content-Type, which is what sent a caller off to file a defect against the
// wrong service. See ApiNotFound's remarks, and the plan at
// design/plans/UI-11-the-404-that-was-already-a-404.md.
//
// MapFallback stamps Order = int.MaxValue, so every endpoint above wins on order as well as on
// precedence. The line's POSITION in this file is readability only - moving it changes nothing.
app.MapFallback("/api/{**rest}", ApiNotFound.Handler);
```

**Gate:** re-run Task 2. All five groups green, and the two RED tests now pass **for the stated
reason** — check the body on the wire once by hand rather than trusting the assertion:

```bash
dotnet run --project src/Radio.Web &
curl -sSD- http://localhost:5002/api/definitely-not-a-route
curl -sS -o /dev/null -w '%{http_code} %{content_type}\n' http://localhost:5002/phone
```

### Task 4 — delete the dead `<NotFound>` fragment *(Option B only)*

Replace `src/Radio.Web/Components/Routes.razor` in full:

```razor
@* No <NotFound> fragment: this is a Blazor Web App, and the framework does not use one.
   It was retained for back-compat in .NET 8/9 (present but inert) and is unsupported on .NET 10,
   which this project targets. The markup that used to sit here promised "Sorry, there's nothing at
   this address." for an unknown URL; that string has never been served, and an unmatched path is
   answered by the ASP.NET Core pipeline instead - a bare 404 for a page route, or the JSON problem
   body in Radio.Web.Configuration.ApiNotFound for anything under /api/ (UI-11). *@
<Router AppAssembly="typeof(Program).Assembly">
  <Found Context="routeData">
    <RouteView RouteData="routeData" DefaultLayout="typeof(Layout.MainLayout)" />
    <FocusOnNavigate RouteData="routeData" Selector="h1" />
  </Found>
</Router>
```

`Router.NotFound` is an optional `[Parameter]` — the stock .NET 8+ Blazor Web App template ships
`Routes.razor` with `<Found>` only — so this compiles and changes no behaviour. `SpaDeepLink_StillServesTheAppShell`
is the regression cover.

### Task 5 — docs

1. **`CLAUDE.md`**, in § *Deployment* beside the existing `/api/health/version` note, add:

> **An unmatched path under `/api/` on either service returns 404, and `Radio.Web` says so in JSON.**
> `Radio.Web` is a Blazor Web App with no SPA fallback (`MapRazorComponents` registers one endpoint per
> `@page` route and no catch-all), so a mistyped API path has always 404'd rather than returning the
> app shell — the `200`-with-`index.html` symptom reported in `UI-11` came from RotaryPhone's service
> on `:5004`, not from ours. Since `UI-11`, `Radio.Web` answers with `application/problem+json` naming
> which service was reached; `Radio.API` returns a bare 404 (`ApiTests.cs:32`). The body is a
> compile-time constant and echoes nothing from the request — see `PHN-5` for why that matters on this
> box.
>
> ```bash
> curl -sS -o /dev/null -w '%{http_code} %{content_type}\n' http://radio:5002/api/nope   # 404 application/problem+json
> curl -sS -o /dev/null -w '%{http_code} %{content_type}\n' http://radio:5000/api/nope   # 404
> curl -sS -o /dev/null -w '%{http_code} %{content_type}\n' http://radio:5002/phone      # 200 text/html
> ```

2. **`docs/queue/UI-11.md`** — do **not** silently rewrite it. Add a `## Correction (2026-09-08)`
   section at the top recording §0.1 and §0.2 with their evidence, and leave the original text below
   it. The row's misattribution is itself a finding worth keeping legible; a tidied row would erase
   the only trace of how a wrong premise crossed two repos.

3. **`docs/queue/CROSS-REPO-HANDOFFS.md`** — add an outbound item telling RotaryPhone that **we did not
   have the hole**, that both 200s trace to `:5004`, and that their symmetric fix is still worth
   shipping on their side. Per their own adopted protocol (§ *Protocol — both proposals adopted*, item
   1) an ack should name what was independently verified: name the archive line, the `git log -S`
   result, and the live `curl`.

4. **`docs/BUILDER_QUEUE.md`** — §8 below carries the replacement row text.

---

## 3. Ordering

1. **Task 1** — extraction. Prove `StaticAssetPipelineTests` still passes before anything else moves.
2. **Task 2** — write and **run** the tests. Record the actual RED/GREEN split. ⛔ Hard checkpoint: if
   the headline test is RED, stop and re-plan (§2 Task 2).
3. **Task 3** — the terminal rule. Re-run Task 2; the two RED tests go green, the three green ones stay
   green.
4. **Task 4** — the dead-markup deletion. Independent of 3; do it after so a bisect separates a routing
   change from a markup change.
5. **Task 5** — docs.
6. Full suite, review, PR.

Tasks 3 and 4 are separable; 1 and 2 are not.

---

## 4. Test plan — what fails first, and in which direction

⭐ **The row asks for a pair, and it is right to.** One assertion alone is satisfiable by a change that
makes the app worse:

- **"unmatched `/api/*` → 404" alone** is satisfied by an app that 404s *everything*. The console goes
  black and the test is green.
- **"deep links → `index.html`" alone** is satisfied by the very bug the row describes — a fallback
  that serves the shell for every path, `/api/*` included.

**Both directions, and what fails first:**

| Direction | Assertion | Before Task 3 | After |
|---|---|---|---|
| Failure is visible | `/api/definitely-not-a-route` → 404, never `text/html` | 🟢 already true (§0.1) | 🟢 |
| Failure is *legible* | …and carries a parseable `application/problem+json` body | 🔴 **RED** — zero bytes, `JsonDocument.Parse` throws | 🟢 |
| Failure leaks nothing | …whose body contains no path, id or query value | 🔴 **RED** — fails its own non-vacuity guard first | 🟢 |
| Success still succeeds | `/api/health/version` → 200 `application/json` | 🟢 | 🟢 **must not change** |
| Success still succeeds | 12 `@page` deep links → 200 `text/html` + shell markers | 🟢 | 🟢 **must not change** |

⚠ **Two of the five are green before the change and their job is to stay green.** They are not
redundant; they are the half of the pair that stops a fix from being worse than the bug. A reviewer
seeing "3 green tests added for a bug fix" should read this table, not delete them.

**Manual UAT is not required** (no hardware, no audio path, no deploy-dependent behaviour), but the
two `curl`s in Task 3's gate are cheap and worth doing once against a locally-run `Radio.Web` so the
body has been seen by a human, not only by an assertion.

**Regression gates:** full `dotnet test` per `CLAUDE.md` (**never** piped into `tail`), and the Release
build at the **47-warning / 0-error** baseline.

---

## 5. Docs and queue

Covered in Task 5. Docs-per-PR convention: `CLAUDE.md`, `docs/queue/UI-11.md`,
`docs/queue/CROSS-REPO-HANDOFFS.md`, `docs/BUILDER_QUEUE.md`.

`design/API_REFERENCE.md` was checked and **is not** touched — it documents `Radio.API`'s surface, and
nothing in `Radio.API` changes.

---

## 6. Deliberately not done

### 6.1 ⛔ `UseStatusCodePagesWithReExecute("/not-found")` — the trap, and why it is named here

This is what the **.NET 10 Blazor Web App template now ships**, and it is what the current Microsoft
"Not Found responses" documentation steers you to. **Do not add it.** It re-executes the pipeline into
a Razor page for *every* 400-599 without a body — including every `/api/*` 404 — and would give a JSON
caller a **`404` with an HTML body**. That is most of this row's original bug arriving through the
front door, and it would arrive with the terminal `/api/` rule in place and every test still green,
because the rule supplies a body and status-code pages only fire on responses that have none.

⭐ **This is the single most likely way for someone to reintroduce the reported symptom**, and it is
the strongest argument for shipping Option B rather than closing the row on §0.1 alone: after Task 3,
`/api/*` has a body, so a future status-code-pages middleware cannot claim it.

If a friendly HTML 404 for **page** routes is ever wanted, that is a separate row, and it must be
scoped to exclude `/api/`.

### 6.2 No change to `Radio.API`

Verified unaffected (§0.4) and already regression-locked at `ApiTests.cs:32-43`. Adding a matching
problem body there was considered and dropped: `Radio.API` serves no static files and has no HTML to
leak, so there is no defect to fix, and it would widen a row whose premise has already collapsed once.
If the JSON body proves useful on the Web side, filing it for `Radio.API` as a small follow-up is
cheap.

### 6.3 No widening beyond `/api/`

The row asks for `/api/`-only and that is right. `/stream/audio` and `/stream/audio/mp3` are
`Radio.API` routes (§0.6) and unreachable from here — but the eleven non-`/api/` page routes on *this*
service are very reachable, and **a wider pattern is how the console goes black**.

> ### ✅ CONFIRMED — 2026-09-09 (Builder, `UI-11`). Measured, and the mechanism is NOT the one you would guess.
>
> ⛔ **This section was briefly struck as false during the `UI-11` cycle, and the strike was wrong.**
> It is restored, and the retraction is left visible below because *how* it went wrong is the useful
> part. **§6.3 is TRUE.** Widening the pattern to `/{**rest}` and running the **whole test project**:
> **7 failed / 1,197 passed** — the four `StaticAssetPipelineTests` cases go RED, `/css/design-system.css`
> returning `NotFound` instead of `OK`.
>
> **The mechanism, since it is not obvious and §0.6 does not cover it. The asymmetry is the crux:
> page routes are endpoints, static files are not.** A real `@page` endpoint *competes* with the
> fallback and wins — on **route precedence first** (a literal segment beats a catch-all, settling it
> before `Order` is consulted at all) and on `Order` second. ⚠ **Do not read `Order = int.MaxValue` as
> the thing protecting those routes** — precedence already did, so a change touching only `Order`
> would look safe on that reading and would not be. **Static files never enter that competition.**
> `WebApplicationBuilder` inserts the automatic `UseRouting()` *before* all user middleware, so routing
> selects the fallback endpoint; then `StaticFileMiddleware` — user middleware at `Program.cs:599` —
> **stands down purely because *some* endpoint is already selected**. And `{**rest}` carries no
> `:nonfile` constraint (that lives only in `MapFallback`'s *default* pattern, which this plan does
> not use).
>
> **Measured vs derived, because §7.2 keeps that distinction for a reason.** `/css/design-system.css`,
> `/js/idle-dimmer.js` and the DSEG font are **measured** 404s — they are the three `InlineData` cases.
> That the Radzen theme and **`_framework/blazor.web.js`** go the same way, and that the circuit
> therefore never starts — an unstyled, non-interactive shell on a 1920x720 wall panel — is
> **derived**: nothing in the suite fetches either. The derivation is sound (one `UseStaticFiles` call
> serves them all), but it is a derivation. **On that derivation, the console goes black literally.**
>
> ⭐ **Why the Builder got this wrong, recorded because the error is more reusable than the fact.** The
> mutation was run as `dotnet test --filter "FullyQualifiedName~ApiNotFoundPipelineTests"` and came
> back 20/20 green — then reported as *"the entire suite"*. **20 is exactly that one class's case
> count.** The guard was in the same project the whole time, in `StaticAssetPipelineTests` — the very
> file this row's Task 1 had *itself moved* an hour earlier. Same shape as `KIOSK-3`'s *"our 'zero
> consumers' grep was scoped to `src/`; the consumer is a shell script."*
> ⚠ **Two earlier mutations had each failed exactly their own test, and that is what made the third
> feel safe. A positive control validates the INSTRUMENT, never the SEARCH SPACE.**
>
> ✅ **One genuine addition survives the retraction: the narrow scope has a *second* reason nobody had
> written down — truthfulness.** `/some-typo` is a mistyped *page*, and a `/{**rest}` rule answers it
> *"No API route on this service matches the request path"* — a well-formed body carrying a wrong
> answer. Both reasons hold, and they are independent: **availability** (static assets die) and
> **truthfulness** (a page miss gets an API answer). `UnmatchedNonApiPath_DoesNotGetTheApiProblemBody`
> pins the second; `StaticAssetPipelineTests` already pinned the first.

### 6.4 No `instance` member, no `traceId`, no logging

§0.7. A correlation id would be useful and is not free here: the natural implementations reach for
`HttpContext.TraceIdentifier` alongside the path, and the path is the thing that must not travel.

---

## 7. Self-review

### 7.1 Placeholder / coverage scan

No `TBD`, no "similar to Task N", no "implement later". Every task carries literal, complete code.
Every row-stated requirement is addressed: terminal 404 for unmatched `/api/*` (Task 3), real routes
still win (§0.6 + Task 2), SPA deep links unbroken (Task 2, 12 routes enumerated not guessed), stream
endpoints checked (§0.6), JSON problem body (Task 3), no leak into body or log (§0.7 + Task 2),
`Radio.API` scope question answered with evidence (§0.4).

### 7.2 Measured vs. derived — say which

| Claim | Basis |
|---|---|
| Live 404 with 0 bytes and no `Content-Type` | **Measured** — one read-only `curl` against `radio-web`, 2026-09-08 |
| `/phone`, `/Error` → 200 `text/html` live | **Measured**, same run |
| No fallback in `src/`, ever | **Measured** — grep + `git log -S`, both empty |
| No `.html` under `src/Radio.Web` | **Measured** — glob, 0 files |
| `MapRazorComponents` registers no catch-all | **Derived** from framework source and Microsoft docs; corroborated by the measurement above |
| `<NotFound>` unsupported on .NET 10 | **Derived** from Microsoft's Blazor routing doc; corroborated — the string is absent from the measured 404 |
| Both incidents were RotaryPhone's | **Derived** from primary records (archive line 99, inbound line 113-114, inbound `:37`) plus the fact that `gvbridge` routes exist only on `:5004` |
| Task 2's predicted RED/GREEN split **in the test host** | **Derived.** ⚠ Not measured — the suite was not run, because a Builder is mid-cycle in this tree and a concurrent build would collide with theirs. **Task 2 exists to measure it.** |

### 7.3 What I could not verify

- **The test host's behaviour**, per the row above. High confidence, zero measurement.
- **Whether `/Error` renders cleanly under `WebApplicationFactory`.** Task 2 names the fallback.
- **RotaryPhone's own service.** Everything said about `:5004` is inference from *our* records and
  *their* messages; I did not read their source. It does not need to be true for this plan — the plan
  stands on what `Radio.Web` does.

### 7.4 Type / API consistency

`MapFallback(string, Delegate)` exists (`FallbackEndpointRouteBuilderExtensions`, .NET 7+) and stamps
`Order = int.MaxValue`. `Results.Text(string?, string?, Encoding?, int?)` exists. `Router.NotFound` is
an optional parameter. `ApiNotFound` is `internal` in `Radio.Web.Configuration`, matching
`StaticAssetCaching` and reachable from `Program.cs` via the existing `using` at line 6.
`{**rest}` matches zero-or-more segments, so `/api` and `/api/` are both caught, and routing is
case-insensitive, so `/API/…` is too — both asserted in Task 2.

---

## 8. Queue row wording

Replace the `UI-11` row's plan cell in `docs/BUILDER_QUEUE.md`:

> `plan` [`UI-11-the-404-that-was-already-a-404.md`](../design/plans/UI-11-the-404-that-was-already-a-404.md)
> · ⚠⚠ **THE ROW'S PREMISE IS FALSE — read §0.1 before claiming.** `Radio.Web` has **no SPA fallback**
> and never has (`git log -S MapFallback -- src/` is empty; there is no `index.html` in the project).
> An unmatched `/api/*` path **already returns 404** — measured live: `Content-Length: 0`, no
> `Content-Type`. Both cited incidents emitted from **RotaryPhone's `:5004`**, not from us — our own
> [archive line 99](BUILDER_QUEUE_ARCHIVE.md) says *"falling through to **their** SPA fallback"*, and
> their message says *"in our own house"*. · **Re-scoped:** the defect is the **missing contract**, not
> a wrong status code — a bodyless 404 on a two-service box is what cost RotaryPhone a probe.
> **§1 is an owner decision before Task 1** (tests-only vs. tests + JSON problem body). ·
> ⛔ **Do not add `UseStatusCodePagesWithReExecute`** — the .NET 10 template ships it and it would give
> `/api/*` an HTML body, which *is* most of the reported bug (§6.1). · **`Radio.API` checked: unaffected,
> and already locked** at `ApiTests.cs:32-43`. Both services checked; there are exactly two. ·
> auto-mergeable on green gates **once §1 is answered**

---

## Planned — 2026-09-08

Planner session. No source touched, no git write, no deploy. One file added: this plan.
