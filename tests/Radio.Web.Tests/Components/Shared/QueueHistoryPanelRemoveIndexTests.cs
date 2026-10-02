using Radio.Web.Components.Shared;
using Radio.Web.Models;
using Xunit;

namespace Radio.Web.Tests.Components.Shared;

/// <summary>
/// AUD-98: the queue panel lists the full playlist, but <c>DELETE /api/queue/{index}</c> counts from the
/// current track. Passing a row's full-list index removed a track as many rows further down as there were
/// played tracks.
/// </summary>
public class QueueHistoryPanelRemoveIndexTests
{
  private static QueueItemDto Row(int fullIndex, string state) =>
    new(fullIndex, $"T{fullIndex}", "A", "B", "3:00", state == "Current", state, fullIndex);

  [Fact]
  public void WithPlayedTracks_TheUpcomingRowMapsToItsPositionAfterTheCurrentOne()
  {
    var items = new List<QueueItemDto>
    {
      Row(0, "Played"), Row(1, "Played"), Row(2, "Current"), Row(3, "Upcoming"), Row(4, "Upcoming"),
    };

    Assert.Equal(1, QueueHistoryPanel.ToQueueIndex(items, items[3]));
    Assert.Equal(2, QueueHistoryPanel.ToQueueIndex(items, items[4]));
  }

  [Fact]
  public void WithNothingPlayed_TheIndexIsUnchanged()
  {
    var items = new List<QueueItemDto> { Row(0, "Current"), Row(1, "Upcoming") };

    Assert.Equal(1, QueueHistoryPanel.ToQueueIndex(items, items[1]));
  }

  [Fact]
  public void WithAnErrorRowInTheUpcomingTracks_TheCurrentRowStillAnchorsIt()
  {
    var items = new List<QueueItemDto>
    {
      Row(0, "Played"), Row(1, "Current"), Row(2, "Error"), Row(3, "Upcoming"),
    };

    Assert.Equal(2, QueueHistoryPanel.ToQueueIndex(items, items[3]));
  }

  [Fact]
  public void WithNoCurrentRow_PlayedRowsAreTheOffset()
  {
    var items = new List<QueueItemDto> { Row(0, "Played"), Row(1, "Upcoming"), Row(2, "Upcoming") };

    Assert.Equal(0, QueueHistoryPanel.ToQueueIndex(items, items[1]));
    Assert.Equal(1, QueueHistoryPanel.ToQueueIndex(items, items[2]));
  }
}
