# BT Disconnect Reason Detection Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Detect BlueZ disconnect reasons via the management socket so the reconnection loop can distinguish phone-user disconnects (suppress reconnect) from signal loss (allow reconnect).

**Architecture:** A new `BluetoothMgmtMonitor` singleton opens the BlueZ HCI management socket (`AF_BLUETOOTH`, `HCI_CHANNEL_CONTROL`) and listens for `MGMT_EV_DEVICE_DISCONNECTED` events in a background loop. It stores the last disconnect reason per device address. When `LinuxBluetoothService` handles a D-Bus `Connected=false` property change, it reads the stored reason to decide whether to start the reconnection loop.

**Tech Stack:** .NET P/Invoke for Linux socket syscalls, BlueZ mgmt protocol (kernel headers), existing `BluetoothReconnectionLoop`, Radzen Blazor UI

---

## Task 1: DisconnectReason Enum (Radio.Core)

**Files:**
- Create: `src/Radio.Core/Interfaces/Audio/BluetoothDisconnectReason.cs`
- Modify: `src/Radio.Core/Interfaces/Audio/IBluetoothService.cs:179-184`

**Step 1: Create the enum**

Create `src/Radio.Core/Interfaces/Audio/BluetoothDisconnectReason.cs`:

```csharp
namespace Radio.Core.Interfaces.Audio;

/// <summary>
/// Disconnect reason from BlueZ management protocol (MGMT_EV_DEVICE_DISCONNECTED).
/// Values match kernel mgmt.h MGMT_DEV_DISCONN_* constants.
/// </summary>
public enum BluetoothDisconnectReason : byte
{
  /// <summary>Unknown reason (default / fallback).</summary>
  Unknown = 0x00,

  /// <summary>Connection timed out (device went out of range).</summary>
  Timeout = 0x01,

  /// <summary>Disconnected by local host (our radio initiated).</summary>
  LocalHost = 0x02,

  /// <summary>Disconnected by remote device (phone user disconnected).</summary>
  Remote = 0x03,

  /// <summary>Authentication failure.</summary>
  AuthFailure = 0x04,

  /// <summary>Local host suspended.</summary>
  LocalHostSuspend = 0x05,
}
```

**Step 2: Add Reason to BluetoothDeviceDisconnectedEventArgs**

In `src/Radio.Core/Interfaces/Audio/IBluetoothService.cs`, replace lines 179-184:

```csharp
/// <summary>Device disconnected args.</summary>
public class BluetoothDeviceDisconnectedEventArgs : EventArgs
{
  public required BluetoothDeviceInfo Device { get; init; }
  /// <summary>True if disconnect was user-initiated (via DisconnectAsync).</summary>
  public bool UserInitiated { get; init; }
  /// <summary>Disconnect reason from BlueZ management protocol.</summary>
  public BluetoothDisconnectReason Reason { get; init; } = BluetoothDisconnectReason.Unknown;
}
```

**Step 3: Add helper method**

Add to the bottom of `BluetoothDisconnectReason.cs`:

```csharp
public static class BluetoothDisconnectReasonExtensions
{
  /// <summary>
  /// Whether this disconnect reason should suppress auto-reconnect.
  /// Remote, LocalHost, AuthFailure, and LocalHostSuspend all suppress.
  /// Only Timeout and Unknown allow reconnect.
  /// </summary>
  public static bool ShouldSuppressReconnect(this BluetoothDisconnectReason reason) =>
    reason is BluetoothDisconnectReason.Remote
      or BluetoothDisconnectReason.LocalHost
      or BluetoothDisconnectReason.AuthFailure
      or BluetoothDisconnectReason.LocalHostSuspend;
}
```

**Step 4: Build to verify**

Run: `dotnet build src/Radio.Core --configuration Release`
Expected: 0 warnings, 0 errors

**Step 5: Commit**

```bash
git add src/Radio.Core/Interfaces/Audio/BluetoothDisconnectReason.cs src/Radio.Core/Interfaces/Audio/IBluetoothService.cs
git commit -m "feat: add BluetoothDisconnectReason enum and enrich disconnect event args"
```

---

## Task 2: Unit Tests for DisconnectReason and MgmtEventParser

**Files:**
- Create: `tests/Radio.Infrastructure.Tests/Platform/Bluetooth/BluetoothMgmtEventParserTests.cs`
- Create: `tests/Radio.Core.Tests/Interfaces/BluetoothDisconnectReasonTests.cs`

**Step 1: Write tests for the ShouldSuppressReconnect helper**

Create `tests/Radio.Core.Tests/Interfaces/BluetoothDisconnectReasonTests.cs`:

