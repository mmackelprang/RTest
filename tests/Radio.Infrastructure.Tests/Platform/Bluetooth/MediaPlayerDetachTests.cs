using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Radio.Core.Configuration;
using Radio.Infrastructure.Platform.Bluetooth;
using Tmds.DBus;
using Xunit;

namespace Radio.Infrastructure.Tests.Platform.Bluetooth;

/// <summary>
/// AUD-14: when BlueZ removes the MediaPlayer1 we are attached to, the attachment is released, so a
/// player re-added at the same path gets a full attach (fresh properties watcher, initial Status read)
/// instead of the dedup's "already attached" early return that kept a watcher on a dead object.
/// </summary>
/// <remarks>Drives the real <see cref="LinuxBluetoothService.OnInterfaceRemoved"/> synchronously; no clocks.</remarks>
public class MediaPlayerDetachTests
{
  private const string PlayerPath = "/org/bluez/hci0/dev_B0_D5_FB_D2_0D_68/player0";

  private sealed class RecordingWatcher : IDisposable
  {
    public bool Disposed { get; private set; }
    public void Dispose() => Disposed = true;
  }

  private static readonly BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

  private static (LinuxBluetoothService Service, RecordingWatcher Watcher) AttachedService()
  {
    var service = new LinuxBluetoothService(
      NullLogger<LinuxBluetoothService>.Instance,
      Options.Create(new BluetoothOptions { AutoReconnect = false }));
    var watcher = new RecordingWatcher();
    Set(service, "_mediaPlayer", new Mock<Radio.Infrastructure.Platform.Bluetooth.Linux.IMediaPlayer1>().Object);
    Set(service, "_mediaPlayerPath", (ObjectPath?)new ObjectPath(PlayerPath));
    Set(service, "_playerPropertiesWatcher", watcher);
    return (service, watcher);
  }

  private static void Set(object target, string field, object? value) =>
    typeof(LinuxBluetoothService).GetField(field, Private)!.SetValue(target, value);

  private static object? Get(object target, string field) =>
    typeof(LinuxBluetoothService).GetField(field, Private)!.GetValue(target);

  [Fact]
  public void RemovingTheAttachedMediaPlayer_DisposesItsWatcher_AndClearsTheAttachment()
  {
    var (service, watcher) = AttachedService();

    service.OnInterfaceRemoved((new ObjectPath(PlayerPath), new[] { "org.bluez.MediaPlayer1" }));

    Assert.True(watcher.Disposed);
    Assert.Null(Get(service, "_mediaPlayer"));
    Assert.Null(Get(service, "_mediaPlayerPath"));
    Assert.Null(Get(service, "_playerPropertiesWatcher"));
  }

  [Fact]
  public void RemovingAPlayerAtAnotherPath_LeavesTheAttachmentAlone()
  {
    var (service, watcher) = AttachedService();

    service.OnInterfaceRemoved((new ObjectPath("/org/bluez/hci0/dev_AA_BB_CC_DD_EE_FF/player0"), new[] { "org.bluez.MediaPlayer1" }));

    Assert.False(watcher.Disposed);
    Assert.NotNull(Get(service, "_mediaPlayer"));
    Assert.Equal(new ObjectPath(PlayerPath), (ObjectPath?)Get(service, "_mediaPlayerPath"));
  }
}
