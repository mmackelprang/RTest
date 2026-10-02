using FluentAssertions;
using Radzen;
using Radio.Web.Services;

namespace Radio.Web.Tests.Services;

/// <summary>UI-32: queueing a folder's tracks in batches, and the toast that reports it.</summary>
public class FolderQueueAdderTests
{
  private static List<string> Paths(int n) => Enumerable.Range(1, n).Select(i => $"/m/{i:D3}.mp3").ToList();

  [Fact]
  public async Task SendsEveryPath_InOrder_InBatchesOfTheGivenSize_ReportingProgressAfterEach()
  {
    var sent = new List<List<string>>();
    var progress = new List<int>();

    var outcome = await FolderQueueAdder.AddInBatchesAsync(
      Paths(45), 20,
      (batch, _) => { sent.Add(batch); return Task.FromResult((true, batch.Count, (string?)null)); },
      done => { progress.Add(done); return Task.CompletedTask; },
      CancellationToken.None);

    sent.Select(b => b.Count).Should().Equal(20, 20, 5);
    sent.SelectMany(b => b).Should().Equal(Paths(45), "batches keep the listing's order");
    progress.Should().Equal(20, 40, 45);
    outcome.Should().Be(new FolderQueueAdder.Outcome(45, 0, true, null));
  }

  [Fact]
  public async Task PartlyAcceptedBatches_CountTheRestAsFailed_AndCarryOn()
  {
    var outcome = await FolderQueueAdder.AddInBatchesAsync(
      Paths(30), 20,
      (batch, _) => Task.FromResult((true, batch.Count - 1, (string?)"1 files failed")),
      _ => Task.CompletedTask,
      CancellationToken.None);

    outcome.Should().Be(new FolderQueueAdder.Outcome(28, 2, true, null));
  }

  [Fact]
  public async Task ABatchWithNothingAdded_StopsTheRun_WithItsError()
  {
    var calls = 0;

    var outcome = await FolderQueueAdder.AddInBatchesAsync(
      Paths(60), 20,
      (batch, _) => ++calls == 1
        ? Task.FromResult((true, batch.Count, (string?)null))
        : Task.FromResult((false, 0, (string?)"Server returned 500")),
      _ => Task.CompletedTask,
      CancellationToken.None);

    calls.Should().Be(2, "nothing is sent after the failing batch");
    outcome.Should().Be(new FolderQueueAdder.Outcome(20, 0, false, "Server returned 500"));
  }

  [Fact]
  public async Task Cancelled_StopsBeforeTheNextBatch_WithoutAnError()
  {
    using var cts = new CancellationTokenSource();
    var calls = 0;

    var outcome = await FolderQueueAdder.AddInBatchesAsync(
      Paths(60), 20,
      (batch, _) => { calls++; cts.Cancel(); return Task.FromResult((true, batch.Count, (string?)null)); },
      _ => Task.CompletedTask,
      cts.Token);

    calls.Should().Be(1);
    outcome.Should().Be(new FolderQueueAdder.Outcome(20, 0, false, null));
  }

  [Fact]
  public async Task NoPaths_SendsNothing()
  {
    var calls = 0;
    var outcome = await FolderQueueAdder.AddInBatchesAsync(
      new List<string>(), 20,
      (batch, _) => { calls++; return Task.FromResult((true, batch.Count, (string?)null)); },
      _ => Task.CompletedTask,
      CancellationToken.None);

    calls.Should().Be(0);
    outcome.Completed.Should().BeTrue();
  }

  [Theory]
  [InlineData(68, 68, 0, null, NotificationSeverity.Success, "Added 68 tracks from ABBA")]
  [InlineData(1, 1, 0, null, NotificationSeverity.Success, "Added 1 track from ABBA")]
  [InlineData(64, 68, 4, null, NotificationSeverity.Success, "Added 64 tracks from ABBA · 4 skipped (unsupported or unreadable)")]
  [InlineData(20, 68, 0, "Server returned 500", NotificationSeverity.Warning, "Added 20 of 68 tracks from ABBA before an error. (Server returned 500)")]
  [InlineData(0, 68, 0, "Server returned 500", NotificationSeverity.Error, "Couldn't add ABBA. Nothing was added. (Server returned 500)")]
  [InlineData(0, 0, 0, null, NotificationSeverity.Warning, "No playable tracks in ABBA")]
  public void Describe_TheToastForEachOutcome(
    int added, int requested, int skipped, string? error, NotificationSeverity severity, string detail)
  {
    var (actualSeverity, _, actualDetail) =
      FolderQueueAdder.Describe(new FolderAddResult("ABBA", added, requested, skipped, error));

    actualSeverity.Should().Be(severity);
    actualDetail.Should().Be(detail);
  }
}
