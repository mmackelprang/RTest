using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Input;
using Radio.Infrastructure.Platform.Input;

namespace Radio.API.Services;

/// <summary>
/// Background service that starts the rotary encoder HID reader and action router.
/// Gated by RotaryEncoderOptions.Enabled — does nothing if encoders are disabled.
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
      _logger.LogInformation("Rotary encoder service is disabled");
      return;
    }

    _logger.LogInformation("Starting rotary encoder service (VID=0x{VID:X4}, PID=0x{PID:X4})",
      _options.Value.VendorId, _options.Value.ProductId);

    try
    {
      await _encoderService.StartAsync(stoppingToken);

      // Keep running until cancellation
      await Task.Delay(Timeout.Infinite, stoppingToken);
    }
    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
    {
      // Normal shutdown
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Rotary encoder service failed");
    }
    finally
    {
      await _encoderService.StopAsync();
      _logger.LogInformation("Rotary encoder service stopped");
    }
  }

  public override void Dispose()
  {
    _actionRouter.Dispose();
    _encoderService.Dispose();
    base.Dispose();
  }
}
