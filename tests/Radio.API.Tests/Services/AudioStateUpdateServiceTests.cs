using System.Reflection;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Radio.API.Hubs;
using Radio.API.Models;
using Radio.API.Services;
using Radio.Core.Models.Audio;

namespace Radio.API.Tests.Services;

/// <summary>
/// Direct unit tests for the private <c>UpdateCurrentMatchAnchor</c> method of
/// <see cref="AudioStateUpdateService"/> (Task #15 PR B, handoff item #33).
///
/// The anchor logic was previously only exercised through the full SignalR +
/// HostedService plumbing, leaving the three corner-cases unverified:
///
/// <list type="bullet">
///   <item>Empty event list → anchor cleared to null.</item>
///   <item>Mixed events with a no-match at the tail → anchor stays on the
///         most-recent matched event (latest-first scan).</item>
///   <item>All-no-match events → anchor cleared to null.</item>
/// </list>
///
/// We instantiate the service with a mocked <c>IHubContext</c> + empty
/// service provider; the hosted-service lifecycle (ExecuteAsync) is never
/// started — we reach into the method via reflection to exercise its
/// branches deterministically.
/// </summary>
public class AudioStateUpdateServiceTests
{
  private static AudioStateUpdateService CreateService()
  {
    var hubContextMock = new Mock<IHubContext<AudioStateHub>>();
    var clientsMock = new Mock<IHubClients>();
    var allClientsMock = new Mock<IClientProxy>();
    clientsMock.SetupGet(c => c.All).Returns(allClientsMock.Object);
    hubContextMock.SetupGet(h => h.Clients).Returns(clientsMock.Object);

    var configuration = new ConfigurationBuilder()
      .AddInMemoryCollection(new Dictionary<string, string?>())
      .Build();

    var services = new ServiceCollection().BuildServiceProvider();

    return new AudioStateUpdateService(
      NullLogger<AudioStateUpdateService>.Instance,
      hubContextMock.Object,
      services,
      configuration);
  }

  private static void InvokeUpdateCurrentMatchAnchor(
    AudioStateUpdateService svc,
    FingerprintStatusSnapshot snapshot)
  {
    var method = typeof(AudioStateUpdateService).GetMethod(
      "UpdateCurrentMatchAnchor",
      BindingFlags.NonPublic | BindingFlags.Instance);
    Assert.NotNull(method);
    method!.Invoke(svc, new object[] { snapshot });
  }

  private static string? ReadCurrentMatchId(AudioStateUpdateService svc)
  {
    var field = typeof(AudioStateUpdateService).GetField(
      "_currentMatchId",
      BindingFlags.NonPublic | BindingFlags.Instance);
    Assert.NotNull(field);
    return (string?)field!.GetValue(svc);
  }

  private static FingerprintEventRecord Matched(string matchId) => new()
  {
    MatchId = matchId,
    AudioSource = "Test",
    SourceType = "Radio",
    IsMatch = true,
    Count = 1,
    Title = $"Song {matchId}",
    Artist = "Test Artist",
    Phase = FingerprintPhase.Matched,
    Timestamp = DateTime.UtcNow.AddSeconds(-30),
  };

  private static FingerprintEventRecord NoMatch() => new()
  {
    MatchId = Guid.NewGuid().ToString("n"),
    AudioSource = "Test",
    SourceType = "Radio",
    IsMatch = false,
    Count = 1,
    Phase = FingerprintPhase.NoMatch,
    Timestamp = DateTime.UtcNow,
  };

  [Fact]
  public void UpdateCurrentMatchAnchor_EmptyEventList_ReturnsNull()
  {
    // Snapshot carries zero events → anchor must clear to null. This is the
    // cold-start / source-just-switched case where the recognition stream
    // has no history yet.
    var svc = CreateService();
    var snapshot = new FingerprintStatusSnapshot
    {
      Phase = FingerprintPhase.Idle,
      IsEnabled = true,
      RecentEvents = Array.Empty<FingerprintEventRecord>(),
    };

    InvokeUpdateCurrentMatchAnchor(svc, snapshot);

    Assert.Null(ReadCurrentMatchId(svc));
  }

  [Fact]
  public void UpdateCurrentMatchAnchor_NoMatchAtTail_KeepsLastMatchAnchored()
  {
    // The latest-first scan ignores trailing no-match events and anchors on
    // the most-recent matched event still in the snapshot. Without this
    // behaviour, a single fresh no-match capture would blank the NOW row
    // mid-track — exactly the bug §P2 of the Arc 2 spec called out.
    var svc = CreateService();
    var snapshot = new FingerprintStatusSnapshot
    {
      Phase = FingerprintPhase.NoMatch,
      IsEnabled = true,
      RecentEvents = new List<FingerprintEventRecord>
      {
        Matched("anchor-target"),
        NoMatch(),
      },
    };

    InvokeUpdateCurrentMatchAnchor(svc, snapshot);

    Assert.Equal("anchor-target", ReadCurrentMatchId(svc));
  }

