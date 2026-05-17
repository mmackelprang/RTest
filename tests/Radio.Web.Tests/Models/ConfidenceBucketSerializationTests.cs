using System.Text.Json;
using Radio.Web.Models;

namespace Radio.Web.Tests.Models;

/// <summary>
/// Regression tests for the JSON contract between Radio.API (server) and
/// Radio.Web (client) for the <see cref="ConfidenceBucket"/> enum.
///
/// <para>
/// The API serializes enums as strings via a global
/// <c>JsonStringEnumConverter</c> registered in <c>Radio.API/Program.cs</c>.
/// HttpClient calls in <c>AudioApiService</c> (e.g.
/// <c>GetFromJsonAsync&lt;FingerprintStatusDto&gt;</c>) use default
/// <c>JsonSerializerOptions</c>, which do <em>not</em> include an enum
/// converter. Without <c>[JsonConverter(typeof(JsonStringEnumConverter))]</c>
/// on the enum declaration, every fingerprint status fetch would throw
/// <c>JsonException</c> and silently leave the recognition UI blank.
/// </para>
///
/// <para>
/// These tests exercise the deserialization path with <em>default</em>
/// options on purpose — bUnit tests reflectively-inject DTOs and bypass
/// the HttpClient pipeline, so without this test the JSON contract is
/// invisible to the test suite. UAT caught the production bug; this
/// test ensures the regression never returns.
/// </para>
/// </summary>
public class ConfidenceBucketSerializationTests
{
  [Theory]
  [InlineData("None", ConfidenceBucket.None)]
  [InlineData("Possible", ConfidenceBucket.Possible)]
  [InlineData("Likely", ConfidenceBucket.Likely)]
  [InlineData("Strong", ConfidenceBucket.Strong)]
  public void FingerprintEventDto_DeserializesConfidenceFromStringEnum_WithDefaultOptions(
    string serverEnumString,
    ConfidenceBucket expected)
  {
    // Mirrors the JSON shape the API emits — string enum, camelCase property names.
    // GetFromJsonAsync defaults: PropertyNameCaseInsensitive=true, no enum converter.
    var json = $$"""
      {
        "matchId": "abc-123",
        "audioSource": "SDR Radio",
        "sourceType": "Radio",
        "isMatch": true,
        "count": 1,
        "confidence": "{{serverEnumString}}",
        "title": "Test Track",
        "artist": "Test Artist",
        "album": null,
        "hasAlbumArt": false,
        "phase": "Matched",
        "timestamp": "2026-05-17T12:00:00Z"
      }
      """;

    var options = new JsonSerializerOptions
    {
      // Match what System.Net.Http.Json.HttpClientJsonExtensions uses by default.
      PropertyNameCaseInsensitive = true
    };

    var dto = JsonSerializer.Deserialize<FingerprintEventDto>(json, options);

    Assert.NotNull(dto);
    Assert.Equal(expected, dto!.Confidence);
    Assert.Equal("abc-123", dto.MatchId);
  }

  [Fact]
  public void FingerprintStatusDto_DeserializesNestedConfidenceEnums_WithDefaultOptions()
  {
    // The real wire shape: a FingerprintStatusDto containing RecentEvents,
    // each with a Confidence enum. This is the exact payload that broke on
    // first contact with a real API in UAT.
    var json = """
      {
        "phase": "Matched",
        "isEnabled": true,
        "fingerprintsPerMinute": 4.0,
        "metadataCallsPerMinute": 1.0,
        "recentEvents": [
          {
            "matchId": "m-1",
            "audioSource": "SDR Radio",
            "sourceType": "Radio",
            "isMatch": true,
            "count": 1,
            "confidence": "Strong",
            "title": "Track One",
            "phase": "Matched",
            "timestamp": "2026-05-17T12:00:00Z"
          },
          {
            "matchId": "m-2",
            "audioSource": "SDR Radio",
            "sourceType": "Radio",
            "isMatch": false,
            "count": 1,
            "confidence": "None",
            "phase": "NoMatch",
            "timestamp": "2026-05-17T12:01:00Z"
          }
        ],
        "lastError": null
      }
      """;

    var options = new JsonSerializerOptions
    {
      PropertyNameCaseInsensitive = true
    };

    var dto = JsonSerializer.Deserialize<FingerprintStatusDto>(json, options);

    Assert.NotNull(dto);
    Assert.Equal(2, dto!.RecentEvents.Count);
    Assert.Equal(ConfidenceBucket.Strong, dto.RecentEvents[0].Confidence);
    Assert.Equal(ConfidenceBucket.None, dto.RecentEvents[1].Confidence);
  }

  [Theory]
  [InlineData(ConfidenceBucket.None, "\"None\"")]
  [InlineData(ConfidenceBucket.Possible, "\"Possible\"")]
  [InlineData(ConfidenceBucket.Likely, "\"Likely\"")]
  [InlineData(ConfidenceBucket.Strong, "\"Strong\"")]
  public void ConfidenceBucket_SerializesAsString_WithDefaultOptions(
    ConfidenceBucket value,
    string expectedJson)
  {
    // Outbound contract sanity check — also confirms the [JsonConverter] is
    // wired bidirectionally, not just for deserialization.
    var json = JsonSerializer.Serialize(value);
    Assert.Equal(expectedJson, json);
  }
}