```csharp
using Radio.Core.Interfaces.Audio;

namespace Radio.Core.Tests.Interfaces;

public class BluetoothDisconnectReasonTests
{
  [Theory]
  [InlineData(BluetoothDisconnectReason.Remote, true)]
  [InlineData(BluetoothDisconnectReason.LocalHost, true)]
  [InlineData(BluetoothDisconnectReason.AuthFailure, true)]
  [InlineData(BluetoothDisconnectReason.LocalHostSuspend, true)]
  [InlineData(BluetoothDisconnectReason.Timeout, false)]
  [InlineData(BluetoothDisconnectReason.Unknown, false)]
  public void ShouldSuppressReconnect_ReturnsExpected(BluetoothDisconnectReason reason, bool expected)
  {
    Assert.Equal(expected, reason.ShouldSuppressReconnect());
  }
}
```

**Step 2: Write tests for mgmt event parsing**

Create `tests/Radio.Infrastructure.Tests/Platform/Bluetooth/BluetoothMgmtEventParserTests.cs`:

```csharp
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Platform.Bluetooth;

namespace Radio.Infrastructure.Tests.Platform.Bluetooth;

public class BluetoothMgmtEventParserTests
{
  // MGMT event header: opcode(2) + index(2) + param_len(2)
  // Device disconnected payload: bdaddr(6) + addr_type(1) + reason(1)
  // Total: 6 + 8 = 14 bytes

  [Fact]
  public void TryParseDeviceDisconnected_ValidEvent_ReturnsTrue()
  {
    // MGMT_EV_DEVICE_DISCONNECTED = 0x000C, index=0x0000, len=8
    // bdaddr = D4:3A:2C:64:87:9E (little-endian: 9E 87 64 2C 3A D4)
    // addr_type = 0x00 (BR/EDR)
    // reason = 0x03 (Remote)
    byte[] data =
    {
      0x0C, 0x00,  // opcode: MGMT_EV_DEVICE_DISCONNECTED
      0x00, 0x00,  // index: hci0
      0x08, 0x00,  // param_len: 8
      0x9E, 0x87, 0x64, 0x2C, 0x3A, 0xD4,  // bdaddr (LE)
      0x00,        // addr_type: BR/EDR
      0x03         // reason: Remote
    };

    var result = BluetoothMgmtEventParser.TryParseDeviceDisconnected(
      data, out var address, out var reason);

    Assert.True(result);
    Assert.Equal("D4:3A:2C:64:87:9E", address);
    Assert.Equal(BluetoothDisconnectReason.Remote, reason);
  }

  [Fact]
  public void TryParseDeviceDisconnected_TimeoutReason_ParsesCorrectly()
  {
    byte[] data =
    {
      0x0C, 0x00, 0x00, 0x00, 0x08, 0x00,
      0xA7, 0xFB, 0xF5, 0x51, 0x20, 0x78,  // 78:20:51:F5:FB:A7
      0x00, 0x01  // reason: Timeout
    };

    var result = BluetoothMgmtEventParser.TryParseDeviceDisconnected(
      data, out var address, out var reason);

    Assert.True(result);
    Assert.Equal("78:20:51:F5:FB:A7", address);
    Assert.Equal(BluetoothDisconnectReason.Timeout, reason);
  }

  [Fact]
  public void TryParseDeviceDisconnected_WrongOpcode_ReturnsFalse()
  {
    byte[] data =
    {
      0x0D, 0x00,  // wrong opcode
      0x00, 0x00, 0x08, 0x00,
      0x9E, 0x87, 0x64, 0x2C, 0x3A, 0xD4,
      0x00, 0x03
    };

    var result = BluetoothMgmtEventParser.TryParseDeviceDisconnected(
      data, out _, out _);

    Assert.False(result);
  }

  [Fact]
  public void TryParseDeviceDisconnected_TooShort_ReturnsFalse()
  {
    byte[] data = { 0x0C, 0x00, 0x00, 0x00, 0x08, 0x00 }; // header only

    var result = BluetoothMgmtEventParser.TryParseDeviceDisconnected(
      data, out _, out _);

    Assert.False(result);
  }

  [Fact]
  public void TryParseDeviceDisconnected_UnknownReason_ParsesAsUnknown()
  {
    byte[] data =
    {
      0x0C, 0x00, 0x00, 0x00, 0x08, 0x00,
      0x9E, 0x87, 0x64, 0x2C, 0x3A, 0xD4,
      0x00, 0xFF  // unknown reason byte
    };

    var result = BluetoothMgmtEventParser.TryParseDeviceDisconnected(
      data, out _, out var reason);

    Assert.True(result);
    Assert.Equal((BluetoothDisconnectReason)0xFF, reason);
  }

  [Theory]
  [InlineData(new byte[] { 0x9E, 0x87, 0x64, 0x2C, 0x3A, 0xD4 }, "D4:3A:2C:64:87:9E")]
  [InlineData(new byte[] { 0xA7, 0xFB, 0xF5, 0x51, 0x20, 0x78 }, "78:20:51:F5:FB:A7")]
  [InlineData(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }, "00:00:00:00:00:00")]
  public void FormatBdAddr_FormatsCorrectly(byte[] bdaddr, string expected)
  {
    Assert.Equal(expected, BluetoothMgmtEventParser.FormatBdAddr(bdaddr));
  }
}
```

