using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Radio.Web.Models;

namespace Radio.Web.Services.ApiClients;

/// <summary>
/// HTTP client for RotaryPhone.API GV Bridge endpoints (radio:5004).
/// Covers GV availability/status, call-adapter mode, and the GV Voicemail + SMS
/// read API. NOTE: this is Google Voice SMS (/api/gvbridge/sms/*), NOT the
/// VoIP.ms trunk SMS surface in GvTrunkApiService.
/// </summary>
public class GvBridgeApiService
{
  private readonly HttpClient _httpClient;
  private readonly ILogger<GvBridgeApiService> _logger;
  private readonly IConfiguration _configuration;
  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    PropertyNameCaseInsensitive = true
  };

  public GvBridgeApiService(HttpClient httpClient,
    ILogger<GvBridgeApiService> logger, IConfiguration configuration)
  {
    _httpClient = httpClient;
    _logger = logger;
    _configuration = configuration;
  }

  public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
  {
    try
    {
      var status = await _httpClient.GetFromJsonAsync<GvBridgeStatusDto>(
        "/api/gvbridge/status", JsonOptions, ct);
      return status != null;
    }
    catch
    {
      return false;
    }
  }

  public async Task<GvBridgeStatusDto?> GetStatusAsync(CancellationToken ct = default)
  {
    try
    {
      return await _httpClient.GetFromJsonAsync<GvBridgeStatusDto>(
        "/api/gvbridge/status", JsonOptions, ct);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get GV Bridge status");
      return null;
    }
  }

  public async Task<GvAdapterModeDto?> GetAdapterModeAsync(CancellationToken ct = default)
  {
    try
    {
      return await _httpClient.GetFromJsonAsync<GvAdapterModeDto>(
        "/api/gvbridge/adapter/mode", JsonOptions, ct);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get adapter mode");
      return null;
    }
  }

  public async Task<bool> SetAdapterModeAsync(string mode, CancellationToken ct = default)
  {
    try
    {
      var content = new StringContent(
        JsonSerializer.Serialize(new { mode }), Encoding.UTF8, "application/json");
      var response = await _httpClient.PutAsync("/api/gvbridge/adapter/mode", content, ct);
      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to set adapter mode to {Mode}", mode);
      return false;
    }
  }

  // ── GV Voicemail (read) ───────────────────────────────────────

  public async Task<VoicemailListDto?> GetVoicemailsAsync(
    int count = 20, string? pageToken = null, CancellationToken ct = default)
  {
    try
    {
      var url = $"/api/gvbridge/voicemail?count={count}";
      if (!string.IsNullOrEmpty(pageToken))
      {
        url += $"&pageToken={Uri.EscapeDataString(pageToken)}";
      }
      return await _httpClient.GetFromJsonAsync<VoicemailListDto>(url, JsonOptions, ct);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get GV voicemail list");
      return null;
    }
  }

  public async Task<VoicemailItemDto?> GetVoicemailAsync(
    string id, CancellationToken ct = default)
  {
    try
    {
      return await _httpClient.GetFromJsonAsync<VoicemailItemDto>(
        $"/api/gvbridge/voicemail/{Uri.EscapeDataString(id)}", JsonOptions, ct);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get GV voicemail {Id}", id);
      return null;
    }
  }

  /// <summary>
  /// Builds the ABSOLUTE radio:5004 URL for a voicemail recording, for binding to
  /// an &lt;audio src&gt;. The DTO's relative AudioUrl resolves against the Web
  /// origin (:5002) and 404s — ALWAYS rebuild absolute against the API base
  /// address (ADR-022 D4 / contract risk #3). Never bind the relative AudioUrl.
  /// </summary>
  public string GetVoicemailAudioUrl(string id)
  {
    var baseUri = _httpClient.BaseAddress
      ?? new Uri("http://radio:5004");
    return new Uri(baseUri, $"/api/gvbridge/voicemail/{Uri.EscapeDataString(id)}/audio")
      .ToString();
  }

  /// <summary>
  /// Mark a voicemail read via GV write-through (ADR-024 §3.1 / §5). Google is the
  /// single source of truth; the returned DTO is authoritative — reconcile the badge
  /// from it. Gated on RotaryPhone:Gv:MarkReadEnabled (in-tree consumer flag; distinct
  /// from RotaryPhone's server-side EnableMarkRead build flag). Flag off → silent no-op
  /// returning null (the caller has ALREADY flipped the row read optimistically; a no-op
  /// must never disturb that). 200 → DTO; 404 → null (item gone); 502/non-2xx → null but
  /// the caller KEEPS the optimistic flip and reconciles on the next list/poll/push.
  /// NEVER auto-retries — one attempt per user action (a UI-driven retry is the right place).
  /// v1 callers pass isRead: true only (§6 — unread may 400 unread_unsupported).
  /// </summary>
  public async Task<VoicemailItemDto?> MarkVoicemailReadAsync(
    string id, bool isRead = true, CancellationToken ct = default)
  {
    if (!_configuration.GetValue("RotaryPhone:Gv:MarkReadEnabled", false))
    {
      return null;  // UI-local optimistic flip already applied by the caller
    }
    try
    {
      // PostAsJsonAsync defaults to JsonSerializerDefaults.Web (camelCase), so the
      // anonymous property serializes to {"isRead":...} per the ADR-024 §3 contract.
      var response = await _httpClient.PostAsJsonAsync(
        $"/api/gvbridge/voicemail/{Uri.EscapeDataString(id)}/read",
        new { isRead }, ct);

      if (response.StatusCode == HttpStatusCode.NotFound)
      {
        return null;   // item gone
      }
      if (!response.IsSuccessStatusCode)
      {
        // 502 = GV unreachable. Keep the optimistic flip; reconcile later. No retry.
        _logger.LogError("Mark-read voicemail {Id} failed: {Status}", id, (int)response.StatusCode);
        return null;
      }
      return await response.Content.ReadFromJsonAsync<VoicemailItemDto>(JsonOptions, ct);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Mark-read voicemail {Id} threw (non-fatal); optimistic flip kept", id);
      return null;
    }
  }

  // ── GV SMS (read) ─────────────────────────────────────────────

  public async Task<SmsThreadListDto?> GetSmsThreadsAsync(
    int count = 20, CancellationToken ct = default)
  {
    try
    {
      return await _httpClient.GetFromJsonAsync<SmsThreadListDto>(
        $"/api/gvbridge/sms/threads?count={count}", JsonOptions, ct);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get GV SMS threads");
      return null;
    }
  }

  /// <summary>
  /// Fetch one conversation's message bodies. Returns an OUTCOME, never a bare null
  /// (GV-8 / UAT F-1): a 502, a timeout, a transport failure and an undeserializable
  /// body are each distinct from "the server returned zero messages," and the caller
  /// MUST NOT render any of them as an empty conversation.
  /// <para>
  /// A group thread whose id contains '/' currently returns a genuine HTTP 200 with an
  /// empty list — that is RotaryPhone's Defect B (thread ids arrive with a literal
  /// %2F and fail their exact string compare), and from our side it IS a successful
  /// empty result. Do NOT special-case it and do NOT change the escaping below:
  /// double-escaping (%252F) still yields 0 messages, and a raw '/' misses their API
  /// route entirely and falls through to their SPA fallback, returning index.html with
  /// HTTP 200. Both were tested. See docs/uat/2026-07-31-gv-live-data/F-1-DIAGNOSIS.md.
  /// </para>
  /// </summary>
  public async Task<GvResult<SmsThreadMessagesDto>> GetSmsThreadMessagesAsync(
    string threadId, int count = 50, CancellationToken ct = default)
  {
    var url = $"/api/gvbridge/sms/threads/{Uri.EscapeDataString(threadId)}?count={count}";
    try
    {
      // GetAsync, not GetFromJsonAsync: the latter calls EnsureSuccessStatusCode()
      // internally, so the status is already thrown away by the time we see the
      // exception — which is precisely how every non-2xx became an indistinguishable null.
      using var response = await _httpClient.GetAsync(url, ct);

      if (!response.IsSuccessStatusCode)
      {
        var errorCode = await ReadErrorCodeAsync(response, ct);
        // KEEP the literal "Failed to get GV SMS thread" substring: it is the documented
        // server-side probe for this surface (journalctl -u radio-web | grep ...). Blazor
        // Server fetches server-side over SignalR, so this log is the ONLY place the
        // failure is observable — it never reaches browser instrumentation.
        _logger.LogError(
          "Failed to get GV SMS thread {ThreadId}: HTTP {Status} {ErrorCode}",
          threadId, (int)response.StatusCode, errorCode ?? "-");
        return GvResult<SmsThreadMessagesDto>.HttpError(response.StatusCode, errorCode);
      }

      var dto = await response.Content
        .ReadFromJsonAsync<SmsThreadMessagesDto>(JsonOptions, ct);
      // Messages is declared non-nullable on SmsThreadMessagesDto, but
      // System.Text.Json does not enforce that on deserialize — a 2xx body that
      // omits "messages" (or an empty body) leaves it null. PhonePage's
      // `.Messages.ToList()` would then throw inside a Blazor event handler and tear
      // down the circuit (reconnect overlay instead of the error state), so treat
      // both as malformed here. `dto?.Messages is null` (rather than
      // `dto == null || dto.Messages == null`) avoids a nullable-analysis warning on
      // the non-nullable Messages property.
      if (dto?.Messages is null)
      {
        _logger.LogError(
          "Failed to get GV SMS thread {ThreadId}: 2xx with {Reason}", threadId,
          dto == null ? "an empty body" : "a missing messages array");
        return GvResult<SmsThreadMessagesDto>.Malformed();
      }
      return GvResult<SmsThreadMessagesDto>.Success(dto);
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
      throw;                    // the CALLER cancelled — not a failure to report as one
    }
    catch (OperationCanceledException ex)
    {
      _logger.LogError(ex, "Failed to get GV SMS thread {ThreadId}: timed out", threadId);
      return GvResult<SmsThreadMessagesDto>.Timeout();
    }
    catch (JsonException ex)
    {
      _logger.LogError(ex, "Failed to get GV SMS thread {ThreadId}: malformed body", threadId);
      return GvResult<SmsThreadMessagesDto>.Malformed();
    }
    catch (NotSupportedException ex)
    {
      // 2xx with a non-JSON content type (e.g. an SPA fallback's index.html).
      _logger.LogError(ex, "Failed to get GV SMS thread {ThreadId}: unsupported content type", threadId);
      return GvResult<SmsThreadMessagesDto>.Malformed();
    }
    catch (HttpRequestException ex)
    {
      _logger.LogError(ex, "Failed to get GV SMS thread {ThreadId}: transport failure", threadId);
      return GvResult<SmsThreadMessagesDto>.Transport();
    }
  }

  /// <summary>
  /// Best-effort read of RotaryPhone's error discriminator from a failure body
  /// (<c>{"error":"upstream_error"}</c> / <c>{"code":"send_disabled"}</c>). Returns null
  /// when the body is empty, is not a JSON object, or carries neither property. NEVER
  /// throws — this is a diagnostic, and a failed parse must not turn one failure into a
  /// different one. Property lookup is case-sensitive, so both casings are tried.
  /// GV-6 reuses this for <c>409 markread_disabled</c>.
  /// </summary>
  private static async Task<string?> ReadErrorCodeAsync(
    HttpResponseMessage response, CancellationToken ct)
  {
    try
    {
      var body = await response.Content.ReadAsStringAsync(ct);
      if (string.IsNullOrWhiteSpace(body))
      {
        return null;
      }
      using var doc = JsonDocument.Parse(body);
      if (doc.RootElement.ValueKind != JsonValueKind.Object)
      {
        return null;
      }
      foreach (var name in new[] { "error", "Error", "code", "Code" })
      {
        if (doc.RootElement.TryGetProperty(name, out var element)
            && element.ValueKind == JsonValueKind.String)
        {
          return element.GetString();
        }
      }
      return null;
    }
    catch
    {
      return null;              // non-JSON error body (e.g. an HTML fallback page)
    }
  }

  /// <summary>
  /// Mark a whole SMS thread read via GV write-through (ADR-024 §3.2 / §5). Per-thread
  /// grain → hasUnread=false. Same posture as MarkVoicemailReadAsync: flag-gated,
  /// 200→DTO, 404→null, 502/non-2xx→null (keep optimistic flip), no auto-retry.
  /// </summary>
  public async Task<SmsThreadDto?> MarkSmsThreadReadAsync(
    string threadId, bool isRead = true, CancellationToken ct = default)
  {
    if (!_configuration.GetValue("RotaryPhone:Gv:MarkReadEnabled", false))
    {
      return null;
    }
    try
    {
      // camelCase via PostAsJsonAsync default → {"isRead":...} (ADR-024 §3 contract).
      var response = await _httpClient.PostAsJsonAsync(
        $"/api/gvbridge/sms/threads/{Uri.EscapeDataString(threadId)}/read",
        new { isRead }, ct);

      if (response.StatusCode == HttpStatusCode.NotFound)
      {
        return null;
      }
      if (!response.IsSuccessStatusCode)
      {
        _logger.LogError("Mark-read thread {ThreadId} failed: {Status}", threadId, (int)response.StatusCode);
        return null;
      }
      return await response.Content.ReadFromJsonAsync<SmsThreadDto>(JsonOptions, ct);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Mark-read thread {ThreadId} threw (non-fatal); optimistic flip kept", threadId);
      return null;
    }
  }
}
