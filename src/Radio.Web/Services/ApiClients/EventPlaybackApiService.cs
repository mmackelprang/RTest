using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Radio.Web.Models;

namespace Radio.Web.Services.ApiClients;

/// <summary>
/// Attended event playback — the READ and STOP halves of /api/audio/events (ADR-029 D1, D6, D7).
/// </summary>
/// <remarks>
/// ⚠ Two methods, deliberately. PHN-1e needs the re-attach read (ADR §8.1) and the stop the
/// last-circuit backstop dispatches (§7.3). Start, seek, pause and resume belong to PR 6, which is
/// the first row with a transport to drive them from — a client method with no caller is a claim
/// that a surface exists.
/// </remarks>
public class EventPlaybackApiService
{
  private readonly HttpClient _httpClient;
  private readonly ILogger<EventPlaybackApiService> _logger;

  public EventPlaybackApiService(HttpClient httpClient, ILogger<EventPlaybackApiService> logger)
  {
    _httpClient = httpClient;
    _logger = logger;
  }

  /// <summary>The one attended playback, or null when there is none to report.</summary>
  /// <remarks>
  /// ⚠ 204 is a real answer, not a failure: it means nothing has EVER been started since the API
  /// booted, and it is distinct from a 200 carrying a Completed snapshot, which means something ran
  /// and finished. Both reach a caller as "nothing live"; only a caller that wants to render a
  /// FINISHED playback needs the difference, and that caller (PR 6's chip) reads the snapshot rather
  /// than this method's null.
  /// </remarks>
  public async Task<EventPlaybackSnapshotDto?> GetCurrentAsync(
    CancellationToken cancellationToken = default)
  {
    try
    {
      using var response =
        await _httpClient.GetAsync("/api/audio/events/current", cancellationToken);

      if (response.StatusCode == HttpStatusCode.NoContent)
      {
        return null;
      }

      response.EnsureSuccessStatusCode();
      return await response.Content.ReadFromJsonAsync<EventPlaybackSnapshotDto>(
        cancellationToken: cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to read the current attended playback");
      return null;
    }
  }

  /// <summary>Stops the playback with this id. False when nothing was stopped, for any reason.</summary>
  /// <remarks>
  /// ⚠ A 404 or a 409 is NOT an error and is not logged as one. Both are ordinary answers to "stop
  /// this": the playback ended between the caller reading the id and this call landing, which on the
  /// last-circuit path — where the id can be minutes old — is the common case rather than the
  /// exceptional one. Only a transport failure is worth a line.
  /// </remarks>
  public async Task<bool> StopAsync(string playbackId, CancellationToken cancellationToken = default)
  {
    try
    {
      using var response = await _httpClient.DeleteAsync(
        $"/api/audio/events/{Uri.EscapeDataString(playbackId)}", cancellationToken);
      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to stop attended playback");
      return false;
    }
  }
}
