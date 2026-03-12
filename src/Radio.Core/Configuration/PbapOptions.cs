namespace Radio.Core.Configuration;

public class PbapOptions
{
  public const string SectionName = "Bluetooth:Pbap";

  public bool AutoSyncOnConnect { get; set; } = true;
  public int SyncStaleThresholdHours { get; set; } = 24;
  public int TransferTimeoutSeconds { get; set; } = 30;
}
