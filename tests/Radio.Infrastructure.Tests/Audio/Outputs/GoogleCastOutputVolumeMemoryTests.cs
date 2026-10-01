using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Radio.Core.Configuration;
using Radio.Infrastructure.Audio.Outputs;
using Sharpcaster.Models.ChromecastStatus;
using Xunit;

namespace Radio.Infrastructure.Tests.Audio.Outputs;

/// <summary>
/// AUD-80: a Cast reconnect puts the speaker back at the volume the console last had for it,
/// instead of pushing <c>GoogleCast.DefaultVolume</c> (70 %) and adopting the echo as master
/// volume.
/// </summary>
/// <remarks>
/// Driven through the real <see cref="GoogleCastOutput.ConnectAsync(ChromecastDeviceInfo, CastConnectOptions, CancellationToken)"/> with the transport, the
/// status read and SET_VOLUME substituted — no fake socket speaks the Cast protocol. The
/// after-start push is invoked directly (<see cref="GoogleCastOutput.SyncVolumeAfterStartAsync"/>)
/// because the StartAsync chain that calls it needs a launched receiver application. The
/// external-change path is raised by reflection, the way SharpCaster raises it.
/// No assertion depends on elapsed time: every call is awaited before anything is asserted.
/// </remarks>
public class GoogleCastOutputVolumeMemoryTests
{
  private sealed class FakeVolumeStore : ICastDeviceVolumeStore
  {
    public Dictionary<string, float> Volumes { get; } = new();

    public Task<float?> GetVolumeAsync(string deviceId, CancellationToken cancellationToken = default) =>
      Task.FromResult<float?>(Volumes.TryGetValue(deviceId, out var v) ? v : null);

    public void Remember(string deviceId, float volume) => Volumes[deviceId] = volume;
  }

  [Fact]
  public async Task ARememberedVolume_IsPushedOnConnect_AndReappliedAfterStart_NotTheDefault()
  {
    using var listener = StartLoopbackListener(out var port);
    var store = new FakeVolumeStore();
    store.Volumes["cast-a"] = 0.25f;
    await using var output = BuildOutput(store);
    await output.InitializeAsync();

    var pushes = new List<float>();
    var published = new List<CastVolumeChangedEventArgs>();
    output.CastVolumeChanged += (_, e) => published.Add(e);
    output.ConnectTransportOverrideForTests = _ => Task.CompletedTask;
    output.CastStatusReadOverrideForTests = () => Task.FromResult<(float, bool)?>((0.70f, false));
    output.CastSetVolumeOverrideForTests = v => { pushes.Add(v); return Task.CompletedTask; };

    await output.ConnectAsync(Device("cast-a", port));
    await output.SyncVolumeAfterStartAsync();

    // Connect restores the remembered 25 %; the after-start push re-applies it. Before AUD-80
    // the after-start push was the output's own Volume, i.e. DefaultVolume (0.7 here).
    Assert.Equal(new[] { 0.25f, 0.25f }, pushes);

    var initial = Assert.Single(published);
    Assert.True(initial.IsInitialSync);
    Assert.Equal(0.25f, initial.Volume, 3);
  }

  [Fact]
  public async Task ADeviceNeverSeen_KeepsAndRemembersItsOwnLevel()
  {
    using var listener = StartLoopbackListener(out var port);
    var store = new FakeVolumeStore();
    await using var output = BuildOutput(store);
    await output.InitializeAsync();

    var pushes = new List<float>();
    output.ConnectTransportOverrideForTests = _ => Task.CompletedTask;
    output.CastStatusReadOverrideForTests = () => Task.FromResult<(float, bool)?>((0.33f, false));
    output.CastSetVolumeOverrideForTests = v => { pushes.Add(v); return Task.CompletedTask; };

    await output.ConnectAsync(Device("cast-a", port));
    Assert.Empty(pushes); // nothing to restore; the device is already at its own level

    await output.SyncVolumeAfterStartAsync();

    Assert.Equal(new[] { 0.33f }, pushes); // its own level, not DefaultVolume
    Assert.Equal(0.33f, store.Volumes["cast-a"], 3);
  }

