# BT Codec Observability Implementation Plan (Phase 1 / Plan C)

> **For Claude:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` to implement this plan task-by-task.

**Goal:** Surface the currently-negotiated A2DP codec name (and SBC bitpool / sample rate when available) as an observable metric + log entry + UI display, so "audio sounds bad today" can be diagnosed against actual codec data instead of memory.

**Architecture:**

- New method `IBluetoothService.GetA2dpCodecInfoAsync(string deviceAddress, CancellationToken ct)` returning `A2dpCodecInfo` (record with `CodecName`, `SampleRateHz`, `BitpoolOrNull`).
- Linux implementation reads BlueZ D-Bus `org.bluez.MediaTransport1` properties (`Codec`, `Configuration`) from the existing `_mediaTransport` reference that `LinuxBluetoothService.AttachMediaTransportAsync` already holds.
  - `Codec` is a `byte` (0x00 = SBC, 0x02 = AAC, 0xFF = vendor-specific i.e. aptX/LDAC/aptX-HD).
  - `Configuration` is a byte-array whose layout depends on codec — for SBC, a 4-byte LC3-style cap blob from which bitpool can be extracted (byte 3 low nibble = max bitpool, high nibble = min bitpool). For AAC/vendor, codec-specific.
- Emit `bluetooth.a2dp.codec` (gauge — codec ID as integer) + `bluetooth.a2dp.bitpool` (gauge — bitpool for SBC, or `-1` for non-SBC) + `bluetooth.a2dp.sample_rate_hz` (gauge) on connect + on `TransportPropertiesChanged` for `Codec`/`Configuration`.
- Surface in `BluetoothStatusDto` + render in `BluetoothPage.razor`.

This plan ships codec *visibility* only — no codec pinning, no negotiation forcing, no behavior change.

**Tech Stack:** existing BlueZ D-Bus client (Tmds.DBus.Protocol per existing usage in `LinuxBluetoothService`), Radio.Metrics, Radzen Blazor UI.

**Addresses**: FM-BT-6 from [`docs/research/2026-05-22-bt-audio-stabilization.md`](../research/2026-05-22-bt-audio-stabilization.md) §4 ("Y but invisible" — failure mode exists but cannot be diagnosed from logs).

---

## Task 0: Author probe scripts (research deliverable)

**Files:**
- Create: `scripts/research/bt_codec_observability_probe.sh`
- Create: `scripts/research/bt_codec_observability_compare.py`

**Step 1: `bt_codec_observability_probe.sh`** — takes args `--duration N --phones <addr1,addr2,addr3>`. Sequentially connects each phone, waits 60 s for codec emission, records:
- Log lines matching `BluetoothCodec: ` (the new log message from Task 4)
- Metric values via `sqlite3 metrics.db "SELECT * FROM gauges WHERE metric LIKE 'bluetooth.a2dp.%'"`
- `bluetoothctl info <MAC>` cross-reference

Output: `events_emitted=<N>, codec_log_lines=<L>, ui_codec_displayed=<bool>, per_phone_codec=<phone1=sbc,phone2=aac,...>`.

**Step 2: `bt_codec_observability_compare.py`** — reads two artifacts; compares `events_emitted` + per-phone codec table. PASS if post-change reports ≥3 codec emissions matching `bluetoothctl info` output for ≥2 of 3 phones.

**Step 3: Commit**

```bash
git add scripts/research/bt_codec_observability_*
git commit -m "scripts(research): add codec observability probe + compare scripts"
```

---

## Task 1: Add `A2dpCodecInfo` record + interface method

**Files:**
- Create: `src/Radio.Core/Interfaces/Audio/A2dpCodecInfo.cs`
- Modify: `src/Radio.Core/Interfaces/Audio/IBluetoothService.cs`

**Step 1: Create the record**

```csharp
namespace Radio.Core.Interfaces.Audio;

/// <summary>
/// Snapshot of the currently-negotiated A2DP codec for a Bluetooth device.
/// Returned by IBluetoothService.GetA2dpCodecInfoAsync.
/// </summary>
public sealed record A2dpCodecInfo
{
  /// <summary>BlueZ codec ID (0x00 = SBC, 0x02 = AAC, 0xFF = vendor-specific).</summary>
  public required byte CodecId { get; init; }