  [Fact]
  public void UpdateCurrentMatchAnchor_AllNoMatch_ClearsAnchor()
  {
    // Snapshot full of no-match events (or just one no-match) and no
    // matched events anywhere → anchor clears so the UI doesn't claim a
    // stale row as "now playing".
    var svc = CreateService();
    var snapshot = new FingerprintStatusSnapshot
    {
      Phase = FingerprintPhase.NoMatch,
      IsEnabled = true,
      RecentEvents = new List<FingerprintEventRecord>
      {
        NoMatch(),
        NoMatch(),
        NoMatch(),
      },
    };

    InvokeUpdateCurrentMatchAnchor(svc, snapshot);

    Assert.Null(ReadCurrentMatchId(svc));
  }

  // ─── RDS broadcast-split predicate (Item A) ───────────────────────────────
  // HasRadioStateChanged keeps deciding WHETHER to broadcast (telemetry
  // consumers — signal meter, gain, recognition NOW-row — stay fed). The new
  // HasRdsRelevantChanged predicate decides the per-broadcast flag value that
  // lets the Web RDS marquee path skip its accumulator append on telemetry-only
  // ticks. Both are private static — reached via reflection like the existing
  // anchor tests above.

  private static bool InvokeHasRdsRelevantChanged(RadioStateDto? prev, RadioStateDto? curr)
  {
    var m = typeof(AudioStateUpdateService).GetMethod(
      "HasRdsRelevantChanged",
      BindingFlags.NonPublic | BindingFlags.Static);
    Assert.NotNull(m);
    return (bool)m!.Invoke(null, new object?[] { prev, curr })!;
  }

  private static bool InvokeHasRadioStateChanged(RadioStateDto? prev, RadioStateDto? curr)
  {
    var m = typeof(AudioStateUpdateService).GetMethod(
      "HasRadioStateChanged",
      BindingFlags.NonPublic | BindingFlags.Static);
    Assert.NotNull(m);
    return (bool)m!.Invoke(null, new object?[] { prev, curr })!;
  }

  [Fact]
  public void RdsRelevant_False_OnTelemetryOnlyChange()
  {
    // RSSI / signal-strength drift every poll on a live station. That MUST still
    // broadcast (the meter needs it) but MUST NOT flag the RDS path — otherwise
    // the RDS accumulator re-runs + the CSS marquee restarts ~twice a second.
    var prev = new RadioStateDto { Frequency = 105_100_000, Band = "FM", RssiDbu = 0, SignalStrength = 40, RdsRadioText = "Hotel California" };
    var curr = new RadioStateDto { Frequency = 105_100_000, Band = "FM", RssiDbu = 5, SignalStrength = 60, RdsRadioText = "Hotel California" };

    Assert.True(InvokeHasRadioStateChanged(prev, curr));   // still broadcasts for the signal meter
    Assert.False(InvokeHasRdsRelevantChanged(prev, curr)); // but does NOT flag the RDS path
  }

  [Fact]
  public void RdsRelevant_True_OnRadioTextChange()
  {
    var prev = new RadioStateDto { Frequency = 105_100_000, Band = "FM", RdsRadioText = "Hotel California" };
    var curr = new RadioStateDto { Frequency = 105_100_000, Band = "FM", RdsRadioText = "Life in the Fast Lane" };

    Assert.True(InvokeHasRdsRelevantChanged(prev, curr));
  }

  [Fact]
  public void RdsRelevant_True_OnFrequencyTune()
  {
    var prev = new RadioStateDto { Frequency = 105_100_000, Band = "FM" };
    var curr = new RadioStateDto { Frequency = 98_500_000, Band = "FM" };

    Assert.True(InvokeHasRdsRelevantChanged(prev, curr));
  }

  [Fact]
  public void RdsRelevant_True_OnNowPlayingMatchIdChange()
  {
    var prev = new RadioStateDto { Frequency = 105_100_000, Band = "FM", NowPlayingMatchId = null };
    var curr = new RadioStateDto { Frequency = 105_100_000, Band = "FM", NowPlayingMatchId = "abc123" };

    Assert.True(InvokeHasRdsRelevantChanged(prev, curr));
  }

  [Fact]
  public void RdsRelevant_True_WhenPreviousNull()
  {
    // First broadcast after a tune / source-switch (no baseline) must populate
    // the RDS card immediately.
    var curr = new RadioStateDto { Frequency = 105_100_000, Band = "FM" };

    Assert.True(InvokeHasRdsRelevantChanged(null, curr));
  }
}
