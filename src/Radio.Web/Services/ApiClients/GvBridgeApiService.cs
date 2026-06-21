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
  /// FLAGGED SEAM (decision 4). v1 read-state is UI-local; there is no GV
  /// mark-read endpoint yet. When RotaryPhone ships POST
  /// /api/gvbridge/voicemail/{id}/read, flip RotaryPhone:Gv:MarkReadEnabled=true
  /// and this becomes the wire call. Today it is a silent no-op returning false
  /// (not persisted) — the caller has ALREADY flipped the row heard locally, so a
  /// no-op must never disturb that. Fire-and-forget; never throws.
  /// </summary>
  public async Task<bool> MarkVoicemailReadAsync(string id, CancellationToken ct = default)
  {
    if (!_configuration.GetValue("RotaryPhone:Gv:MarkReadEnabled", false))
    {
      return false;  // UI-local only in v1
    }
    try
    {
      var response = await _httpClient.PostAsync(
        $"/api/gvbridge/voicemail/{Uri.EscapeDataString(id)}/read", null, ct);
      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      _logger.LogDebug(ex, "Mark-read failed for voicemail {Id} (non-fatal)", id);
      return false;
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
}
