using System.Text.RegularExpressions;
using Radio.Infrastructure.Platform.Display;

namespace Radio.Infrastructure.Tests.Platform.Display;

/// <summary>
/// The panel power-on command exists in three places — <see cref="MutterPanelPowerControl"/>, the
/// canonical <c>radio-api.service</c>'s <c>ExecStopPost=</c>, and the fallback drop-in provision.sh
/// installs on an older box — and all three must say the same thing (<c>ENC-22</c>). The unit-file
/// copies are the ones that run when the process that could otherwise wake the panel is gone, so a
/// drifted copy fails exactly when it is the last line of defence.
/// </summary>
public class PanelPowerCommandParityTests
{
  [Theory]
  [InlineData("deploy/common/radio-api.service")]
  [InlineData("deploy/provision/systemd/radio-api.service.d/panel-power-on.conf")]
  public void ExecStopPost_RunsTheSamePowerOnCommandAsTheService(string relativePath)
  {
    string[] lines = File.ReadAllLines(Path.Combine(RepoRoot(), relativePath))
      .Where(l => l.StartsWith("ExecStopPost=", StringComparison.Ordinal))
      .ToArray();

    string line = Assert.Single(lines);

    // '-' so a failure never marks the unit failed: a stop with no graphical session is ordinary.
    Assert.StartsWith("ExecStopPost=-/usr/bin/env DBUS_SESSION_BUS_ADDRESS=unix:path=/run/user/1000/bus /usr/bin/gdbus ", line);

    string args = line[(line.IndexOf("/usr/bin/gdbus ", StringComparison.Ordinal) + "/usr/bin/gdbus ".Length)..];
    List<string> tokens = Regex.Matches(args, "\"([^\"]*)\"|(\\S+)")
      .Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value)
      .ToList();

    Assert.Equal(MutterPanelPowerControl.BuildArguments(on: true), tokens);
  }

  [Fact]
  public void TheServiceCommand_SetsPowerSaveModeZeroForOn_AndThreeForOff()
  {
    // 3 is the value measured on the box on 2026-09-28 (dpms=Off, 20/20 samples). 1 and 2 are
    // standby/suspend and were not measured.
    Assert.Equal("<0>", MutterPanelPowerControl.BuildArguments(on: true)[^1]);
    Assert.Equal("<3>", MutterPanelPowerControl.BuildArguments(on: false)[^1]);
  }

  private static string RepoRoot()
  {
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "RadioConsole.sln")))
    {
      dir = dir.Parent;
    }

    Assert.True(dir is not null, "RadioConsole.sln not found above the test output directory");
    return dir!.FullName;
  }
}
