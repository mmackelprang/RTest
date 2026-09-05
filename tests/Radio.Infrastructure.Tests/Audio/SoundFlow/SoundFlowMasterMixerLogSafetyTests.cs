using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Audio.SoundFlow;
using Radio.Infrastructure.Audio.Sources.Events;
using Radio.Infrastructure.Tests.External;

namespace Radio.Infrastructure.Tests.Audio.SoundFlow;

/// <summary>
/// <c>TTS-11</c> <c>T3</c>: <see cref="SoundFlowMasterMixer"/>'s add/remove bookkeeping carries no
/// utterance text, even while it is holding a real <see cref="TTSEventSource"/>.
/// </summary>
/// <remarks>
/// ⚠ THIS IS THE MOST VALUABLE TEST IN THE TTS-11 SET, because it pins the GENERIC leak rather
/// than a domain-specific one. The defect was never "three lines that each decided to log text": it
/// was one property — <c>TTSEventSource.Name</c> is user content wearing a display name's clothes —
/// meeting a habit of logging an <c>IAudioSource</c>'s name from code that has no idea what it is
/// holding. A future <c>IAudioSource</c> implementation whose <c>Name</c> embeds user text is
/// caught here and nowhere else in the suite.
///
/// ⚠ <c>RemoveSource</c>'s arm is LATENT — no caller in the tree passes a TTS source to it today —
/// so the only thing that proves it is covered is running the mutation for it. Plan § 4.4 requires
/// both, and both were run.
///
/// ⛔ The "Removed audio source … from mixer" wording is NOT this row's to fix. CLAUDE.md §
/// Pre-Merge Review names it as a known comment-accuracy defect (the method mutates
/// <c>_sources</c> only), and it needs a row about mixer detach semantics rather than an
/// opportunistic reword inside a logging fix.
/// </remarks>
public class SoundFlowMasterMixerLogSafetyTests
{
  /// <summary>
  /// Chosen to be absent from every other fixture in the suite, so a <c>DoesNotContain</c> cannot
  /// pass by accident against generic text.
  /// </summary>
  private const string Sentinel = "Marmalade sentinel four seven";

  private static TTSEventSource CreateRealTtsSource(CapturingLoggerProvider logs) =>
    new(
      Sentinel,
      new TTSParameters(),
      new MemoryStream(new byte[1000]),
      TimeSpan.FromMilliseconds(10),
      logs.CreateLogger<TTSEventSource>());

  [Fact]
  public void NeitherAddingNorRemovingARealTtsSourceWritesTheUtteranceOrItsName()
  {
    var logs = new CapturingLoggerProvider();
    var mixer = new SoundFlowMasterMixer(logs.CreateLogger<SoundFlowMasterMixer>());
    var source = CreateRealTtsSource(logs);

    // The source's Name really does embed the text — that is the property this row does NOT change
    // (plan § 0.4: Name is a display property and changing it would be a UI change wearing a
    // logging fix's clothes). So the mixer must be the thing that declines to log it.
    Assert.Contains(Sentinel[..20], source.Name, StringComparison.Ordinal);

    mixer.AddSource(source);
    mixer.RemoveSource(source);

    // ⚠ Without this the whole test passes vacuously against a mixer that logs nothing at all.
    Assert.NotEmpty(logs.Messages);

    foreach (var message in logs.Messages)
    {
      Assert.DoesNotContain(Sentinel, message, StringComparison.Ordinal);
      // The truncated form too: Name clips at 47 characters, so a leak through Name would show up
      // as a prefix rather than the whole sentinel.
      Assert.DoesNotContain("Marmalade", message, StringComparison.Ordinal);
      Assert.DoesNotContain("TTS: ", message, StringComparison.Ordinal);
    }
  }

  [Fact]
  public void BothBookkeepingLinesAreWrittenAndCarryTheIdAndTheType()
  {
    // "No name" must not be achieved by logging nothing: Id is what joins a mixer line to the
    // ducking lines in AudioManager for the same source, and Type is what replaced Name.
    var logs = new CapturingLoggerProvider();
    var mixer = new SoundFlowMasterMixer(logs.CreateLogger<SoundFlowMasterMixer>());
    var source = CreateRealTtsSource(logs);

    mixer.AddSource(source);
    mixer.RemoveSource(source);

    var added = Assert.Single(
      logs.Messages, m => m.StartsWith("Added audio source", StringComparison.Ordinal));
    var removed = Assert.Single(
      logs.Messages, m => m.StartsWith("Removed audio source", StringComparison.Ordinal));

    foreach (var line in new[] { added, removed })
    {
      Assert.Contains(source.Id, line, StringComparison.Ordinal);
      Assert.Contains(AudioSourceType.TTS.ToString(), line, StringComparison.Ordinal);
    }
  }
}