  /// <summary>Human-readable codec name (e.g. "SBC", "AAC", "aptX", "aptX-HD", "LDAC").</summary>
  public required string CodecName { get; init; }

  /// <summary>Negotiated sample rate (e.g. 48000); 0 if unknown.</summary>
  public required int SampleRateHz { get; init; }

  /// <summary>SBC bitpool value (2–53 typical). Null for non-SBC codecs.</summary>
  public int? BitpoolOrNull { get; init; }

  /// <summary>Raw Configuration bytes (codec-specific layout). For diagnostics.</summary>
  public required byte[] RawConfiguration { get; init; }
}
```

**Step 2: Add interface method + event**

```csharp
/// <summary>
/// Gets the currently-negotiated A2DP codec info for the device. Returns null
/// if no transport is active or the codec is not yet known.
/// </summary>
Task<A2dpCodecInfo?> GetA2dpCodecInfoAsync(string deviceAddress, CancellationToken ct = default);

/// <summary>
/// Raised when the negotiated A2DP codec changes (on connect, or if BlueZ re-negotiates
/// mid-session). Subscribers should refresh any cached codec state.
/// </summary>
event EventHandler<A2dpCodecChangedEventArgs>? A2dpCodecChanged;
```

Plus event-args:

```csharp
public class A2dpCodecChangedEventArgs : EventArgs
{
  public required string DeviceAddress { get; init; }
  public required A2dpCodecInfo CodecInfo { get; init; }
}
```

**Step 3: Stubs in Windows + Mock implementations**

```csharp
public Task<A2dpCodecInfo?> GetA2dpCodecInfoAsync(string deviceAddress, CancellationToken ct = default)
  => Task.FromResult<A2dpCodecInfo?>(null);
public event EventHandler<A2dpCodecChangedEventArgs>? A2dpCodecChanged;
```

**Step 4: Build + commit**

```bash
git add src/Radio.Core/Interfaces/Audio/A2dpCodecInfo.cs \
        src/Radio.Core/Interfaces/Audio/IBluetoothService.cs \
        src/Radio.Infrastructure/Platform/Bluetooth/WindowsBluetoothService.cs \
        src/Radio.Infrastructure/Platform/Bluetooth/MockBluetoothService.cs
git commit -m "feat(bt): add A2dpCodecInfo record + IBluetoothService codec API"
```

---

## Task 2: Codec parser (pure logic, testable)

**Files:**
- Create: `src/Radio.Infrastructure/Platform/Bluetooth/Linux/A2dpCodecConfigParser.cs`

**Step 1: Implement the parser**

```csharp
using Radio.Core.Interfaces.Audio;

namespace Radio.Infrastructure.Platform.Bluetooth.Linux;

/// <summary>
/// Pure parser for BlueZ MediaTransport1.Codec + Configuration properties.
/// No I/O — fully testable.
///
/// BlueZ codec IDs (from net/bluetooth/a2dp-codecs.h):
///   0x00 = SBC, 0x02 = MPEG-2/4 AAC, 0xFF = vendor-specific (aptX, LDAC, etc.)
///
/// SBC Configuration layout (4 bytes):
///   byte 0: sampling-freq (bit 4..7) | channel-mode (bit 0..3)
///   byte 1: block-length (bit 4..7) | subbands (bit 2..3) | allocation (bit 0..1)
///   byte 2: min-bitpool
///   byte 3: max-bitpool
///
/// AAC Configuration is 6 bytes; vendor-specific is variable.
/// </summary>
internal static class A2dpCodecConfigParser
{
  public static A2dpCodecInfo Parse(byte codecId, byte[] configuration)
  {
    var name = CodecName(codecId, configuration);
    var sampleRate = SampleRate(codecId, configuration);
    var bitpool = (codecId == 0x00 && configuration.Length >= 4)
      ? configuration[3]  // max-bitpool
      : (int?)null;

    return new A2dpCodecInfo
    {
      CodecId = codecId,
      CodecName = name,
      SampleRateHz = sampleRate,
      BitpoolOrNull = bitpool,
      RawConfiguration = configuration
    };
  }

  internal static string CodecName(byte codecId, byte[] configuration) => codecId switch
  {
    0x00 => "SBC",
    0x02 => "AAC",
    0xFF when configuration.Length >= 6 => ParseVendorCodec(configuration),
    _ => $"Unknown-0x{codecId:X2}"
  };

