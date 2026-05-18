using Moq;
using Radio.API.Mappers;
using Radio.API.Models;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;

namespace Radio.API.Tests.Mappers;

/// <summary>
/// Locks down the raw-confidence → <see cref="ConfidenceBucket"/> projection
/// introduced in PR 2 of the Radio Controller Polish arc. The threshold table
/// is the API surface for fingerprint match strength — drift here would push
/// raw percentages back through to the UI in disguise.
///
/// Thresholds (server-side, applied at the API boundary):
/// <list type="bullet">
///   <item><description>Strong   — score ≥ 0.90</description></item>
///   <item><description>Likely   — 0.80 ≤ score &lt; 0.90</description></item>
///   <item><description>Possible — 0.60 ≤ score &lt; 0.80</description></item>
///   <item><description>None     — no match OR score &lt; 0.60</description></item>
/// </list>
/// </summary>
public class AudioDtoMapperTests
{
  [Theory]
  [InlineData(0.95, ConfidenceBucket.Strong)]
  [InlineData(0.90, ConfidenceBucket.Strong)]
  [InlineData(0.89999, ConfidenceBucket.Likely)]
  [InlineData(0.85, ConfidenceBucket.Likely)]
  [InlineData(0.80, ConfidenceBucket.Likely)]
  [InlineData(0.79999, ConfidenceBucket.Possible)]
  [InlineData(0.70, ConfidenceBucket.Possible)]
  [InlineData(0.60, ConfidenceBucket.Possible)]
  [InlineData(0.59999, ConfidenceBucket.None)]
  [InlineData(0.50, ConfidenceBucket.None)]
  [InlineData(0.0, ConfidenceBucket.None)]
  public void ToConfidenceBucket_FoldsRawScoreIntoBand(double rawScore, ConfidenceBucket expected)
  {
    Assert.Equal(expected, AudioDtoMapper.ToConfidenceBucket(isMatch: true, rawConfidence: rawScore));
  }

  [Fact]
  public void ToConfidenceBucket_NoMatch_AlwaysReturnsNone()
  {
    // A fingerprint event that produced no match must surface as None on the
    // wire even when there's a stray legacy confidence value sitting on the
    // server-side record — the wire shape must mirror the IsMatch flag.
    Assert.Equal(ConfidenceBucket.None,
      AudioDtoMapper.ToConfidenceBucket(isMatch: false, rawConfidence: 0.95));
    Assert.Equal(ConfidenceBucket.None,
      AudioDtoMapper.ToConfidenceBucket(isMatch: false, rawConfidence: null));
  }

  [Fact]
  public void ToConfidenceBucket_NullRawConfidence_ReturnsNone()
  {
    // A match flagged true but with no raw score (a pipeline edge case) must
    // also surface as None — the UI's pip widget needs an unambiguous band.
    Assert.Equal(ConfidenceBucket.None,
      AudioDtoMapper.ToConfidenceBucket(isMatch: true, rawConfidence: null));
  }

  // ---------------------------------------------------------------------------
  // Duplicate-key regression coverage for the metadata copy in MapToDto.
  //
  // The original bug surfaced as:
  //   ArgumentException: An item with the same key has already been added.
  //   Key: Duration
  // raised from inside AudioDtoMapper.MapToDto when /api/sources serialized an
  // active source whose live metadata dictionary yielded the same key twice
  // during enumeration (race with background metadata writers — fingerprinting,
  // AVRCP, file-tag loader). The fix replaces the LINQ .ToDictionary() call
  // with an indexer-assignment copy (CopyMetadataSafe) so a duplicate key is
  // silently coalesced rather than thrown.
  //
  // These tests exercise the helper directly and through MapToDto with a fake
  // dictionary that mimics the race by emitting a duplicate key during
  // enumeration.
  // ---------------------------------------------------------------------------

  [Fact]
  public void CopyMetadataSafe_WithDuplicateKeysDuringEnumeration_DoesNotThrow()
  {
    // The fake represents a concurrent-mutation race: the iterator yields the
    // same key twice. A raw .ToDictionary() would throw ArgumentException here.
    var racing = new DuplicateKeyEnumerableDictionary
    {
      { StandardMetadataKeys.Title, "Test Track" },
      { StandardMetadataKeys.Duration, TimeSpan.FromSeconds(180) },
      // Second yield of the same key — last-write-wins should swallow it.
      { StandardMetadataKeys.Duration, TimeSpan.FromSeconds(999) }
    };

    var copy = AudioDtoMapper.CopyMetadataSafe(racing);

    Assert.True(copy.ContainsKey(StandardMetadataKeys.Duration));
    // Last writer wins — this is documented behaviour, not load-bearing in
    // production (the only realistic source of duplicates is a race during
    // enumeration, where either value is equally valid).
    Assert.Equal(TimeSpan.FromSeconds(999), copy[StandardMetadataKeys.Duration]);
    Assert.Equal("Test Track", copy[StandardMetadataKeys.Title]);
  }

