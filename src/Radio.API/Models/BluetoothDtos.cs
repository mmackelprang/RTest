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
}

public class BluetoothDeviceRequest
{
  public string DeviceAddress { get; set; } = string.Empty;
}

public class BluetoothStartRequest
{
  public string? DeviceName { get; set; }
}