  /// <summary>
  /// For vendor-specific (0xFF) codec, the first 6 bytes of Configuration are:
  ///   bytes 0..3: vendor ID (little-endian)
  ///   bytes 4..5: codec ID (little-endian)
  /// </summary>
  internal static string ParseVendorCodec(byte[] configuration)
  {
    var vendorId = (uint)(configuration[0] | (configuration[1] << 8) | (configuration[2] << 16) | (configuration[3] << 24));
    var codecId = (ushort)(configuration[4] | (configuration[5] << 8));
    // Vendor IDs from Bluetooth SIG company ID assignments
    return (vendorId, codecId) switch
    {
      (0x004F, 0x0001) => "aptX",
      (0x000A, 0x0001) => "aptX",         // Qualcomm/CSR
      (0x00D7, 0x0024) => "aptX-HD",      // Qualcomm extension
      (0x012D, 0x00AA) => "LDAC",         // Sony
      (0x053A, 0x4C32) => "LHDC",         // Savitech
      _ => $"Vendor-0x{vendorId:X8}/0x{codecId:X4}"
    };
  }

  internal static int SampleRate(byte codecId, byte[] configuration)
  {
    if (codecId == 0x00 && configuration.Length >= 1)
    {
      // SBC: sampling-freq in bits 4..7 of byte 0
      var freqBits = (configuration[0] >> 4) & 0x0F;
      return freqBits switch
      {
        0x08 => 16000,
        0x04 => 32000,
        0x02 => 44100,
        0x01 => 48000,
        _ => 0
      };
    }
    if (codecId == 0x02 && configuration.Length >= 4)
    {
      // AAC: sampling-freq is a 12-bit field starting at byte 1 bit 0
      var freqBits = ((configuration[1] & 0xFF) << 4) | ((configuration[2] >> 4) & 0x0F);
      return freqBits switch
      {
        0x800 => 8000,
        0x400 => 11025,
        0x200 => 12000,
        0x100 => 16000,
        0x080 => 22050,
        0x040 => 24000,
        0x020 => 32000,
        0x010 => 44100,
        0x008 => 48000,
        0x004 => 64000,
        0x002 => 88200,
        0x001 => 96000,
        _ => 0
      };
    }
    return 0;
  }
}
```

**Step 2: Build + commit**

```bash
git add src/Radio.Infrastructure/Platform/Bluetooth/Linux/A2dpCodecConfigParser.cs
git commit -m "feat(bt): A2dpCodecConfigParser for BlueZ codec/config bytes"
```

---

## Task 3: Tests for the codec parser

**Files:**
- Create: `tests/Radio.Infrastructure.Tests/Platform/Bluetooth/Linux/A2dpCodecConfigParserTests.cs`

**Step 1: Write tests**

```csharp
using Radio.Infrastructure.Platform.Bluetooth.Linux;

namespace Radio.Infrastructure.Tests.Platform.Bluetooth.Linux;

public class A2dpCodecConfigParserTests
{
  [Theory]
  [InlineData(0x00, "SBC")]
  [InlineData(0x02, "AAC")]
  [InlineData(0x05, "Unknown-0x05")]
  public void CodecName_KnownAndUnknown(byte codecId, string expected)
  {
    Assert.Equal(expected, A2dpCodecConfigParser.CodecName(codecId, new byte[6]));
  }

  [Fact]
  public void ParseSBC_48kHz_StereoJoint_Bitpool53()
  {
    // SBC config: byte0 = 0x21 (48kHz=0x1<<4 | joint-stereo=0x1), byte3 = 53 (max bitpool)
    byte[] config = { 0x21, 0x35, 2, 53 };
    var info = A2dpCodecConfigParser.Parse(0x00, config);
    Assert.Equal("SBC", info.CodecName);
    Assert.Equal(48000, info.SampleRateHz);
    Assert.Equal(53, info.BitpoolOrNull);
  }

  [Fact]
  public void ParseSBC_44kHz_Bitpool35()
  {
    byte[] config = { 0x21 ^ 0x10 | 0x21, 0x35, 2, 35 };  // 44.1kHz
    var info = A2dpCodecConfigParser.Parse(0x00, new byte[] { 0x22, 0x35, 2, 35 });
    Assert.Equal(44100, info.SampleRateHz);
    Assert.Equal(35, info.BitpoolOrNull);
  }

