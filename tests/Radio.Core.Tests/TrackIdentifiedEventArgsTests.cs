using Radio.Core.Events;
using Radio.Core.Models.Audio;
using Xunit;

namespace Radio.Core.Tests;

/// <summary>AUD-33: the staleness comparison sources use to drop a result sampled from the previous track.</summary>
public class TrackIdentifiedEventArgsTests
{
  private static readonly DateTime TrackStarted = new(2026, 9, 26, 12, 38, 1, DateTimeKind.Utc);

  private static TrackIdentifiedEventArgs Args(DateTime? captureStartedAt) =>
    new(new TrackMetadata
    {
      Id = "x",
      Title = "t",
      Artist = "a",
      Source = MetadataSource.Shazam,
      CreatedAt = TrackStarted,
      UpdatedAt = TrackStarted
    }, 0.8, captureStartedAt);

  [Fact]
  public void CapturedBeforeTheTrackStarted_IsStale()
  {
    Assert.True(Args(TrackStarted.AddSeconds(-9)).WasCapturedBefore(TrackStarted));
  }

  [Fact]
  public void CapturedAtOrAfterTheTrackStarted_IsNotStale()
  {
    Assert.False(Args(TrackStarted).WasCapturedBefore(TrackStarted));
    Assert.False(Args(TrackStarted.AddSeconds(1)).WasCapturedBefore(TrackStarted));
  }

  [Fact]
  public void UnknownCaptureTime_IsNeverStale()
  {
    Assert.False(Args(null).WasCapturedBefore(TrackStarted));
  }

  [Fact]
  public void UnknownTrackStart_IsNeverStale()
  {
    Assert.False(Args(TrackStarted).WasCapturedBefore(null));
  }
}
