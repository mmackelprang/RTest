using Microsoft.AspNetCore.Mvc;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.External;
using Radio.Core.Interfaces.Input;
using Radio.Infrastructure.Platform.Input;

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
  /// Everything the encoder Settings surface renders: tier, timestamps, flash staleness, and the
  /// designed-vs-read-back value of every configurable field (ENC-8).
  /// </summary>
  [HttpGet("encoder/provisioning")]
  [ProducesResponseType(typeof(RotaryEncoderProvisioningSnapshot), StatusCodes.Status200OK)]
  public IActionResult GetEncoderProvisioning()
  {
    var provisioning = _serviceProvider.GetService<IRotaryEncoderProvisioning>();
    if (provisioning == null)
    {
      // The encoder subsystem is not registered in this host at all. Distinct from "disabled" and
      // from "not connected", and the page says so rather than showing an empty table.
      return Ok(new RotaryEncoderProvisioningSnapshot());
    }

    return Ok(provisioning.GetSnapshot());
  }

  /// <summary>Pushes the configuration and verifies it by read-back. Does not write flash.</summary>
  [HttpPost("encoder/reapply")]
  [ProducesResponseType(typeof(RotaryEncoderProvisioningSnapshot), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status409Conflict)]
  public Task<IActionResult> ReapplyEncoderConfig(CancellationToken ct) =>
    RunProvisioningAsync(p => p.ReapplyAsync(ct));

  /// <summary>Pushes, verifies, then writes the verified bytes to the device's flash.</summary>
  [HttpPost("encoder/save")]
  [ProducesResponseType(typeof(RotaryEncoderProvisioningSnapshot), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status409Conflict)]
  public Task<IActionResult> SaveEncoderConfigToDevice(CancellationToken ct) =>
    RunProvisioningAsync(p => p.SaveToDeviceAsync(ct));

  /// <summary>Sets one knob's direction override and pushes it immediately.</summary>
  [HttpPut("encoder/reverse/{index:int}")]
  [ProducesResponseType(typeof(RotaryEncoderProvisioningSnapshot), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  [ProducesResponseType(StatusCodes.Status409Conflict)]
  public Task<IActionResult> SetEncoderReverse(int index, [FromBody] SetEncoderReverseRequest request, CancellationToken ct)
  {
    if (index < 0 || index >= RotaryEncoderDeviceConfig.EncoderCount)
    {
      return Task.FromResult<IActionResult>(BadRequest(new { error = "Encoder index out of range" }));
    }

    return RunProvisioningAsync(p => p.SetReverseAsync(index, request.Reverse, ct));
  }

  /// <summary>Sends the device's counter-reset command.</summary>
  [HttpPost("encoder/reset-counters")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status409Conflict)]
  public async Task<IActionResult> ResetEncoderCounters(CancellationToken ct)
  {
    var provisioning = _serviceProvider.GetService<IRotaryEncoderProvisioning>();
    if (provisioning == null)
    {
      return Conflict(new { error = "The encoder subsystem is not available." });
    }

    // "sent", not "zeroed": the protocol has no acknowledgement for this command.
    bool sent = await provisioning.ResetCountersAsync(ct);
    return sent ? Ok(new { sent = true }) : Conflict(new { error = "The encoder is not connected." });
  }

  /// <summary>
  /// What each knob currently does, read from the router's own dispatch table (ENC-8).
  ///
  /// <para>
  /// ⚠ This is the <b>software</b> mapping, which is not guaranteed to be the cabinet's engraved
  /// order. Since ENC-5 the two agree on every index except <b>2</b>, where the visualiser sits
  /// until ENC-7 puts PRESETS there. The Settings page renders both and names whichever knobs
  /// actually disagree; it does not pick one.
  /// </para>
  /// </summary>
  [HttpGet("encoder/mapping")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  public IActionResult GetEncoderMapping()
  {
    var router = _serviceProvider.GetService<RotaryEncoderActionRouter>();
    if (router == null)
    {
      return Ok(Array.Empty<object>());
    }

    return Ok(router.Mapping.Select(m => new
    {
      m.EncoderIndex,
      CabinetName = RotaryEncoderCabinetNames.For(m.EncoderIndex),
      m.TurnDescription,
      m.PressDescription,
    }));
  }

  private async Task<IActionResult> RunProvisioningAsync(
    Func<IRotaryEncoderProvisioning, Task<RotaryEncoderProvisioningSnapshot>> operation)
  {
    var provisioning = _serviceProvider.GetService<IRotaryEncoderProvisioning>();
    if (provisioning == null)
    {
      return Conflict(new { error = "The encoder subsystem is not available." });
    }

    try
    {
      return Ok(await operation(provisioning));
    }
    catch (InvalidOperationException ex)
    {
      // Thrown when the device is not connected. 409 rather than 500: nothing failed, the hardware
      // is simply not there, and the page renders that differently.
      return Conflict(new { error = ex.Message });
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Encoder provisioning operation failed");
      return StatusCode(500, new { error = "The encoder did not accept the request." });
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

/// <summary>Body of <c>PUT api/integrations/encoder/reverse/{index}</c>.</summary>
public sealed record SetEncoderReverseRequest(bool Reverse);