  [Fact]
  public void ParseAAC_NoBitpool()
  {
    byte[] config = { 0x80, 0x01, 0x8C, 0x80, 0x00, 0xFA };  // arbitrary
    var info = A2dpCodecConfigParser.Parse(0x02, config);
    Assert.Equal("AAC", info.CodecName);
    Assert.Null(info.BitpoolOrNull);
  }

  [Fact]
  public void ParseVendor_aptX()
  {
    // aptX vendor=0x004F, codec=0x0001
    byte[] config = { 0x4F, 0x00, 0x00, 0x00, 0x01, 0x00, 0x20, 0x00 };
    var info = A2dpCodecConfigParser.Parse(0xFF, config);
    Assert.Equal("aptX", info.CodecName);
  }

  [Fact]
  public void ParseVendor_LDAC()
  {
    // LDAC vendor=0x012D, codec=0x00AA
    byte[] config = { 0x2D, 0x01, 0x00, 0x00, 0xAA, 0x00, 0x20, 0x00 };
    var info = A2dpCodecConfigParser.Parse(0xFF, config);
    Assert.Equal("LDAC", info.CodecName);
  }

  [Fact]
  public void ParseVendor_Unknown_ReturnsHexId()
  {
    byte[] config = { 0xDE, 0xAD, 0xBE, 0xEF, 0x01, 0x00 };
    var info = A2dpCodecConfigParser.Parse(0xFF, config);
    Assert.StartsWith("Vendor-0x", info.CodecName);
  }

