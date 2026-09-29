using FluentAssertions;
using Radzen;
using Radio.Web.Components.Dialogs;
using Radio.Web.Models;
using Xunit;

namespace Radio.Web.Tests.Components.Dialogs;

/// <summary>
/// The load-playlist notification must report what reached the queue, not the playlist's saved size. It used to
/// print <c>ItemCount</c>, so a playlist whose files had all moved showed "Loaded 'Beatles' (31 tracks)" over an
/// empty queue.
/// </summary>
public class LoadPlaylistDialogTests
{
  [Fact]
  public void DescribeLoad_AllLoaded_IsSuccessWithLoadedCount()
  {
    var (severity, _, detail) = LoadPlaylistDialog.DescribeLoad("Local", new PlaylistLoadResultDto { Loaded = 24, Skipped = 0 });

    severity.Should().Be(NotificationSeverity.Success);
    detail.Should().Be("Loaded 'Local' (24 tracks)");
  }

  [Fact]
  public void DescribeLoad_SomeMissing_IsWarningWithBothCounts()
  {
    var (severity, _, detail) = LoadPlaylistDialog.DescribeLoad("Mix", new PlaylistLoadResultDto { Loaded = 20, Skipped = 3 });

    severity.Should().Be(NotificationSeverity.Warning);
    detail.Should().Be("Loaded 20 of 23 tracks from 'Mix' (3 files not found)");
  }

  [Fact]
  public void DescribeLoad_AllMissing_IsErrorAndSaysTheQueueIsUnchanged()
  {
    var (severity, _, detail) = LoadPlaylistDialog.DescribeLoad("Beatles", new PlaylistLoadResultDto { Loaded = 0, Skipped = 31 });

    severity.Should().Be(NotificationSeverity.Error);
    detail.Should().Be("All 31 files in 'Beatles' are missing. The queue was left unchanged.");
  }
}