**Step 3: Run tests to verify they fail**

Run: `dotnet test tests/Radio.Core.Tests --filter "BluetoothDisconnectReasonTests" --configuration Release -v n`
Expected: PASS (enum and extension already exist from Task 1)

Run: `dotnet test tests/Radio.Infrastructure.Tests --filter "BluetoothMgmtEventParserTests" --configuration Release -v n`
Expected: FAIL — `BluetoothMgmtEventParser` class doesn't exist yet

**Step 4: Commit tests**

```bash
git add tests/Radio.Core.Tests/Interfaces/BluetoothDisconnectReasonTests.cs tests/Radio.Infrastructure.Tests/Platform/Bluetooth/BluetoothMgmtEventParserTests.cs
git commit -m "test: add disconnect reason and mgmt event parser tests (red)"
```

---

## Task 3: BluetoothMgmtEventParser (Pure Logic)

**Files:**
- Create: `src/Radio.Infrastructure/Platform/Bluetooth/BluetoothMgmtEventParser.cs`

**Step 1: Implement the parser**

Create `src/Radio.Infrastructure/Platform/Bluetooth/BluetoothMgmtEventParser.cs`:

```csharp
using System.Buffers.Binary;
using Radio.Core.Interfaces.Audio;

namespace Radio.Infrastructure.Platform.Bluetooth;

/// <summary>
/// Parses BlueZ management protocol events from raw byte buffers.
/// Pure static methods — no I/O, fully testable.
/// </summary>
internal static class BluetoothMgmtEventParser
{
  // BlueZ mgmt protocol constants
  internal const ushort MgmtEvDeviceDisconnected = 0x000C;
  internal const int MgmtHeaderSize = 6; // opcode(2) + index(2) + param_len(2)
  internal const int BdAddrSize = 6;
  internal const int AddrInfoSize = 7; // bdaddr(6) + addr_type(1)
  internal const int DisconnectedPayloadSize = 8; // addr_info(7) + reason(1)

  /// <summary>
  /// Attempts to parse a MGMT_EV_DEVICE_DISCONNECTED event from raw bytes.
  /// </summary>
  /// <param name="data">Raw bytes received from mgmt socket.</param>
  /// <param name="address">Parsed Bluetooth address (e.g., "D4:3A:2C:64:87:9E").</param>
  /// <param name="reason">Parsed disconnect reason.</param>
  /// <returns>True if the event was a device disconnected event and was parsed successfully.</returns>
  public static bool TryParseDeviceDisconnected(
    ReadOnlySpan<byte> data,
    out string address,
    out BluetoothDisconnectReason reason)
  {
    address = string.Empty;
    reason = BluetoothDisconnectReason.Unknown;

    if (data.Length < MgmtHeaderSize + DisconnectedPayloadSize)
      return false;

    var opcode = BinaryPrimitives.ReadUInt16LittleEndian(data);
    if (opcode != MgmtEvDeviceDisconnected)
      return false;

    // Skip index(2) and param_len(2) — we already validated minimum length
    var payload = data.Slice(MgmtHeaderSize);
    address = FormatBdAddr(payload.Slice(0, BdAddrSize));
    reason = (BluetoothDisconnectReason)payload[AddrInfoSize]; // byte after addr_info

    return true;
  }

  /// <summary>
  /// Formats a 6-byte BD_ADDR (little-endian from kernel) into "XX:XX:XX:XX:XX:XX" string.
  /// BlueZ stores addresses in reverse byte order (little-endian).
  /// </summary>
  public static string FormatBdAddr(ReadOnlySpan<byte> bdaddr)
  {
    if (bdaddr.Length < BdAddrSize)
      return "00:00:00:00:00:00";

    return $"{bdaddr[5]:X2}:{bdaddr[4]:X2}:{bdaddr[3]:X2}:{bdaddr[2]:X2}:{bdaddr[1]:X2}:{bdaddr[0]:X2}";
  }
}
```

**Step 2: Run tests to verify they pass**

Run: `dotnet test tests/Radio.Infrastructure.Tests --filter "BluetoothMgmtEventParserTests" --configuration Release -v n`
Expected: All 6 tests PASS

Run: `dotnet test tests/Radio.Core.Tests --filter "BluetoothDisconnectReasonTests" --configuration Release -v n`
Expected: All 6 tests PASS

