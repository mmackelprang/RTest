using System.Text.Json;
using Microsoft.Extensions.Logging;
using Moq;
using Radio.Configuration.Abstractions;
using Radio.Configuration.Models;
using Radio.Infrastructure.Audio.Outputs;
using Xunit;

namespace Radio.Infrastructure.Tests.Audio.Outputs;

/// <summary>
/// AUD-80: the per-device Cast volume map persisted as one JSON entry.
/// </summary>
/// <remarks>
/// Remember persists in the background; each test awaits the write it expects through a
/// TaskCompletionSource completed by the mocked SetValueAsync — a rendezvous, not a delay.
/// </remarks>
public class ConfigStoreCastDeviceVolumeStoreTests
{
  private const string DeviceA = "https://192.168.86.25/";
  private const string DeviceB = "https://192.168.86.26/";

  [Fact]
  public async Task PersistedVolumes_AreReadBack_AndDeviceIdsWithColonsSurvive()
  {
    var (manager, _) = Manager(persistedJson: JsonSerializer.Serialize(
      new Dictionary<string, float> { [DeviceA] = 0.25f }));
    var store = new ConfigStoreCastDeviceVolumeStore(Logger(), manager.Object);

    Assert.Equal(0.25f, await store.GetVolumeAsync(DeviceA));
    Assert.Null(await store.GetVolumeAsync(DeviceB));
  }

  [Fact]
  public async Task Remember_WritesTheWholeMap_UnderTheCanonicalKey_WithoutDroppingOtherDevices()
  {
    // A Remember before anything was read must not overwrite a device that is only on disk.
    var (manager, writes) = Manager(persistedJson: JsonSerializer.Serialize(
      new Dictionary<string, float> { [DeviceA] = 0.25f }));
    var store = new ConfigStoreCastDeviceVolumeStore(Logger(), manager.Object);

    var written = writes.Next();
    store.Remember(DeviceB, 0.4f);
    var json = await written.WaitAsync(TimeSpan.FromSeconds(10));

    var map = JsonSerializer.Deserialize<Dictionary<string, float>>(json)!;
    Assert.Equal(0.25f, map[DeviceA]);
    Assert.Equal(0.4f, map[DeviceB]);
    manager.Verify(m => m.SetValueAsync(
      "sqlite", ConfigStoreCastDeviceVolumeStore.Key, It.IsAny<string>(), It.IsAny<CancellationToken>()),
      Times.Once);

    // Visible immediately, independent of the write.
    Assert.Equal(0.4f, await store.GetVolumeAsync(DeviceB));
  }

  // --- helpers ---

  private sealed class WriteLog
  {
    private TaskCompletionSource<string>? _pending;

    public Task<string> Next()
    {
      _pending = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
      return _pending.Task;
    }

    public void Record(string value) => _pending?.TrySetResult(value);
  }

  private static (Mock<IConfigurationManager> Manager, WriteLog Writes) Manager(string? persistedJson)
  {
    var writes = new WriteLog();
    var manager = new Mock<IConfigurationManager>();
    manager.SetupGet(m => m.CurrentStoreType).Returns(ConfigurationStoreType.Sqlite);
    manager
      .Setup(m => m.GetValueAsync<string>(
        "sqlite", ConfigStoreCastDeviceVolumeStore.Key, It.IsAny<ConfigurationReadMode>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync(persistedJson);
    manager
      .Setup(m => m.SetValueAsync(
        "sqlite", ConfigStoreCastDeviceVolumeStore.Key, It.IsAny<string>(), It.IsAny<CancellationToken>()))
      .Callback<string, string, string, CancellationToken>((_, _, value, _) => writes.Record(value))
      .Returns(Task.CompletedTask);
    return (manager, writes);
  }

  private static ILogger<ConfigStoreCastDeviceVolumeStore> Logger() =>
    new Mock<ILogger<ConfigStoreCastDeviceVolumeStore>>().Object;
}
