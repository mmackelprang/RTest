using Radio.Web.Models;
using Radio.Web.Services;

namespace Radio.Web.Tests.Services;

/// <summary>
/// The rules <c>HouseAudioPoller.HoldZone</c> leaves to the panel. Tested here because the case that
/// matters most, a snapshot raised before a release and handled after it, depends on the order of
/// two items in the render queue, which a rendered component gives a test no way to arrange.
/// </summary>
public class HouseAudioCommandLedgerTests
{
  private readonly HouseAudioCommandLedger _ledger = new();

  private static HouseZone Zone(string name, int volume, bool muted = false) =>
    new(0, name == "Kitchen" ? 0 : 1, name, true, volume, 0, muted, 100, 0, 0, false, 0, 30, false, 0);

  private static HouseAudioSnapshot Snapshot(bool stale, params HouseZone[] zones) => new(
    stale ? HouseAudioHealth.Unreachable : HouseAudioHealth.Ok, null, zones, [], stale);

  private void Report(bool stale, params HouseZone[] zones) =>
    Assert.True(_ledger.Accept(Snapshot(stale, zones), _ledger.Releases));

  private int ShownVolume(string name = "Kitchen") =>
    _ledger.Zones.Single(zone => zone.Name == name).Volume;

  private HouseAudioCommand SetVolume(int volume, string name = "Kitchen") =>
    _ledger.Begin([name], zone => zone with { Volume = volume });

  [Fact]
  public void UnmutedVolume_ComesFromReports_AndSurvivesAMute()
  {
    HouseZone kitchen = Zone("Kitchen", 40);
    Assert.Null(_ledger.UnmutedVolume(kitchen));

    Report(false, kitchen);
    Report(false, Zone("Kitchen", 0, muted: true));

    Assert.Equal(40, _ledger.UnmutedVolume(kitchen));
  }

  [Fact]
  public void UnmutedVolume_IsNotSetByACommand_SoACommandThatFailsCannotDisturbIt()
  {
    Report(false, Zone("Kitchen", 40));

    HouseAudioCommand command = SetVolume(0);
    Assert.Equal(40, _ledger.UnmutedVolume(Zone("Kitchen", 40)));

    // A report while the command is held: the room shows 0, and only the reported 40 may be remembered.
    Report(false, Zone("Kitchen", 40));
    Assert.Equal(40, _ledger.UnmutedVolume(Zone("Kitchen", 40)));

    _ledger.Fail(command);
    Assert.Equal(40, _ledger.UnmutedVolume(Zone("Kitchen", 40)));
  }

  [Fact]
  public void UnmutedVolume_ForAnotherRoomsCommand_IsStillOnlyTheReportedVolume()
  {
    Report(false, Zone("Kitchen", 40), Zone("Den", 20));
    HouseAudioCommand command = SetVolume(0, "Kitchen");
    _ledger.Release(command);

    Report(false, Zone("Kitchen", 44), Zone("Den", 24));

    Assert.Equal(44, _ledger.UnmutedVolume(Zone("Kitchen", 44)));
    Assert.Equal(24, _ledger.UnmutedVolume(Zone("Den", 24)));
  }

  [Fact]
  public void RememberUnmutedVolume_SurvivesAMutedReport()
  {
    Report(false, Zone("Kitchen", 40));

    _ledger.RememberUnmutedVolume(Zone("Kitchen", 40), 60);
    Assert.Equal(60, _ledger.UnmutedVolume(Zone("Kitchen", 40)));

    Report(false, Zone("Kitchen", 0, muted: true));
    Assert.Equal(60, _ledger.UnmutedVolume(Zone("Kitchen", 40)));
  }

  [Fact]
  public void RememberUnmutedVolume_SurvivesAReportOfAHeldRoom()
  {
    Report(false, Zone("Kitchen", 40));
    _ledger.RememberUnmutedVolume(Zone("Kitchen", 40), 60);

    // A mute is in flight, so the room is held, and this report still says 40.
    _ledger.Begin(["Kitchen"], zone => zone with { Muted = true });
    Report(false, Zone("Kitchen", 40));

    Assert.Equal(60, _ledger.UnmutedVolume(Zone("Kitchen", 40)));
  }

  [Fact]
  public void RememberUnmutedVolume_SurvivesAStaleReport()
  {
    Report(false, Zone("Kitchen", 40));
    _ledger.RememberUnmutedVolume(Zone("Kitchen", 40), 60);

    Report(true, Zone("Kitchen", 40));

    Assert.Equal(60, _ledger.UnmutedVolume(Zone("Kitchen", 40)));
  }

  [Fact]
  public void RememberUnmutedVolume_GivesWayToAFreshReport()
  {
    Report(false, Zone("Kitchen", 40));
    _ledger.RememberUnmutedVolume(Zone("Kitchen", 40), 60);

    Report(false, Zone("Kitchen", 44));

    Assert.Equal(44, _ledger.UnmutedVolume(Zone("Kitchen", 44)));
  }

  [Fact]
  public void UnmutedVolume_IsNothingForARoomOnlyEverSeenMuted()
  {
    Report(false, Zone("Kitchen", 0, muted: true));

    Assert.Null(_ledger.UnmutedVolume(Zone("Kitchen", 0, muted: true)));
  }

  [Fact]
  public void NewCommand_ShowsAtOnce()
  {
    Report(false, Zone("Kitchen", 20));

    SetVolume(60);

    Assert.Equal(60, ShownVolume());
  }

