using System.Text.Json;
using System.Text.Json.Serialization;
using Radio.Infrastructure.Platform.Input;

namespace Radio.Infrastructure.Tests.Platform.Input;

/// <summary>
/// The contract between the ENC-17 virtual-encoder harness
/// (<c>tools/encoder-harness/virtual_encoder.py</c>) and the shipped
/// <see cref="RotaryEncoderDecoder"/>.
///
/// <para>
/// One artifact, two readers. <c>tools/encoder-harness/report-vectors.json</c> holds the golden
/// frames; the harness's <c>--selftest</c> checks that the bytes it builds equal each vector's
/// <c>hex</c>, and the tests below decode that same <c>hex</c> with the real decoder the appliance
/// runs. A change to either side that the other does not follow fails one of the two.
/// </para>
///
/// <para>
/// Why bother: <c>design/INTEGRATIONS.md</c> documented a wrong 8-byte encoder report format for
/// months and nothing mechanical caught it. A harness that encoded its frames its own way could
/// reproduce that failure silently — it would inject bytes that are not what the hardware sends,
/// and every UAT driven by it would be measuring fiction, confidently. A harness that only agrees
/// with itself proves nothing about the decoder.
/// </para>
///
/// <para>
/// <b>What these tests do not cover.</b> Nothing here opens <c>/dev/uhid</c>, or exercises the
/// kernel HID stack, HidSharp, or <see cref="HidRotaryEncoderService"/>'s I/O — those need the
/// appliance. Nor do they run the Python harness; they read the file it is checked against. This
/// file covers the frame layout and the accumulator semantics, and nothing else.
/// </para>
/// </summary>
public class VirtualEncoderHarnessProtocolTests
{
  /// <summary>Full report 0x01 length including the report ID.</summary>
  private const int PositionsReportLength = RotaryEncoderDecoder.PositionPayloadSize + 1;   // 37

  /// <summary>Index of the VOLUME encoder, per the vectors' <c>encoderNames</c>.</summary>
  private const int VolumeEncoder = 0;

  private static readonly string VectorsPath =
    Path.Combine(AppContext.BaseDirectory, "report-vectors.json");

  private static readonly JsonSerializerOptions VectorJsonOptions = new()
  {
    PropertyNameCaseInsensitive = true,
  };

  private static readonly VectorsFile Vectors = LoadVectors();

  private static VectorsFile LoadVectors()
  {
    if (!File.Exists(VectorsPath))
    {
      throw new FileNotFoundException(
        $"report-vectors.json is not in the test output at '{VectorsPath}'. The " +
        @"<Content Include=""..\..\tools\encoder-harness\report-vectors.json"">" +
        " item in Radio.Infrastructure.Tests.csproj is what copies it there.",
        VectorsPath);
    }

    return JsonSerializer.Deserialize<VectorsFile>(File.ReadAllText(VectorsPath), VectorJsonOptions)
      ?? throw new InvalidOperationException($"'{VectorsPath}' deserialized to null.");
  }

  // ---------------------------------------------------------------------------------------------
  // Tests
  // ---------------------------------------------------------------------------------------------

  [Fact]
  public void VectorsFile_IsPresentInTheTestOutput()
  {
    // A missing vectors file fails every test in this file, because the static load throws. This
    // is the one whose name says why, and the loader's message names the csproj item that copies
    // the file — so the failure gets read rather than diagnosed.
    Assert.True(File.Exists(VectorsPath), $"report-vectors.json is not at '{VectorsPath}'.");
    Assert.NotEmpty(Vectors.PositionReports);
    Assert.NotEmpty(Vectors.DecodeSequences);
  }

  [Theory]
  [MemberData(nameof(PositionReportNames))]
  public void PositionReport_DecodesBackToItsVector(string name)
  {
    // Positions and buttons only. The first report of a connection is absorbed as a baseline, so
    // every vector decoded on a fresh decoder yields zero deltas whatever its accumulators hold —
    // asserting deltas here would assert the baseline rule, not the frame. The accumulators are
    // exercised by the decode sequences below.
    PositionReport vector = VectorNamed(name);
    byte[] frame = Convert.FromHexString(vector.Hex);

    Assert.Equal(PositionsReportLength, frame.Length);
    Assert.Equal(Vectors.PositionsReportSize, frame.Length);

    var decoder = new RotaryEncoderDecoder();
    decoder.BeginConnection(frame.Length);

    Assert.True(decoder.Decode(frame, frame.Length),
      $"vector '{name}': the decoder rejected the frame.");

    int[] decodedPositions = ReadPositions(decoder);
    Assert.True(vector.Positions.SequenceEqual(decodedPositions),
      $"vector '{name}': positions expected [{Join(vector.Positions)}], got [{Join(decodedPositions)}].");

    // A fresh connection starts with every button released, so a set mask bit is a press edge and
    // a clear bit is no transition at all.
    bool?[] expectedChanges = ButtonEdgesFromReleased(vector.ButtonsMask);
    Assert.True(expectedChanges.SequenceEqual(decoder.ButtonChanges),
      $"vector '{name}': button changes expected [{Join(expectedChanges)}], " +
      $"got [{Join(decoder.ButtonChanges)}].");
  }

