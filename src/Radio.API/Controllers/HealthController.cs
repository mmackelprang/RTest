using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Radio.API.Models;

namespace Radio.API.Controllers;

/// <summary>
/// Health and identity endpoints. The version endpoint is the source of truth
/// for deploy verification: it returns the git SHA baked into the assembly at
/// build time, which deploy scripts compare against the local HEAD they
/// published from.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class HealthController : ControllerBase
{
  private static readonly VersionInfoDto _cached = BuildVersionInfo();

  /// <summary>
  /// Returns the git SHA, informational version, and build timestamp of the
  /// running API binary. Used by deploy scripts to confirm the deployed code
  /// matches the locally-built commit.
  /// </summary>
  [HttpGet("version")]
  [ProducesResponseType(typeof(VersionInfoDto), StatusCodes.Status200OK)]
  public ActionResult<VersionInfoDto> GetVersion() => Ok(_cached);

  private static VersionInfoDto BuildVersionInfo()
  {
    var assembly = typeof(HealthController).Assembly;
    var name = assembly.GetName();

    var informational = assembly
      .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
      .InformationalVersion ?? string.Empty;

    // .NET SDK formats InformationalVersion as "<version>+<SourceRevisionId>"
    // when SourceRevisionId is set. Anything after the first '+' is the SHA.
    var plusIndex = informational.IndexOf('+');
    var sha = plusIndex >= 0 && plusIndex < informational.Length - 1
      ? informational[(plusIndex + 1)..]
      : "unknown";

    // Assembly.Location is empty for PublishSingleFile builds, which is how the
    // deploy scripts ship Radio.API. Fall back to the host process path.
    var location = assembly.Location;
    if (string.IsNullOrEmpty(location))
    {
      location = Environment.ProcessPath ?? string.Empty;
    }

    DateTime buildTimestamp;
    try
    {
      buildTimestamp = string.IsNullOrEmpty(location)
        ? DateTime.MinValue
        : System.IO.File.GetLastWriteTimeUtc(location);
    }
    catch
    {
      buildTimestamp = DateTime.MinValue;
    }

    return new VersionInfoDto
    {
      GitSha = sha,
      GitShaShort = sha.Length >= 7 ? sha[..7] : sha,
      InformationalVersion = informational,
      AssemblyVersion = name.Version?.ToString() ?? string.Empty,
      BuildTimestampUtc = buildTimestamp,
      AssemblyName = name.Name ?? string.Empty,
    };
  }
}
