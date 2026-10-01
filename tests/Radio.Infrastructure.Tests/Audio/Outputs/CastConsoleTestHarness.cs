using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Audio.Outputs;
using Sharpcaster.Models.ChromecastStatus;
using Xunit;

namespace Radio.Infrastructure.Tests.Audio.Outputs;

/// <summary>
/// AUD-81 test harness: a <see cref="GoogleCastOutput"/> connected through the real
/// <c>ConnectAsync</c> with the transport, status read, SET_VOLUME and SET_MUTE substituted, and
/// put in <c>Streaming</c> by reflection (StartAsync needs a launched receiver application). Every
/// command the output sends is recorded, in order, in <see cref="Commands"/>.
/// </summary>
internal sealed class CastConsoleTestHarness : IAsyncDisposable
{
  private readonly System.Net.Sockets.TcpListener _listener;
  private readonly int _port;

  public GoogleCastOutput Output { get; }
  public FakeTimeProvider Time { get; } = new();
  public FakeCastVolumeStore Store { get; } = new();

  /// <summary>
  /// Every SET_VOLUME ("vol"), SET_MUTE ("mute") and receiver-application stop ("appstop") sent,
  /// in order.
  /// </summary>
  public List<(string Kind, float Level, bool Muted)> Commands { get; } = new();
  private readonly object _commandsLock = new();

  /// <summary>Non-initial <see cref="GoogleCastOutput.CastVolumeChanged"/> events (external changes).</summary>
  public List<CastVolumeChangedEventArgs> External { get; } = new();

  /// <summary>
  /// When set, each SET_VOLUME awaits it after recording itself — the rendezvous a test uses to
  /// hold a push in flight. <see cref="VolumeSendEntered"/> completes when a send is parked.
  /// </summary>
  public TaskCompletionSource? VolumeGate { get; set; }
  public TaskCompletionSource VolumeSendEntered { get; private set; } = NewTcs();

  /// <summary>When set, the next SET_VOLUME throws it.</summary>
  public Exception? FailNextVolume { get; set; }

  public CastConsoleTestHarness()
  {
    _listener = StartLoopbackListener(out _port);
    var options = new AudioOutputOptions();
    options.GoogleCast.DefaultVolume = 0.7f;
    options.GoogleCast.CacheFilePath =
      Path.Combine(Path.GetTempPath(), $"cast-cache-{Guid.NewGuid():N}.json");
    Output = new GoogleCastOutput(
      new Mock<ILogger<GoogleCastOutput>>().Object,
      Options.Create(options),
      volumeStore: Store,
      timeProvider: Time);

    Output.CastVolumeChanged += (_, e) =>
    {
      if (!e.IsInitialSync)
      {
        lock (_commandsLock)
        {
          External.Add(e);
        }
      }
    };

    Output.ConnectTransportOverrideForTests = _ => Task.CompletedTask;
    Output.CastSetVolumeOverrideForTests = async v =>
    {
      lock (_commandsLock)
      {
        Commands.Add(("vol", v, false));
      }

      var fail = FailNextVolume;
      FailNextVolume = null;
      var gate = VolumeGate;
      VolumeSendEntered.TrySetResult();
      if (gate != null)
      {
        await gate.Task;
      }

      if (fail != null)
      {
        throw fail;
      }
    };
    Output.CastSetMuteOverrideForTests = m =>
    {
      lock (_commandsLock)
      {
        Commands.Add(("mute", 0f, m));
      }
      return Task.CompletedTask;
    };
    Output.CastStopApplicationOverrideForTests = () =>
    {
      lock (_commandsLock)
      {
        Commands.Add(("appstop", 0f, false));
      }
      return AppStop();
    };
  }

  /// <summary>
  /// The receiver-application stop's outcome: true (no application of ours left running) by
  /// default; a test replaces it with false or a throw.
  /// </summary>
  public Func<Task<bool>> AppStop { get; set; } = () => Task.FromResult(true);

  /// <summary>The kinds of every command sent, in order ("vol", "mute", "appstop").</summary>
  public List<string> Kinds()
  {
    lock (_commandsLock)
    {
      return Commands.Select(c => c.Kind == "mute" ? (c.Muted ? "mute" : "unmute") : c.Kind).ToList();
    }
  }

