using Microsoft.Extensions.Diagnostics.HealthChecks;
using Radio.Core.Interfaces.Audio;

namespace Radio.API.Health;

/// <summary>
/// Health check that reports the status of the Bluetooth audio pipeline.
/// </summary>
public class BluetoothHealthCheck : IHealthCheck
{
  private readonly IBluetoothService _bluetoothService;

  public BluetoothHealthCheck(IBluetoothService bluetoothService)
  {
    _bluetoothService = bluetoothService;
  }

  public Task<HealthCheckResult> CheckHealthAsync(
    HealthCheckContext context,
    CancellationToken cancellationToken = default)
  {
    var status = _bluetoothService.PipelineStatus;
    var connected = _bluetoothService.ConnectedDevice;

    var data = new Dictionary<string, object>
    {
      ["pipelineStatus"] = status.ToString(),
      ["connectedDevice"] = connected != null
        ? $"{connected.Name} ({connected.Address})"
        : "none",
    };

    var result = status switch
    {
      BluetoothPipelineStatus.Healthy => HealthCheckResult.Healthy(
        "Bluetooth pipeline active", data),
      BluetoothPipelineStatus.Inactive => HealthCheckResult.Healthy(
        "Bluetooth is disabled", data),
      BluetoothPipelineStatus.Degraded => HealthCheckResult.Degraded(
        "No Bluetooth device connected", null, data),
      BluetoothPipelineStatus.Broken => HealthCheckResult.Unhealthy(
        "Device connected but capture stream missing", null, data),
      // AUD-11: without this arm the discard below would report a routine handset pause as
      // Unhealthy "Unknown pipeline state".
      BluetoothPipelineStatus.WaitingForCaptureNode => HealthCheckResult.Degraded(
        "Bluetooth device connected but its PipeWire capture node is gone; capture is parked and "
        + "will re-bind when the node reappears (AUD-10/AUD-11)", null, data),
      _ => HealthCheckResult.Unhealthy("Unknown pipeline state", null, data)
    };

    return Task.FromResult(result);
  }
}
