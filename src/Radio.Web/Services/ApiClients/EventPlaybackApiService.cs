using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Radio.Web.Models;

namespace Radio.Web.Services.ApiClients;

/// <summary>
/// Attended event playback — the READ and STOP halves of /api/audio/events (ADR-029 D1, D6, D7).
/// </summary>
/// <remarks>
/// ⚠ Six methods: the four transport verbs plus the read and the stop. PHN-1e shipped only
/// GetCurrentAsync and StopAsync on the principle that a client method with no caller is a claim that
/// a surface exists; PR 6 is that surface, so the rest land here.
///
/// ⚠ Every method swallows and returns null / false rather than throwing. The callers are Blazor
/// event handlers on a wall panel: an exception out of one is an unhandled circuit error and a blank
/// screen, which is strictly worse than a button that appeared not to work. The server is the
/// authority regardless — the next broadcast corrects whatever the caller assumed.
///
/// ⚠ THAT CLAIM IS LOAD-BEARING ON WHERE THE PATH IS BUILT, and it was false until PHN-2's review.
/// SeekAsync / PauseAsync / ResumeAsync were expression-bodied and NOT async, so their
/// Uri.EscapeDataString(playbackId) ran in the CALLER's synchronous frame — before PostTransportAsync
/// was entered and therefore before any try. A null id threw straight into a Blazor handler.
/// StopAsync did the same call and did not have the bug, purely because it is async and the escape
/// sits inside its try. Every one of the four now builds its path INSIDE the try that catches. Do not
/// hoist a path expression back into an argument list.
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

  /// <summary>Starts a voicemail playback. Returns the accepted snapshot, or null with a reason.</summary>
  /// <remarks>
  /// ⚠ The 202 answers BEFORE any audio exists (ADR-029 §8.1). A non-null return therefore means
  /// "accepted", never "playing" — the snapshot's State is Preparing and the outcome arrives later on
  /// the hub. A caller treating this as success renders Playing over a fetch that is about to 404 in a
  /// blackout, which is the failure handoff §Cross-5 exists to prevent.
  ///
  /// ⚠ 409 is an expected answer rather than a fault — it is what the API returns when
  /// GvMedia:Enabled is false (EventPlaybackController.cs:96-104) — and it comes back as a reason
  /// string so the caller can say what happened instead of showing a generic failure. ⚠ It IS still
  /// logged: the LogWarning below fires for every non-success status, 409 included, and since LOG-11
  /// Warning is precisely the level that reaches journald on a box where log volume is audible. That
  /// is accepted here and not below in PostTransportAsync, because a start is one deliberate tap
  /// rather than a transport verb that can repeat, and a refused start with no line anywhere is a
  /// button that did nothing for a reason nobody can recover afterwards.
  ///
  /// ⚠ Voicemail only. The Speech arm has no caller until PHN-3, and this file's own history is the
  /// argument for not adding one before it does.
  /// </remarks>
  public async Task<(EventPlaybackSnapshotDto? Snapshot, string? Reason)> StartVoicemailAsync(
    string mediaId, int durationSeconds, string? label,
    CancellationToken cancellationToken = default)
  {
    try
    {
      // An anonymous body rather than a shared DTO: Radio.Web has no copy of EventPlaybackRequestDto
      // and must not grow one for five fields. The API's binder is case-insensitive and every field
      // on that DTO is nullable by design (EventPlaybackModels.cs:18-45), so an omitted field is a
      // well-defined null that Validate rejects by name rather than a model-binder 400.
      var body = new
      {
        kind = "RemoteMedia",
        mediaKind = "GvVoicemail",
        mediaId,
        durationSeconds,
        label
      };

      using var response =
        await _httpClient.PostAsJsonAsync("/api/audio/events", body, cancellationToken);

      if (response.IsSuccessStatusCode)
      {
        var snapshot = await response.Content.ReadFromJsonAsync<EventPlaybackSnapshotDto>(
          cancellationToken: cancellationToken);
        return (snapshot, null);
      }

      var reason = await ReadReasonAsync(response, cancellationToken);
      _logger.LogWarning(
        "Attended playback refused: {Status} {Reason}", (int)response.StatusCode, reason);
      return (null, reason);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to start attended playback");
      return (null, "Transport");
    }
  }

  /// <summary>Seeks. Returns the re-anchored snapshot, or null when it did not apply.</summary>
  public Task<EventPlaybackSnapshotDto?> SeekAsync(
    string playbackId, TimeSpan position, CancellationToken cancellationToken = default) =>
    PostTransportAsync(
      playbackId, "seek", new { positionSeconds = position.TotalSeconds }, cancellationToken);

  /// <summary>Pauses. Returns the re-anchored snapshot, or null when it did not apply.</summary>
  public Task<EventPlaybackSnapshotDto?> PauseAsync(
    string playbackId, CancellationToken cancellationToken = default) =>
    PostTransportAsync(playbackId, "pause", null, cancellationToken);

  /// <summary>Resumes. Returns the re-anchored snapshot, or null when it did not apply.</summary>
  public Task<EventPlaybackSnapshotDto?> ResumeAsync(
    string playbackId, CancellationToken cancellationToken = default) =>
    PostTransportAsync(playbackId, "resume", null, cancellationToken);

  /// <remarks>
  /// ⚠ A 404 or a 409 returns null and logs NOTHING. Both are ordinary: 404 is a playback that ended
  /// between the render and the tap, and 409 is a transport verb reaching a playback that has no
  /// source yet — which is exactly what Preparing and Waiting are (PHN-1f §0.2, S15). Neither is worth
  /// a line on a box where log volume is audible.
  ///
  /// ⚠ It takes the id and the VERB rather than a finished path, so that Uri.EscapeDataString runs
  /// inside this try. Passing an interpolated path in would move that call back into the caller's
  /// synchronous frame, which is the defect the class remark describes.
  /// </remarks>
  private async Task<EventPlaybackSnapshotDto?> PostTransportAsync(
    string playbackId, string verb, object? body, CancellationToken cancellationToken)
  {
    try
    {
      var path = $"/api/audio/events/{Uri.EscapeDataString(playbackId)}/{verb}";

      using var response = body is null
        ? await _httpClient.PostAsync(path, content: null, cancellationToken)
        : await _httpClient.PostAsJsonAsync(path, body, cancellationToken);

      return response.IsSuccessStatusCode
        ? await response.Content.ReadFromJsonAsync<EventPlaybackSnapshotDto>(
            cancellationToken: cancellationToken)
        : null;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Attended playback transport call failed");
      return null;
    }
  }

  private static async Task<string?> ReadReasonAsync(
    HttpResponseMessage response, CancellationToken cancellationToken)
  {
    try
    {
      using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
      using var document = await System.Text.Json.JsonDocument.ParseAsync(
        stream, cancellationToken: cancellationToken);
      return document.RootElement.TryGetProperty("reason", out var reason)
        ? reason.GetString()
        : null;
    }
    catch
    {
      // A body that is not the { error, reason } shape is not worth failing over. The caller's
      // fallback copy is honest for an unrecognised reason.
      return null;
    }
  }
}
