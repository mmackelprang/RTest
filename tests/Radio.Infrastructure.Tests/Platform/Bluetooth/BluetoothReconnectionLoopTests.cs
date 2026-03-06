using Microsoft.Extensions.Logging;
using Moq;
using Radio.Core.Configuration;
using Radio.Infrastructure.Platform.Bluetooth;

namespace Radio.Infrastructure.Tests.Platform.Bluetooth;

public class BluetoothReconnectionLoopTests
{
  private static BluetoothOptions FastOptions(int maxAttempts = 5) => new()
  {
    AutoReconnect = true,
    ReconnectBaseDelayMs = 10,
    ReconnectMaxDelayMs = 100,
    MaxReconnectAttempts = maxAttempts
  };

  [Theory]
  [InlineData(1, 1000, 60000, 1000)]
  [InlineData(2, 1000, 60000, 2000)]
  [InlineData(3, 1000, 60000, 4000)]
  [InlineData(4, 1000, 60000, 8000)]
  [InlineData(5, 1000, 60000, 16000)]
  [InlineData(6, 1000, 60000, 32000)]
  [InlineData(7, 1000, 60000, 60000)] // capped at max
  public void CalculateBackoffDelay_ReturnsCorrectExponentialValues(
    int attempt, int baseMs, int maxMs, int expectedMs)
  {
    var delay = BluetoothReconnectionLoop.CalculateBackoffDelay(attempt, baseMs, maxMs);
    Assert.Equal(TimeSpan.FromMilliseconds(expectedMs), delay);
  }

  [Fact]
  public void CalculateBackoffDelay_CapsAtMax()
  {
    var delay = BluetoothReconnectionLoop.CalculateBackoffDelay(20, 3000, 60000);
    Assert.Equal(TimeSpan.FromMilliseconds(60000), delay);
  }

  [Fact]
  public async Task Loop_StopsAfterMaxAttempts()
  {
    var logger = Mock.Of<ILogger>();
    var options = FastOptions(maxAttempts: 3);
    int attempts = 0;

    using var loop = new BluetoothReconnectionLoop(
      logger, options,
      connectFunc: (addr, ct) => { Interlocked.Increment(ref attempts); return Task.FromResult(false); },
      isDeviceConnected: () => false);

    loop.Start("AA:BB:CC:DD:EE:FF");

    // Wait for loop to exhaust attempts (3 attempts × ~10ms delay each + processing)
    await Task.Delay(1000);

    Assert.Equal(3, attempts);
    Assert.False(loop.IsActive);
  }

  [Fact]
  public async Task Loop_StopsWhenDeviceConnects()
  {
    var logger = Mock.Of<ILogger>();
    var options = FastOptions(maxAttempts: 20);
    int attempts = 0;
    bool deviceConnected = false;

    using var loop = new BluetoothReconnectionLoop(
      logger, options,
      connectFunc: (addr, ct) =>
      {
        Interlocked.Increment(ref attempts);
        return Task.FromResult(false);
      },
      isDeviceConnected: () => Volatile.Read(ref deviceConnected));

    loop.Start("AA:BB:CC:DD:EE:FF");

    // Let a couple of attempts fire, then simulate device connected
    await Task.Delay(100);
    Volatile.Write(ref deviceConnected, true);
    await Task.Delay(500);

    // Should have stopped before reaching max attempts
    Assert.True(attempts < 20);
  }

  [Fact]
  public async Task Loop_StopsWhenCancelled()
  {
    var logger = Mock.Of<ILogger>();
    var options = FastOptions(maxAttempts: 100);
    int attempts = 0;

    using var loop = new BluetoothReconnectionLoop(
      logger, options,
      connectFunc: (addr, ct) => { Interlocked.Increment(ref attempts); return Task.FromResult(false); },
      isDeviceConnected: () => false);

    loop.Start("AA:BB:CC:DD:EE:FF");
    await Task.Delay(50);
    loop.Cancel();
    await Task.Delay(200);

    var attemptsAfterCancel = attempts;
    await Task.Delay(200);

    // No new attempts after cancel
    Assert.Equal(attemptsAfterCancel, attempts);
  }

  [Fact]
  public async Task Loop_CallsConnectFuncWithCorrectAddress()
  {
    var logger = Mock.Of<ILogger>();
    var options = FastOptions(maxAttempts: 1);
    string? capturedAddress = null;

    using var loop = new BluetoothReconnectionLoop(
      logger, options,
      connectFunc: (addr, ct) => { capturedAddress = addr; return Task.FromResult(false); },
      isDeviceConnected: () => false);

    loop.Start("11:22:33:44:55:66");
    await Task.Delay(200);

    Assert.Equal("11:22:33:44:55:66", capturedAddress);
  }

  [Fact]
  public async Task Loop_SuccessfulConnectBreaksLoop()
  {
    var logger = Mock.Of<ILogger>();
    var options = FastOptions(maxAttempts: 10);
    int attempts = 0;

    using var loop = new BluetoothReconnectionLoop(
      logger, options,
      connectFunc: (addr, ct) =>
      {
        var attempt = Interlocked.Increment(ref attempts);
        return Task.FromResult(attempt >= 2); // succeed on 2nd attempt
      },
      isDeviceConnected: () => false);

    loop.Start("AA:BB:CC:DD:EE:FF");
    await Task.Delay(500);

    Assert.Equal(2, attempts);
  }

  [Fact]
  public async Task Start_CancelsPriorLoop()
  {
    var logger = Mock.Of<ILogger>();
    var options = FastOptions(maxAttempts: 100);
    int firstLoopAttempts = 0;
    int secondLoopAttempts = 0;
    string? lastAddress = null;

    using var loop = new BluetoothReconnectionLoop(
      logger, options,
      connectFunc: (addr, ct) =>
      {
        lastAddress = addr;
        if (addr == "11:11:11:11:11:11")
          Interlocked.Increment(ref firstLoopAttempts);
        else
          Interlocked.Increment(ref secondLoopAttempts);
        return Task.FromResult(false);
      },
      isDeviceConnected: () => false);

    loop.Start("11:11:11:11:11:11");
    await Task.Delay(50);

    loop.Start("22:22:22:22:22:22");
    await Task.Delay(200);

    var firstFinal = firstLoopAttempts;
    await Task.Delay(200);

    // First loop should have stopped
    Assert.Equal(firstFinal, firstLoopAttempts);
    // Second loop should be running
    Assert.True(secondLoopAttempts > 0);
  }
}
