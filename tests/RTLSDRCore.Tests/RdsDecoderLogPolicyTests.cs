using RTLSDRCore.DSP;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace RTLSDRCore.Tests;

/// <summary>
/// LOG-12: what <see cref="RdsDecoder"/> writes at Information. Measured on the box on 2026-09-27,
/// "Station name =" (30.8k/day, a rolling-PS station) and "Block sync lost" (16.6k/day, weak signal)
/// were 54 % of radio-api's file sink. Both keep their detail at Debug.
/// </summary>
public class RdsDecoderLogPolicyTests
{
  private const int SampleRate = 240000;
  private const float PilotFrequency = 19000f;

  private readonly Collector _sink = new();
  private readonly ManualClock _clock = new();

  private RdsDecoder Create()
  {
    var logger = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(_sink).CreateLogger();
    return new RdsDecoder(SampleRate, logger, _clock);
  }

  [Fact]
  public void StationName_IsInformationOncePerTune_ThenDebug()
  {
    var decoder = Create();

    RdsDecoderTestSeam.FeedSyntheticRdsSignal(decoder, "ROCK 92 ");
    RdsDecoderTestSeam.FeedSyntheticRdsSignal(decoder, "BY ARTST");   // a rolling-PS page

    var names = _sink.Where("RDS: Station name = ");
    Assert.Equal(new[] { LogEventLevel.Information, LogEventLevel.Debug }, names.Select(e => e.Level));

    decoder.Reset(); // a tune
    RdsDecoderTestSeam.FeedSyntheticRdsSignal(decoder, "ROCK 92 ");

    Assert.Equal(LogEventLevel.Information, _sink.Where("RDS: Station name = ").Last().Level);
  }

  [Fact]
  public void SyncLoss_IsDebugPerEvent_WithAtMostOneInformationTallyPerFiveMinutes()
  {
    var decoder = Create();

    LoseSync(decoder);
    var perEvent = _sink.Where("RDS: Block sync lost after");
    Assert.NotEmpty(perEvent);
    Assert.All(perEvent, e => Assert.Equal(LogEventLevel.Debug, e.Level));
    Assert.Empty(_sink.Where("RDS: Block sync lost {Count}"));   // window still open

    _clock.Advance(TimeSpan.FromMinutes(5));
    LoseSync(decoder);

    var tally = Assert.Single(_sink.Where("RDS: Block sync lost {Count}"));
    Assert.Equal(LogEventLevel.Information, tally.Level);
    // The tally counts every loss in its window, including the one that closed it.
    Assert.Equal(_sink.Where("RDS: Block sync lost after").Count, (int)((ScalarValue)tally.Properties["Count"]).Value!);
  }

  // Acquire sync on a clean signal, then feed noise until it is lost.
  private static void LoseSync(RdsDecoder decoder)
  {
    RdsDecoderTestSeam.FeedSyntheticRdsSignal(decoder, "ROCK 92 ");
    var random = new Random(42);
    var noise = new float[SampleRate / 2];
    for (var i = 0; i < noise.Length; i++)
    {
      noise[i] = (float)(random.NextDouble() * 2.0 - 1.0) * 0.1f;
    }
    for (var offset = 0; offset < noise.Length; offset += 4096)
    {
      var count = Math.Min(4096, noise.Length - offset);
      decoder.Process(noise.AsSpan(offset, count), count, 0f, PilotFrequency);
    }
  }

  private sealed class Collector : ILogEventSink
  {
    private readonly List<LogEvent> _events = new();

    public void Emit(LogEvent logEvent) => _events.Add(logEvent);

    public List<LogEvent> Where(string templatePrefix) =>
      _events.Where(e => e.MessageTemplate.Text.StartsWith(templatePrefix, StringComparison.Ordinal)).ToList();
  }

  private sealed class ManualClock : TimeProvider
  {
    private long _ticks = 1_000_000;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => _ticks;

    public void Advance(TimeSpan by) => _ticks += by.Ticks;
  }
}
