using Microsoft.AspNetCore.Mvc;
using Radio.API.Models;
using Radio.Core.Utilities;

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
    // Parsing lives in Radio.Core so this service and Radio.Web answer the version question
    // identically — a deploy check that compares two differently-derived answers is not a check.
    AssemblyBuildInfo info = AssemblyBuildInfo.For(typeof(HealthController).Assembly);

    return new VersionInfoDto
    {
      GitSha = info.GitSha,
      GitShaShort = info.GitShaShort,
      InformationalVersion = info.InformationalVersion,
      AssemblyVersion = info.AssemblyVersion,
      BuildTimestampUtc = info.BuildTimestampUtc,
      AssemblyName = info.AssemblyName,
    };
  }
}
