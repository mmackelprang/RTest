using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Audio.Outputs;
using Sharpcaster;
using Sharpcaster.Channels;
using Sharpcaster.Models;
using Xunit;

namespace Radio.Infrastructure.Tests.Audio.Outputs;

/// <summary>
/// AUD-54: Cast lifecycle defects in <see cref="GoogleCastOutput"/> — subscriptions, channel
/// registrations and state that leaked or were left half-cleared across a connect, a device
/// switch or a stop.
/// </summary>
/// <remarks>
/// Connections are made offline the way <c>GoogleCastOutputConcurrencyTests</c> and
/// <c>GoogleCastOutputConnectionLossTests</c> make them: the transport and status-read overrides
/// stand in for a Cast handshake no fake socket can complete. Where a test needs the client to be
/// REUSED across connects (the live-discovery path), it seeds the discovered-receiver cache
/// directly, because discovery itself is mDNS. No assertion depends on elapsed time.
/// </remarks>
public class GoogleCastOutputLifecycleTests
{
  [Fact]
  public async Task SwitchingDevices_OnAReusedClient_LeavesExactlyOneReceiverStatusHandler_AndNoneAfterDisconnect()
  {
    await using var output = await InitializedOutputAsync();
    SeedLiveReceiver(output, "cast-a", "10.0.0.1");
    SeedLiveReceiver(output, "cast-b", "10.0.0.2");

    await output.ConnectAsync(Device("cast-a", "10.0.0.1"));
    var client = ClientOf(output);
    Assert.Equal(1, ReceiverStatusHandlerCount(client, output));

    await output.ConnectAsync(Device("cast-b", "10.0.0.2"));

    // The defect needs the SAME client for both connects; prove the test reached that path.
    Assert.Same(client, ClientOf(output));
    Assert.Equal("cast-b", output.ConnectedDevice?.Id);
    Assert.Equal(1, ReceiverStatusHandlerCount(client, output));

    await output.DisconnectAsync();
    Assert.Equal(0, ReceiverStatusHandlerCount(client, output));
  }

  [Fact]
  public async Task RegisteringTheDirectChannelRepeatedly_ReplacesIt_SoTheChannelCountStaysConstant()
  {
    // Each StartAsync in DirectChannel mode registers a new DirectCastAudioChannel on the
    // (reused) client. SharpCaster routes inbound messages to the FIRST channel with a
    // matching namespace, so an appended duplicate is one the live service never hears from.
    await using var output = await InitializedOutputAsync();
    var client = new ChromecastClient();
    var baseline = ChannelsOf(client).Count;
    const string ns = "urn:x-cast:test.audio";

    var first = new DirectCastAudioChannel(ns, NullLoggerFor());
    output.RegisterCustomChannel(client, first);
    var second = new DirectCastAudioChannel(ns, NullLoggerFor());
    output.RegisterCustomChannel(client, second);
    var third = new DirectCastAudioChannel(ns, NullLoggerFor());
    output.RegisterCustomChannel(client, third);

    var channels = ChannelsOf(client);
    Assert.Equal(baseline + 1, channels.Count);
    Assert.Same(third, Assert.Single(channels, c => c.Namespace == ns));
  }

  [Fact]
  public async Task UnregisteringTheDirectChannel_RemovesOnlyThatChannel()
  {
    await using var output = await InitializedOutputAsync();
    var client = new ChromecastClient();
    var baseline = ChannelsOf(client).Count;
    var channel = new DirectCastAudioChannel("urn:x-cast:test.audio", NullLoggerFor()) { Client = client };
    output.RegisterCustomChannel(client, channel);

    output.UnregisterCustomChannel(channel);
    output.UnregisterCustomChannel(channel); // idempotent

    var channels = ChannelsOf(client);
    Assert.Equal(baseline, channels.Count);
    Assert.DoesNotContain(channels, c => ReferenceEquals(c, channel));
    Assert.NotNull(client.GetChannel<ReceiverChannel>()); // SharpCaster's own channels untouched
  }

  [Fact]
  public async Task TestPlayUrl_WhileStreaming_IsRefusedWithoutTouchingTheSession()
  {
    // The test playback relaunches the receiver app; on a live DirectChannel session that
    // invalidates the transport id the streaming loop is sending to.
    await using var output = await InitializedOutputAsync();
    SeedLiveReceiver(output, "cast-a", "10.0.0.1");
    await output.ConnectAsync(Device("cast-a", "10.0.0.1"));
    MarkStreaming(output);

    // Hang guard only: unrefused, the call waits on a LAUNCH reply no offline client gets.
    var result = await output.TestPlayUrlAsync("http://example.invalid/a.mp3", "audio/mpeg")
      .WaitAsync(TimeSpan.FromSeconds(10));

    Assert.False(ReadProperty<bool>(result, "success"));
    Assert.Contains("stop casting", ReadProperty<string>(result, "error"));
    Assert.Equal(AudioOutputState.Streaming, output.State);
    Assert.Equal("cast-a", output.ConnectedDevice?.Id);
  }

