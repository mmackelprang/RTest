using Radio.Core.Interfaces.Audio;

namespace Radio.Core.Tests.Interfaces;

public class BluetoothDisconnectReasonTests
{
  [Theory]
  [InlineData(BluetoothDisconnectReason.Remote, true)]
  [InlineData(BluetoothDisconnectReason.LocalHost, true)]
  [InlineData(BluetoothDisconnectReason.AuthFailure, true)]
  [InlineData(BluetoothDisconnectReason.LocalHostSuspend, true)]
  [InlineData(BluetoothDisconnectReason.Timeout, false)]
  [InlineData(BluetoothDisconnectReason.Unknown, false)]
  public void ShouldSuppressReconnect_ReturnsExpected(BluetoothDisconnectReason reason, bool expected)
  {
    Assert.Equal(expected, reason.ShouldSuppressReconnect());
  }
}
