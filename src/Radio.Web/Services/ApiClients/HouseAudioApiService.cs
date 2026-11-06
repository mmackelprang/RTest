using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Radio.Web.Models;

namespace Radio.Web.Services.ApiClients;

/// <summary>
/// HTTP client for the RNET controller's routes on <c>HouseAudio:BaseUrl</c>. See
/// <c>openapi.yaml</c> in the <c>rnet-pi</c> repo.
/// </summary>
/// <remarks>
/// <para>
/// Calls return a <see cref="HouseAudioResult{T}"/> and do not throw for a failed call. Only the
/// caller's own cancellation surfaces, as an <see cref="OperationCanceledException"/>.
/// </para>
/// <para>
/// A success here means the controller accepted a command, not that the amp acted on it: its routes
/// answer without an acknowledgement from the amp.
/// </para>
/// <para>
/// ⚠ Failures log at Debug, by type only. The tab polls every few seconds, so a dead controller would
/// otherwise write to the journal on that schedule, and a request's URL names a room.
/// </para>
/// </remarks>
public class HouseAudioApiService
{
  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    PropertyNameCaseInsensitive = true
  };

  private readonly HttpClient _httpClient;
  private readonly HouseAudioPasswordStore _passwords;
  private readonly ILogger<HouseAudioApiService> _logger;

  public HouseAudioApiService(
    HttpClient httpClient,
    HouseAudioPasswordStore passwords,
    ILogger<HouseAudioApiService> logger)
  {
    _httpClient = httpClient;
    _passwords = passwords;
    _logger = logger;
  }

  public Task<HouseAudioResult<HouseSystemInfo>> GetSystemAsync(CancellationToken ct = default) =>
    ReadAsync<HouseSystemInfo>("/system", ct);

  /// <summary>
  /// The controller's zones, less any it has no name for: it reports those as null, and every zone
  /// route and <see cref="HouseAudioPoller.HoldZone"/> go by name.
  /// </summary>
  public async Task<HouseAudioResult<IReadOnlyList<HouseZone>>> GetZonesAsync(CancellationToken ct = default)
  {
    HouseAudioResult<IReadOnlyList<HouseZone>> result = await ReadAsync<IReadOnlyList<HouseZone>>("/zones", ct);
    return result.Value is null
      ? result
      : result with { Value = result.Value.Where(zone => !string.IsNullOrEmpty(zone?.Name)).ToList() };
  }

  public Task<HouseAudioResult<IReadOnlyList<HouseSource>>> GetSourcesAsync(CancellationToken ct = default) =>
    ReadAsync<IReadOnlyList<HouseSource>>("/sources", ct);

  public Task<HouseAudioResult<bool>> DeleteZoneAsync(string zoneName, CancellationToken ct = default) =>
    WriteAsync(HttpMethod.Delete, ZonePath(zoneName), null, ct);

  public Task<HouseAudioResult<bool>> SetPowerAsync(string zoneName, bool on, CancellationToken ct = default) =>
    WriteAsync(HttpMethod.Put, ZonePath(zoneName) + (on ? "/on" : "/off"), null, ct);

  public Task<HouseAudioResult<bool>> SetMuteAsync(string zoneName, bool muted, CancellationToken ct = default) =>
    WriteAsync(HttpMethod.Put, ZonePath(zoneName) + (muted ? "/mute" : "/unmute"), null, ct);

  public Task<HouseAudioResult<bool>> SetVolumeAsync(string zoneName, int volume, CancellationToken ct = default) =>
    WriteAsync(HttpMethod.Put, $"{ZonePath(zoneName)}/volume/{volume}", null, ct);

  public Task<HouseAudioResult<bool>> SetSourceAsync(string zoneName, int sourceId, CancellationToken ct = default) =>
    WriteAsync(HttpMethod.Put, $"{ZonePath(zoneName)}/source/{sourceId}", null, ct);

  /// <summary>
  /// Sets a zone parameter: 0 Bass, 1 Treble, 2 Loudness, 3 Balance, 4 Turn On Volume, 6 Do Not
  /// Disturb, 7 Party Mode. The controller reads 1 and 0 as true and false for Loudness and Do Not Disturb.
  /// </summary>
  public Task<HouseAudioResult<bool>> SetParameterAsync(
    string zoneName, int paramId, int value, CancellationToken ct = default) =>
    WriteAsync(HttpMethod.Put, $"{ZonePath(zoneName)}/parameter/{paramId}/{value}", null, ct);

  public Task<HouseAudioResult<bool>> SetMaxVolumeAsync(string zoneName, int volume, CancellationToken ct = default) =>
    WriteAsync(HttpMethod.Put, $"{ZonePath(zoneName)}/maxvolume/{volume}", null, ct);

  public Task<HouseAudioResult<bool>> SetAllPowerAsync(bool on, CancellationToken ct = default) =>
    WriteAsync(HttpMethod.Put, on ? "/on" : "/off", null, ct);

  public Task<HouseAudioResult<bool>> SetAllMuteAsync(bool muted, CancellationToken ct = default) =>
    WriteAsync(HttpMethod.Put, muted ? "/mute" : "/unmute", null, ct);

  public Task<HouseAudioResult<bool>> RenameControllerAsync(string name, CancellationToken ct = default) =>
    WriteAsync(HttpMethod.Put, "/system/name", new { name }, ct);

  /// <summary>Adds a source on an empty input. A refusal is 409, input occupied, or 400, bad value.</summary>
  public Task<HouseAudioResult<bool>> AddSourceAsync(
    int sourceId, string name, int type, CancellationToken ct = default) =>
    // "sourceID" is spelled as the controller reads it, case-sensitively.
    WriteAsync(HttpMethod.Post, "/sources", new { sourceID = sourceId, name, type }, ct);

  public Task<HouseAudioResult<bool>> UpdateSourceAsync(
    int sourceId, string name, int type, CancellationToken ct = default) =>
    WriteAsync(HttpMethod.Put, $"/sources/{sourceId}", new { name, type }, ct);

  /// <summary>Deletes a source. A refusal is 409, a powered zone using it, or 404, no such source.</summary>
  public Task<HouseAudioResult<bool>> DeleteSourceAsync(int sourceId, CancellationToken ct = default) =>
    WriteAsync(HttpMethod.Delete, $"/sources/{sourceId}", null, ct);

  private static string ZonePath(string zoneName) => "/" + Uri.EscapeDataString(zoneName);

  private async Task<HouseAudioResult<T>> ReadAsync<T>(string path, CancellationToken ct)
  {
    if (string.IsNullOrEmpty(_passwords.Get()))
    {
      return new HouseAudioResult<T>(HouseAudioHealth.NoPassword, default);
    }

    try
    {
      using HttpResponseMessage response = await _httpClient.GetAsync(path, ct);
      if (!response.IsSuccessStatusCode)
      {
        return new HouseAudioResult<T>(HealthOfFailure(response.StatusCode), default);
      }

      T? value = await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct);
      return value is null
        ? new HouseAudioResult<T>(HouseAudioHealth.Unreachable, default)
        : new HouseAudioResult<T>(HouseAudioHealth.Ok, value);
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
      throw;
    }
    catch (Exception ex)
    {
      _logger.LogDebug("House Audio read failed: {ExceptionType}", ex.GetType().Name);
      return new HouseAudioResult<T>(HouseAudioHealth.Unreachable, default);
    }
  }

  private async Task<HouseAudioResult<bool>> WriteAsync(
    HttpMethod method, string path, object? body, CancellationToken ct)
  {
    if (string.IsNullOrEmpty(_passwords.Get()))
    {
      return new HouseAudioResult<bool>(HouseAudioHealth.NoPassword, false);
    }

    try
    {
      using HttpRequestMessage request = new(method, path);
      if (body is not null)
      {
        request.Content = JsonContent.Create(body);
      }

      using HttpResponseMessage response = await _httpClient.SendAsync(request, ct);
      if (response.IsSuccessStatusCode)
      {
        return new HouseAudioResult<bool>(HouseAudioHealth.Ok, true);
      }

      return new HouseAudioResult<bool>(HealthOfWriteFailure(response.StatusCode), false);
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
      throw;
    }
    catch (Exception ex)
    {
      _logger.LogDebug("House Audio write failed: {ExceptionType}", ex.GetType().Name);
      return new HouseAudioResult<bool>(HouseAudioHealth.Unreachable, false);
    }
  }

  private static HouseAudioHealth HealthOfFailure(HttpStatusCode status) => status switch
  {
    HttpStatusCode.Unauthorized => HouseAudioHealth.WrongPassword,
    HttpStatusCode.ServiceUnavailable => HouseAudioHealth.AmpDisconnected,
    _ => HouseAudioHealth.Unreachable
  };

  // The controller answered and refused the command, so its health is not in question.
  private static HouseAudioHealth HealthOfWriteFailure(HttpStatusCode status) => status switch
  {
    HttpStatusCode.BadRequest or HttpStatusCode.NotFound or HttpStatusCode.Conflict => HouseAudioHealth.Ok,
    _ => HealthOfFailure(status)
  };
}
