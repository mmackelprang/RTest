using System.Net;
using System.Net.Sockets;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Audio.Outputs;
using Sharpcaster;
using Sharpcaster.Models.ChromecastStatus;
using Xunit;

namespace Radio.Infrastructure.Tests.Audio.Outputs;

/// <summary>
/// AUD-37 (final review M-1): a <see cref="GoogleCastOutput.StartAsync(bool, CancellationToken)"/>
/// whose connection is torn down while it is starting must not go on to stream. Before the guard,
/// the reconnect watcher's uninterruptible start, overtaken by a user's teardown, carried on to
/// <c>DirectCastStreamingService.Start()</c> and <c>Streaming</c> on a connection nobody owned —
/// a send loop into a closed socket, with its loss report disarmed, until the next Cast connect.
/// </summary>
/// <remarks>
/// The connection is made through the real <c>ConnectAsync</c> (transport and status read
/// substituted, as in <c>GoogleCastOutputConnectionLossTests</c>), and the launch is substituted
/// (<c>CastLaunchApplicationOverrideForTests</c>), so the start runs the real mode branch,
/// start-time mute, channel registration and streaming service. Each test parks the start at one
/// network seam, runs a <c>DisconnectAsync</c> to completion there, and only then releases it —
/// a rendezvous, not a race: nothing is timed. One test per check the start makes, so removing
/// any one of them fails its test.
/// </remarks>
public class GoogleCastOutputStartSupersededTests : IDisposable
{
  private static readonly TimeSpan HangGuard = TimeSpan.FromSeconds(15);

  private readonly TcpListener _listener;
  private readonly int _port;
  private readonly Mock<IAudioEngine> _engine = new();
  private int _readersCreated;

  public GoogleCastOutputStartSupersededTests()
  {
    _listener = new TcpListener(IPAddress.Loopback, 0);
    _listener.Start();
    _port = ((IPEndPoint)_listener.LocalEndpoint).Port;
    _ = Task.Run(async () =>
    {
      try
      {
        while (true)
        {
          using var client = await _listener.AcceptTcpClientAsync();
          client.Close();
        }
      }
      catch
      {
        // Listener stopped at teardown.
      }
    });

    _engine
      .Setup(e => e.CreateStreamReader(It.IsAny<string>(), It.IsAny<double?>()))
      .Returns(() =>
      {
        Interlocked.Increment(ref _readersCreated);
        return new MemoryStream(new byte[4096]);
      });
  }

  public void Dispose() => _listener.Stop();

  [Fact]
  public async Task ATeardownDuringTheLaunch_LeavesNothingStreaming()
  {
    // Check 1: after the launch, before anything of the start's own exists.
    await using var output = await ConnectedDirectChannelOutputAsync();
    var client = ClientOf(output);
    var channelsBefore = ChannelCount(client);

    var launchEntered = NewTcs();
    var launchGate = NewTcs();
    output.CastLaunchApplicationOverrideForTests = async () =>
    {
      launchEntered.TrySetResult();
      await launchGate.Task;
      return LaunchStatus();
    };

    var start = output.StartAsync(automaticAttempt: true, CancellationToken.None);
    await launchEntered.Task.WaitAsync(HangGuard);

    await output.DisconnectAsync().WaitAsync(HangGuard);
    Assert.Equal(AudioOutputState.Ready, output.State);

    launchGate.SetResult();
    await start.WaitAsync(HangGuard); // returns normally: superseded is not a failure

    Assert.Equal(AudioOutputState.Ready, output.State); // as the teardown left it — not Streaming, not Error
    Assert.False(output.IsEnabled);
    Assert.Null(output.DirectStreaming);
    Assert.Equal(0, Volatile.Read(ref _readersCreated));
    Assert.Equal(channelsBefore, ChannelCount(client));
  }

  [Fact]
  public async Task ATeardownDuringTheStartTimeMute_NeverStartsTheSendLoop_AndUnregistersTheChannel()
  {
    // Check 2: after the AUD-81 start-time mute (whose order is unchanged: the mute is still sent
    // before any chunk could be), before DirectCastStreamingService.Start().
    await using var output = await ConnectedDirectChannelOutputAsync();
    output.AttachConsoleFollower(() => true, NullLogger.Instance); // a muted console: the start mutes
    var client = ClientOf(output);
    var channelsBefore = ChannelCount(client);
    output.CastLaunchApplicationOverrideForTests = () => Task.FromResult<ChromecastStatus?>(LaunchStatus());

    var muteEntered = NewTcs();
    var muteGate = NewTcs();
    var mutes = new List<bool>();
    output.CastSetMuteOverrideForTests = async m =>
    {
      lock (mutes)
      {
        mutes.Add(m);
      }
      muteEntered.TrySetResult();
      await muteGate.Task;
    };

    var start = output.StartAsync(automaticAttempt: true, CancellationToken.None);
    await muteEntered.Task.WaitAsync(HangGuard);

    // The channel is registered (replacing, not appending — AUD-54) before the mute.
    Assert.Equal(channelsBefore + 1, ChannelCount(client));

    await output.DisconnectAsync().WaitAsync(HangGuard);

    muteGate.SetResult();
    await start.WaitAsync(HangGuard);

    Assert.Equal(new[] { true }, mutes.ToArray());
    Assert.NotEqual(AudioOutputState.Streaming, output.State);
    Assert.Equal(AudioOutputState.Ready, output.State);
    Assert.False(output.IsEnabled);
    Assert.Null(output.DirectStreaming);
    Assert.Equal(0, Volatile.Read(ref _readersCreated)); // the send loop never started
    Assert.Equal(channelsBefore, ChannelCount(client));  // our channel removed again
  }

