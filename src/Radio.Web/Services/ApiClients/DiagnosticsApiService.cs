using System.Net.Http.Json;
using System.Text.Json;
using Radio.Web.Models;

namespace Radio.Web.Services.ApiClients;

/// <summary>
/// HTTP client for RotaryPhone.API diagnostics endpoints (<c>/api/diagnostics/*</c>).
/// Read-only consumption per the BT/audio boundary (RotaryPhone is consume-only from
/// Radio.Web) — surfaces the GV audio-bridge stats, the SIP message log, and the call
/// timeline for <c>PhoneDiagnosticsPanel</c> so an operator can watch a call's media
/// flow live while debugging "no call audio".
/// Failures are logged at Debug (this is polled ~2s while the Diagnostics tab is open;
/// the shared <c>ApiConnectionLoggingHandler</c> already throttles connection-refused).
/// </summary>
public class DiagnosticsApiService
{
  private readonly HttpClient _httpClient;
  private readonly ILogger<DiagnosticsApiService> _logger;
  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    PropertyNameCaseInsensitive = true
  };

  public DiagnosticsApiService(HttpClient httpClient, ILogger<DiagnosticsApiService> logger)
  {
    _httpClient = httpClient;
    _logger = logger;
  }

  /// <summary>Live GV audio-bridge stats. <c>null</c> when the API is unreachable.</summary>
  public async Task<AudioBridgeStatsDto?> GetAudioBridgeAsync(CancellationToken ct = default)
  {
    try
    {
      return await _httpClient.GetFromJsonAsync<AudioBridgeStatsDto>(
        "/api/diagnostics/audio-bridge", JsonOptions, ct);
    }
    catch (Exception ex)
    {
      _logger.LogDebug(ex, "Failed to get audio-bridge diagnostics");
      return null;
    }
  }

  /// <summary>Recent SIP messages (newest last). Empty list on failure.</summary>
  public async Task<List<SipMessageDto>> GetSipLogAsync(int count = 30, CancellationToken ct = default)
  {
    try
    {
      return await _httpClient.GetFromJsonAsync<List<SipMessageDto>>(
        $"/api/diagnostics/sip-log?count={count}", JsonOptions, ct) ?? [];
    }
    catch (Exception ex)
    {
      _logger.LogDebug(ex, "Failed to get SIP log");
      return [];
    }
  }

  /// <summary>Recent call-timeline events (newest last). Empty list on failure.</summary>
  public async Task<List<CallTimelineDto>> GetTimelineAsync(int count = 30, CancellationToken ct = default)
  {
    try
    {
      return await _httpClient.GetFromJsonAsync<List<CallTimelineDto>>(
        $"/api/diagnostics/timeline?count={count}", JsonOptions, ct) ?? [];
    }
    catch (Exception ex)
    {
      _logger.LogDebug(ex, "Failed to get call timeline");
      return [];
    }
  }
}
