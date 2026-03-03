using Microsoft.AspNetCore.Mvc;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.External;
using Radio.Core.Interfaces.Input;

namespace Radio.API.Controllers;

/// <summary>
/// API controller for integration runtime status (rotary encoders, phone integration).
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class IntegrationsController : ControllerBase
{
  private readonly ILogger<IntegrationsController> _logger;
  private readonly IServiceProvider _serviceProvider;

  public IntegrationsController(
    ILogger<IntegrationsController> logger,
    IServiceProvider serviceProvider)
  {
    _logger = logger;
    _serviceProvider = serviceProvider;
  }

  /// <summary>
  /// Get rotary encoder runtime status.
  /// </summary>
  [HttpGet("encoder/status")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  public IActionResult GetEncoderStatus()
  {
    try
    {
      var encoderService = _serviceProvider.GetService<IRotaryEncoderService>();
      if (encoderService == null)
      {
        return Ok(new
        {
          Enabled = false,
          IsConnected = false,
          DevicePath = "",
          VendorId = 0,
          ProductId = 0
        });
      }

      var options = _serviceProvider.GetService<Microsoft.Extensions.Options.IOptions<RotaryEncoderOptions>>();
      var config = options?.Value;

      return Ok(new
      {
        Enabled = true,
        encoderService.IsConnected,
        DevicePath = config?.DevicePath ?? "",
        VendorId = config?.VendorId ?? 0,
        ProductId = config?.ProductId ?? 0
      });
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error getting encoder status");
      return StatusCode(500, new { error = "Failed to get encoder status" });
    }
  }

  /// <summary>
  /// Get phone integration runtime status.
  /// </summary>
  [HttpGet("phone/status")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  public IActionResult GetPhoneStatus()
  {
    try
    {
      var phoneService = _serviceProvider.GetService<IPhoneIntegrationService>();
      if (phoneService == null)
      {
        return Ok(new
        {
          Enabled = false,
          IsConnected = false,
          CurrentState = "Idle",
          CallerNumber = "",
          CallerName = "",
          HubUrl = ""
        });
      }

      var options = _serviceProvider.GetService<Microsoft.Extensions.Options.IOptions<PhoneIntegrationOptions>>();
      var config = options?.Value;

      return Ok(new
      {
        Enabled = true,
        phoneService.IsConnected,
        CurrentState = phoneService.CurrentState.ToString(),
        CallerNumber = phoneService.CallerNumber ?? "",
        CallerName = phoneService.CallerName ?? "",
        HubUrl = config?.HubUrl ?? ""
      });
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error getting phone integration status");
      return StatusCode(500, new { error = "Failed to get phone status" });
    }
  }
}
