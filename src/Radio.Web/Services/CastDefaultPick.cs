using Microsoft.Extensions.Logging;
using Radio.Web.Models;
using Radio.Web.Services.ApiClients;

namespace Radio.Web.Services;

/// <summary>Result of picking Cast with a saved default device.</summary>
public enum CastDefaultPickOutcome
{
  /// <summary>The API reported the connect succeeded (2xx).</summary>
  Connected,

  /// <summary>The connect request failed for any reason — non-2xx, transport error or exception.</summary>
  Failed
}

/// <summary>
/// The console's one-tap Cast pick when a default Cast device is saved (AUD-85).
/// </summary>
/// <remarks>
/// <para>
/// Sends exactly one request: <c>POST /api/devices/cast/connect</c>. That endpoint connects,
/// starts, promotes Cast through the output gate and re-saves the default on success, so the
/// pick does not first call <c>POST /api/devices/output</c> — that call starts the API's own
/// fire-and-forget auto-connect, and two parties connecting at once is what produced the
/// <c>Cannot connect in state Connecting</c> 500 this row was filed for.
/// </para>
/// <para>
/// Never clears the saved default. A failed request is not evidence the device is gone (a busy
/// output answers 409, a transient fault 500), so the caller keeps the default and lets the user
/// pick from the dropdown instead.
/// </para>
/// </remarks>
public static class CastDefaultPick
{
  /// <summary>Connects to <paramref name="device"/>; reports whether the API accepted it.</summary>
  public static async Task<CastDefaultPickOutcome> ConnectAsync(
    DevicesApiService devicesApi,
    CastDeviceDto device,
    ILogger logger,
    CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(devicesApi);
    ArgumentNullException.ThrowIfNull(device);
    ArgumentNullException.ThrowIfNull(logger);

    try
    {
      bool connected = await devicesApi.ConnectToCastDeviceAsync(device, cancellationToken);
      if (connected)
      {
        return CastDefaultPickOutcome.Connected;
      }

      logger.LogWarning("Connect to the default Cast device failed; the saved default is kept");
      return CastDefaultPickOutcome.Failed;
    }
    catch (Exception ex)
    {
      // ConnectToCastDeviceAsync catches its own exceptions today; this guards the contract
      // that a failed pick is reported as Failed rather than thrown into the layout.
      logger.LogWarning(ex, "Connect to the default Cast device threw; the saved default is kept");
      return CastDefaultPickOutcome.Failed;
    }
  }
}
