using Bunit;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Radzen;
using Radio.Web.Components.Shared;
using Radio.Web.Models;
using Radio.Web.Services;
using Radio.Web.Services.ApiClients;
using Radio.Web.Services.Hub;

namespace Radio.Web.Tests.Components.Shared;

/// <summary>
/// bUnit tests for <see cref="QueueHistoryPanel"/> introduced by PR 3 of the design
/// tightening arc. Focuses on the currently-playing row treatment (amber-left border
/// + ▶ glyph) and absence of raw <see cref="TimeSpan"/> formatting in queue rows.
///
/// Per the FileBrowserDialog test pattern, the QueueApi / HistoryApi clients return
/// null when no server is running, so RefreshQueueAsync sets <c>_queueItems</c> to
/// empty and the queue grid is hidden. To exercise the currently-playing branch we
/// would need to mock the QueueApi — the existing test rig doesn't, so we only assert
/// the empty-state and no-fractional-seconds invariants that hold without a server.
/// </summary>
public class QueueHistoryPanelTests : TestContext
{
  private readonly ILoggerFactory _loggerFactory;

  public QueueHistoryPanelTests()
  {
    _loggerFactory = new NullLoggerFactory();

    JSInterop.Mode = JSRuntimeMode.Loose;

    var configuration = new ConfigurationBuilder()
      .AddInMemoryCollection(new Dictionary<string, string?>
      {
        { "ApiBaseUrl", "http://localhost:5000" }
      })
      .Build();

    Services.AddSingleton<IConfiguration>(configuration);
    Services.AddSingleton(_loggerFactory);
    Services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

    Services.AddRadzenComponents();

    Services.AddHttpClient<QueueApiService>();
    Services.AddHttpClient<PlayHistoryApiService>();
    Services.AddHttpClient<AudioApiService>();
    Services.AddHttpClient<FileApiService>();
    Services.AddHttpClient<PlaylistApiService>();
    Services.AddHttpClient<SourcesApiService>();
    Services.AddHttpClient<ConfigurationApiService>(); // dependency of QueuePersistenceService

    Services.AddSingleton(sp =>
      new AudioStateHubService(
        NullLogger<AudioStateHubService>.Instance,
        sp.GetRequiredService<IConfiguration>()
      )
    );

    Services.AddSingleton<QueuePersistenceService>();
  }

  protected override void Dispose(bool disposing)
  {
    if (disposing)
    {
      _loggerFactory?.Dispose();
    }
    base.Dispose(disposing);
  }

  [Fact]
  public void QueueHistoryPanel_Renders_Without_Errors()
  {
    var cut = RenderComponent<QueueHistoryPanel>();
    Assert.NotNull(cut);
  }

  [Fact]
  public void QueueHistoryPanel_DoesNotEmit_FractionalSecondsInMarkup()
  {
    // Defense in depth: the panel never renders raw TimeSpan.ToString() output. Even with
    // an empty queue / history (the case here, no API server running), the rendered HTML
    // must not contain a "00:00:00.0000000"-style artefact. This catches accidental future
    // regressions that introduce ToString() back into the templates.
    var cut = RenderComponent<QueueHistoryPanel>();
    Assert.DoesNotMatch(@"\d+:\d{2}:\d{2}\.\d{4,}", cut.Markup);
  }

  [Fact]
  public void SumQueueRuntime_EmptyList_ReturnsZero()
  {
    QueueHistoryPanel.SumQueueRuntime(Array.Empty<QueueItemDto>())
      .Should().Be(TimeSpan.Zero);
  }

  [Fact]
  public void SumQueueRuntime_MixedShortFormat_AccumulatesSeconds()
  {
    // "m:ss" rows sum into the total. 3:00 + 4:30 = 7:30.
    var items = new[]
    {
      MakeItem("3:00"),
      MakeItem("4:30"),
    };
    var total = QueueHistoryPanel.SumQueueRuntime(items);
    total.Should().Be(TimeSpan.FromSeconds(180 + 270));
  }

  [Fact]
  public void SumQueueRuntime_LongFormat_AccumulatesHours()
  {
    // "h:mm:ss" rows are passed through directly.
    var items = new[]
    {
      MakeItem("1:30:00"),
      MakeItem("0:15:30"),
    };
    var total = QueueHistoryPanel.SumQueueRuntime(items);
    total.Should().Be(TimeSpan.FromMinutes(90 + 15) + TimeSpan.FromSeconds(30));
  }

  [Fact]
  public void SumQueueRuntime_NullOrEmptyDurations_ContributeZero()
  {
    // Empty or null Duration strings must not throw and must not poison the running total.
    var items = new[]
    {
      MakeItem(""),
      MakeItem(null),
      MakeItem("3:00"),
    };
    QueueHistoryPanel.SumQueueRuntime(items).Should().Be(TimeSpan.FromMinutes(3));
  }

  [Fact]
  public void SumQueueRuntime_UnparseableDuration_Skipped()
  {
    // A future server bug emitting a non-numeric duration must not crash the UI.
    var items = new[]
    {
      MakeItem("not-a-time"),
      MakeItem("2:15"),
    };
    QueueHistoryPanel.SumQueueRuntime(items)
      .Should().Be(TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(15));
  }

  private static QueueItemDto MakeItem(string? duration) => new(
    Index: 0,
    Title: "T",
    Artist: "A",
    Album: "Al",
    Duration: duration,
    IsCurrent: false,
    State: "Upcoming",
    FullPlaylistIndex: 0);
}
