using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Radio.Web.Models;
using Radio.Web.Services;
using Radio.Web.Services.Hub;
using Radio.Web.Tests.TestHelpers;

namespace Radio.Web.Tests.Services;

/// <summary>
/// ConsolePlaybackState — the ONE subscriber to AudioStateStore.EventPlaybackChanged (PHN-2 §0.6).
/// </summary>
/// <remarks>
/// ⚠ The broadcast half is driven by calling <c>OnHubEventPlaybackChanged</c> directly, which is
/// internal for exactly this reason, the same family as AudioStateStoreEventPlaybackTests. The hub
/// service is constructed over <see cref="OfflineHubTransport"/> and never started, so nothing here
/// opens a socket.
///
/// ⚠ There is deliberately NO dispose-unsubscribe test. The obvious one reflects over
/// AudioStateStore's event backing field, and a Func&lt;Task&gt;? event with explicit accessors would
/// make it pass vacuously. Five consecutive cycles in this arc have found a test that passed against
/// a deliberately broken implementation, and an assertion that cannot fail is how that keeps
/// happening. The unsubscribe is covered by inspection and by the PR's grep 4.
/// </remarks>
public class ConsolePlaybackStateTests
{
  private static AudioStateStore NewStore() =>
    new(
      NullLogger<AudioStateStore>.Instance,
      new AudioStateHubService(
        NullLogger<AudioStateHubService>.Instance,
        new ConfigurationBuilder().Build(),
        transport: new OfflineHubTransport()));

  private static EventPlaybackSnapshotDto SnapshotOf(string state) =>
    new("evp-1", "RemoteMedia", "Voicemail from Jane", state, TimeSpan.FromSeconds(30),
      TimeSpan.Zero, DateTimeOffset.UtcNow, null);

  [Fact]
  public async Task ItSubscribesToTheStoreExactlyOnce_NoMatterHowManySubscribersItHasItself()
  {
    var store = NewStore();
    using var state = new ConsolePlaybackState(store, NullLogger<ConsolePlaybackState>.Instance);

    var first = 0;
    var second = 0;
    state.Changed += () => { first++; return Task.CompletedTask; };
    state.Changed += () => { second++; return Task.CompletedTask; };

    await store.OnHubEventPlaybackChanged(SnapshotOf("Playing"));

    Assert.Equal(1, first);
    Assert.Equal(1, second);
  }

  [Fact]
  public async Task AThrowingSubscriberDoesNotStarveTheOnesRegisteredAfterIt()
  {
    var store = NewStore();
    using var state = new ConsolePlaybackState(store, NullLogger<ConsolePlaybackState>.Instance);

    var reached = false;
    // ⚠ Throws SYNCHRONOUSLY, before any await. That is the half of UI-6 that is starvation rather
    // than a lost log line, and it is the half this class exists not to reproduce.
    state.Changed += () => throw new InvalidOperationException("boom");
    state.Changed += () => { reached = true; return Task.CompletedTask; };

    await store.OnHubEventPlaybackChanged(SnapshotOf("Playing"));

    Assert.True(reached);
  }

  [Fact]
  public async Task ATerminalSnapshotIsRetained_SoNothingPlayingIsAStateAndNotAnAbsence()
  {
    var store = NewStore();
    using var state = new ConsolePlaybackState(store, NullLogger<ConsolePlaybackState>.Instance);

    await store.OnHubEventPlaybackChanged(SnapshotOf("Playing"));
    Assert.True(state.IsLive);

    await store.OnHubEventPlaybackChanged(SnapshotOf("Completed"));
    Assert.False(state.IsLive);
    Assert.NotNull(state.Snapshot);
  }

  [Theory]
  [InlineData("RemoteMedia", "Voicemail")]
  [InlineData("Speech", "Message")]
  // A kind this build has never heard of must not paint a raw wire token on the panel.
  [InlineData("SomethingNewer", "Playing")]
  [InlineData(null, "Playing")]
  public async Task TheChipLabelIsTheKind_AndAnUnknownKindDegrades(string? kind, string expected)
  {
    var store = NewStore();
    using var state = new ConsolePlaybackState(store, NullLogger<ConsolePlaybackState>.Instance);

    await store.OnHubEventPlaybackChanged(
      new EventPlaybackSnapshotDto("evp-1", kind, "L", "Playing", null, TimeSpan.Zero,
        DateTimeOffset.UtcNow, null));

    Assert.Equal(expected, state.KindLabel);
  }
}
