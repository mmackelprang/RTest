using Radio.Core.Interfaces.Audio;

namespace Radio.Core.Tests;

/// <summary>
/// Tests for the IPrimaryAudioSource interface and related types.
/// </summary>
public class PrimaryAudioSourceTests
{
  [Fact]
  public void PlaybackCompletionReason_HasExpectedValues()
  {
    var reasons = Enum.GetValues<PlaybackCompletionReason>();

    Assert.Contains(PlaybackCompletionReason.EndOfContent, reasons);
    Assert.Contains(PlaybackCompletionReason.UserStopped, reasons);
    Assert.Contains(PlaybackCompletionReason.Error, reasons);
    Assert.Contains(PlaybackCompletionReason.Disposed, reasons);
  }

  [Fact]
  public void AudioSourceCompletedEventArgs_CanBeCreated()
  {
    var args = new AudioSourceCompletedEventArgs
    {
      SourceId = "test-source",
      Reason = PlaybackCompletionReason.EndOfContent
    };

    Assert.Equal("test-source", args.SourceId);
    Assert.Equal(PlaybackCompletionReason.EndOfContent, args.Reason);
    Assert.Null(args.Error);
  }

  [Fact]
  public void AudioSourceCompletedEventArgs_WithError_ContainsError()
  {
    var error = new InvalidOperationException("Test error");
    var args = new AudioSourceCompletedEventArgs
    {
      SourceId = "test-source",
      Reason = PlaybackCompletionReason.Error,
      Error = error
    };

    Assert.Equal("test-source", args.SourceId);
    Assert.Equal(PlaybackCompletionReason.Error, args.Reason);
    Assert.Same(error, args.Error);
  }

  [Fact]
  public void AudioSourceCompletedEventArgs_UserStopped_HasCorrectReason()
  {
    var args = new AudioSourceCompletedEventArgs
    {
      SourceId = "test-source",
      Reason = PlaybackCompletionReason.UserStopped
    };

    Assert.Equal(PlaybackCompletionReason.UserStopped, args.Reason);
  }

  [Fact]
  public void AudioSourceCompletedEventArgs_Disposed_HasCorrectReason()
  {
    var args = new AudioSourceCompletedEventArgs
    {
      SourceId = "test-source",
      Reason = PlaybackCompletionReason.Disposed
    };

    Assert.Equal(PlaybackCompletionReason.Disposed, args.Reason);
  }
}