  [Fact]
  public async Task WithNothingRememberedAndNoReading_TheDeviceIsLeftAlone()
  {
    using var listener = StartLoopbackListener(out var port);
    await using var output = BuildOutput(new FakeVolumeStore());
    await output.InitializeAsync();

    var pushes = new List<float>();
    output.ConnectTransportOverrideForTests = _ => Task.CompletedTask;
    output.CastStatusReadOverrideForTests = () => Task.FromResult<(float, bool)?>(null);
    output.CastSetVolumeOverrideForTests = v => { pushes.Add(v); return Task.CompletedTask; };

    await output.ConnectAsync(Device("cast-a", port));
    await output.SyncVolumeAfterStartAsync();

    Assert.Empty(pushes);
  }

  [Fact]
  public async Task AVolumeSetOnTheSpeaker_IsRememberedForTheNextConnect()
  {
    using var listener = StartLoopbackListener(out var port);
    var store = new FakeVolumeStore();
    await using var output = BuildOutput(store);
    await output.InitializeAsync();

    var published = new List<CastVolumeChangedEventArgs>();
    output.CastVolumeChanged += (_, e) => published.Add(e);
    output.ConnectTransportOverrideForTests = _ => Task.CompletedTask;
    output.CastStatusReadOverrideForTests = () => Task.FromResult<(float, bool)?>((0.50f, false));
    output.CastSetVolumeOverrideForTests = _ => Task.CompletedTask;

    await output.ConnectAsync(Device("cast-a", port));
    RaiseReceiverStatus(output, 0.30);

    Assert.Contains(published, e => !e.IsInitialSync && Math.Abs(e.Volume - 0.30f) < 0.001f);
    Assert.Equal(0.30f, store.Volumes["cast-a"], 3);
  }

  [Fact]
  public async Task TheDevicesConfirmationOfAnAfterStartPush_IsNotReportedAsAnExternalChange()
  {
    // THE 70 % BUG. Measured on the box 2026-09-29: every connect was followed by
    // "Synced volume from Cast device: 70 % (initial: false)" — the device confirming the
    // after-start SET_VOLUME, arriving un-baselined and written to master volume.
    //
    // The connect-time push is made to fail so _lastSetVolume is left at the device's
    // reading (0.70); only the after-start push's own baseline can then stop the echo.
    using var listener = StartLoopbackListener(out var port);
    var store = new FakeVolumeStore();
    store.Volumes["cast-a"] = 0.25f;
    await using var output = BuildOutput(store);
    await output.InitializeAsync();

    var published = new List<CastVolumeChangedEventArgs>();
    output.CastVolumeChanged += (_, e) => published.Add(e);
    output.ConnectTransportOverrideForTests = _ => Task.CompletedTask;
    output.CastStatusReadOverrideForTests = () => Task.FromResult<(float, bool)?>((0.70f, false));

    var calls = 0;
    output.CastSetVolumeOverrideForTests = _ =>
      ++calls == 1 ? throw new InvalidOperationException("first push fails") : Task.CompletedTask;

    await output.ConnectAsync(Device("cast-a", port));
    await output.SyncVolumeAfterStartAsync();
    Assert.Equal(2, calls);

    RaiseReceiverStatus(output, 0.25); // the device confirming the level we just set

    Assert.DoesNotContain(published, e => !e.IsInitialSync);
  }