  [Fact]
  public async Task ATeardownDuringTheAfterStartVolumePush_StopsTheSendLoopItStarted_AndNeverMarksStreaming()
  {
    // Check 3: before IsEnabled/Streaming. The send loop has already started here; the abandoned
    // start must stop it and remove its channel.
    await using var output = await ConnectedDirectChannelOutputAsync();
    var client = ClientOf(output);
    var channelsBefore = ChannelCount(client);
    output.CastLaunchApplicationOverrideForTests = () => Task.FromResult<ChromecastStatus?>(LaunchStatus());

    var volumeEntered = NewTcs();
    var volumeGate = NewTcs();
    DirectCastStreamingService? streamingDuringPush = null;
    output.CastSetVolumeOverrideForTests = async _ =>
    {
      streamingDuringPush = output.DirectStreaming;
      volumeEntered.TrySetResult();
      await volumeGate.Task;
    };

    var states = new List<AudioOutputState>();
    output.StateChanged += (_, e) =>
    {
      lock (states)
      {
        states.Add(e.NewState);
      }
    };

    var start = output.StartAsync(automaticAttempt: true, CancellationToken.None);
    await volumeEntered.Task.WaitAsync(HangGuard);

    Assert.NotNull(streamingDuringPush);
    Assert.True(streamingDuringPush!.IsStreaming); // the loop is running at this point
    Assert.Equal(1, Volatile.Read(ref _readersCreated));

    await output.DisconnectAsync().WaitAsync(HangGuard);

    volumeGate.SetResult();
    await start.WaitAsync(HangGuard);

    Assert.Equal(AudioOutputState.Ready, output.State);
    Assert.False(output.IsEnabled);
    lock (states)
    {
      Assert.DoesNotContain(AudioOutputState.Streaming, states);
    }
    Assert.Null(output.DirectStreaming);
    Assert.False(streamingDuringPush.IsStreaming);           // stopped by the abandoned start
    Assert.Equal(channelsBefore, ChannelCount(client));
  }

  [Fact]
  public async Task AStartNobodyTearsDown_StillStreams()
  {
    // The guard's other direction: an undisturbed start reaches Streaming with its loop running.
    await using var output = await ConnectedDirectChannelOutputAsync();
    var client = ClientOf(output);
    var channelsBefore = ChannelCount(client);
    output.CastLaunchApplicationOverrideForTests = () => Task.FromResult<ChromecastStatus?>(LaunchStatus());
    output.CastSetVolumeOverrideForTests = _ => Task.CompletedTask;

    await output.StartAsync(automaticAttempt: true, CancellationToken.None).WaitAsync(HangGuard);

    Assert.Equal(AudioOutputState.Streaming, output.State);
    Assert.True(output.IsEnabled);
    Assert.NotNull(output.DirectStreaming);
    Assert.Equal(1, Volatile.Read(ref _readersCreated));
    Assert.Equal(channelsBefore + 1, ChannelCount(client));
  }

  // --- helpers ---

  private async Task<GoogleCastOutput> ConnectedDirectChannelOutputAsync()
  {
    var options = new AudioOutputOptions();
    options.GoogleCast.StreamingMode = "DirectChannel";
    options.GoogleCast.CacheFilePath =
      Path.Combine(Path.GetTempPath(), $"cast-cache-{Guid.NewGuid():N}.json");
    var output = new GoogleCastOutput(new Mock<ILogger<GoogleCastOutput>>().Object, Options.Create(options));

    await output.InitializeAsync();
    output.SetAudioEngine(_engine.Object);
    output.ConnectTransportOverrideForTests = _ => Task.CompletedTask;
    output.CastStatusReadOverrideForTests = () => Task.FromResult<(float Volume, bool Muted)?>((0.40f, false));
    output.CastStopApplicationOverrideForTests = () => Task.FromResult(true);
    await output.ConnectAsync(new ChromecastDeviceInfo
    {
      Id = "cast-a",
      FriendlyName = "Office speaker",
      IpAddress = "127.0.0.1",
      Port = _port,
      Model = "test"
    });

    Assert.Equal("cast-a", output.ConnectedDevice?.Id);
    Assert.Equal(AudioOutputState.Ready, output.State);
    return output;
  }

  private static ChromecastStatus LaunchStatus() => new()
  {
    Applications = new System.Collections.ObjectModel.Collection<ChromecastApplication>
    {
      new() { AppId = "CC1AD845", TransportId = "transport-1" }
    }
  };

  private static ChromecastClient ClientOf(GoogleCastOutput output)
  {
    var field = typeof(GoogleCastOutput).GetField("_client", BindingFlags.NonPublic | BindingFlags.Instance);
    return (ChromecastClient)field!.GetValue(output)!;
  }

  /// <summary>The number of channels registered on SharpCaster's private <c>Channels</c> list.</summary>
  private static int ChannelCount(ChromecastClient client)
  {
    var prop = client.GetType().GetProperty("Channels", BindingFlags.NonPublic | BindingFlags.Instance);
    Assert.NotNull(prop);
    return ((System.Collections.IEnumerable)prop!.GetValue(client)!).Cast<object>().Count();
  }

  private static TaskCompletionSource NewTcs() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