  [Theory]
  [MemberData(nameof(DecodeSequenceNames))]
  public void DecodeSequence_RunsAsTheVectorsDescribe(string name)
  {
    DecodeSequence sequence = SequenceNamed(name);

    var decoder = new RotaryEncoderDecoder();
    decoder.BeginConnection(sequence.ReportLength);

    for (int index = 0; index < sequence.Steps.Length; index++)
    {
      DecodeStep step = sequence.Steps[index];

      // Every assertion below carries the step's own note, so a failure reads as the sentence the
      // vector author wrote rather than an index into a JSON file.
      string where = step.Note is null
        ? $"{sequence.Name} step {index}"
        : $"{sequence.Name} step {index} ({step.Note})";

      // A step form this runner does not implement would otherwise be skipped in silence — a test
      // that reads a vector and asserts less than it read is drift wearing a green checkmark.
      Assert.True(step.Unrecognized.Count == 0,
        $"{where}: unrecognized step key(s) [{string.Join(", ", step.Unrecognized.Keys)}].");
      Assert.True(step.BeginConnection is not null || step.Frame is not null,
        $"{where}: a step must either reconnect or deliver a frame.");

      if (step.BeginConnection is int reportLength)
      {
        decoder.BeginConnection(reportLength);
      }

      if (step.Frame is not null)
      {
        byte[] frame = Convert.FromHexString(VectorNamed(step.Frame).Hex);
        Assert.True(decoder.Decode(frame, frame.Length),
          $"{where}: the decoder rejected frame '{step.Frame}'.");
      }

      if (step.ExpectDeltas is int[] expectedDeltas)
      {
        Assert.True(expectedDeltas.SequenceEqual(decoder.Deltas),
          $"{where}: deltas expected [{Join(expectedDeltas)}], got [{Join(decoder.Deltas)}].");
      }

      if (step.ExpectButtonChanges is bool?[] expectedChanges)
      {
        Assert.True(expectedChanges.SequenceEqual(decoder.ButtonChanges),
          $"{where}: button changes expected [{Join(expectedChanges)}], " +
          $"got [{Join(decoder.ButtonChanges)}].");
      }

      if (step.ExpectIsBaselinedAfter is bool expectedBaselined)
      {
        if (expectedBaselined)
        {
          Assert.True(decoder.IsBaselined, $"{where}: expected the decoder to be baselined.");
        }
        else
        {
          Assert.False(decoder.IsBaselined, $"{where}: expected the decoder not to be baselined.");
        }
      }
    }
  }

  /// <summary>
  /// Designer's highest-weighted encoder test, verbatim: <i>"Turn a knob ~50 detents while
  /// unplugged, then replug: volume does not jump."</i>
  ///
  /// <para>
  /// The device's movement accumulator is free-running — it keeps counting while nothing is
  /// listening — so a host that differenced a fresh sample against its last remembered value would
  /// deliver every missed detent at once, on the volume knob. The first report after a reconnect is
  /// therefore a baseline, not an input.
  /// </para>
  ///
  /// <para>
  /// The JSON sequence <c>ReconnectAfterFiftyOfflineDetentsDeliversNoJump</c> covers the same
  /// ground from the same frames. Spelling it out again is deliberate duplication: this is the
  /// behaviour worth being greppable and readable without a JSON file open beside it.
  /// </para>
  /// </summary>
  [Fact]
  public void ReconnectAfterFiftyOfflineDetents_DeliversNoJump()
  {
    byte[] baseline = FrameNamed("baselineZero");
    byte[] oneDetentUp = FrameNamed("volumeOneDetentUp");
    byte[] fiftyOfflineDetents = FrameNamed("accumulatorAfterFiftyOfflineDetents");

    var decoder = new RotaryEncoderDecoder();
    decoder.BeginConnection(PositionsReportLength);

    // First report of the connection: absorbed, accumulator 0.
    Assert.True(decoder.Decode(baseline, baseline.Length));
    Assert.Equal(0, decoder.Deltas[VolumeEncoder]);
    Assert.True(decoder.IsBaselined);

    // One real detent before the unplug, so the decoder is holding a non-zero last value.
    Assert.True(decoder.Decode(oneDetentUp, oneDetentUp.Length));
    Assert.Equal(1, decoder.Deltas[VolumeEncoder]);

    // The unplug and the replug. Between these two lines the knob is turned ~50 detents with
    // nobody listening; the device counts them all the same.
    decoder.BeginConnection(PositionsReportLength);

    // THE ASSERTION. Fifty units of accrued movement arrive and produce no movement at all.
    Assert.True(decoder.Decode(fiftyOfflineDetents, fiftyOfflineDetents.Length));
    Assert.Equal(0, decoder.Deltas[VolumeEncoder]);

    // And the knob is live again on the very next report, measured from the new baseline:
    // accumulator 50 -> 1 is -49, which is the honest difference and not a replay of the outage.
    Assert.True(decoder.Decode(oneDetentUp, oneDetentUp.Length));
    Assert.Equal(-49, decoder.Deltas[VolumeEncoder]);
  }