  [Fact]
  public void DuringAHold_TheZonesSnapshotEntryIsIgnored_AndOtherZonesAreNot()
  {
    Report(false, Zone("Kitchen", 20), Zone("Den", 20));
    HouseAudioCommand command = SetVolume(60);

    Report(false, Zone("Kitchen", 25), Zone("Den", 30));

    Assert.Equal(60, ShownVolume());
    Assert.Equal(30, ShownVolume("Den"));

    // Ignored, not waiting underneath: a failure returns to what was reported before the hold.
    _ledger.Release(command);
    _ledger.Fail(command);
    Assert.Equal(20, ShownVolume());
  }

  [Fact]
  public void AfterRelease_TheCommandedValueStands_UntilTheFirstNonStaleSnapshot()
  {
    Report(false, Zone("Kitchen", 20));
    HouseAudioCommand command = SetVolume(60);

    _ledger.Release(command);
    Assert.Equal(60, ShownVolume());

    Report(false, Zone("Kitchen", 58));
    Assert.Equal(58, ShownVolume());
  }

  [Fact]
  public void StaleSnapshot_DoesNotReplaceACommandedValue()
  {
    Report(false, Zone("Kitchen", 20));
    HouseAudioCommand command = SetVolume(60);
    _ledger.Release(command);

    Report(true, Zone("Kitchen", 20));

    Assert.Equal(60, ShownVolume());
  }

  [Fact]
  public void FailedCommand_ReturnsAtOnceToTheLastReportedState_EvenIfThatWasStale()
  {
    Report(false, Zone("Kitchen", 20));
    HouseAudioCommand command = SetVolume(60);
    Report(true, Zone("Kitchen", 20));

    _ledger.Release(command);
    _ledger.Fail(command);

    Assert.Equal(20, ShownVolume());
  }

  [Fact]
  public void FailedCommand_LeavesAnotherCommandOnTheSameZoneInPlace()
  {
    Report(false, Zone("Kitchen", 20));
    HouseAudioCommand volume = SetVolume(60);
    HouseAudioCommand mute = _ledger.Begin(["Kitchen"], zone => zone with { Muted = true });

    _ledger.Release(mute);
    _ledger.Fail(mute);

    HouseZone shown = _ledger.Zones.Single();
    Assert.False(shown.Muted);
    Assert.Equal(60, shown.Volume);
    _ledger.Release(volume);
  }

  [Fact]
  public void OverlappingHolds_AfterTheReleaseMeansAfterTheLastOne()
  {
    Report(false, Zone("Kitchen", 20));
    HouseAudioCommand first = SetVolume(40);
    HouseAudioCommand second = SetVolume(60);

    _ledger.Release(first);
    Report(false, Zone("Kitchen", 40));
    Assert.Equal(60, ShownVolume());

    _ledger.Release(second);
    Report(false, Zone("Kitchen", 60));
    Report(false, Zone("Kitchen", 62));
    Assert.Equal(62, ShownVolume());
  }

  [Fact]
  public void SnapshotRaisedBeforeARelease_AndHandledAfterIt_IsDiscarded()
  {
    Report(false, Zone("Kitchen", 20));
    HouseAudioCommand command = SetVolume(60);

    // What the Updated handler reads on the poller's thread, before it queues the render.
    long releasesAtRaise = _ledger.Releases;
    HouseAudioSnapshot raised = Snapshot(false, Zone("Kitchen", 20));

    _ledger.Release(command);

    Assert.False(_ledger.Accept(raised, releasesAtRaise));
    Assert.Equal(60, ShownVolume());

    Report(false, Zone("Kitchen", 60));
    Assert.Equal(60, ShownVolume());
  }

  [Fact]
  public void SnapshotRaisedBeforeARelease_IsKept_IfStale_SoADeadControllerStillShows()
  {
    Report(false, Zone("Kitchen", 20));
    HouseAudioCommand command = SetVolume(60);
    long releasesAtRaise = _ledger.Releases;

    _ledger.Release(command);

    Assert.True(_ledger.Accept(Snapshot(true, Zone("Kitchen", 20)), releasesAtRaise));
    Assert.Equal(60, ShownVolume());
  }

  [Fact]
  public void ACommandOnSeveralZones_FailsOnAllOfThem()
  {
    Report(false, Zone("Kitchen", 20), Zone("Den", 30));
    HouseAudioCommand command = _ledger.Begin(["Kitchen", "Den"], zone => zone with { Muted = true });
    Assert.All(_ledger.Zones, zone => Assert.True(zone.Muted));

    _ledger.Release(command);
    _ledger.Fail(command);

    Assert.All(_ledger.Zones, zone => Assert.False(zone.Muted));
  }

  [Fact]
  public void ACommandOnSeveralZones_HoldsThemAll_UntilItIsReleased()
  {
    Report(false, Zone("Kitchen", 20), Zone("Den", 30));
    HouseAudioCommand command = _ledger.Begin(["Kitchen", "Den"], zone => zone with { Muted = true });

    Report(false, Zone("Kitchen", 20), Zone("Den", 30));
    Assert.All(_ledger.Zones, zone => Assert.True(zone.Muted));

    _ledger.Release(command);
    Report(false, Zone("Kitchen", 20, muted: true), Zone("Den", 30));

    Assert.True(_ledger.Zones.Single(zone => zone.Name == "Kitchen").Muted);
    Assert.False(_ledger.Zones.Single(zone => zone.Name == "Den").Muted);
  }
}
