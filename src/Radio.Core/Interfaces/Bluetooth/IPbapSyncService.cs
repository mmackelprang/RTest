namespace Radio.Core.Interfaces.Bluetooth;

public interface IPbapSyncService
{
  Task<PbapSyncResult> SyncContactsAsync(string deviceAddress, CancellationToken ct = default);
  Task<PbapSyncStatus> GetSyncStatusAsync(string? deviceAddress = null);
}

public class PbapSyncResult
{
  public bool Success { get; set; }
  public int ContactCount { get; set; }
  public string? ErrorMessage { get; set; }
}

public class PbapSyncStatus
{
  public List<DeviceSyncInfo> Devices { get; set; } = new();
}

public class DeviceSyncInfo
{
  public string DeviceAddress { get; set; } = string.Empty;
  public string? DeviceName { get; set; }
  public int ContactCount { get; set; }
  public DateTime? LastSynced { get; set; }
  public bool IsStale { get; set; }
}
