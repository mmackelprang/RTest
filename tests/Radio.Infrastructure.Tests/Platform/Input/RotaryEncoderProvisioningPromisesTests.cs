using Radio.Core.Configuration;
using Radio.Infrastructure.Platform.Input;

namespace Radio.Infrastructure.Tests.Platform.Input;

/// <summary>
/// The two promises ENC-8 makes that nothing else in the suite would catch breaking: that no code
/// path can send the device's factory reset, and that the Settings page's idea of a "safety field"
/// is the verifier's idea of one.
/// </summary>
public class RotaryEncoderProvisioningPromisesTests
{
  /// <summary>The enum member this repository must never send. Named once so both halves agree.</summary>
  private const string ForbiddenCommand = "ResetDefaults";

  private static DirectoryInfo FindRepositoryRoot()
  {
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "RadioConsole.sln")))
    {
      dir = dir.Parent;
    }

    Assert.True(dir is not null,
      "Could not locate the repository root (RadioConsole.sln) from the test output directory");
    return dir!;
  }

  /// <summary>
  /// ENC-8 plan §0.3 item 1. <c>RotaryEncoderCommand.ResetDefaults</c> (<c>0x03/0x02</c>) wipes the
  /// flashed configuration and leaves the device on its factory tiers — read off this hardware on
  /// 2026-09-02 as <c>step=1</c> with (150 ms ×5), (80 ms ×15), (40 ms ×50), which at the host's 2 %
  /// per unit is <b>one detent from silence to full</b>. The member exists in the enum because the
  /// protocol defines it; nothing in <c>src/</c> may reference it.
  ///
  /// <para>
  /// Comments are stripped before matching, following
  /// <c>NoRawMiniAudioEngineConstructionTests</c>: several places in this repository explain in prose
  /// which command they deliberately do not send, and prose is not a code path.
  /// </para>
  /// </summary>
  [Fact]
  public void NoProductionCode_SendsTheDeviceFactoryReset()
  {
    DirectoryInfo root = FindRepositoryRoot();
    string declaringFile = Path.GetFullPath(
      Path.Combine(root.FullName, "src", "Radio.Core", "Configuration", "RotaryEncoderDeviceConfig.cs"));

    // Anchor the guard first. If the enum member were renamed, a search for the old name would find
    // nothing and this test would pass while a differently-named factory reset went out on the wire —
    // the guard has to fail loudly rather than quietly stop guarding.
    Assert.True(
      Enum.IsDefined(typeof(RotaryEncoderCommand), ForbiddenCommand),
      $"RotaryEncoderCommand.{ForbiddenCommand} no longer exists. It was renamed or removed, so this " +
      "test is no longer guarding anything - update it to the new name rather than deleting it.");
    Assert.True(File.Exists(declaringFile), $"Expected the command enum to live at {declaringFile}");

    var offenders = new List<string>();

    foreach (string file in Directory.EnumerateFiles(
               Path.Combine(root.FullName, "src"), "*.cs", SearchOption.AllDirectories))
    {
      // The enum's own declaration is the one legitimate mention.
      if (string.Equals(Path.GetFullPath(file), declaringFile, StringComparison.OrdinalIgnoreCase))
      {
        continue;
      }

      string[] lines = File.ReadAllLines(file);
      for (int i = 0; i < lines.Length; i++)
      {
        string code = lines[i];
        int comment = code.IndexOf("//", StringComparison.Ordinal);
        if (comment >= 0)
        {
          code = code[..comment];
        }

        if (code.Contains(ForbiddenCommand, StringComparison.Ordinal))
        {
          offenders.Add($"{Path.GetRelativePath(root.FullName, file)}:{i + 1}");
        }
      }
    }

    Assert.True(offenders.Count == 0,
      "Production code referenced the device factory reset, which puts one volume detent between " +
      "silence and full: " + string.Join(", ", offenders));
  }

  /// <summary>
  /// Two places decide what a "safety field" is: <see cref="RotaryEncoderConfigVerifier.Compare"/>,
  /// whose answer tightens the host's volume clamp from 6 units per event to 2, and
  /// <c>HidRotaryEncoderService.ProjectFields</c>, whose answer the Settings page renders. They are
  /// written out by hand in both places, so this compares the two sets rather than trusting that.
  ///
  /// <para>
  /// A field shown as ordinary while the clamp treats it as a safety field would make the page lie
  /// about why the volume knob has gone sluggish — which is the one question the page exists to
  /// answer.
  /// </para>
  /// </summary>
  [Fact]
  public void ProjectedSafetyFlags_MatchTheVerifiersOwnClassification()
  {
    RotaryEncoderDeviceConfig designed = RotaryEncoderConfigDefaults.Create();
    RotaryEncoderDeviceConfig mutated = CloneWithEveryFieldDifferent(designed);

    // Every field differs, so Compare emits a mismatch for each one and both sets are complete.
    HashSet<(int, string)> verifierSafety = RotaryEncoderConfigVerifier
      .Compare(designed, mutated)
      .Where(m => m.IsSafetyField)
      .Select(m => (m.EncoderIndex, m.Field))
      .ToHashSet();

    HashSet<(int, string)> projectedSafety = HidRotaryEncoderService
      .ProjectFields(designed, mutated)
      .Where(f => f.IsSafetyField)
      .Select(f => (f.EncoderIndex, f.Field))
      .ToHashSet();

    Assert.NotEmpty(verifierSafety);
    Assert.Equal(verifierSafety, projectedSafety);
  }

  /// <summary>
  /// Mutates every comparable field so <c>Compare</c> reports all of them. Values are chosen to
  /// differ from the designed table rather than to be meaningful.
  /// </summary>
  private static RotaryEncoderDeviceConfig CloneWithEveryFieldDifferent(RotaryEncoderDeviceConfig source)
  {
    var clone = new RotaryEncoderDeviceConfig
    {
      Version = source.Version,
      StepsPerDetent = source.StepsPerDetent == 2 ? 4 : 2,
    };

    for (int i = 0; i < RotaryEncoderDeviceConfig.EncoderCount; i++)
    {
      RotaryEncoderChannelConfig from = source.Encoders[i];
      RotaryEncoderChannelConfig to = clone.Encoders[i];

      to.MinValue = from.MinValue + 1;
      to.MaxValue = from.MaxValue + 1;
      to.StepSize = from.StepSize + 1;
      to.Wrap = !from.Wrap;
      to.Reverse = !from.Reverse;

      for (int t = 0; t < RotaryEncoderDeviceConfig.TiersPerEncoder; t++)
      {
        to.Tiers[t] = new RotaryEncoderAccelerationTier
        {
          ThresholdMs = (ushort)(from.Tiers[t].ThresholdMs + 1),
          Multiplier = (ushort)(from.Tiers[t].Multiplier + 1),
        };
      }
    }

    return clone;
  }
}
