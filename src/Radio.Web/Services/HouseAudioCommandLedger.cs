using Radio.Web.Models;

namespace Radio.Web.Services;

/// <summary>One command a panel has sent, and what it does to the rooms it names.</summary>
internal sealed class HouseAudioCommand(IReadOnlyList<string> zoneNames, Func<HouseZone, HouseZone> change)
{
  public IReadOnlyList<string> ZoneNames { get; } = zoneNames;

  public HouseZone ApplyTo(HouseZone zone) => change(zone);
}

/// <summary>
/// What the House Audio panel shows: the last reported state of each room, with the commands the
/// controller has not yet reported back laid over it.
/// </summary>
/// <remarks>
/// <para>
/// Commands are kept as changes rather than as a commanded copy of the room, so two commands can
/// overlap on one room and the one that fails can be taken back without losing the other.
/// </para>
/// <para>
/// Render thread only, except <see cref="Releases"/>, which the poller's <c>Updated</c> handler reads.
/// </para>
/// </remarks>
internal sealed class HouseAudioCommandLedger
{
  private sealed class ZoneCommands
  {
    public int Holds { get; set; }

    public List<HouseAudioCommand> Commands { get; } = [];
  }

  private readonly Dictionary<string, ZoneCommands> _commands = new(StringComparer.Ordinal);
  private readonly Dictionary<(int ControllerId, int ZoneId), int> _unmutedVolumes = [];
  private IReadOnlyList<HouseZone> _reported = [];
  private long _releases;

  /// <summary>
  /// Counts releases. The <c>Updated</c> handler reads this before it queues a snapshot, and passes
  /// it to <see cref="Accept"/>, which is what tells a snapshot raised before a release from one
  /// raised after it, whenever it happens to be handled.
  /// </summary>
  public long Releases => Interlocked.Read(ref _releases);

  /// <summary>Every room as the panel should show it.</summary>
  public IReadOnlyList<HouseZone> Zones => _reported.Select(Shown).ToList();

  /// <summary>
  /// The volume an unmuted room was last known to be at, or null if none has been since this panel
  /// opened. The controller mutes a room by taking its volume to 0 and does not report the volume it
  /// will return to, so this is a remembered value. It comes from reports and from volume commands
  /// the controller accepted, never from a command still in flight, so one that fails cannot disturb it.
  /// </summary>
  public int? UnmutedVolume(HouseZone zone) =>
    _unmutedVolumes.TryGetValue((zone.ControllerId, zone.ZoneId), out int volume) ? volume : null;

  /// <summary>
  /// Records a volume the controller accepted. A mute right after it would otherwise leave no report
  /// that ever carried this volume, and the room would show the one before.
  /// </summary>
  public void RememberUnmutedVolume(HouseZone zone, int volume) =>
    _unmutedVolumes[(zone.ControllerId, zone.ZoneId)] = volume;

  /// <summary>Starts a command, which shows at once, and holds its rooms until <see cref="Release"/>.</summary>
  public HouseAudioCommand Begin(IReadOnlyList<string> zoneNames, Func<HouseZone, HouseZone> change)
  {
    HouseAudioCommand command = new(zoneNames, change);
    foreach (string zoneName in zoneNames)
    {
      if (!_commands.TryGetValue(zoneName, out ZoneCommands? zone))
      {
        zone = new ZoneCommands();
        _commands[zoneName] = zone;
      }

      zone.Holds++;
      zone.Commands.Add(command);
    }

    return command;
  }

  /// <summary>
  /// The command has finished. A success stands until the first non-stale snapshot after the last
  /// hold on its room ends.
  /// </summary>
  /// <remarks>
  /// ⚠ Call this after disposing the poller's hold, not before. A snapshot raised between the two
  /// could then carry the new count and the old value, and would count as coming after the release.
  /// </remarks>
  public void Release(HouseAudioCommand command)
  {
    foreach (string zoneName in command.ZoneNames)
    {
      _commands[zoneName].Holds--;
    }

    Interlocked.Increment(ref _releases);
  }

  /// <summary>Takes a command back: its rooms return at once to their last reported state.</summary>
  public void Fail(HouseAudioCommand command)
  {
    foreach (string zoneName in command.ZoneNames)
    {
      if (_commands.TryGetValue(zoneName, out ZoneCommands? zone))
      {
        zone.Commands.Remove(command);
      }
    }
  }

  /// <summary>
  /// Takes a snapshot from the poller. Returns false, changing nothing, for a successful poll raised
  /// before a release that has happened since, which can only hold the value from before the command.
  /// A stale snapshot is always taken, so a dead controller shows, but it clears no command.
  /// </summary>
  /// <param name="snapshot">The poller's snapshot.</param>
  /// <param name="releasesAtRaise"><see cref="Releases"/>, read when the snapshot was raised.</param>
  public bool Accept(HouseAudioSnapshot snapshot, long releasesAtRaise)
  {
    if (!snapshot.Stale && releasesAtRaise != Releases)
    {
      return false;
    }

    // Matched on the ids, because two zones can share a name.
    _reported = snapshot.Zones
      .Select(zone => !IsHeld(zone.Name)
        ? zone
        : _reported.FirstOrDefault(shown => shown.ControllerId == zone.ControllerId && shown.ZoneId == zone.ZoneId) ?? zone)
      .ToList();

    // Only a fresh report of a room no command is holding. A stale snapshot repeats old zones and a held
    // room keeps its old entry, and either could be older than a volume the controller has since accepted.
    foreach (HouseZone zone in snapshot.Zones.Where(zone => !snapshot.Stale && !zone.Muted && !IsHeld(zone.Name)))
    {
      _unmutedVolumes[(zone.ControllerId, zone.ZoneId)] = zone.Volume;
    }

    if (!snapshot.Stale)
    {
      foreach (string zoneName in _commands.Keys.Where(name => !IsHeld(name)).ToList())
      {
        _commands.Remove(zoneName);
      }
    }

    return true;
  }

  private bool IsHeld(string zoneName) =>
    _commands.TryGetValue(zoneName, out ZoneCommands? zone) && zone.Holds > 0;

  private HouseZone Shown(HouseZone reported) =>
    !_commands.TryGetValue(reported.Name, out ZoneCommands? zone)
      ? reported
      : zone.Commands.Aggregate(reported, (shown, command) => command.ApplyTo(shown));
}
