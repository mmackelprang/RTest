using System.Diagnostics;
using System.Runtime.InteropServices;
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
  private readonly IOptionsMonitor<PbapOptions> _optionsMonitor;
  private readonly ILogger<PbapSyncService> _logger;
  private readonly SemaphoreSlim _syncLock = new(1, 1);

  private PbapOptions Options => _optionsMonitor.CurrentValue;

  public PbapSyncService(
    IBluetoothService bluetoothService,
    IPbapContactRepository contactRepo,
    IOptionsMonitor<PbapOptions> optionsMonitor,
    ILogger<PbapSyncService> logger)
  {
    _bluetoothService = bluetoothService;
    _contactRepo = contactRepo;
    _optionsMonitor = optionsMonitor;
    _logger = logger;
  }

  protected override Task ExecuteAsync(CancellationToken stoppingToken)
  {
    if (Options.AutoSyncOnConnect)
    {
      _bluetoothService.DeviceConnected += OnDeviceConnected;
    }
    _logger.LogInformation("PBAP sync service started (AutoSync={AutoSync})", Options.AutoSyncOnConnect);
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

      if (lastSynced == null || (DateTime.UtcNow - lastSynced.Value).TotalHours >= Options.SyncStaleThresholdHours)
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

    if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
    {
      _logger.LogWarning("PBAP sync requires Linux with BlueZ/obexd — skipped on {OS}", RuntimeInformation.OSDescription);
      return new PbapSyncResult { Success = false, ErrorMessage = "PBAP sync requires Linux" };
    }

    if (!await _syncLock.WaitAsync(TimeSpan.Zero, ct))
    {
      _logger.LogInformation("PBAP sync already in progress for another request — skipping");
      return new PbapSyncResult { Success = false, ErrorMessage = "Sync already in progress" };
    }

    string? tempFile = null;

    try
    {
      // Use a path outside /tmp — the service runs with PrivateTmp=true, but obexd
      // (separate user service) writes to the real filesystem. A /tmp path would be
      // invisible across the mount namespace boundary.
      var dataDir = Path.Combine(AppContext.BaseDirectory, "..", "data");
      Directory.CreateDirectory(dataDir);
      tempFile = Path.Combine(dataDir, $"pbap-sync-{deviceAddress.Replace(":", "")}-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}.vcf");

      // Ensure obexd is running (user service)
      await EnsureObexdRunningAsync(ct);

      // Run the Python helper script that manages the D-Bus OBEX session lifetime.
      // The script creates a PBAP session, selects the internal phonebook,
      // downloads all contacts via PullAll, monitors the transfer, and exits.
      await DownloadPhonebookAsync(deviceAddress, tempFile, ct);

      if (!File.Exists(tempFile) || new FileInfo(tempFile).Length == 0)
      {
        _logger.LogWarning("PBAP download produced empty file for {Address}", deviceAddress);
        return new PbapSyncResult { Success = true, ContactCount = 0 };
      }

      var result = await ProcessDownloadedVcfAsync(deviceAddress, tempFile, ct);

      // OBEX session teardown causes a LocalHost disconnect, which suppresses
      // auto-reconnect. Give BlueZ a moment to finish cleanup, then reconnect.
      _ = ReconnectAfterSyncAsync(deviceAddress);

      return result;
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "PBAP sync failed for {Address}", deviceAddress);
      return new PbapSyncResult { Success = false, ErrorMessage = ex.Message };
    }
    finally
    {
      if (tempFile != null && File.Exists(tempFile))
      {
        try { File.Delete(tempFile); }
        catch { /* best effort */ }
      }
      _syncLock.Release();
    }
  }

  private async Task ReconnectAfterSyncAsync(string deviceAddress)
  {
    try
    {
      // Wait for OBEX/BlueZ to finish tearing down the PBAP RFCOMM channel
      await Task.Delay(3000);
      _logger.LogInformation("Reconnecting to {Address} after PBAP sync", deviceAddress);
      await _bluetoothService.ConnectAsync(deviceAddress);
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to reconnect after PBAP sync for {Address}", deviceAddress);
    }
  }

  private async Task EnsureObexdRunningAsync(CancellationToken ct)
  {
    var psi = new ProcessStartInfo("systemctl", "--user is-active obex")
    {
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
      CreateNoWindow = true
    };

    using var proc = Process.Start(psi);
    if (proc == null) return;

    var output = await proc.StandardOutput.ReadToEndAsync(ct);
    await proc.WaitForExitAsync(ct);

    if (proc.ExitCode != 0 || !output.Trim().Equals("active", StringComparison.OrdinalIgnoreCase))
    {
      _logger.LogInformation("Starting obexd user service");
      var startPsi = new ProcessStartInfo("systemctl", "--user start obex")
      {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
      };

      using var startProc = Process.Start(startPsi);
      if (startProc != null)
      {
        await startProc.WaitForExitAsync(ct);
        if (startProc.ExitCode != 0)
        {
          _logger.LogWarning("Failed to start obexd (exit code {Code})", startProc.ExitCode);
        }
        // Give obexd a moment to register on D-Bus
        await Task.Delay(500, ct);
      }
    }
  }

  private async Task DownloadPhonebookAsync(string deviceAddress, string outputPath, CancellationToken ct)
  {
    // Locate the Python helper script — deployed to Bluetooth/ alongside the executable.
    // Use AppContext.BaseDirectory (works with single-file publish, unlike Assembly.Location).
    var scriptPath = Path.Combine(AppContext.BaseDirectory, "Bluetooth", "pbap_download.py");

    if (!File.Exists(scriptPath))
    {
      throw new FileNotFoundException($"PBAP download script not found at {scriptPath}");
    }

    var timeout = Options.TransferTimeoutSeconds;
    var psi = new ProcessStartInfo("python3", $"\"{scriptPath}\" \"{deviceAddress}\" \"{outputPath}\" {timeout}")
    {
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
      CreateNoWindow = true
    };

    // Ensure DBUS_SESSION_BUS_ADDRESS is set for the session bus
    if (!psi.Environment.ContainsKey("DBUS_SESSION_BUS_ADDRESS"))
    {
      var uid = Environment.GetEnvironmentVariable("UID")
        ?? (Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR")?.Split('/').LastOrDefault());
      var runtimeDir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") ?? $"/run/user/{uid ?? "1000"}";
      psi.Environment["DBUS_SESSION_BUS_ADDRESS"] = $"unix:path={runtimeDir}/bus";
    }

    _logger.LogDebug("Running PBAP download: python3 {Script} {Address} {Output} {Timeout}",
      scriptPath, deviceAddress, outputPath, timeout);

    using var proc = Process.Start(psi)
      ?? throw new InvalidOperationException("Failed to start PBAP download process");

    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeout + 10)); // script timeout + margin

    var stdout = await proc.StandardOutput.ReadToEndAsync(timeoutCts.Token);
    var stderr = await proc.StandardError.ReadToEndAsync(timeoutCts.Token);

    await proc.WaitForExitAsync(timeoutCts.Token);

    if (proc.ExitCode != 0)
    {
      var errorDetail = !string.IsNullOrWhiteSpace(stderr) ? stderr.Trim() : stdout.Trim();
      throw new InvalidOperationException($"PBAP download failed (exit {proc.ExitCode}): {errorDetail}");
    }

    _logger.LogInformation("PBAP download complete: {Result}", stdout.Trim());
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
          IsStale = s.LastSynced == null || (DateTime.UtcNow - s.LastSynced.Value).TotalHours >= Options.SyncStaleThresholdHours
        };
      }).ToList()
    };
  }
}
