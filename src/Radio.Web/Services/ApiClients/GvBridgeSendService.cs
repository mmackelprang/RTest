using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Radio.Web.Models;
using Radio.Web.Services;

namespace Radio.Web.Services.ApiClients;

/// <summary>SMS send is not built on RotaryPhone yet — flag off → "coming soon".</summary>
public class SendNotAvailableException : Exception
{
  public SendNotAvailableException()
    : base("Texting send is coming soon.") { }
}

/// <summary>GV bridge degraded (reconnecting) — send is gated regardless of flag.</summary>
public class SendUnavailableException : Exception
{
  public SendUnavailableException()
    : base("Google Voice is reconnecting.") { }
}

/// <summary>HTTP 429 — caller keeps the text, shows "Sending too fast", no auto-retry.</summary>
public class SendRateLimitedException : Exception { }

/// <summary>A send is already outstanding on this thread (single-flight guard).</summary>
public class SendInFlightException : Exception { }

/// <summary>
/// Isolated, flagged GV SMS send seam (ADR-022 D7). Read client stays
/// unconditionally safe; this is the only write path. Guardrails: single-flight
/// per thread, 429 → typed exception + preserve text + NEVER auto-retry, and a
/// degraded gate (GvBridgeStatusService.IsAvailable). The endpoint
/// (POST /api/gvbridge/sms/send) does not exist yet — flag default false.
/// </summary>
public class GvBridgeSendService
{
  private readonly HttpClient _httpClient;
  private readonly ILogger<GvBridgeSendService> _logger;
  private readonly IConfiguration _configuration;
  private readonly GvBridgeStatusService _status;
  private readonly ConcurrentDictionary<string, byte> _inFlight = new();

  public GvBridgeSendService(HttpClient httpClient,
    ILogger<GvBridgeSendService> logger,
    IConfiguration configuration,
    GvBridgeStatusService status)
  {
    _httpClient = httpClient;
    _logger = logger;
    _configuration = configuration;
    _status = status;
  }

  public bool SendEnabled => _configuration.GetValue("RotaryPhone:Gv:SendEnabled", false);

  public async Task<SmsMessageDto> SendAsync(string threadId, string text,
    CancellationToken ct = default)
  {
    // Check order matters (ADR-022 D7 + handoff guardrails): flag → degraded →
    // single-flight, then the POST. Each gate throws a distinct typed exception
    // the compose UI catches to render the right calm message.
    if (!SendEnabled) throw new SendNotAvailableException();
    if (!_status.IsAvailable) throw new SendUnavailableException();
    if (!_inFlight.TryAdd(threadId, 1)) throw new SendInFlightException();

    try
    {
      // Wired when the endpoint ships. Request ≈ { threadId, text } → created
      // SmsMessageDto. Confirm SendSmsResponse shape first (contract risk #5).
      _logger.LogDebug("Sending GV SMS on thread {ThreadId}", threadId);
      var response = await _httpClient.PostAsJsonAsync(
        "/api/gvbridge/sms/send", new SendSmsRequest(threadId, text), ct);

      if (response.StatusCode == HttpStatusCode.TooManyRequests)
        throw new SendRateLimitedException();
      if (!response.IsSuccessStatusCode)
        throw new HttpRequestException($"Send failed: {(int)response.StatusCode}");

      var result = await response.Content
        .ReadFromJsonAsync<SendSmsResponse>(cancellationToken: ct);
      if (result?.Message == null)
        throw new HttpRequestException("Send returned no message");
      return result.Message;
    }
    finally
    {
      _inFlight.TryRemove(threadId, out _);
    }
  }
}