  [Fact]
  public void CopyMetadataSafe_PreservesAllUniqueEntries()
  {
    var source = new Dictionary<string, object>
    {
      [StandardMetadataKeys.Title] = "Be Still My Soul",
      [StandardMetadataKeys.Artist] = "Steven Sharp Nelson",
      [StandardMetadataKeys.Album] = "Sacred Cello",
      [StandardMetadataKeys.Duration] = TimeSpan.FromSeconds(164),
      ["Source"] = "Bluetooth",
      ["Device"] = "Pixel 8"
    };

    var copy = AudioDtoMapper.CopyMetadataSafe(source);

    Assert.Equal(6, copy.Count);
    foreach (var kvp in source)
    {
      Assert.Equal(kvp.Value, copy[kvp.Key]);
    }
  }

  [Fact]
  public void MapToDto_WithDuplicateDurationKeyInLiveMetadata_DoesNotThrow()
  {
    // Reproduces the original Tester report: a primary source whose live
    // metadata dictionary races during enumeration and emits Duration twice.
    // MapToDto must absorb this without bubbling an ArgumentException to the
    // /api/sources HTTP response.
    var racing = new DuplicateKeyEnumerableDictionary
    {
      { StandardMetadataKeys.Title, "Be Still My Soul" },
      { StandardMetadataKeys.Artist, "Steven Sharp Nelson" },
      { StandardMetadataKeys.Duration, "00:02:44" },
      // Duplicate yield mid-enumeration — the production bug.
      { StandardMetadataKeys.Duration, "00:02:44" }
    };

    var sourceMock = new Mock<IPrimaryAudioSource>();
    sourceMock.SetupGet(s => s.Id).Returns("bt-1");
    sourceMock.SetupGet(s => s.Name).Returns("Bluetooth Audio");
    sourceMock.SetupGet(s => s.Type).Returns(AudioSourceType.Bluetooth);
    sourceMock.SetupGet(s => s.Category).Returns(AudioSourceCategory.Primary);
    sourceMock.SetupGet(s => s.State).Returns(AudioSourceState.Playing);
    sourceMock.SetupGet(s => s.Volume).Returns(0.75f);
    sourceMock.SetupGet(s => s.IsSeekable).Returns(false);
    sourceMock.SetupGet(s => s.Metadata).Returns(racing);

    var dto = sourceMock.Object.MapToDto();

    Assert.NotNull(dto);
    Assert.NotNull(dto.Metadata);
    Assert.True(dto.Metadata!.ContainsKey(StandardMetadataKeys.Duration));
    Assert.Equal("Be Still My Soul", dto.Metadata[StandardMetadataKeys.Title]);
    // Bluetooth enrichment (lines 44-50 of AudioDtoMapper) still runs after
    // the metadata copy, so Source/Device must be present too.
    Assert.Equal("Bluetooth", dto.Metadata["Source"]);
    Assert.True(dto.Metadata.ContainsKey("Device"));
  }

  /// <summary>
  /// An <see cref="IReadOnlyDictionary{TKey, TValue}"/> that simulates a
  /// concurrent-mutation race by emitting each entry exactly as it was added,
  /// even if the same key is added twice. The real
  /// <see cref="System.Collections.Generic.Dictionary{TKey, TValue}"/> can
  /// briefly enter this state if a resize/rehash races with enumeration — the
  /// iterator picks up the key from both the old and new buckets.
  /// </summary>
  private sealed class DuplicateKeyEnumerableDictionary : IReadOnlyDictionary<string, object>
  {
    private readonly List<KeyValuePair<string, object>> _entries = new();

    public void Add(string key, object value) => _entries.Add(new KeyValuePair<string, object>(key, value));

    public object this[string key]
    {
      get
      {
        foreach (var kvp in _entries)
        {
          if (kvp.Key == key)
          {
            return kvp.Value;
          }
        }
        throw new KeyNotFoundException(key);
      }
    }

    public IEnumerable<string> Keys => _entries.Select(kvp => kvp.Key);
    public IEnumerable<object> Values => _entries.Select(kvp => kvp.Value);
    public int Count => _entries.Count;

    public bool ContainsKey(string key) => _entries.Any(kvp => kvp.Key == key);

    public bool TryGetValue(string key, out object value)
    {
      foreach (var kvp in _entries)
      {
        if (kvp.Key == key)
        {
          value = kvp.Value;
          return true;
        }
      }
      value = null!;
      return false;
    }

    public IEnumerator<KeyValuePair<string, object>> GetEnumerator() => _entries.GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _entries.GetEnumerator();
  }
}
