using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Radio.API.Hubs;
using Radio.API.Models;
using Radio.API.Services;
using Radio.Core.Interfaces.Audio;
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

  // ─── attended event playback broadcast (ADR-029 D6 §8.1) ─────────────────

  /// <summary>
  /// Builds the service over a service provider that really contains <paramref name="eventPlayback"/>,
  /// and captures every hub send.
  /// </summary>
  /// <remarks>
  /// ⚠ The capture seam is <c>IClientProxy.SendCoreAsync</c>, not <c>SendAsync</c>. <c>SendAsync</c>
  /// is an extension method and cannot be intercepted by Moq; <c>SendCoreAsync(string, object?[],
  /// CancellationToken)</c> is what it forwards to, and <c>args[0]</c> is the payload. This is the
  /// same seam <c>SleepServiceTests</c> already asserts through.
  ///
  /// ⚠ The fake goes in through a POPULATED ServiceCollection rather than a constructor parameter,
  /// because <see cref="AudioStateUpdateService"/> resolves it with
  /// <c>IServiceProvider.GetService</c>. The existing <c>CreateService()</c> above builds an empty
  /// provider, which is exactly the "not registered at all" case
  /// <see cref="AMissingEventPlaybackServiceDisablesTheBroadcastRatherThanFailingToStart"/> pins.
  /// </remarks>
  private static AudioStateUpdateService CreateServiceWith(
    IEventPlaybackService? eventPlayback,
    Action<string, object?[]>? onSend = null,
    Exception? sendThrows = null)
  {
    var hubContextMock = new Mock<IHubContext<AudioStateHub>>();
    var clientsMock = new Mock<IHubClients>();
    var allClientsMock = new Mock<IClientProxy>();

    var setup = allClientsMock.Setup(c => c.SendCoreAsync(
      It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()));

    if (sendThrows is not null)
    {
      setup.ThrowsAsync(sendThrows);
    }
    else
    {
      setup
        .Callback<string, object?[], CancellationToken>(
          (method, args, _) => onSend?.Invoke(method, args))
        .Returns(Task.CompletedTask);
    }

    clientsMock.SetupGet(c => c.All).Returns(allClientsMock.Object);
    hubContextMock.SetupGet(h => h.Clients).Returns(clientsMock.Object);

    var configuration = new ConfigurationBuilder()
      .AddInMemoryCollection(new Dictionary<string, string?>())
      .Build();

    var collection = new ServiceCollection();
    if (eventPlayback is not null)
    {
      collection.AddSingleton(eventPlayback);
    }

    return new AudioStateUpdateService(
      NullLogger<AudioStateUpdateService>.Instance,
      hubContextMock.Object,
      collection.BuildServiceProvider(),
      configuration);
  }

  /// <summary>
  /// A bounded poll INSIDE a wait, never a sleep before an assertion. The handler is
  /// <c>async void</c>, so the raise returns before the send has necessarily happened; the
  /// rendezvous is on the observation.
  /// </summary>
  private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
  {
    var deadline = DateTime.UtcNow + timeout;
    while (DateTime.UtcNow < deadline)
    {
      if (condition())
      {
        return;
      }
      await Task.Delay(10);
    }
    Assert.Fail($"Condition was not met within {timeout}.");
  }

  private static EventPlaybackSnapshot PlayingSnapshot(string id = "evp-abc") => new(
    id, EventPlaybackKind.RemoteMedia, "Voicemail from Jane",
    EventPlaybackState.Playing, TimeSpan.FromSeconds(29), TimeSpan.Zero,
    DateTimeOffset.UtcNow, null);

  [Fact]
  public async Task EventPlaybackChanged_PutsStateAndKindOnTheWireAsStrings()
  {
    // ⚠ THE C-47 PIN. Radio.API registers JsonStringEnumConverter on
    // AddControllers().AddJsonOptions ONLY; SignalR serialises through
    // JsonHubProtocol.PayloadSerializerOptions, which this project never configures. Handing the
    // snapshot record straight to SendAsync would put "state": 1 on the hub while
    // GET /api/audio/events/current says "state": "Playing" — and ADR-029 §8.1 feeds BOTH into the
    // same client field, the REST call as the seed and this as the update.
    //
    // ⚠ Asserted by SERIALISING the captured payload and reading the JSON, not by reflecting over the
    // anonymous type. Both would catch today's defect; only the JSON states the property that
    // actually matters, which is what a client parses.
    var fake = new FakeEventPlaybackService();
    object? captured = null;

    var service = CreateServiceWith(fake, onSend: (method, args) =>
    {
      if (method == "EventPlaybackChanged")
      {
        captured = args[0];
      }
    });

    fake.Raise(PlayingSnapshot());

    await WaitUntilAsync(() => captured is not null, TimeSpan.FromSeconds(5));

    var json = JsonSerializer.Serialize(
      captured, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    using var doc = JsonDocument.Parse(json);

    Assert.Equal(JsonValueKind.String, doc.RootElement.GetProperty("state").ValueKind);
    Assert.Equal("Playing", doc.RootElement.GetProperty("state").GetString());
    Assert.Equal(JsonValueKind.String, doc.RootElement.GetProperty("kind").ValueKind);
    Assert.Equal("RemoteMedia", doc.RootElement.GetProperty("kind").GetString());

    service.Dispose();
  }

  [Fact]
  public async Task EventPlaybackChanged_RoundTripsIntoTheWebDtoShape()
  {
    // The closest a single assembly can get to proving the ADR §8.1 contract: the payload this
    // service puts on the wire deserialises into the member set Radio.Web's EventPlaybackSnapshotDto
    // declares, with every field intact.
    //
    // ⚠ What it does NOT prove, stated rather than implied: it does not exercise JsonHubProtocol's
    // own serializer options, and no unit test in this repo can — the hub protocol is not reachable
    // from here. U1 and the real wire shape are settled on the box (plan §2.2 item 1). A `state` that
    // arrives there as a number rather than "Playing" is this pin having been got wrong, and it will
    // look like "the chip does not update" rather than like a serialisation fault.
    var fake = new FakeEventPlaybackService();
    object? captured = null;

    var service = CreateServiceWith(fake, onSend: (method, args) =>
    {
      if (method == "EventPlaybackChanged")
      {
        captured = args[0];
      }
    });

    var sent = new EventPlaybackSnapshot(
      "evp-round-trip", EventPlaybackKind.Speech, "Message from Jane",
      EventPlaybackState.Failed, TimeSpan.FromSeconds(7.5), TimeSpan.FromSeconds(2),
      DateTimeOffset.UtcNow, "MediaNotFound");
    fake.Raise(sent);

    await WaitUntilAsync(() => captured is not null, TimeSpan.FromSeconds(5));

    var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    var json = JsonSerializer.Serialize(captured, options);
    var dto = JsonSerializer.Deserialize<WebShapedSnapshot>(json, options);

    Assert.NotNull(dto);
    Assert.Equal("evp-round-trip", dto!.Id);
    Assert.Equal("Speech", dto.Kind);
    Assert.Equal("Message from Jane", dto.Label);
    Assert.Equal("Failed", dto.State);
    Assert.Equal(TimeSpan.FromSeconds(7.5), dto.Duration);
    Assert.Equal(TimeSpan.FromSeconds(2), dto.PositionAtBroadcast);
    Assert.Equal(sent.BroadcastAtUtc, dto.BroadcastAtUtc);
    Assert.Equal("MediaNotFound", dto.FailureReason);

    service.Dispose();
  }

  [Fact]
  public void AMissingEventPlaybackServiceDisablesTheBroadcastRatherThanFailingToStart()
  {
    // GetService, not GetRequiredService — the same posture every sibling collaborator here takes.
    // This service has to start on a box where parts of the audio stack are not registered at all,
    // and a hosted service that throws in its constructor takes the whole host down with it.
    var service = CreateServiceWith(eventPlayback: null);

    Assert.NotNull(service);
    service.Dispose();
  }

  [Fact]
  public async Task DisposeUnsubscribesFromPlaybackChanged()
  {
    // IEventPlaybackService is a SINGLETON, so a missed unsubscribe keeps a disposed hosted service
    // reachable from a live event source for the rest of the process — and it would keep broadcasting
    // through a hub context it no longer owns.
    var fake = new FakeEventPlaybackService();
    var sends = 0;

    var service = CreateServiceWith(fake, onSend: (method, _) =>
    {
      if (method == "EventPlaybackChanged")
      {
        Interlocked.Increment(ref sends);
      }
    });

    fake.Raise(PlayingSnapshot("evp-before"));
    await WaitUntilAsync(() => Volatile.Read(ref sends) == 1, TimeSpan.FromSeconds(5));

    service.Dispose();
    Assert.Equal(0, fake.SubscriberCount);

    fake.Raise(PlayingSnapshot("evp-after"));
    Assert.Equal(1, Volatile.Read(ref sends));
  }

  [Fact]
  public void ASubscriberExceptionDoesNotEscapeTheHandler()
  {
    // The async void hazard, and the reason the catch-all is there. An exception escaping an
    // async void handler is a process-level fault; escaping it here would ALSO be logged by
    // EventPlaybackService.Raise as "a PlaybackChanged subscriber threw" — accurate, but filed
    // against the seam rather than against the broadcaster.
    var fake = new FakeEventPlaybackService();
    var service = CreateServiceWith(fake, sendThrows: new InvalidOperationException("hub is down"));

    var ex = Record.Exception(() => fake.Raise(PlayingSnapshot()));

    Assert.Null(ex);
    service.Dispose();
  }

  /// <summary>Mirrors Radio.Web's EventPlaybackSnapshotDto member-for-member.</summary>
  /// <remarks>
  /// A local copy rather than a project reference: Radio.API.Tests does not reference Radio.Web, and
  /// adding that dependency to assert a wire shape would couple two assemblies that are deliberately
  /// connected only by JSON. If the two ever drift, this test is what notices.
  /// </remarks>
  private sealed record WebShapedSnapshot(
    string Id,
    string? Kind,
    string? Label,
    string? State,
    TimeSpan? Duration,
    TimeSpan PositionAtBroadcast,
    DateTimeOffset BroadcastAtUtc,
    string? FailureReason);
}