  [Fact]
  public void HarnessFrameLayout_MatchesTheDecodersOffsets()
  {
    // The vectors' hex strings are 37 bytes wide and were generated against this payload size.
    // Moving either constant without regenerating the other side also breaks the per-vector tests
    // above; this one names the constant that moved, rather than leaving a dozen length failures
    // to be read backwards.
    Assert.Equal(37, RotaryEncoderDecoder.PositionPayloadSize + 1);
    Assert.Equal(37, Vectors.PositionsReportSize);
    Assert.Equal(1, Vectors.ReportIdPositions);
    Assert.Equal(RotaryEncoderDecoder.ReportIdPositions, (byte)Vectors.ReportIdPositions);
    Assert.Equal(RotaryEncoderDecoder.EncoderCount, Vectors.EncoderNames.Length);

    // VolumeEncoder is used above as the knob Designer's reconnect test is about, so pin the
    // index rather than leaving it as a comment that could go stale.
    Assert.Equal("VOLUME", Vectors.EncoderNames[VolumeEncoder]);
  }

  // ---------------------------------------------------------------------------------------------
  // Theory data
  // ---------------------------------------------------------------------------------------------

  public static TheoryData<string> PositionReportNames()
  {
    var names = new TheoryData<string>();
    foreach (PositionReport report in Vectors.PositionReports)
    {
      names.Add(report.Name);
    }

    return names;
  }

  public static TheoryData<string> DecodeSequenceNames()
  {
    var names = new TheoryData<string>();
    foreach (DecodeSequence sequence in Vectors.DecodeSequences)
    {
      names.Add(sequence.Name);
    }

    return names;
  }

  // ---------------------------------------------------------------------------------------------
  // Helpers
  // ---------------------------------------------------------------------------------------------

  private static PositionReport VectorNamed(string name) =>
    Vectors.PositionReports.SingleOrDefault(r => r.Name == name)
      ?? throw new InvalidOperationException(
        $"report-vectors.json has no positionReports entry named '{name}'.");

  private static DecodeSequence SequenceNamed(string name) =>
    Vectors.DecodeSequences.SingleOrDefault(s => s.Name == name)
      ?? throw new InvalidOperationException(
        $"report-vectors.json has no decodeSequences entry named '{name}'.");

  private static byte[] FrameNamed(string name) => Convert.FromHexString(VectorNamed(name).Hex);

  private static int[] ReadPositions(RotaryEncoderDecoder decoder) =>
    Enumerable.Range(0, RotaryEncoderDecoder.EncoderCount).Select(decoder.GetPosition).ToArray();

  /// <summary>
  /// The button transitions a decoder that has just connected reports for <paramref name="mask"/>:
  /// a set bit is a press edge, a clear bit is no transition, because every button starts released.
  /// </summary>
  private static bool?[] ButtonEdgesFromReleased(int mask)
  {
    var edges = new bool?[RotaryEncoderDecoder.EncoderCount];
    for (int i = 0; i < edges.Length; i++)
    {
      edges[i] = (mask & (1 << i)) != 0 ? true : null;
    }

    return edges;
  }

  private static string Join(IEnumerable<int> values) => string.Join(", ", values);

  private static string Join(IEnumerable<bool?> values) => string.Join(", ", values.Select(Describe));

  private static string Describe(bool? change) => change switch
  {
    true => "pressed",
    false => "released",
    null => "no change",
  };

  // ---------------------------------------------------------------------------------------------
  // report-vectors.json shape
  // ---------------------------------------------------------------------------------------------

  private sealed record VectorsFile(
    int ReportIdPositions,
    int PositionsReportSize,
    int ConfigReportSize,
    string[] EncoderNames,
    PositionReport[] PositionReports,
    DecodeSequence[] DecodeSequences);

  private sealed record PositionReport(
    string Name,
    int[] Positions,
    int ButtonsMask,
    int TiersByte,
    int[] Accumulators,
    string Hex);

  private sealed record DecodeSequence(
    string Name,
    string Description,
    int ReportLength,
    DecodeStep[] Steps);

  private sealed record DecodeStep
  {
    public int? BeginConnection { get; init; }

    public string? Frame { get; init; }

    public int[]? ExpectDeltas { get; init; }

    public bool?[]? ExpectButtonChanges { get; init; }

    public bool? ExpectIsBaselinedAfter { get; init; }

    public string? Note { get; init; }

    /// <summary>
    /// Step keys this runner has no handler for. Collected rather than dropped so a vectors file
    /// that grows a new step form fails instead of quietly asserting less than it reads.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement> Unrecognized { get; init; } = [];
  }
}
