using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;

namespace Radio.Infrastructure.Platform.Display;

/// <summary>
/// Sets panel power through Mutter's <c>org.gnome.Mutter.DisplayConfig</c> <c>PowerSaveMode</c>
/// property on the desktop session bus (<c>ENC-22</c>).
///
/// <para>
/// <b>Why this route and not GNOME ScreenSaver.</b> <c>ENC-15</c> found that
/// <c>org.gnome.ScreenSaver.SetActive</c> does not reach DPMS-off on this box and produced a
/// 13-second on/off oscillation. <c>PowerSaveMode</c> was measured on <c>radio</c> on 2026-09-28 from
/// a service-like environment: <c>0 → 3</c> put <c>/sys/class/drm/card1-DP-1/dpms</c> at <c>Off</c>
/// for 20 of 20 one-second samples, and <c>3 → 0</c> brought it back with the kiosk reconnected.
/// </para>
///
/// <para>
/// <b>Why <c>gdbus</c> and not a D-Bus library.</b> It is the exact command that was measured, it is
/// the same command <c>radio-api.service</c>'s <c>ExecStopPost=</c> runs and the recovery line in
/// <c>design/INTEGRATIONS.md</c> gives, so all three cannot drift apart; and it is called a handful of
/// times a day. No shell is involved — arguments go through <see cref="ProcessStartInfo.ArgumentList"/>.
/// <c>radio-api</c> runs as the session user, so no <c>sudo -u</c> is needed either (the retired
/// ScreenSaver route used one).
/// </para>
/// </summary>
public sealed class MutterPanelPowerControl : IPanelPowerControl
{
  /// <summary>Mutter's value for "on". <c>3</c> is "off"; 1 and 2 are standby/suspend, not used.</summary>
  internal const int PowerSaveOn = 0;

  /// <summary>Mutter's value for "off" — the one measured on the box.</summary>
  internal const int PowerSaveOff = 3;

  // gdbus's own reply timeout, and a process-level backstop above it. A wedged compositor must not
  // leave a power command hanging forever; the caller treats a timeout as "not confirmed".
  private const int GdbusTimeoutSeconds = 5;
  private static readonly TimeSpan ProcessTimeout = TimeSpan.FromSeconds(8);

  private readonly ILogger<MutterPanelPowerControl> _logger;
  private readonly IOptionsMonitor<PanelPowerOptions> _options;

  public MutterPanelPowerControl(ILogger<MutterPanelPowerControl> logger, IOptionsMonitor<PanelPowerOptions> options)
  {
    _logger = logger;
    _options = options;
  }

  /// <summary>
  /// The argument list for one power command, exposed so a test can pin it against the recovery line
  /// and the unit file's <c>ExecStopPost=</c>.
  /// </summary>
  internal static IReadOnlyList<string> BuildArguments(bool on) =>
  [
    "call", "--session",
    "--timeout", GdbusTimeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
    "--dest", "org.gnome.Mutter.DisplayConfig",
    "--object-path", "/org/gnome/Mutter/DisplayConfig",
    "--method", "org.freedesktop.DBus.Properties.Set",
    "org.gnome.Mutter.DisplayConfig", "PowerSaveMode",
    $"<{(on ? PowerSaveOn : PowerSaveOff)}>",
  ];

  /// <inheritdoc />
  public async Task<bool> SetPanelPowerAsync(bool on, CancellationToken cancellationToken = default)
  {
    if (!OperatingSystem.IsLinux())
    {
      // No Mutter off Linux. Reported as not confirmed, so the service never believes a panel it
      // cannot see was powered off.
      _logger.LogDebug("Panel power control is Linux-only; ignoring request to power {State}", on ? "on" : "off");
      return false;
    }

    var psi = new ProcessStartInfo
    {
      FileName = "gdbus",
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
      CreateNoWindow = true,
    };
    foreach (string arg in BuildArguments(on))
    {
      psi.ArgumentList.Add(arg);
    }

    psi.Environment["DBUS_SESSION_BUS_ADDRESS"] = _options.CurrentValue.SessionBusAddress;

    try
    {
      using var process = Process.Start(psi);
      if (process is null)
      {
        _logger.LogWarning("Panel power {State}: gdbus did not start", on ? "on" : "off");
        return false;
      }

      using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
      timeout.CancelAfter(ProcessTimeout);

      Task<string> stderr = process.StandardError.ReadToEndAsync(timeout.Token);
      Task<string> stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
      try
      {
        await process.WaitForExitAsync(timeout.Token);
      }
      catch (OperationCanceledException)
      {
        try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
        _logger.LogWarning("Panel power {State}: gdbus did not return within {Seconds}s",
          on ? "on" : "off", ProcessTimeout.TotalSeconds);
        return false;
      }

      await Task.WhenAll(stderr, stdout);
      if (process.ExitCode != 0)
      {
        _logger.LogWarning("Panel power {State} failed (gdbus exit {Code}): {Error}",
          on ? "on" : "off", process.ExitCode, stderr.Result.Trim());
        return false;
      }

      return true;
    }
    catch (Exception ex)
    {
      // Includes a cancellation of the caller's token: "never throws" is the contract, and the
      // caller's answer to any failure is the same — the change is not confirmed.
      _logger.LogWarning(ex, "Panel power {State} failed: could not run gdbus", on ? "on" : "off");
      return false;
    }
  }
}