/// <summary>
/// The attended-playback seam, reduced to what the broadcast subscriber touches: a retained
/// <see cref="Current"/>, the event, and a <see cref="Raise"/> the test drives.
/// </summary>
/// <remarks>
/// <see cref="SubscriberCount"/> exists so <c>DisposeUnsubscribesFromPlaybackChanged</c> can assert
/// the unsubscribe DIRECTLY rather than only inferring it from a broadcast that did not happen. Both
/// assertions are made; the count is the one that cannot pass for the wrong reason.
/// </remarks>
internal sealed class FakeEventPlaybackService : IEventPlaybackService
{
  private EventHandler<EventPlaybackSnapshot>? _handlers;

  public EventPlaybackSnapshot? Current { get; set; }

  public List<string> StopIds { get; } = [];

  public int SubscriberCount => _handlers?.GetInvocationList().Length ?? 0;

  public event EventHandler<EventPlaybackSnapshot>? PlaybackChanged
  {
    add => _handlers += value;
    remove => _handlers -= value;
  }

  public void Raise(EventPlaybackSnapshot snapshot)
  {
    Current = snapshot;
    _handlers?.Invoke(this, snapshot);
  }

  public Task<EventPlaybackSnapshot> StartAsync(
    EventPlaybackRequest request, CancellationToken cancellationToken = default)
    => throw new NotSupportedException("PHN-1e's subscribers never start a playback.");

  public Task<bool> StopAsync(string playbackId, CancellationToken cancellationToken = default)
  {
    StopIds.Add(playbackId);
    return Task.FromResult(true);
  }

  public Task<bool> SeekAsync(
    string playbackId, TimeSpan position, CancellationToken cancellationToken = default)
    => Task.FromResult(false);

  public Task<bool> PauseAsync(string playbackId, CancellationToken cancellationToken = default)
    => Task.FromResult(false);

  public Task<bool> ResumeAsync(string playbackId, CancellationToken cancellationToken = default)
    => Task.FromResult(false);
}
