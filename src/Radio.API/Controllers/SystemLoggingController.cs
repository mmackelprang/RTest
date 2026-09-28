using Microsoft.AspNetCore.Mvc;
using Radio.API.Logging;

namespace Radio.API.Controllers;

/// <summary>
/// LOG-5: read and change Serilog minimum levels at runtime, without a restart.
/// </summary>
/// <remarks>
/// <para>
/// Only sources that configuration names (<c>Serilog:MinimumLevel:Default</c> and each
/// <c>Serilog:MinimumLevel:Override</c> key) have a switch — see <see cref="LogLevelSwitches"/> for why,
/// and for the shadowing rule between a namespace and its prefixes. Changes are not persisted; a
/// restart restores configuration.
/// </para>
/// <para>
/// <b>Authorization:</b> none, like every other endpoint here (see <see cref="SystemLogsController"/>).
/// The worst a caller can do is raise volume into the file sink, which LOG-1's size cap bounds; the
/// journald console sink is Warning-restricted independently of these switches (LOG-11).
/// </para>
/// </remarks>
[ApiController]
[Route("api/system/logging")]
public class SystemLoggingController : ControllerBase
{
  private readonly LogLevelSwitches _switches;
  private readonly ILogger<SystemLoggingController> _logger;

  public SystemLoggingController(LogLevelSwitches switches, ILogger<SystemLoggingController> logger)
  {
    _switches = switches;
    _logger = logger;
  }

  /// <summary>Every switchable source with its current and configured level.</summary>
  [HttpGet("levels")]
  [ProducesResponseType(typeof(IReadOnlyList<LogLevelState>), StatusCodes.Status200OK)]
  public ActionResult<IReadOnlyList<LogLevelState>> GetLevels()
  {
    return Ok(_switches.GetAll());
  }

  /// <summary>
  /// Sets the minimum level for one source. <paramref name="source"/> is matched exactly and
  /// case-sensitively (<c>Default</c>, or an override key such as <c>Radio</c>).
  /// </summary>
  /// <response code="200">The level was applied; the body is the source's new state.</response>
  /// <response code="400">The level is not one of Verbose, Debug, Information, Warning, Error, Fatal.</response>
  /// <response code="404">No switch exists for that source.</response>
  [HttpPut("levels/{source}")]
  [ProducesResponseType(typeof(LogLevelState), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  public ActionResult<LogLevelState> SetLevel(string source, [FromBody] SetLogLevelRequest request)
  {
    if (!LogLevelSwitches.TryParseLevel(request?.Level, out var level))
    {
      return BadRequest(new { error = "level must be one of Verbose, Debug, Information, Warning, Error, Fatal" });
    }

    var previous = _switches.TrySet(source, level);
    if (previous is null)
    {
      // Do not echo the source back: route values are caller-controlled text.
      return NotFound(new { error = "no switch for that source; GET levels lists the switchable sources" });
    }

    // Warning so the change reaches the journal as well as the file: "why is the log suddenly
    // large" is answered by this line. The source is a configured key at this point (TrySet found
    // it), not arbitrary caller text.
    _logger.LogWarning(
      "LOG-5: log level for {Source} changed at runtime {Previous} -> {Level} (not persisted; a restart restores configuration)",
      source, previous.Value, level);

    return Ok(_switches.Get(source));
  }

  /// <summary>Returns every switch to its configured level.</summary>
  [HttpPost("levels/reset")]
  [ProducesResponseType(typeof(IReadOnlyList<LogLevelState>), StatusCodes.Status200OK)]
  public ActionResult<IReadOnlyList<LogLevelState>> ResetLevels()
  {
    _switches.ResetAll();
    _logger.LogWarning("LOG-5: all log levels reset to configuration");
    return Ok(_switches.GetAll());
  }
}

/// <summary>Body of <c>PUT api/system/logging/levels/{source}</c>.</summary>
/// <param name="Level">A Serilog level name, case-insensitive.</param>
public sealed record SetLogLevelRequest(string? Level);
