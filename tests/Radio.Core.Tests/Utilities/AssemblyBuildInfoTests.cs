using System.Reflection;
using Radio.Core.Utilities;

namespace Radio.Core.Tests.Utilities;

/// <summary>
/// Tests for <see cref="AssemblyBuildInfo"/> — the shared build-identity parser behind deploy
/// verification (OPS-1). These matter more than their size suggests: if this misreads a SHA,
/// a deploy check either passes a stale binary or fails a good one, and both failures present
/// as something else entirely.
/// </summary>
public class AssemblyBuildInfoTests
{
  [Fact]
  public void For_ParsesShaFromInformationalVersion_WhenSourceRevisionIdIsSet()
  {
    // The SDK writes "<version>+<sha>"; everything after the first '+' is the SHA.
    Assembly assembly = typeof(AssemblyBuildInfoTests).Assembly;
    string informational = assembly
      .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
      .InformationalVersion ?? string.Empty;

    AssemblyBuildInfo info = AssemblyBuildInfo.For(assembly);

    int plusIndex = informational.IndexOf('+');
    if (plusIndex >= 0 && plusIndex < informational.Length - 1)
    {
      Assert.Equal(informational[(plusIndex + 1)..], info.GitSha);
    }
    else
    {
      // Built without SourceRevisionId. "unknown" is the contract, and callers treat it as
      // "cannot verify" rather than as a mismatch — so it must never be empty or null.
      Assert.Equal("unknown", info.GitSha);
    }
  }

  [Fact]
  public void For_ShortShaIsFirstSevenCharacters_AndNeverThrowsOnShortInput()
  {
    AssemblyBuildInfo info = AssemblyBuildInfo.For(typeof(AssemblyBuildInfoTests).Assembly);

    Assert.False(string.IsNullOrEmpty(info.GitShaShort));
    if (info.GitSha.Length >= 7)
    {
      Assert.Equal(info.GitSha[..7], info.GitShaShort);
    }
    else
    {
      // Guards the substring: "unknown" is 7 chars, but a hand-set SourceRevisionId could be
      // shorter, and truncating past the end would throw inside a health endpoint.
      Assert.Equal(info.GitSha, info.GitShaShort);
    }
  }

  [Fact]
  public void For_ReportsTheAssemblyItWasAsked_About_NotTheCallers()
  {
    // Radio.API and Radio.Web both call this, and a deploy compares their answers separately.
    // Returning the calling assembly's identity instead of the requested one would make both
    // services report whichever assembly happened to invoke the helper.
    AssemblyBuildInfo core = AssemblyBuildInfo.For(typeof(AssemblyBuildInfo).Assembly);

    Assert.Equal("Radio.Core", core.AssemblyName);
  }

  [Fact]
  public void For_Throws_WhenAssemblyIsNull()
  {
    Assert.Throws<ArgumentNullException>(() => AssemblyBuildInfo.For(null!));
  }
}