**Step 3: Commit**

```bash
git add src/Radio.Infrastructure/Platform/Bluetooth/BluetoothMgmtEventParser.cs
git commit -m "feat: implement BluetoothMgmtEventParser for mgmt protocol events"
```

---

## Task 4: BluetoothMgmtMonitor (Socket Listener)

**Files:**
- Create: `src/Radio.Infrastructure/Platform/Bluetooth/BluetoothMgmtMonitor.cs`

**Step 1: Implement the monitor**

Create `src/Radio.Infrastructure/Platform/Bluetooth/BluetoothMgmtMonitor.cs`:

```csharp
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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
  private readonly ConcurrentDictionary<string, BluetoothDisconnectReason> _lastReasons = new(StringComparer.OrdinalIgnoreCase);
  private IntPtr _socketFd = new(-1);

  // Linux socket constants
  private const int AF_BLUETOOTH = 31;
  private const int SOCK_RAW = 3;
  private const int BTPROTO_HCI = 1;
  private const ushort HCI_DEV_NONE = 0xFFFF;
  private const ushort HCI_CHANNEL_CONTROL = 3;

  public BluetoothMgmtMonitor(ILogger<BluetoothMgmtMonitor> logger)
  {
    _logger = logger;
  }

  /// <summary>
  /// Gets and removes the last disconnect reason for a device address.
  /// Returns Unknown if no reason was recorded.
  /// </summary>
  public BluetoothDisconnectReason ConsumeDisconnectReason(string deviceAddress)
  {
    if (_lastReasons.TryRemove(deviceAddress, out var reason))
    {
      _logger.LogDebug("Consumed disconnect reason {Reason} for {Address}", reason, deviceAddress);
      return reason;
    }
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
      if (_socketFd == new IntPtr(-1))
      {
        _logger.LogWarning("Failed to open BlueZ mgmt socket — disconnect reasons unavailable. " +
          "Ensure CAP_NET_ADMIN capability is set (AmbientCapabilities=CAP_NET_ADMIN in systemd service)");
        return;
      }

      _logger.LogInformation("BlueZ mgmt socket opened — listening for disconnect events");
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
        bytesRead = await Task.Run(() =>
        {
          // Use poll with timeout so we can check cancellation
          var pollFd = new PollFd { fd = _socketFd.ToInt32(), events = PollEvents.POLLIN };
          int pollResult = poll(ref pollFd, 1, 1000); // 1s timeout
          if (pollResult <= 0 || (pollFd.revents & PollEvents.POLLIN) == 0)
            return 0;

          return read(_socketFd, buffer, buffer.Length);
        }, ct);
      }
      catch (OperationCanceledException)
      {
        break;
      }

      if (bytesRead <= 0)
        continue;

      if (BluetoothMgmtEventParser.TryParseDeviceDisconnected(
        buffer.AsSpan(0, bytesRead), out var address, out var reason))
      {
        _lastReasons[address] = reason;
        _logger.LogInformation("Mgmt disconnect event: {Address} reason={Reason}", address, reason);
      }
    }
  }

  private IntPtr CreateMgmtSocket()
  {
    var fd = socket(AF_BLUETOOTH, SOCK_RAW, BTPROTO_HCI);
    if (fd < 0)
    {
      _logger.LogWarning("socket(AF_BLUETOOTH) failed with errno {Errno}", Marshal.GetLastPInvokeError());
      return new IntPtr(-1);
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
      return new IntPtr(-1);
    }

    return new IntPtr(fd);
  }

  private void CloseMgmtSocket()
  {
    var fd = _socketFd;
    _socketFd = new IntPtr(-1);
    if (fd != new IntPtr(-1))
    {
      close(fd.ToInt32());
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
  private static extern int read(IntPtr fd, byte[] buf, int count);

  [DllImport("libc", SetLastError = true)]
  private static extern int close(int fd);

  [DllImport("libc", SetLastError = true)]
  private static extern int poll(ref PollFd fds, int nfds, int timeout);
}
```

**Step 2: Build to verify**

Run: `dotnet build src/Radio.Infrastructure --configuration Release`
Expected: 0 warnings, 0 errors

**Step 3: Commit**

```bash
git add src/Radio.Infrastructure/Platform/Bluetooth/BluetoothMgmtMonitor.cs
git commit -m "feat: add BluetoothMgmtMonitor background service for mgmt socket events"
```

---

## Task 5: DI Registration and Service File Update

**Files:**
- Modify: `src/Radio.Infrastructure/DependencyInjection/AudioServiceExtensions.cs`
- Modify: `deploy/common/radio-api.service`

**Step 1: Register BluetoothMgmtMonitor in DI**

