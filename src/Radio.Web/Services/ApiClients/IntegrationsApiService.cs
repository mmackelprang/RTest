using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Radio.Web.Models;

namespace Radio.Web.Services.ApiClients;

/// <summary>
/// API client service for integration status endpoints (encoders, phone, notifications).
/// </summary>
public class IntegrationsApiService
{
  private readonly HttpClient _httpClient;
  private readonly ILogger<IntegrationsApiService> _logger;
  /// <summary>
  /// Deserialization options for every call in this client.
  ///
  /// <para>
  /// ⚠ <b>The enum converter is required, not defensive.</b> Radio.API registers a
  /// <see cref="System.Text.Json.Serialization.JsonStringEnumConverter"/> in its MVC options
  /// (<c>Program.cs</c>), so every enum crosses the wire as a <b>string</b> — <c>"NeverSaved"</c>,
  /// not <c>0</c>. Without the matching converter here, <c>System.Text.Json</c> throws on the first
  /// enum property, the call returns null, and the page renders a loading spinner for ever with the
  /// only evidence in the Web service's log. That is exactly how ENC-8 shipped to the appliance
  /// before UAT caught it: "The JSON value could not be converted to EncoderFlashStateDto. Path:
  /// $.flash". The hermetic test rig fails every request, so null is the expected result there and
  /// no unit test could see the difference.
  /// </para>
  /// </summary>
  internal static readonly JsonSerializerOptions JsonOptions = new()
  {
    PropertyNameCaseInsensitive = true,
    Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
  };

  public IntegrationsApiService(HttpClient httpClient, ILogger<IntegrationsApiService> logger)
  {
    _httpClient = httpClient;
    _logger = logger;
  }

  public async Task<EncoderStatusDto?> GetEncoderStatusAsync(CancellationToken cancellationToken = default)
  {
    try
    {
      return await _httpClient.GetFromJsonAsync<EncoderStatusDto>(
        "/api/integrations/encoder/status", JsonOptions, cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get encoder status");
      return null;
    }
  }

  public async Task<EncoderProvisioningDto?> GetEncoderProvisioningAsync(CancellationToken cancellationToken = default)
  {
    try
    {
      return await _httpClient.GetFromJsonAsync<EncoderProvisioningDto>(
        "/api/integrations/encoder/provisioning", JsonOptions, cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get encoder provisioning state");
      return null;
    }
  }

  public async Task<List<EncoderMappingDto>?> GetEncoderMappingAsync(CancellationToken cancellationToken = default)
  {
    try
    {
      return await _httpClient.GetFromJsonAsync<List<EncoderMappingDto>>(
        "/api/integrations/encoder/mapping", JsonOptions, cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get encoder mapping");
      return null;
    }
  }

  /// <summary>
  /// Runs one provisioning command. Returns the resulting snapshot, or null when the request failed.
  ///
  /// <para>
  /// ⚠ Null means "we do not know the new state", not "nothing changed" — the caller must re-read
  /// rather than assume, and must not report success. A 409 (device not connected) lands here too.
  /// </para>
  /// </summary>
  private async Task<EncoderProvisioningDto?> PostProvisioningAsync(string path, CancellationToken cancellationToken)
  {
    try
    {
      HttpResponseMessage response = await _httpClient.PostAsync(path, content: null, cancellationToken);
      if (!response.IsSuccessStatusCode)
      {
        _logger.LogWarning("Encoder provisioning call {Path} returned {Status}", path, (int)response.StatusCode);
        return null;
      }

      return await response.Content.ReadFromJsonAsync<EncoderProvisioningDto>(JsonOptions, cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Encoder provisioning call {Path} failed", path);
      return null;
    }
  }

  public Task<EncoderProvisioningDto?> ReapplyEncoderConfigAsync(CancellationToken cancellationToken = default) =>
    PostProvisioningAsync("/api/integrations/encoder/reapply", cancellationToken);

  public Task<EncoderProvisioningDto?> SaveEncoderConfigToDeviceAsync(CancellationToken cancellationToken = default) =>
    PostProvisioningAsync("/api/integrations/encoder/save", cancellationToken);

  public async Task<bool> ResetEncoderCountersAsync(CancellationToken cancellationToken = default)
  {
    try
    {
      HttpResponseMessage response = await _httpClient.PostAsync(
        "/api/integrations/encoder/reset-counters", content: null, cancellationToken);
      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Encoder counter reset failed");
      return false;
    }
  }

  public async Task<EncoderProvisioningDto?> SetEncoderReverseAsync(
    int encoderIndex, bool reverse, CancellationToken cancellationToken = default)
  {
    try
    {
      HttpResponseMessage response = await _httpClient.PutAsJsonAsync(
        $"/api/integrations/encoder/reverse/{encoderIndex}", new { Reverse = reverse }, cancellationToken);
      if (!response.IsSuccessStatusCode)
      {
        _logger.LogWarning("Setting encoder {Index} direction returned {Status}", encoderIndex, (int)response.StatusCode);
        return null;
      }

      return await response.Content.ReadFromJsonAsync<EncoderProvisioningDto>(JsonOptions, cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Setting encoder {Index} direction failed", encoderIndex);
      return null;
    }
  }

  public async Task<PhoneIntegrationStatusDto?> GetPhoneStatusAsync(CancellationToken cancellationToken = default)
  {
    try
    {
      return await _httpClient.GetFromJsonAsync<PhoneIntegrationStatusDto>(
        "/api/integrations/phone/status", JsonOptions, cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get phone integration status");
      return null;
    }
  }

  public async Task<bool> SendTestNotificationAsync(string message, int priority, CancellationToken cancellationToken = default)
  {
    try
    {
      var payload = new { Message = message, Priority = priority };
      var content = new StringContent(
        JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
      var response = await _httpClient.PostAsync("/api/notifications/announce", content, cancellationToken);
      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to send test notification");
      return false;
    }
  }
}
