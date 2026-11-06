using System.Diagnostics;
using Radio.Infrastructure.Platform.Bluetooth;

namespace Radio.Infrastructure.Tests.Platform.Bluetooth;

/// <summary>
/// <c>using var process</c> disposes the managed handle and leaves the OS process running, so a
/// cancelled <c>pw-cli</c> read has to end the process itself. The auto-switch probe cancels one
/// whenever its window expires mid-read.
/// </summary>
public class ProcessReadCancellationTests
{
  [Fact]
  public async Task CancelledRead_EndsTheProcess()
  {
    // A real process that outlives the test by a wide margin.
    var psi = OperatingSystem.IsWindows()
      ? new ProcessStartInfo("ping", "-n 60 127.0.0.1")
      : new ProcessStartInfo("sleep", "60");
    psi.RedirectStandardOutput = true;
    psi.RedirectStandardError = true;
    psi.UseShellExecute = false;
    psi.CreateNoWindow = true;

    using var process = Process.Start(psi)!;
    using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

    try
    {
      await Assert.ThrowsAnyAsync<OperationCanceledException>(
        () => LinuxBluetoothService.ReadToExitAsync(process, cts.Token));

      Assert.True(process.WaitForExit(5000), "The cancelled process is still running");
    }
    finally
    {
      if (!process.HasExited)
      {
        process.Kill(entireProcessTree: true);
      }
    }
  }
}