  [Fact]
  public void RawConfigurationPreserved()
  {
    byte[] config = { 0x21, 0x35, 2, 53 };
    var info = A2dpCodecConfigParser.Parse(0x00, config);
    Assert.Equal(config, info.RawConfiguration);
  }
}
```

**Step 2: Run tests**

```bash
dotnet test tests/Radio.Infrastructure.Tests --filter "A2dpCodecConfigParserTests" --configuration Release -v n
```
Expected: 8 PASS.

**Step 3: Commit**

```bash
git add tests/Radio.Infrastructure.Tests/Platform/Bluetooth/Linux/A2dpCodecConfigParserTests.cs
git commit -m "test(bt): unit tests for A2dpCodecConfigParser"
```

---

## Task 4: Read codec from BlueZ D-Bus in `LinuxBluetoothService`

**Files:**
- Modify: `src/Radio.Infrastructure/Platform/Bluetooth/LinuxBluetoothService.cs`

**Step 1: Implement `GetA2dpCodecInfoAsync`**

Use the existing `_mediaTransport` reference (already attached via `AttachMediaTransportAsync` at [L2092](../../src/Radio.Infrastructure/Platform/Bluetooth/LinuxBluetoothService.cs)). Read `Codec` (byte) + `Configuration` (byte[]) properties via D-Bus.

```csharp
public async Task<A2dpCodecInfo?> GetA2dpCodecInfoAsync(string deviceAddress, CancellationToken ct = default)
{
  var transport = _mediaTransport;
  if (transport == null) return null;

  try
  {
    var codecId = (byte)await transport.GetAsync("Codec");
    var config = (byte[])await transport.GetAsync("Configuration");
    return A2dpCodecConfigParser.Parse(codecId, config);
  }
  catch (Exception ex)
  {
    _logger.LogDebug(ex, "GetA2dpCodecInfoAsync read failed for {Address}", deviceAddress);
    return null;
  }
}
```

(Adjust D-Bus property-read syntax to match the project's existing Tmds.DBus usage. Look at how `AttachMediaTransportAsync` already reads transport properties for the right pattern.)

**Step 2: Raise `A2dpCodecChanged` on transport-properties change**

In the existing `OnTransportPropertiesChanged` handler, when `Codec` or `Configuration` changes:

```csharp
if (changed.ContainsKey("Codec") || changed.ContainsKey("Configuration"))
{
  var info = await GetA2dpCodecInfoAsync(ConnectedDevice?.Address ?? "", default);
  if (info != null && ConnectedDevice != null)
  {
    _logger.LogInformation(
      "BluetoothCodec: {Address} negotiated codec={Codec} sampleRate={Rate}Hz bitpool={Bitpool}",
      ConnectedDevice.Address, info.CodecName, info.SampleRateHz, info.BitpoolOrNull?.ToString() ?? "n/a");

    _metricsCollector?.Gauge("bluetooth.a2dp.codec", info.CodecId);
    _metricsCollector?.Gauge("bluetooth.a2dp.sample_rate_hz", info.SampleRateHz);
    _metricsCollector?.Gauge("bluetooth.a2dp.bitpool", info.BitpoolOrNull ?? -1);

    A2dpCodecChanged?.Invoke(this, new A2dpCodecChangedEventArgs
    {
      DeviceAddress = ConnectedDevice.Address,
      CodecInfo = info
    });
  }
}
```

**Step 3: Initial emission on transport attach**

In `AttachMediaTransportAsync` after the transport reference is stored, emit once so the initial codec is logged even if no later change occurs:

```csharp
_ = Task.Run(async () =>
{
  await Task.Delay(500);  // give BlueZ a beat to populate properties
  var info = await GetA2dpCodecInfoAsync(ConnectedDevice?.Address ?? "", default);
  if (info != null && ConnectedDevice != null)
  {
    _logger.LogInformation(
      "BluetoothCodec: {Address} initial codec={Codec} sampleRate={Rate}Hz bitpool={Bitpool}",
      ConnectedDevice.Address, info.CodecName, info.SampleRateHz, info.BitpoolOrNull?.ToString() ?? "n/a");
    _metricsCollector?.Gauge("bluetooth.a2dp.codec", info.CodecId);
    _metricsCollector?.Gauge("bluetooth.a2dp.sample_rate_hz", info.SampleRateHz);
    _metricsCollector?.Gauge("bluetooth.a2dp.bitpool", info.BitpoolOrNull ?? -1);
    A2dpCodecChanged?.Invoke(this, new A2dpCodecChangedEventArgs { DeviceAddress = ConnectedDevice.Address, CodecInfo = info });
  }
});
```

**Step 4: Build + commit**

```bash
git add src/Radio.Infrastructure/Platform/Bluetooth/LinuxBluetoothService.cs
git commit -m "feat(bt): read + emit A2DP codec info from BlueZ MediaTransport1"
```

---

## Task 5: Surface in `BluetoothStatusDto` + UI

**Files:**
- Modify: `src/Radio.API/Models/BluetoothDtos.cs`
- Modify: `src/Radio.Web/Models/ApiModels.cs`
- Modify: `src/Radio.API/Controllers/BluetoothController.cs`
- Modify: `src/Radio.Web/Components/Pages/BluetoothPage.razor`

**Step 1: Add fields to both DTOs** (Web has its own copy per MEMORY: "Web project has its own DTO records"):

```csharp
public string? CodecName { get; set; }
public int? SampleRateHz { get; set; }
public int? Bitpool { get; set; }
```

**Step 2: Populate in `BluetoothController.BuildStatus()`**

```csharp
var codec = await _bluetoothService.GetA2dpCodecInfoAsync(connectedDevice?.Address ?? "", CancellationToken.None);
dto.CodecName = codec?.CodecName;
dto.SampleRateHz = codec?.SampleRateHz;
dto.Bitpool = codec?.BitpoolOrNull;
```

(`BuildStatus` may be sync today; if so, make it async or do a fire-and-forget cache pattern.)

**Step 3: Render in `BluetoothPage.razor`**

In the Connected Device Card, alongside name + address:

```razor
@if (!string.IsNullOrEmpty(_status.CodecName))
{
  <RadzenBadge Variant="Variant.Outlined" BadgeStyle="BadgeStyle.Info" Style="margin-top:4px">
    @_status.CodecName
    @if (_status.SampleRateHz is > 0)
    {
      <span>@((_status.SampleRateHz.Value / 1000.0).ToString("F1")) kHz</span>
    }
    @if (_status.Bitpool is > 0)
    {
      <span>bitpool @_status.Bitpool</span>
    }
  </RadzenBadge>
}
```

**Step 4: Build + commit**

```bash
git add src/Radio.API/Models/BluetoothDtos.cs \
        src/Radio.Web/Models/ApiModels.cs \
        src/Radio.API/Controllers/BluetoothController.cs \
        src/Radio.Web/Components/Pages/BluetoothPage.razor
