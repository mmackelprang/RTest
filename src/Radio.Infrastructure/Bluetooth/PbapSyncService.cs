using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Interfaces.Bluetooth;

namespace Radio.Infrastructure.Bluetooth;

public class PbapSyncService : BackgroundService, IPbapSyncService
{
  private readonly IBluetoothService _bluetoothService;
  private readonly IPbapContactRepository _contactRepo;
  private readonly PbapOptions _options;
  private readonly ILogger<PbapSyncService> _logger;

  public PbapSyncService(
    IBluetoothService bluetoothService,
    IPbapContactRepository contactRepo,
    IOptions<PbapOptions> options,
    ILogger<PbapSyncService> logger)
  {
    _bluetoothService = bluetoothService;
    _contactRepo = contactRepo;
    _options = options.Value;
    _logger = logger;
  }

  protected override Task ExecuteAsync(CancellationToken stoppingToken)
  {
    if (_options.AutoSyncOnConnect)
    {
      _bluetoothService.DeviceConnected += OnDeviceConnected;
    }
    _logger.LogInformation("PBAP sync service started (AutoSync={AutoSync})", _options.AutoSyncOnConnect);
    return Task.CompletedTask;
  }

  public override Task StopAsync(CancellationToken cancellationToken)
  {
    _bluetoothService.DeviceConnected -= OnDeviceConnected;
    return base.StopAsync(cancellationToken);
  }

  private async void OnDeviceConnected(object? sender, BluetoothDeviceConnectedEventArgs e)
  {
    var address = e.Device.Address;
    _logger.LogInformation("Device connected: {Address} — checking PBAP sync status", address);

    try
    {
      var summary = await _contactRepo.GetSyncSummaryAsync(address);
      var lastSynced = summary.FirstOrDefault().LastSynced;

      if (lastSynced == null || (DateTime.UtcNow - lastSynced.Value).TotalHours >= _options.SyncStaleThresholdHours)
      {
        _logger.LogInformation("PBAP sync needed for {Address} (last sync: {LastSync})", address, lastSynced?.ToString() ?? "never");
        await SyncContactsAsync(address);
      }
      else
      {
        _logger.LogDebug("PBAP contacts for {Address} are fresh (synced {LastSync})", address, lastSynced);
      }
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Auto PBAP sync failed for {Address}", address);
    }
  }

  public async Task<PbapSyncResult> SyncContactsAsync(string deviceAddress, CancellationToken ct = default)
  {
    _logger.LogInformation("Starting PBAP sync for {Address}", deviceAddress);
    string? tempFile = null;

    try
    {
      // D-Bus OBEX session lifecycle:
      // 1. Connect to session bus
      // 2. CreateSession on org.bluez.obex.Client1 with { "Target": "PBAP" }
      // 3. Get PhonebookAccess1 interface
      // 4. Select("int", "pb")
      // 5. PullAll(tempFile, filters) → Transfer object
      // 6. Monitor Transfer1.Status via PropertiesChanged signal + TaskCompletionSource
      // 7. On "complete": read temp file

      tempFile = Path.Combine(Path.GetTempPath(), $"pbap-sync-{deviceAddress.Replace(":", "")}-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}.vcf");

      // D-Bus OBEX PBAP session not yet implemented — requires Linux + obexd.
      // The structure is ready; the actual D-Bus calls will be implemented
      // when tested on the target device with obexd running.
      throw new NotImplementedException("D-Bus OBEX PBAP session not yet implemented — requires Linux + obexd");
    }
    catch (NotImplementedException)
    {
      _logger.LogWarning("PBAP D-Bus implementation pending — sync skipped for {Address}", deviceAddress);
      return new PbapSyncResult { Success = false, ErrorMessage = "PBAP D-Bus not yet implemented" };
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "PBAP sync failed for {Address}", deviceAddress);
      return new PbapSyncResult { Success = false, ErrorMessage = ex.Message };
    }
    finally
    {
      // Clean up temp file
      if (tempFile != null && File.Exists(tempFile))
      {
        try { File.Delete(tempFile); }
        catch { /* best effort */ }
      }
    }
  }

  /// <summary>
  /// Called after successful temp file download. Parses and stores contacts.
  /// Extracted for testability — can be called directly in integration tests.
  /// </summary>
  internal async Task<PbapSyncResult> ProcessDownloadedVcfAsync(string deviceAddress, string vcfFilePath, CancellationToken ct = default)
  {
    var vcfContent = await File.ReadAllTextAsync(vcfFilePath, ct);
    var contacts = VCardParser.Parse(vcfContent);

    if (contacts.Count == 0)
    {
      _logger.LogWarning("No contacts parsed from PBAP download for {Address}", deviceAddress);
      return new PbapSyncResult { Success = true, ContactCount = 0 };
    }

    await _contactRepo.UpsertContactsAsync(deviceAddress, contacts, ct);

    _logger.LogInformation("PBAP sync complete for {Address}: {Count} contacts", deviceAddress, contacts.Count);
    return new PbapSyncResult { Success = true, ContactCount = contacts.Count };
  }

  public async Task<PbapSyncStatus> GetSyncStatusAsync(string? deviceAddress = null)
  {
    var summary = await _contactRepo.GetSyncSummaryAsync(deviceAddress);
    var pairedDevices = _bluetoothService.PairedDevices;

    return new PbapSyncStatus
    {
      Devices = summary.Select(s =>
      {
        var device = pairedDevices?.FirstOrDefault(d => d.Address == s.DeviceAddress);
        return new DeviceSyncInfo
        {
          DeviceAddress = s.DeviceAddress,
          DeviceName = device?.Name,
          ContactCount = s.ContactCount,
          LastSynced = s.LastSynced,
          IsStale = s.LastSynced == null || (DateTime.UtcNow - s.LastSynced.Value).TotalHours >= _options.SyncStaleThresholdHours
        };
      }).ToList()
    };
  }
}