In `src/Radio.Infrastructure/DependencyInjection/AudioServiceExtensions.cs`, find the Bluetooth service registration block (near the `services.Configure<BluetoothOptions>` line) and add the mgmt monitor registration just before the `IBluetoothService` factory:

```csharp
// Register BluetoothMgmtMonitor as singleton + hosted service
services.AddSingleton<BluetoothMgmtMonitor>();
services.AddHostedService(sp => sp.GetRequiredService<BluetoothMgmtMonitor>());
```

**Important:** Use the `AddSingleton + AddHostedService(factory)` pattern so `LinuxBluetoothService` can inject the same instance. See MEMORY.md "DI / Hosted Service Gotchas".

**Step 2: Update BluetoothServiceFactory to pass monitor**

In `src/Radio.Infrastructure/Platform/Bluetooth/BluetoothServiceFactory.cs`, update the `LinuxBluetoothService` constructor call to also pass `BluetoothMgmtMonitor`:

```csharp
var mgmtMonitor = sp.GetService<BluetoothMgmtMonitor>();
```

Pass it to `LinuxBluetoothService` constructor.

**Step 3: Add CAP_NET_ADMIN to systemd service**

In `deploy/common/radio-api.service`, add this line in the `[Service]` section, after `NoNewPrivileges=true` (line 55). **IMPORTANT:** `NoNewPrivileges=true` blocks `AmbientCapabilities`, so we must change it to `false` or remove it:

Replace:
```ini
NoNewPrivileges=true
```

With:
```ini
NoNewPrivileges=false
AmbientCapabilities=CAP_NET_ADMIN
```

**Step 4: Build to verify**

Run: `dotnet build src/Radio.Infrastructure --configuration Release`
Expected: 0 warnings, 0 errors

**Step 5: Commit**

```bash
git add src/Radio.Infrastructure/DependencyInjection/AudioServiceExtensions.cs src/Radio.Infrastructure/Platform/Bluetooth/BluetoothServiceFactory.cs deploy/common/radio-api.service
git commit -m "feat: register BluetoothMgmtMonitor in DI and add CAP_NET_ADMIN to service"
```

---

## Task 6: Integrate Monitor into LinuxBluetoothService

**Files:**
- Modify: `src/Radio.Infrastructure/Platform/Bluetooth/LinuxBluetoothService.cs:24-77` (constructor), `532-567` (disconnect handler)

**Step 1: Add monitor field and constructor parameter**

In `LinuxBluetoothService.cs`, add a new field after `_reconnectionLoop` (line 63):

```csharp
private readonly BluetoothMgmtMonitor? _mgmtMonitor;
```

Add `BluetoothMgmtMonitor? mgmtMonitor = null` to the constructor parameters (line 65-71) and assign:

```csharp
_mgmtMonitor = mgmtMonitor;
```

**Step 2: Use disconnect reason in the disconnect handler**

In `WatchDevicePropertiesAsync()` at lines 532-567, replace the disconnect handling block. The key change is reading the reason from the monitor and using it to decide reconnection:

Replace lines 532-567 with:

```csharp
var wasUserInitiated = _userInitiatedDisconnect;
_userInitiatedDisconnect = false;

// Read disconnect reason from mgmt monitor (arrives before D-Bus property change)
var mgmtReason = _mgmtMonitor?.ConsumeDisconnectReason(updatedDevice.Address)
  ?? BluetoothDisconnectReason.Unknown;

// If user initiated via our UI, override reason to LocalHost
if (wasUserInitiated)
  mgmtReason = BluetoothDisconnectReason.LocalHost;

RecordDisconnectionMetrics();
StopCaptureSubprocess();
// Clean up media transport on disconnect
_transportPropertiesWatcher?.Dispose();
_transportPropertiesWatcher = null;
_mediaTransport = null;
_mediaTransportPath = null;
DeviceVolume = null;

_logger.LogInformation(
  "Bluetooth device disconnected: {DeviceName} ({Address}) reason={Reason} (user-initiated: {UserInitiated})",
  updatedDevice.Name, updatedDevice.Address, mgmtReason, wasUserInitiated);

// Re-show adapter so other devices can discover and pair
_ = SetDiscoverableAsync(true);

DeviceDisconnected?.Invoke(this, new BluetoothDeviceDisconnectedEventArgs
{
  Device = updatedDevice,
  UserInitiated = wasUserInitiated,
  Reason = mgmtReason
});

// Start auto-reconnection only for reasons that suggest signal loss
var shouldReconnect = _options.AutoReconnect && !mgmtReason.ShouldSuppressReconnect();
if (shouldReconnect)
{
  _reconnectionLoop?.Dispose();
  _reconnectionLoop = new BluetoothReconnectionLoop(
    _logger, _options,
    (addr, ct) => ConnectAsync(addr, ct),
    () => ConnectedDevice != null,
    _metricsCollector);
  _reconnectionLoop.Start(updatedDevice.Address);
}
else if (!wasUserInitiated)
{
  _logger.LogInformation("Auto-reconnect suppressed: reason={Reason} for {Address}",
    mgmtReason, updatedDevice.Address);
}
```

