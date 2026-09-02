using System.Reflection;

namespace Radio.Core.Utilities;

/// <summary>
/// Build identity read out of an assembly: the git SHA baked in at publish time, plus the
/// version strings and file timestamp that go with it.
///
/// <para>
/// This is the source of truth for deploy verification. A deploy publishes with
/// <c>-p:SourceRevisionId=&lt;sha&gt;</c> and then asks the running service what SHA it is
/// carrying; if the answer does not match, the deploy did not take effect. That check is only
/// worth anything if every service answers it the same way, which is why the parsing lives here
/// rather than being copied per host — two copies of this logic would be two chances for a
/// service to report its version slightly differently and quietly pass a check it should fail.
/// </para>
/// </summary>
public sealed record AssemblyBuildInfo
{
  public required string GitSha { get; init; }
  public required string GitShaShort { get; init; }
  public required string InformationalVersion { get; init; }
  public required string AssemblyVersion { get; init; }
  public required DateTime BuildTimestampUtc { get; init; }
  public required string AssemblyName { get; init; }

  /// <summary>
  /// Reads build identity from <paramref name="assembly"/>.
  ///
  /// <para>
  /// Throws <see cref="ArgumentNullException"/> for a null assembly, and nothing else: a binary
  /// published without <c>SourceRevisionId</c> reports a SHA of <c>"unknown"</c> rather than
  /// failing, which callers compare against and treat as "cannot verify" rather than as a
  /// mismatch. An unreadable file timestamp degrades to <see cref="DateTime.MinValue"/>. Both
  /// matter because this runs inside a health endpoint, where throwing would turn "I cannot tell
  /// you my version" into "I am down".
  /// </para>
  /// </summary>
  public static AssemblyBuildInfo For(Assembly assembly)
  {
    ArgumentNullException.ThrowIfNull(assembly);

    AssemblyName name = assembly.GetName();

    string informational = assembly
      .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
      .InformationalVersion ?? string.Empty;

    // The .NET SDK formats InformationalVersion as "<version>+<SourceRevisionId>" when
    // SourceRevisionId is set. Anything after the first '+' is the SHA.
    int plusIndex = informational.IndexOf('+');
    string sha = plusIndex >= 0 && plusIndex < informational.Length - 1
      ? informational[(plusIndex + 1)..]
      : "unknown";

    // Assembly.Location is empty for PublishSingleFile builds, which is how the deploy scripts
    // ship these services. Fall back to the host process path.
    string location = assembly.Location;
    if (string.IsNullOrEmpty(location))
    {
      location = Environment.ProcessPath ?? string.Empty;
    }

    DateTime buildTimestamp;
    try
    {
      buildTimestamp = string.IsNullOrEmpty(location)
        ? DateTime.MinValue
        : File.GetLastWriteTimeUtc(location);
    }
    catch
    {
      buildTimestamp = DateTime.MinValue;
    }

    return new AssemblyBuildInfo
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
