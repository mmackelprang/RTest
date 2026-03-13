using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;

namespace Radio.Infrastructure.Platform.Bluetooth;

/// <summary>
/// Background service that listens on the BlueZ management socket for
/// MGMT_EV_DEVICE_DISCONNECTED events and stores the disconnect reason
/// per device address. LinuxBluetoothService reads the stored reason
/// when handling D-Bus Connected=false property changes.
///
/// Requires CAP_NET_ADMIN capability or root.
/// Linux-only — on other platforms, returns Unknown for all queries.
/// </summary>
internal sealed class BluetoothMgmtMonitor : BackgroundService
{
  private readonly ILogger<BluetoothMgmtMonitor> _logger;
  private readonly int _hciIndex;
  private readonly ConcurrentDictionary<string, BluetoothDisconnectReason> _lastReasons = new(StringComparer.OrdinalIgnoreCase);
  private int _socketFd = -1;

  // Linux socket constants
  private const int AF_BLUETOOTH = 31;
  private const int SOCK_RAW = 3;
  private const int BTPROTO_HCI = 1;
  private const ushort HCI_DEV_NONE = 0xFFFF;
  private const ushort HCI_CHANNEL_CONTROL = 3;

  // Mgmt protocol opcodes
  private const ushort MGMT_OP_READ_VERSION = 0x0001;

  public BluetoothMgmtMonitor(ILogger<BluetoothMgmtMonitor> logger, IOptions<BluetoothOptions> options)
  {
    _logger = logger;
    _hciIndex = ParseHciIndex(options.Value.AdapterName);
  }

  /// <summary>
  /// Parses the HCI device index from an adapter name (e.g., "hci0" → 0, "hci1" → 1).
  /// Returns -1 if the name is null/empty or doesn't match the expected format.
  /// </summary>
  internal static int ParseHciIndex(string? adapterName)
  {
    if (string.IsNullOrEmpty(adapterName) || !adapterName.StartsWith("hci"))
      return -1;
    return int.TryParse(adapterName.AsSpan(3), out var index) ? index : -1;
  }

  /// <summary>
  /// Gets and removes the last disconnect reason for a device address.
  /// Waits briefly for the mgmt event to arrive since the D-Bus property
  /// change may fire before our poll loop processes the kernel event.
  /// Returns Unknown if no reason was recorded within the wait period.
  /// </summary>
  public BluetoothDisconnectReason ConsumeDisconnectReason(string deviceAddress, int maxWaitMs = 300)
  {
    var deadline = Environment.TickCount64 + maxWaitMs;
    while (Environment.TickCount64 < deadline)
    {
      if (_lastReasons.TryRemove(deviceAddress, out var reason))
      {
        _logger.LogDebug("Consumed disconnect reason {Reason} for {Address}", reason, deviceAddress);
        return reason;
      }
      Thread.Sleep(25);
    }
    _logger.LogDebug("No mgmt disconnect reason arrived for {Address} within {MaxWaitMs}ms", deviceAddress, maxWaitMs);
    return BluetoothDisconnectReason.Unknown;
  }

  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
    {
      _logger.LogInformation("BluetoothMgmtMonitor: not Linux, skipping");
      return;
    }

