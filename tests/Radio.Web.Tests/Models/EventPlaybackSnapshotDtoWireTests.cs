using System.Text.Json;
using Radio.Web.Models;

namespace Radio.Web.Tests.Models;

/// <summary>
/// The Radio.Web half of ADR-029 §8.1's wire contract: the payload <c>Radio.API</c> broadcasts as
/// <c>EventPlaybackChanged</c> — and returns from <c>GET /api/audio/events/current</c> — deserialises
/// into the real <see cref="EventPlaybackSnapshotDto"/> with every field intact.
/// </summary>
/// <remarks>
/// <para>
/// ⚠ <b>This file exists because the API-side round-trip test could not prove what its name claimed.</b>
/// <c>Radio.API.Tests.AudioStateUpdateServiceTests.EventPlaybackChanged_RoundTripsIntoTheWebDtoShape</c>
/// deserialises into <c>WebShapedSnapshot</c> — a private record declared inside that test file which
/// <i>mirrors</i> this DTO by hand. There is no compile-time link between the two, so renaming or
/// retyping a member on <b>either</b> side leaves that test green while the contract is broken. It is a
/// real test of the emitted shape; it is not a test of this type.
/// </para>
/// <para>
/// The two together are the chain: the API test pins <b>what Radio.API emits</b>, this one pins that
/// <b>the real Radio.Web DTO reads that</b>, and the literal below is the shared contract. A change on
/// either side reds one of them. ⚠ <b>Keep the literal identical to the API test's captured payload</b> —
/// that is the whole mechanism, and the two projects cannot reference each other to enforce it.
/// </para>
/// <para>
/// ⚠ <b>What it still does not prove:</b> that the real <c>JsonHubProtocol</c> carries it. Nothing in a
/// test host does — that is <c>PHN-1e</c> <b>U1</b>, settled on the appliance instead (two
/// <c>Received EventPlaybackChanged event</c> lines emitted from inside the typed handler).
/// </para>
/// </remarks>
public class EventPlaybackSnapshotDtoWireTests
{
  /// <summary>
  /// Exactly what <c>Radio.API</c> puts on the wire — camelCase, both enums as STRINGS, and
  /// <c>TimeSpan</c>/<c>DateTimeOffset</c> in STJ's built-in renderings.
  /// </summary>
  private const string WirePayload =
    """
    {
      "id": "evp-52d6b108257b44d8906d298f50b1cb00",
      "kind": "RemoteMedia",
      "label": "Voicemail from Jane",
      "state": "Playing",
      "duration": "00:00:29",
      "positionAtBroadcast": "00:00:04.5000000",
      "broadcastAtUtc": "2026-09-05T00:04:28.9617039+00:00",
      "failureReason": null
    }
    """;

  private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

  [Fact]
  public void TheWirePayloadFillsEveryMemberOfTheRealDto()
  {
    var dto = JsonSerializer.Deserialize<EventPlaybackSnapshotDto>(WirePayload, WebOptions);

    Assert.NotNull(dto);
    Assert.Equal("evp-52d6b108257b44d8906d298f50b1cb00", dto!.Id);
    Assert.Equal("RemoteMedia", dto.Kind);
    Assert.Equal("Voicemail from Jane", dto.Label);
    Assert.Equal("Playing", dto.State);
    Assert.Equal(TimeSpan.FromSeconds(29), dto.Duration);
    Assert.Equal(TimeSpan.FromSeconds(4.5), dto.PositionAtBroadcast);
    Assert.Equal(
      DateTimeOffset.Parse("2026-09-05T00:04:28.9617039+00:00"), dto.BroadcastAtUtc);
    Assert.Null(dto.FailureReason);

    // The property the circuit backstop and PR 6's chip both gate on.
    Assert.True(dto.IsLive);
  }

  [Fact]
  public void TheEnumsAreREADAsStringsRatherThanNumbers()
  {
    // ⚠ The direction that actually breaks. SignalR does NOT use MVC's JsonStringEnumConverter —
    // AddControllers().AddJsonOptions configures the output formatter and nothing else — so a
    // snapshot handed straight to SendAsync would put "state": 1 on the hub while GET /current says
    // "state": "Playing". ADR §8.1 feeds BOTH into the same client field, so a numeric arrival is
    // not a parse failure here; State is string?, and it would silently hold "1" and match nothing.
    var numericPayload =
      WirePayload.Replace("\"state\": \"Playing\"", "\"state\": 1", StringComparison.Ordinal);

    // ⚠ It is a HARD PARSE FAILURE, not a silent mismatch, and that is worth pinning rather than
    // assuming: State is string?, and STJ refuses to read a JSON number into a string rather than
    // quietly storing "1". So a regression on the API side surfaces as a loud deserialisation error
    // on this one — the better of the two available failures, because the alternative is a chip that
    // renders nothing and a log that says nothing.
    Assert.Throws<JsonException>(
      () => JsonSerializer.Deserialize<EventPlaybackSnapshotDto>(numericPayload, WebOptions));
  }

  [Theory]
  [InlineData("Preparing", true)]
  [InlineData("Playing", true)]
  [InlineData("Paused", true)]
  [InlineData("Completed", false)]
  [InlineData("Stopped", false)]
  [InlineData("Failed", false)]
  public void IsLiveClassifiesEveryStateThisBuildKnows(string state, bool expected)
  {
    var dto = JsonSerializer.Deserialize<EventPlaybackSnapshotDto>(
      WirePayload.Replace("\"Playing\"", $"\"{state}\"", StringComparison.Ordinal), WebOptions);

    Assert.Equal(expected, dto!.IsLive);
  }

  [Fact]
  public void AStateThisBuildHasNeverHeardOfCountsAsLIVE()
  {
    // ⭐ This is what lets PHN-1f's Waiting state ship without a lockstep Radio.Web deploy, and it is
    // the reason IsLive is written as "not one of the terminal three" rather than "one of the live
    // three". An unknown state that is in fact playing MUST keep its stop control; the alternative
    // is a voicemail nobody can stop because the panel does not recognise its own state name.
    var dto = JsonSerializer.Deserialize<EventPlaybackSnapshotDto>(
      WirePayload.Replace("\"Playing\"", "\"Waiting\"", StringComparison.Ordinal), WebOptions);

    Assert.Equal("Waiting", dto!.State);
    Assert.True(dto.IsLive);
  }

  [Fact]
  public void AMissingStateIsTheOneThingThatIsNotLive()
  {
    // A payload that carried no state at all is not a playback this build should offer a stop for.
    var dto = JsonSerializer.Deserialize<EventPlaybackSnapshotDto>(
      WirePayload.Replace("\"state\": \"Playing\",", string.Empty, StringComparison.Ordinal),
      WebOptions);

    Assert.Null(dto!.State);
    Assert.False(dto.IsLive);
  }
}