  [Fact]
  public async Task AHeldConnect_PushesNoVolume_AndReportsNoStatus_UntilTheReceiverIsConfirmedFree()
  {
    // AUD-37 review M5. The reconnect watcher connects BEFORE it knows the speaker is free. A
    // speaker serving another sender must not have its volume jumped to our remembered level,
    // and that sender's volume/mute changes must not reach the console (the CastVolumeChanged
    // subscriber writes master volume and mute) or our memory for the device.
    using var listener = StartLoopbackListener(out var port);
    var store = new FakeVolumeStore();
    store.Volumes["cast-a"] = 0.25f;
    await using var output = BuildOutput(store);
    await output.InitializeAsync();

    var pushes = new List<float>();
    var published = new List<CastVolumeChangedEventArgs>();
    output.CastVolumeChanged += (_, e) => published.Add(e);
    output.ConnectTransportOverrideForTests = _ => Task.CompletedTask;
    output.CastStatusReadOverrideForTests = () => Task.FromResult<(float, bool)?>((0.70f, false));
    output.CastSetVolumeOverrideForTests = v => { pushes.Add(v); return Task.CompletedTask; };

    await output.ConnectAsync(Device("cast-a", port), new CastConnectOptions { HoldUntilReceiverConfirmed = true });

    Assert.True(output.IsHoldingForReceiverConfirmation);
    Assert.Empty(pushes); // not the remembered 25 %, not anything

    RaiseReceiverStatus(output, 0.40);              // the other sender turns it down…
    RaiseReceiverStatus(output, 0.40, muted: true); // …and mutes it
    Assert.DoesNotContain(published, e => !e.IsInitialSync);
    Assert.Equal(0.25f, store.Volumes["cast-a"], 3); // our memory for the device is untouched

    // Confirmed free: the start's after-launch push applies the remembered level.
    output.ConfirmReceiverAvailable();
    await output.SyncVolumeAfterStartAsync();
    Assert.Equal(new[] { 0.25f }, pushes);

    // And from here statuses are reported again.
    RaiseReceiverStatus(output, 0.55);
    Assert.Contains(published, e => !e.IsInitialSync && Math.Abs(e.Volume - 0.55f) < 0.001f);
  }

  [Fact]
  public async Task AHold_DoesNotOutliveItsConnect()
  {
    using var listener = StartLoopbackListener(out var port);
    var store = new FakeVolumeStore();
    store.Volumes["cast-a"] = 0.25f;
    await using var output = BuildOutput(store);
    await output.InitializeAsync();

    var pushes = new List<float>();
    output.ConnectTransportOverrideForTests = _ => Task.CompletedTask;
    output.CastStatusReadOverrideForTests = () => Task.FromResult<(float, bool)?>((0.70f, false));
    output.CastSetVolumeOverrideForTests = v => { pushes.Add(v); return Task.CompletedTask; };

    var held = new CastConnectOptions { HoldUntilReceiverConfirmed = true };

    // A user's connect replacing a held one is not held: the remembered level is pushed at once.
    await output.ConnectAsync(Device("cast-a", port), held);
    await output.ConnectAsync(Device("cast-a", port));
    Assert.False(output.IsHoldingForReceiverConfirmation);
    Assert.Equal(new[] { 0.25f }, pushes);

    // A disconnect ends a hold too.
    await output.ConnectAsync(Device("cast-a", port), held);
    Assert.True(output.IsHoldingForReceiverConfirmation);
    await output.DisconnectAsync();
    Assert.False(output.IsHoldingForReceiverConfirmation);
  }

  // --- helpers ---

  private static void RaiseReceiverStatus(GoogleCastOutput output, double level, bool muted = false)
  {
    var handler = typeof(GoogleCastOutput).GetMethod(
      "OnReceiverStatusChanged", BindingFlags.NonPublic | BindingFlags.Instance);
    Assert.NotNull(handler);
    var status = new ChromecastStatus { Volume = new() { Level = level, Muted = muted } };
    handler!.Invoke(output, new object?[] { null, status });
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
        // Listener disposed at test teardown.
      }
    });

    return listener;
  }

  private static ChromecastDeviceInfo Device(string id, int port) => new()
  {
    Id = id,
    FriendlyName = $"Fake {id}",
    IpAddress = "127.0.0.1",
    Port = port,
    Model = "Test"
  };

  private static GoogleCastOutput BuildOutput(ICastDeviceVolumeStore store)
  {
    var options = new AudioOutputOptions();
    options.GoogleCast.DefaultVolume = 0.7f;
    options.GoogleCast.CacheFilePath =
      Path.Combine(Path.GetTempPath(), $"cast-cache-{Guid.NewGuid():N}.json");

    return new GoogleCastOutput(
      new Mock<ILogger<GoogleCastOutput>>().Object,
      Options.Create(options),
      volumeStore: store);
  }
}