**Step 3: Build to verify**

Run: `dotnet build src/Radio.Infrastructure --configuration Release`
Expected: 0 warnings, 0 errors

**Step 4: Commit**

```bash
git add src/Radio.Infrastructure/Platform/Bluetooth/LinuxBluetoothService.cs
git commit -m "feat: use mgmt disconnect reason to decide reconnection behavior"
```

---

## Task 7: API Reconnection Status Endpoint

**Files:**
- Modify: `src/Radio.API/Models/BluetoothDtos.cs`
- Modify: `src/Radio.API/Controllers/BluetoothController.cs`
- Modify: `src/Radio.Core/Interfaces/Audio/IBluetoothService.cs`

**Step 1: Add reconnection status to IBluetoothService**

In `src/Radio.Core/Interfaces/Audio/IBluetoothService.cs`, add after `DeviceVolume` property (line 129):

```csharp
/// <summary>Whether a reconnection loop is currently active.</summary>
bool IsReconnecting { get; }

/// <summary>Cancel any active reconnection loop.</summary>
void CancelReconnection();
```

**Step 2: Implement in LinuxBluetoothService**

In `LinuxBluetoothService.cs`, add the property and method:

```csharp
public bool IsReconnecting => _reconnectionLoop?.IsActive == true;

public void CancelReconnection()
{
  _reconnectionLoop?.Cancel();
  _logger.LogInformation("Reconnection loop cancelled by user");
}
```

**Step 3: Implement in NullBluetoothService and WindowsBluetoothService**

Add stub implementations to both:
```csharp
public bool IsReconnecting => false;
public void CancelReconnection() { }
```

**Step 4: Add reconnection fields to DTOs**

In `src/Radio.API/Models/BluetoothDtos.cs`, add to `BluetoothStatusDto`:

```csharp
public bool IsReconnecting { get; set; }
public string? LastDisconnectReason { get; set; }
```

In `src/Radio.Web/Models/ApiModels.cs`, add the same fields to the Web project's `BluetoothStatusDto` (around line 758).

**Step 5: Update BluetoothController.BuildStatus()**

In `BluetoothController.cs`, update `BuildStatus()` (line 168-181) to include the new fields:

```csharp
IsReconnecting = _bluetoothService.IsReconnecting,
```

**Step 6: Add cancel-reconnect endpoint**

In `BluetoothController.cs`, add after the disconnect endpoint:

```csharp
[HttpPost("cancel-reconnect")]
[ProducesResponseType(typeof(BluetoothStatusDto), StatusCodes.Status200OK)]
public ActionResult<BluetoothStatusDto> CancelReconnect()
{
  _bluetoothService.CancelReconnection();
  return Ok(BuildStatus());
}
```

**Step 7: Build to verify**

Run: `dotnet build --configuration Release`
Expected: 0 warnings, 0 errors

**Step 8: Commit**

```bash
git add src/Radio.Core/Interfaces/Audio/IBluetoothService.cs src/Radio.Infrastructure/Platform/Bluetooth/LinuxBluetoothService.cs src/Radio.API/Controllers/BluetoothController.cs src/Radio.API/Models/BluetoothDtos.cs src/Radio.Web/Models/ApiModels.cs
git commit -m "feat: add reconnection status endpoint and cancel-reconnect API"
```

**Note:** Also update `BluetoothServiceFactory.NullBluetoothService` and any Windows BT service with the new interface members.

---

## Task 8: BluetoothPage UI Updates

**Files:**
- Modify: `src/Radio.Web/Components/Pages/BluetoothPage.razor:76-112` (Connected Device Card)
- Modify: `src/Radio.Web/Services/ApiClients/BluetoothApiService.cs`

**Step 1: Add CancelReconnectAsync to BluetoothApiService**

In `src/Radio.Web/Services/ApiClients/BluetoothApiService.cs`, add:

```csharp
public async Task<bool> CancelReconnectAsync()
{
  try
  {
    var response = await _httpClient.PostAsync("api/bluetooth/cancel-reconnect", null);
    return response.IsSuccessStatusCode;
  }
  catch (Exception ex)
  {
    _logger.LogError(ex, "Error cancelling Bluetooth reconnection");
    return false;
  }
}
```

**Step 2: Update BluetoothPage Connected Device Card**

Replace the "Connected Device Card" section (lines 76-112) in `BluetoothPage.razor`. When no device is connected, show reconnection status if active:

```razor
@* Connected Device Card *@
<RadzenCard Style="padding:16px">
  <div style="display:flex; flex-direction:column; gap:8px">
    <h6>Connected Device</h6>

    @if (_status.ConnectedDevice != null)
    {
      <div style="display:flex; flex-direction:row; gap:12px; align-items:center">
        <RadzenIcon Icon="bluetooth_connected" Style="font-size:2rem; color:var(--rz-success)" />
        <div style="display:flex; flex-direction:column; gap:0">
          <span style="font-size:1rem; font-weight:600">@_status.ConnectedDevice.Name</span>
          <span style="font-size:0.75rem; color:var(--text-low)">@_status.ConnectedDevice.Address</span>
        </div>
        <div style="flex:1"></div>
        <RadzenButton Variant="Variant.Outlined" ButtonStyle="ButtonStyle.Danger" Size="ButtonSize.Small"
                      Click="DisconnectAsync" Icon="link_off"
                      Disabled="@_isDisconnecting">
          @if (_isDisconnecting)
          {
            <RadzenProgressBarCircular Size="ProgressBarCircularSize.Small" Mode="ProgressBarMode.Indeterminate" Style="margin-right:4px" />
            <span>Disconnecting</span>
          }
          else
          {
            <span>Disconnect</span>
          }
        </RadzenButton>
      </div>
    }
    else if (_status.IsReconnecting)
    {
      <div style="display:flex; flex-direction:row; gap:12px; align-items:center">
        <RadzenProgressBarCircular Size="ProgressBarCircularSize.Small" Mode="ProgressBarMode.Indeterminate" />
        <span style="font-size:0.875rem">Reconnecting to last device...</span>
        <div style="flex:1"></div>
        <RadzenButton Variant="Variant.Outlined" ButtonStyle="ButtonStyle.Warning" Size="ButtonSize.Small"
                      Click="CancelReconnectAsync" Icon="stop" Text="Stop" />
      </div>
    }
    else
    {
      <RadzenAlert AlertStyle="AlertStyle.Info" Size="AlertSize.Small" AllowClose="false">
        @if (!string.IsNullOrEmpty(_status.LastDisconnectReason))
        {
          @GetDisconnectMessage(_status.LastDisconnectReason)
        }
        else
        {
          <span>No device connected. Pair a device below, or connect a previously paired device.</span>
        }
      </RadzenAlert>
    }
  </div>
</RadzenCard>
```

**Step 3: Add CancelReconnectAsync and helper method to @code block**

In the `@code` section, add:

```csharp
private async Task CancelReconnectAsync()
{
  try
  {
    await BluetoothApi.CancelReconnectAsync();
    NotificationService.Notify(NotificationSeverity.Info, "Bluetooth", "Reconnection cancelled");
    await RefreshStatusAsync();
  }
  catch (Exception ex)
  {
    Logger.LogError(ex, "Error cancelling reconnection");
    NotificationService.Notify(NotificationSeverity.Error, "Error", "Failed to cancel reconnection");
  }
}

private static string GetDisconnectMessage(string reason) => reason switch
{
  "Remote" => "Device disconnected by user",
  "Timeout" => "Connection lost (device out of range)",
  "AuthFailure" => "Authentication failed",
  "LocalHost" => "Disconnected",
  _ => "No device connected. Pair a device below, or connect a previously paired device."
};
```

**Step 4: Build to verify**

Run: `dotnet build --configuration Release`
Expected: 0 warnings, 0 errors

**Step 5: Commit**

```bash
git add src/Radio.Web/Components/Pages/BluetoothPage.razor src/Radio.Web/Services/ApiClients/BluetoothApiService.cs
git commit -m "feat: show disconnect reason and reconnection status in Bluetooth UI"
```

---

## Task 9: Track LastDisconnectReason in LinuxBluetoothService

**Files:**
- Modify: `src/Radio.Infrastructure/Platform/Bluetooth/LinuxBluetoothService.cs`
- Modify: `src/Radio.Core/Interfaces/Audio/IBluetoothService.cs`
- Modify: `src/Radio.API/Controllers/BluetoothController.cs`

**Step 1: Add LastDisconnectReason to interface**

In `IBluetoothService.cs`, add after `CancelReconnection()`:

```csharp
/// <summary>Last disconnect reason for UI display. Null if no disconnect has occurred.</summary>
BluetoothDisconnectReason? LastDisconnectReason { get; }
```

**Step 2: Implement in LinuxBluetoothService**

Add field and property:
```csharp
private BluetoothDisconnectReason? _lastDisconnectReason;
public BluetoothDisconnectReason? LastDisconnectReason => _lastDisconnectReason;
```

Set it in the disconnect handler (after `mgmtReason` is determined):
```csharp
_lastDisconnectReason = mgmtReason;
```