  /// <summary>
  /// Connects to <paramref name="deviceId"/> with the device reporting the given status, or —
  /// when <paramref name="statusRead"/> is given — with that delegate standing in for the
  /// initial status read.
  /// </summary>
  public async Task ConnectAsync(
    string deviceId = "cast-a",
    float reportedLevel = 0.40f,
    bool reportedMuted = false,
    bool streaming = true,
    Func<Task<(float Volume, bool Muted)?>>? statusRead = null)
  {
    if (Output.State == AudioOutputState.Created)
    {
      await Output.InitializeAsync();
    }

    Output.CastStatusReadOverrideForTests =
      statusRead ?? (() => Task.FromResult<(float, bool)?>((reportedLevel, reportedMuted)));
    await Output.ConnectAsync(new ChromecastDeviceInfo
    {
      Id = deviceId,
      FriendlyName = $"Speaker {deviceId}",
      IpAddress = "127.0.0.1",
      Port = _port,
      Model = "test"
    });

    if (streaming)
    {
      MarkStreaming();
    }
  }

  /// <summary>Stands in for StartAsync reaching Streaming.</summary>
  public void MarkStreaming()
  {
    typeof(AudioOutputBase).GetProperty(nameof(AudioOutputBase.State))!.SetValue(Output, AudioOutputState.Streaming);
    Assert.Equal(AudioOutputState.Streaming, Output.State);
  }

  public CastConsoleTarget Target()
  {
    var target = Output.GetConsoleVolumeTarget();
    Assert.NotNull(target);
    return target!.Value;
  }

  public List<float> VolumeSends()
  {
    lock (_commandsLock)
    {
      return Commands.Where(c => c.Kind == "vol").Select(c => c.Level).ToList();
    }
  }

  public List<bool> MuteSends()
  {
    lock (_commandsLock)
    {
      return Commands.Where(c => c.Kind == "mute").Select(c => c.Muted).ToList();
    }
  }

  public void ClearCommands()
  {
    lock (_commandsLock)
    {
      Commands.Clear();
    }
    VolumeSendEntered = NewTcs();
  }

  /// <summary>Raises a receiver status the way SharpCaster does.</summary>
  public void RaiseStatus(double level, bool muted = false)
  {
    var handler = typeof(GoogleCastOutput).GetMethod(
      "OnReceiverStatusChanged", BindingFlags.NonPublic | BindingFlags.Instance);
    Assert.NotNull(handler);
    var status = new ChromecastStatus { Volume = new() { Level = level, Muted = muted } };
    handler!.Invoke(Output, new object?[] { null, status });
  }

  public static TaskCompletionSource NewTcs() => new(TaskCreationOptions.RunContinuationsAsynchronously);

  public async ValueTask DisposeAsync()
  {
    VolumeGate?.TrySetResult();
    await Output.DisposeAsync();
    _listener.Stop();
  }

  private static System.Net.Sockets.TcpListener StartLoopbackListener(out int port)
  {
    var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
    listener.Start();
    port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;

    _ = Task.Run(async () =>
    {
      try
      {
        while (true)
        {
          using var client = await listener.AcceptTcpClientAsync();
          client.Close();
        }
      }
      catch
      {
        // Listener stopped at teardown.
      }
    });

    return listener;
  }
}

/// <summary>An in-memory <see cref="ICastDeviceVolumeStore"/> that records every Remember call.</summary>
internal sealed class FakeCastVolumeStore : ICastDeviceVolumeStore
{
  private readonly object _lock = new();
  public Dictionary<string, float> Volumes { get; } = new();
  public List<(string DeviceId, float Volume)> Remembered { get; } = new();

  public Task<float?> GetVolumeAsync(string deviceId, CancellationToken cancellationToken = default)
  {
    lock (_lock)
    {
      return Task.FromResult<float?>(Volumes.TryGetValue(deviceId, out var v) ? v : null);
    }
  }

  public void Remember(string deviceId, float volume)
  {
    lock (_lock)
    {
      Volumes[deviceId] = volume;
      Remembered.Add((deviceId, volume));
    }
  }
}