  [Fact]
  public async Task Stop_WhenAStepOutsideTheMediaStopThrows_StillLeavesTheOutputDisabled()
  {
    // A pre-cancelled token makes StopAsync's lock wait throw — one of the steps that sits
    // outside the media-stop try and lands in the outer catch.
    await using var output = await InitializedOutputAsync(configure: o => o.Enabled = true);
    SeedLiveReceiver(output, "cast-a", "10.0.0.1");
    await output.ConnectAsync(Device("cast-a", "10.0.0.1"));
    MarkStreaming(output);
    Assert.True(output.IsEnabled);

    using var cancelled = new CancellationTokenSource();
    cancelled.Cancel();
    await output.StopAsync(cancelled.Token);

    Assert.Equal(AudioOutputState.Stopped, output.State);
    Assert.False(output.IsEnabled);
  }

  [Fact]
  public async Task Stop_CancelsAMetadataReloadStillWaitingOutItsDebounce()
  {
    await using var output = await StreamingOutputWithPendingMetadataUpdateAsync();
    var pending = PendingMetadataDebounce(output);
    Assert.False(pending.IsCancellationRequested);

    await output.StopAsync();

    // Asserted on the debounce's own source, not on the 3 s reload not happening: no timing.
    Assert.True(pending.IsCancellationRequested);
    Assert.Null(PendingMetadataDebounceOrNull(output));
  }

  [Fact]
  public async Task Disconnect_CancelsAMetadataReloadStillWaitingOutItsDebounce()
  {
    await using var output = await StreamingOutputWithPendingMetadataUpdateAsync();
    var pending = PendingMetadataDebounce(output);

    await output.DisconnectAsync();

    Assert.True(pending.IsCancellationRequested);
    Assert.Null(PendingMetadataDebounceOrNull(output));
  }

  [Fact]
  public async Task Stop_WithNoMediaSessionOnTheConnection_LogsNothingAtWarning()
  {
    // DirectChannel never loads media, so the client has no media status and SharpCaster's
    // MediaChannel.StopAsync throws "MediaSessionID is not available". Measured on the box as a
    // Warning on every teardown, where journald volume correlates with audio distortion.
    var logger = new CapturingLogger<GoogleCastOutput>();
    await using var output = await InitializedOutputAsync(logger, o => o.StreamingMode = "DirectChannel");
    SeedLiveReceiver(output, "cast-a", "10.0.0.1");
    await output.ConnectAsync(Device("cast-a", "10.0.0.1"));
    MarkStreaming(output);
    logger.Entries.Clear();

    await output.StopAsync();

    Assert.Equal(AudioOutputState.Stopped, output.State);
    Assert.Empty(logger.AtOrAbove(LogLevel.Warning));
  }

  [Fact]
  public async Task Disconnect_AfterAHandledConnectionLoss_LogsNothingAtWarning()
  {
    // AUD-84 follow-up 3: the loss handler has already cleared the connection, so the
    // teardown that follows finds nothing connected. That is expected, not a warning.
    var logger = new CapturingLogger<GoogleCastOutput>();
    await using var output = await InitializedOutputAsync(logger);
    SeedLiveReceiver(output, "cast-a", "10.0.0.1");
    await output.ConnectAsync(Device("cast-a", "10.0.0.1"));
    MarkStreaming(output);

    output.ReportConnectionLost(PublishedGeneration(output), "test loss", null);
    await output.LastConnectionLossHandling.WaitAsync(TimeSpan.FromSeconds(15));
    Assert.Null(output.ConnectedDevice);
    logger.Entries.Clear();

    await output.DisconnectAsync();

    Assert.Empty(logger.AtOrAbove(LogLevel.Warning));
  }

  // --- helpers ---

  private static int PublishedGeneration(GoogleCastOutput output)
  {
    var field = typeof(GoogleCastOutput).GetField("_publishedGeneration", BindingFlags.NonPublic | BindingFlags.Instance);
    return (int)field!.GetValue(output)!;
  }

  private static async Task<GoogleCastOutput> StreamingOutputWithPendingMetadataUpdateAsync()
  {
    var output = await InitializedOutputAsync();
    SeedLiveReceiver(output, "cast-a", "10.0.0.1");
    await output.ConnectAsync(Device("cast-a", "10.0.0.1"));
    output.SetStreamUrl("http://127.0.0.1:1/stream/audio/mp3");
    MarkStreaming(output);
    await output.UpdateNowPlayingMetadataAsync("Title", "Artist", "Album", null);
    return output;
  }

  private static CancellationTokenSource PendingMetadataDebounce(GoogleCastOutput output)
  {
    var cts = PendingMetadataDebounceOrNull(output);
    Assert.NotNull(cts);
    return cts!;
  }

