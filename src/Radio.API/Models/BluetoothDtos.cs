using Radio.Core.Interfaces.Audio;

namespace Radio.API.Models;

public class BluetoothStatusDto
{
  public bool IsAvailable { get; set; }
  public string State { get; set; } = string.Empty;
  public bool IsDiscovering { get; set; }
  public BluetoothDeviceDto? ConnectedDevice { get; set; }
  public List<BluetoothDeviceDto> PairedDevices { get; set; } = [];
  public List<BluetoothDeviceDto> DiscoveredDevices { get; set; } = [];
  public bool IsReconnecting { get; set; }
  public string? LastDisconnectReason { get; set; }

  // A2DP codec observability (Plan C / FM-BT-6) — populated from
  // IBluetoothService.GetA2dpCodecInfoAsync on the active transport. Null when
  // no device is connected or the negotiated codec is not yet known.
  public string? CodecName { get; set; }
  public int? SampleRateHz { get; set; }
  public int? Bitpool { get; set; }
}

public class BluetoothDeviceRequest
{
  public string DeviceAddress { get; set; } = string.Empty;
}

public class BluetoothStartRequest
{
  public string? DeviceName { get; set; }
}
