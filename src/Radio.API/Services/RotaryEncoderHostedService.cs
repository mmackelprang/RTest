using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Input;
using Radio.Infrastructure.Platform.Input;

namespace Radio.API.Services;

/// <summary>
/// Background service that starts the rotary encoder HID reader and action router.
///
/// <para>
/// ENC-0: <c>RotaryEncoderOptions.Enabled</c> is an <b>escape hatch</b>, not a gate. It defaults to
/// true and exists so a misbehaving encoder can be switched off without crawling behind the
/// furniture. Whether the subsystem does anything is decided by <b>presence</b> — the reader watches
/// for the device and degrades quietly when it is absent.
/// </para>
/// </summary>
public class RotaryEncoderHostedService : BackgroundService
{
  private readonly ILogger<RotaryEncoderHostedService> _logger;
  private readonly IRotaryEncoderService _encoderService;
  private readonly RotaryEncoderActionRouter _actionRouter;
  private readonly IOptions<RotaryEncoderOptions> _options;

  public RotaryEncoderHostedService(
    ILogger<RotaryEncoderHostedService> logger,
    IRotaryEncoderService encoderService,
    RotaryEncoderActionRouter actionRouter,
    IOptions<RotaryEncoderOptions> options)
  {
    _logger = logger;
    _encoderService = encoderService;
    _actionRouter = actionRouter;
    _options = options;
  }

  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    if (!_options.Value.Enabled)
    {
      // Silent about everything from here: the owner turned the knobs off deliberately and
      // must not be nagged about the consequence.
      _logger.LogInformation(
        "Rotary encoder input is switched off by configuration (RotaryEncoder:Enabled=false)");
      return;
    }

    _logger.LogInformation("Starting rotary encoder service (VID=0x{VID:X4}, PID=0x{PID:X4})",
      _options.Value.VendorId, _options.Value.ProductId);

    var retryDelay = TimeSpan.FromSeconds(5);
    var maxRetryDelay = TimeSpan.FromMinutes(2);

    while (!stoppingToken.IsCancellationRequested)
    {
      try
      {
        await _encoderService.StartAsync(stoppingToken);

        // Keep running until cancellation
        await Task.Delay(Timeout.Infinite, stoppingToken);
      }
      catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
      {
        break;
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Rotary encoder service failed, retrying in {Delay}s", retryDelay.TotalSeconds);

        try { await _encoderService.StopAsync(); } catch { /* cleanup best-effort */ }

        try { await Task.Delay(retryDelay, stoppingToken); }
        catch (OperationCanceledException) { break; }

        retryDelay = TimeSpan.FromSeconds(Math.Min(retryDelay.TotalSeconds * 2, maxRetryDelay.TotalSeconds));
      }
    }

    try { await _encoderService.StopAsync(); } catch { /* cleanup best-effort */ }
    _logger.LogInformation("Rotary encoder service stopped");
  }

  public override void Dispose()
  {
    _actionRouter.Dispose();
    _encoderService.Dispose();
    base.Dispose();
  }
}