    try
    {
      _socketFd = CreateMgmtSocket();
      if (_socketFd < 0)
      {
        _logger.LogWarning("Failed to open BlueZ mgmt socket — disconnect reasons unavailable. " +
          "Ensure CAP_NET_ADMIN capability is set (AmbientCapabilities=CAP_NET_ADMIN in systemd service)");
        return;
      }

      _logger.LogInformation("BlueZ mgmt socket opened — listening for disconnect events (filter: {Filter})",
        _hciIndex >= 0 ? $"hci{_hciIndex}" : "all adapters");
      await ReadLoopAsync(stoppingToken);
    }
    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
    {
      // Normal shutdown
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "BluetoothMgmtMonitor read loop failed");
    }
    finally
    {
      CloseMgmtSocket();
    }
  }

  private async Task ReadLoopAsync(CancellationToken ct)
  {
    var buffer = new byte[512];

    while (!ct.IsCancellationRequested)
    {
      int bytesRead;
      try
      {
        // Run blocking poll+read on thread pool to avoid blocking the hosted service
        bytesRead = await Task.Run(() =>
        {
          var pollFd = new PollFd { fd = _socketFd, events = PollEvents.POLLIN };
          int pollResult = poll(ref pollFd, (nuint)1, 100);
          if (pollResult <= 0 || (pollFd.revents & PollEvents.POLLIN) == 0)
          {
            return 0;
          }

          return (int)read(_socketFd, buffer, (nuint)buffer.Length);
        }, ct);
      }
      catch (OperationCanceledException)
      {
        break;
      }

      if (bytesRead <= 0)
      {
        continue;
      }

      // Log all received mgmt events for debugging
      var span = buffer.AsSpan(0, bytesRead);
      var opcode = bytesRead >= 2
        ? BitConverter.ToUInt16(buffer, 0)
        : 0;
      _logger.LogDebug("Mgmt event received: {Bytes} bytes, opcode=0x{Opcode:X4}", bytesRead, opcode);

      // Filter events by controller index when configured
      if (_hciIndex >= 0)
      {
        var eventIndex = BluetoothMgmtEventParser.GetControllerIndex(span);
        if (eventIndex >= 0 && eventIndex != _hciIndex)
        {
          _logger.LogDebug("Skipping mgmt event from hci{Index} (configured: hci{Configured})",
            eventIndex, _hciIndex);
          continue;
        }
      }

      if (BluetoothMgmtEventParser.TryParseDeviceDisconnected(
        span, out var address, out var reason))
      {
        _lastReasons[address] = reason;
        _logger.LogInformation("Mgmt disconnect event: {Address} reason={Reason}", address, reason);
      }
    }
  }

  private int CreateMgmtSocket()
  {
    var fd = socket(AF_BLUETOOTH, SOCK_RAW, BTPROTO_HCI);
    if (fd < 0)
    {
      _logger.LogWarning("socket(AF_BLUETOOTH) failed with errno {Errno}", Marshal.GetLastPInvokeError());
      return -1;
    }

    var addr = new SockAddrHci
    {
      hci_family = AF_BLUETOOTH,
      hci_dev = HCI_DEV_NONE,
      hci_channel = HCI_CHANNEL_CONTROL
    };

    if (bind(fd, ref addr, Marshal.SizeOf<SockAddrHci>()) < 0)
    {
      var errno = Marshal.GetLastPInvokeError();
      _logger.LogWarning("bind(HCI_CHANNEL_CONTROL) failed with errno {Errno}", errno);
      close(fd);
      return -1;
    }

    // Send MGMT_OP_READ_VERSION to activate event delivery.
    // The kernel requires this handshake before it starts sending events
    // like MGMT_EV_DEVICE_DISCONNECTED to the socket.
    if (!SendReadVersion(fd))
    {
      _logger.LogWarning("Failed to send MGMT_OP_READ_VERSION handshake");
      close(fd);
      return -1;
    }

    // Read and discard the version response so the socket is ready for events
    var responseBuf = new byte[512];
    var pollFd = new PollFd { fd = fd, events = PollEvents.POLLIN };
    if (poll(ref pollFd, (nuint)1, 2000) > 0)
    {
      var n = (int)read(fd, responseBuf, (nuint)responseBuf.Length);
      _logger.LogInformation("Mgmt READ_VERSION response: {Bytes} bytes", n);
    }
    else
    {
      _logger.LogWarning("No response to MGMT_OP_READ_VERSION within 2s");
    }

    return fd;
  }

  private bool SendReadVersion(int fd)
  {
    // mgmt_hdr: opcode (2 bytes LE) + index (2 bytes LE) + length (2 bytes LE)
    // READ_VERSION: opcode=0x0001, index=0xFFFF (MGMT_INDEX_NONE), length=0
    var cmd = new byte[] { 0x01, 0x00, 0xFF, 0xFF, 0x00, 0x00 };
    var written = write(fd, cmd, (nuint)cmd.Length);
    if (written != cmd.Length)
    {
      _logger.LogWarning("write(READ_VERSION) returned {Written}, errno={Errno}",
        written, Marshal.GetLastPInvokeError());
      return false;
    }
    return true;
  }

  private void CloseMgmtSocket()
  {
    var fd = Interlocked.Exchange(ref _socketFd, -1);
    if (fd >= 0)
    {
      close(fd);
      _logger.LogDebug("BlueZ mgmt socket closed");
    }
  }

  public override void Dispose()
  {
    CloseMgmtSocket();
    base.Dispose();
  }

  // P/Invoke declarations for Linux socket operations

  [StructLayout(LayoutKind.Sequential)]
  private struct SockAddrHci
  {
    public ushort hci_family;
    public ushort hci_dev;
    public ushort hci_channel;
  }

  [Flags]
  private enum PollEvents : short
  {
    POLLIN = 0x0001,
  }

  [StructLayout(LayoutKind.Sequential)]
  private struct PollFd
  {
    public int fd;
    public PollEvents events;
    public PollEvents revents;
  }

  [DllImport("libc", SetLastError = true)]
  private static extern int socket(int domain, int type, int protocol);

  [DllImport("libc", SetLastError = true)]
  private static extern int bind(int fd, ref SockAddrHci addr, int addrlen);

  [DllImport("libc", SetLastError = true)]
  private static extern nint read(int fd, byte[] buf, nuint count);

  [DllImport("libc", SetLastError = true)]
  private static extern nint write(int fd, byte[] buf, nuint count);

  [DllImport("libc", SetLastError = true)]
  private static extern int close(int fd);

  [DllImport("libc", SetLastError = true)]
  private static extern int poll(ref PollFd fds, nuint nfds, int timeout);
}
