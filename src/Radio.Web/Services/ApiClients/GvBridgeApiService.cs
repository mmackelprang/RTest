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

  public async Task<SmsThreadMessagesDto?> GetSmsThreadMessagesAsync(
    string threadId, int count = 50, CancellationToken ct = default)
  {
    try
    {
      return await _httpClient.GetFromJsonAsync<SmsThreadMessagesDto>(
        $"/api/gvbridge/sms/threads/{Uri.EscapeDataString(threadId)}?count={count}",
        JsonOptions, ct);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get GV SMS thread {ThreadId}", threadId);
      return null;
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