Clear it on successful connection (in the `Connected=true` branch):
```csharp
_lastDisconnectReason = null;
```

**Step 3: Implement stubs in NullBluetoothService and WindowsBluetoothService**

```csharp
public BluetoothDisconnectReason? LastDisconnectReason => null;
```

**Step 4: Wire into BuildStatus()**

In `BluetoothController.BuildStatus()`:
```csharp
LastDisconnectReason = _bluetoothService.LastDisconnectReason?.ToString(),
```

**Step 5: Build and run all tests**

Run: `dotnet build --configuration Release && dotnet test --configuration Release -v n`
Expected: Build succeeds, all tests pass

**Step 6: Commit**

```bash
git add src/Radio.Core/Interfaces/Audio/IBluetoothService.cs src/Radio.Infrastructure/Platform/Bluetooth/LinuxBluetoothService.cs src/Radio.API/Controllers/BluetoothController.cs
git commit -m "feat: track and expose last disconnect reason for UI display"
```

**Note:** Also update `BluetoothServiceFactory.NullBluetoothService` and any Windows BT service stubs.

---

## Task 10: Update Existing Tests

**Files:**
- Modify: `tests/Radio.Infrastructure.Tests/Platform/Bluetooth/BluetoothReconnectionLoopTests.cs`
- Check: Any other tests that reference `BluetoothDeviceDisconnectedEventArgs`

**Step 1: Check for test compilation issues**

Run: `dotnet build tests/ --configuration Release`

If any tests reference `BluetoothDeviceDisconnectedEventArgs` without the new `Reason` property, they should still compile (it has a default value). If any tests mock `IBluetoothService`, they may need the new `IsReconnecting`, `CancelReconnection()`, and `LastDisconnectReason` members added to the mock setup.

**Step 2: Add reconnection-with-reason tests**

Add to `BluetoothReconnectionLoopTests.cs`:

```csharp
[Fact]
public void ShouldSuppressReconnect_RemoteDisconnect_ReturnsTrue()
{
  // This validates the decision logic used by LinuxBluetoothService
  Assert.True(BluetoothDisconnectReason.Remote.ShouldSuppressReconnect());
}

[Fact]
public void ShouldSuppressReconnect_TimeoutDisconnect_ReturnsFalse()
{
  Assert.False(BluetoothDisconnectReason.Timeout.ShouldSuppressReconnect());
}
```

**Step 3: Run all tests**

Run: `dotnet test --configuration Release -v n`
Expected: All tests pass

**Step 4: Commit**

```bash
git add tests/
git commit -m "test: update existing tests for new IBluetoothService members"
```

---

## Task 11: Full Build Verification and Final Commit

**Step 1: Full build**

Run: `dotnet build --configuration Release`
Expected: 0 warnings, 0 errors

**Step 2: Run all tests**

Run: `dotnet test --configuration Release -v n`
Expected: All ~1,697+ tests pass

**Step 3: Update task_plan.md**

Change Phase 7 status from `pending` to `complete`.

**Step 4: Final commit**

```bash
git add task_plan.md
git commit -m "docs: mark Phase 7 (BT disconnect reason) complete"
```

---

## Task 12: Deploy and Integration Test on Ubuntu

**Step 1: Deploy to Ubuntu**

Run: `./deploy/Deploy-ToLinux.ps1 -TargetHost radio -Runtime linux-x64`

**Step 2: Verify CAP_NET_ADMIN is active**

SSH to Ubuntu and check:
```bash
sudo systemctl daemon-reload
sudo systemctl restart radio-api
journalctl -u radio-api -p info --since "1 minute ago" | grep -i mgmt
```
Expected: "BlueZ mgmt socket opened — listening for disconnect events"

**Step 3: Test phone-side disconnect**

1. Connect Pixel 8 Pro to radio via Bluetooth
2. Verify connected in UI at `http://radio:5002/bluetooth`
3. On phone: Settings > Connected devices > Grandpas Radio > Disconnect
4. Check radio logs: `journalctl -u radio-api -p info --since "1 minute ago" | grep -i disconnect`
5. Expected: `reason=Remote` — no reconnection loop should start
6. UI should show "Device disconnected by user"

**Step 4: Test signal loss (out of range)**

1. Connect phone
2. Walk phone out of Bluetooth range (or disable phone's BT radio briefly)
3. Expected: `reason=Timeout` — reconnection loop should start
4. UI should show "Reconnecting to last device..." with Stop button
5. Bring phone back in range — should auto-reconnect

**Step 5: Test "Stop Reconnecting" button**

1. Trigger a timeout disconnect
2. While reconnecting, click "Stop" button in UI
3. Expected: Reconnection loop stops, UI shows normal disconnected state
