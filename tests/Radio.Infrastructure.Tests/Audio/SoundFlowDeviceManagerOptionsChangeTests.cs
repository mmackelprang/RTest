using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Radio.Core.Configuration;
using Radio.Infrastructure.Audio.SoundFlow;
using Radio.Configuration.Abstractions;

namespace Radio.Infrastructure.Tests.Audio;

/// <summary>
/// Covers the debounce/coalesce behaviour of <see cref="SoundFlowDeviceManager"/>'s
/// <c>IOptionsMonitor&lt;AudioOutputOptions&gt;.OnChange</c> handler.
///
/// <para>The handler used to be <c>_ = LoadDisplaySettingsFromStoreAsync()</c> — one
/// unawaited, unserialized reload per config write. Each reload re-enumerates audio devices,
/// so a volume-slider drag (13 config writes inside one second, per the 2026-08-10 journal)
/// put 13 thread pool threads into MiniAudio's non-thread-safe PulseAudio main loop and
/// aborted <c>radio-api</c>. These tests pin the fan-out at its source.</para>
/// </summary>
public class SoundFlowDeviceManagerOptionsChangeTests
{
  /// <summary>
  /// Longer than the manager's 300ms debounce window, with margin for a loaded CI box.
  /// </summary>
  private static readonly TimeSpan PastDebounceWindow = TimeSpan.FromMilliseconds(1500);

  private const string HiddenNamesKey = "AudioOutput:DeviceDisplay:HiddenDeviceNames";

  private sealed class Harness
  {
    public required SoundFlowDeviceManager Manager { get; init; }
    public required Mock<IConfigurationManager> ConfigManager { get; init; }

    /// <summary>The listener the manager registered with IOptionsMonitor.</summary>
    public required Action<AudioOutputOptions, string?> RaiseOptionsChanged { get; init; }

    /// <summary>
    /// How many times the manager has read display settings back from the config store.
    /// One reload == one read of each key, so this is the reload count.
    /// </summary>
    public int StoreReloadCount() => ConfigManager.Invocations.Count(i =>
      i.Method.Name == nameof(IConfigurationManager.GetValueAsync) &&
      i.Arguments.Any(a => a as string == HiddenNamesKey));
  }

  private static Harness CreateHarness()
  {
    var loggerMock = new Mock<ILogger<SoundFlowDeviceManager>>();
    var configManagerMock = new Mock<IConfigurationManager>();
    var audioPreferencesMock = new Mock<IOptionsMonitor<AudioPreferences>>();
    var audioOutputOptionsMock = new Mock<IOptionsMonitor<AudioOutputOptions>>();

    audioPreferencesMock.Setup(x => x.CurrentValue).Returns(new AudioPreferences());
    audioOutputOptionsMock.Setup(x => x.CurrentValue).Returns(new AudioOutputOptions());

    // IOptionsMonitor<T>.OnChange(Action<T>) is an extension that forwards to the
    // two-argument interface method, so capturing that is how the production listener
    // gets a handle we can fire.
    Action<AudioOutputOptions, string?>? captured = null;
    audioOutputOptionsMock
      .Setup(x => x.OnChange(It.IsAny<Action<AudioOutputOptions, string?>>()))
      .Callback<Action<AudioOutputOptions, string?>>(listener => captured = listener)
      .Returns(Mock.Of<IDisposable>());

    var manager = new SoundFlowDeviceManager(
      loggerMock.Object,
      configManagerMock.Object,
      audioPreferencesMock.Object,
      audioOutputOptionsMock.Object);

    Assert.True(captured is not null,
      "SoundFlowDeviceManager must subscribe to AudioOutputOptions changes");

    return new Harness
    {
      Manager = manager,
      ConfigManager = configManagerMock,
      RaiseOptionsChanged = captured!
    };
  }

  /// <summary>
  /// The regression test for the crash. Thirteen writes in a second is the exact burst the
  /// journal recorded before the first SIGABRT; the manager must collapse it to one reload.
  /// </summary>
  [Fact]
  public async Task OptionsChangeBurst_CoalescesToASingleStoreReload()
  {
    var harness = CreateHarness();
    var options = new AudioOutputOptions();

    for (var i = 0; i < 13; i++)
    {
      harness.RaiseOptionsChanged(options, Options.DefaultName);
    }

    // Nothing should have run yet — the whole point of a trailing edge.
    Assert.Equal(0, harness.StoreReloadCount());

    await Task.Delay(PastDebounceWindow);

    Assert.Equal(1, harness.StoreReloadCount());

    harness.Manager.Dispose();
  }

  [Fact]
  public async Task SingleOptionsChange_StillTriggersExactlyOneReload()
  {
    var harness = CreateHarness();

    harness.RaiseOptionsChanged(new AudioOutputOptions(), Options.DefaultName);
    await Task.Delay(PastDebounceWindow);

    Assert.Equal(1, harness.StoreReloadCount());

    harness.Manager.Dispose();
  }

  /// <summary>
  /// Two bursts separated by more than the debounce window are two distinct user actions and
  /// must each apply — a debounce that swallowed the second would silently drop settings.
  /// </summary>
  [Fact]
  public async Task SeparatedOptionsChanges_EachTriggerTheirOwnReload()
  {
    var harness = CreateHarness();

    harness.RaiseOptionsChanged(new AudioOutputOptions(), Options.DefaultName);
    await Task.Delay(PastDebounceWindow);
    harness.RaiseOptionsChanged(new AudioOutputOptions(), Options.DefaultName);
    await Task.Delay(PastDebounceWindow);

    Assert.Equal(2, harness.StoreReloadCount());

    harness.Manager.Dispose();
  }

  /// <summary>
  /// A pending reload must not fire against a disposed manager — otherwise shutdown races a
  /// device enumeration.
  /// </summary>
  [Fact]
  public async Task Dispose_CancelsAPendingReload()
  {
    var harness = CreateHarness();

    harness.RaiseOptionsChanged(new AudioOutputOptions(), Options.DefaultName);
    harness.Manager.Dispose();

    await Task.Delay(PastDebounceWindow);

    Assert.Equal(0, harness.StoreReloadCount());
  }

  [Fact]
  public void Dispose_IsIdempotent()
  {
    var harness = CreateHarness();

    harness.Manager.Dispose();
    harness.Manager.Dispose();
  }
}
