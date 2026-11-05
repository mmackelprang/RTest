namespace Radio.Web.Models;

/// <summary>
/// One room, as <c>GET /zones</c> on the RNET controller reports it. <c>PartyMode</c> is 0 = Off,
/// 1 = On, 2 = Master.
/// </summary>
public record HouseZone(
  int ControllerId,
  int ZoneId,
  string Name,
  bool Power,
  int Volume,
  int SourceId,
  bool Muted,
  int MaxVolume,
  int Bass,
  int Treble,
  bool Loudness,
  int Balance,
  int TurnOnVolume,
  bool DoNotDisturb,
  int PartyMode);

/// <summary>One amp input, as <c>GET /sources</c> reports it. <c>Type</c> runs 0 to 19.</summary>
public record HouseSource(int SourceId, string Name, int Type);

/// <summary>
/// The controller itself, as <c>GET /system</c> reports it. <c>Address</c> is its first non-internal
/// IPv4 address, or null if it has none.
/// </summary>
public record HouseSystemInfo(string Name, string? Address, bool SerialConnected, string Version);

/// <summary>What a call to the RNET controller found.</summary>
public enum HouseAudioHealth
{
  /// <summary>The controller answered.</summary>
  Ok,

  /// <summary>No usable answer: connection failure, timeout, server error or unreadable body.</summary>
  Unreachable,

  /// <summary>The controller is up, and its link to the amp is down (HTTP 503).</summary>
  AmpDisconnected,

  /// <summary>The controller refused the password (HTTP 401).</summary>
  WrongPassword,

  /// <summary>No password can be read here, so nothing was sent.</summary>
  NoPassword
}

/// <summary>
/// The outcome of one call. <see cref="Value"/> is null unless a read's <see cref="Health"/> is
/// <see cref="HouseAudioHealth.Ok"/>. For a write it says whether the controller accepted the command,
/// so <c>(Ok, false)</c> is a controller that answered and refused: 400, 404 or 409.
/// </summary>
public record HouseAudioResult<T>(HouseAudioHealth Health, T? Value);

/// <summary>What one poll of the controller found.</summary>
/// <param name="Health">The first failure of this poll, or <see cref="HouseAudioHealth.Ok"/>.</param>
/// <param name="System">The newest controller info read, which can predate this poll.</param>
/// <param name="Zones">The newest zones read. Empty if no poll has succeeded.</param>
/// <param name="Sources">The newest sources read. Empty if no poll has succeeded.</param>
/// <param name="Stale">True when this poll failed, so the zones and sources are older than it.</param>
public record HouseAudioSnapshot(
  HouseAudioHealth Health,
  HouseSystemInfo? System,
  IReadOnlyList<HouseZone> Zones,
  IReadOnlyList<HouseSource> Sources,
  bool Stale);
