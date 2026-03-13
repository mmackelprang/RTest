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
  /// Extracts the controller index from a mgmt event header (bytes 2-3, little-endian).
  /// Returns -1 if the buffer is too short.
  /// </summary>
  public static int GetControllerIndex(ReadOnlySpan<byte> data)
  {
    if (data.Length < 4)
      return -1;
    return BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(2));
  }

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
    {
      return false;
    }

    var opcode = BinaryPrimitives.ReadUInt16LittleEndian(data);
    if (opcode != MgmtEvDeviceDisconnected)
    {
      return false;
    }

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
    {
      return "00:00:00:00:00:00";
    }

    return $"{bdaddr[5]:X2}:{bdaddr[4]:X2}:{bdaddr[3]:X2}:{bdaddr[2]:X2}:{bdaddr[1]:X2}:{bdaddr[0]:X2}";
  }
}
