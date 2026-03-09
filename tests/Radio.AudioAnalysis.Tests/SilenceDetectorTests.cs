using Radio.AudioAnalysis;

namespace Radio.AudioAnalysis.Tests;

public class SilenceDetectorTests
{
  [Fact]
  public void FindZeroRuns_DetectsSingleRun()
  {
    var samples = new float[] { 0.5f, 0.5f, 0f, 0f, 0f, 0f, 0.5f };
    var runs = SilenceDetector.FindZeroRuns(samples, minRunLength: 4);
    Assert.Single(runs);
    Assert.Equal(2, runs[0].Start);
    Assert.Equal(4, runs[0].Length);
  }

  [Fact]
  public void FindZeroRuns_IgnoresShortRuns()
  {
    var samples = new float[] { 0.5f, 0f, 0f, 0.5f };
    var runs = SilenceDetector.FindZeroRuns(samples, minRunLength: 4);
    Assert.Empty(runs);
  }

  [Fact]
  public void FindZeroRuns_DetectsMultipleRuns()
  {
    // [0.5, 0,0,0,0, 0.5, 0,0,0,0, 0.5]
    var samples = new float[] { 0.5f, 0f, 0f, 0f, 0f, 0.5f, 0f, 0f, 0f, 0f, 0.5f };
    var runs = SilenceDetector.FindZeroRuns(samples, minRunLength: 4);
    Assert.Equal(2, runs.Count);
  }

  [Fact]
  public void FindZeroRuns_DetectsRunAtEnd()
  {
    var samples = new float[] { 0.5f, 0f, 0f, 0f, 0f };
    var runs = SilenceDetector.FindZeroRuns(samples, minRunLength: 4);
    Assert.Single(runs);
    Assert.Equal(1, runs[0].Start);
    Assert.Equal(4, runs[0].Length);
  }

  [Fact]
  public void FindRepeatedSampleRuns_DetectsRepeatedNonZeroValues()
  {
    var samples = new float[] { 0.1f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.2f };
    var runs = SilenceDetector.FindRepeatedSampleRuns(samples, minRunLength: 8);
    Assert.Single(runs);
    Assert.Equal(1, runs[0].Start);
    Assert.Equal(8, runs[0].Length);
  }

  [Fact]
  public void FindRepeatedSampleRuns_IgnoresRepeatedZeros()
  {
    var samples = new float[20]; // all zeros
    var runs = SilenceDetector.FindRepeatedSampleRuns(samples, minRunLength: 4);
    Assert.Empty(runs); // zeros are excluded by design
  }

  [Fact]
  public void FindRepeatedSampleRuns_ReturnsEmptyForShortInput()
  {
    var runs = SilenceDetector.FindRepeatedSampleRuns(new float[] { 0.5f }, minRunLength: 2);
    Assert.Empty(runs);
  }

  [Fact]
  public void FindClippingRuns_DetectsClipping()
  {
    var samples = new float[] { 0.5f, 1.0f, 1.0f, 1.0f, 1.0f, 0.5f };
    var runs = SilenceDetector.FindClippingRuns(samples, threshold: 0.999f, minRunLength: 4);
    Assert.Single(runs);
    Assert.Equal(1, runs[0].Start);
    Assert.Equal(4, runs[0].Length);
  }

  [Fact]
  public void FindClippingRuns_DetectsNegativeClipping()
  {
    var samples = new float[] { 0.5f, -1.0f, -1.0f, -1.0f, -1.0f, 0.5f };
    var runs = SilenceDetector.FindClippingRuns(samples, threshold: 0.999f, minRunLength: 4);
    Assert.Single(runs);
  }

  [Fact]
  public void FindClippingRuns_IgnoresBelowThreshold()
  {
    var samples = new float[] { 0.5f, 0.99f, 0.99f, 0.99f, 0.99f, 0.5f };
    var runs = SilenceDetector.FindClippingRuns(samples, threshold: 0.999f, minRunLength: 4);
    Assert.Empty(runs);
  }
}