  private static CancellationTokenSource? PendingMetadataDebounceOrNull(GoogleCastOutput output)
  {
    var field = typeof(GoogleCastOutput).GetField("_metadataDebouncesCts", BindingFlags.NonPublic | BindingFlags.Instance);
    Assert.NotNull(field);
    return (CancellationTokenSource?)field!.GetValue(output);
  }

  private static T ReadProperty<T>(object anonymous, string name)
  {
    var prop = anonymous.GetType().GetProperty(name);
    Assert.NotNull(prop);
    return (T)prop!.GetValue(anonymous)!;
  }

  private static ILogger NullLoggerFor() => Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

  internal static List<Sharpcaster.Interfaces.IChromecastChannel> ChannelsOf(ChromecastClient client)
  {
    var prop = typeof(ChromecastClient).GetProperty("Channels", BindingFlags.NonPublic | BindingFlags.Instance);
    Assert.NotNull(prop);
    return ((IEnumerable<Sharpcaster.Interfaces.IChromecastChannel>)prop!.GetValue(client)!).ToList();
  }

  internal static async Task<GoogleCastOutput> InitializedOutputAsync(
    ILogger<GoogleCastOutput>? logger = null,
    Action<GoogleCastOutputOptions>? configure = null)
  {
    var options = new AudioOutputOptions();
    options.GoogleCast.CacheFilePath =
      Path.Combine(Path.GetTempPath(), $"cast-cache-{Guid.NewGuid():N}.json");
    configure?.Invoke(options.GoogleCast);
    var output = new GoogleCastOutput(
      logger ?? new CapturingLogger<GoogleCastOutput>(), Options.Create(options));

    await output.InitializeAsync();
    output.ConnectTransportOverrideForTests = _ => Task.CompletedTask;
    output.CastStatusReadOverrideForTests = () => Task.FromResult<(float Volume, bool Muted)?>(null);
    return output;
  }

  internal static ChromecastDeviceInfo Device(string id, string ip) => new()
  {
    Id = id,
    FriendlyName = $"Speaker {id}",
    IpAddress = ip,
    Port = 8009,
    Model = "test"
  };

  /// <summary>Puts a receiver where live discovery would, so ConnectAsync reuses the client.</summary>
  internal static void SeedLiveReceiver(GoogleCastOutput output, string id, string ip)
  {
    var field = typeof(GoogleCastOutput).GetField("_discoveredReceivers", BindingFlags.NonPublic | BindingFlags.Instance);
    var receivers = (IDictionary<string, ChromecastReceiver>)field!.GetValue(output)!;
    receivers[id] = new ChromecastReceiver
    {
      DeviceUri = new Uri($"https://{ip}:8009"),
      Port = 8009,
      Name = $"Speaker {id}",
      Model = "test"
    };
  }

  internal static ChromecastClient ClientOf(GoogleCastOutput output)
  {
    var field = typeof(GoogleCastOutput).GetField("_client", BindingFlags.NonPublic | BindingFlags.Instance);
    return (ChromecastClient)field!.GetValue(output)!;
  }

  /// <summary>
  /// How many times <paramref name="output"/>'s handler is attached to the client's
  /// <see cref="ReceiverChannel.ReceiverStatusChanged"/> — read from the event's backing
  /// delegate, since raising the event would also run the echo filter.
  /// </summary>
  internal static int ReceiverStatusHandlerCount(ChromecastClient client, GoogleCastOutput output)
  {
    var channel = client.GetChannel<ReceiverChannel>();
    var field = typeof(ReceiverChannel).GetField(
      nameof(ReceiverChannel.ReceiverStatusChanged), BindingFlags.NonPublic | BindingFlags.Instance);
    Assert.NotNull(field);
    var handlers = (Delegate?)field!.GetValue(channel);
    return handlers?.GetInvocationList().Count(d => ReferenceEquals(d.Target, output)) ?? 0;
  }

  /// <summary>Stands in for StartAsync, which needs a launched receiver application.</summary>
  internal static void MarkStreaming(GoogleCastOutput output)
  {
    typeof(AudioOutputBase).GetProperty(nameof(AudioOutputBase.State))!.SetValue(output, AudioOutputState.Streaming);
    Assert.Equal(AudioOutputState.Streaming, output.State);
  }
}

/// <summary>Records every log call's level and rendered message.</summary>
internal sealed class CapturingLogger<T> : ILogger<T>
{
  public ConcurrentQueue<(LogLevel Level, string Message)> Entries { get; } = new();

  public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

  public bool IsEnabled(LogLevel logLevel) => true;

  public void Log<TState>(
    LogLevel logLevel, EventId eventId, TState state, Exception? exception,
    Func<TState, Exception?, string> formatter)
  {
    Entries.Enqueue((logLevel, formatter(state, exception)));
  }

  public IReadOnlyList<(LogLevel Level, string Message)> AtOrAbove(LogLevel level) =>
    Entries.Where(e => e.Level >= level).ToList();
}