git commit -m "feat(bt): show negotiated codec in BluetoothPage"
```

---

## Task 6: Full build + test

```bash
dotnet build --configuration Release
dotnet test --configuration Release --verbosity normal
```
Expected: 0 warnings; all ~1,697+ tests pass.

---

## Task 7: Deploy + integration test

```bash
./deploy/Deploy-ToLinux.ps1 -TargetHost radio -Runtime linux-x64
```

Connect each test phone in turn; verify:

```bash
ssh mmack@radio "journalctl -u radio-api --since '1 minute ago' | grep BluetoothCodec"
```
Expected: one `BluetoothCodec: ... initial codec=<name>` line per phone connect.

UI: visit `http://radio:5002/bluetooth` — codec badge appears next to connected device.

Cross-reference with `bluetoothctl info <MAC>`:
```bash
ssh mmack@radio "bluetoothctl info <PHONE_MAC> | grep -iE 'codec|configuration'"
```

---

## Task 8: Verify acceptance criteria

Baseline + post-change via the probe script:

```bash
ssh mmack@radio "/opt/radio-console/scripts/research/bt_codec_observability_probe.sh \
  --duration 1800 --phones 'phone-a,phone-b,phone-c'" \
  > baseline_bt_codec.txt   # this is the BASELINE — main has no codec emission

# After deploying this branch:
ssh mmack@radio "/opt/radio-console/scripts/research/bt_codec_observability_probe.sh \
  --duration 1800 --phones 'phone-a,phone-b,phone-c'" \
  > after_bt_codec.txt
```

**Success criterion**:
- `events_emitted ≥ 3` (one per phone connect) in `after_bt_codec.txt`
- `codec_log_lines ≥ 3` with parseable codec names from the set {SBC, AAC, aptX, aptX-HD, LDAC, LHDC}
- `ui_codec_displayed = true`
- Cross-reference: codec name matches `bluetoothctl info <MAC>` output for `≥2` of `3` phones (third allowed to mismatch in case of non-standard vendor codec)

**Debug-agent verification**:
```bash
python3 scripts/research/bt_codec_observability_compare.py baseline_bt_codec.txt after_bt_codec.txt
```
Expected: `PASS`.

---

## Task 9: Open PR + merge

```bash
git push -u origin feat/bt-codec-observability

gh pr create --title "feat(bt): surface negotiated A2DP codec + bitpool as observable metric" --body "$(cat <<'EOF'
## Summary

Implements [Plan C from the Cast/BT research arc](../docs/plans/2026-05-22-cast-bt-phase-1-2-arc.md) — closes the FM-BT-6 visibility gap. Reads `Codec` + `Configuration` properties from BlueZ `MediaTransport1` D-Bus, parses per BlueZ's `a2dp-codecs.h` layout, emits as metrics + log + UI badge.

Read-only addition — no behavior change. Establishes the diagnostic foundation needed before acting on any other FM-BT issue (e.g. "audio sounds bad" → "ah, fell to SBC bitpool 35" instead of "no data, restart").

## Acceptance criteria (verified)

- 3 phones with different codec capability sets → 3 distinct codec emissions
- Cross-reference with `bluetoothctl info` matches for ≥2 of 3 phones
- UI codec badge visible on `/bluetooth` page

## Test plan

- [x] 8 unit tests for codec config parser (SBC bitpool, AAC sample rate, vendor IDs)
- [x] D-Bus property read verified on `radio` host
- [x] Metrics emission visible in `metrics.db` gauges table

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

Merge once Mark approves.

---

## Out of scope

- **Codec pinning / negotiation forcing**: this plan ships visibility only. Pinning would be a separate, behavior-changing plan.
- **AVRCP absolute-volume display**: separate concern; this plan is codec only.
- **Cast-side codec observability**: HM mode's MP3 encode is a known constant (320 kbps CBR by default); DC mode ships raw PCM. Neither has the same "invisible" problem as BT.
- **Per-codec mitigation actions** (e.g. "if SBC bitpool drops below 30, alert"): the metric is the foundation; alerts are downstream work.
- **Windows BT path**: stub only.
