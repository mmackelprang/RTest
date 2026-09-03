using System.Text.Json;
using FluentAssertions;
using Radio.Web.Models;
using Xunit;

namespace Radio.Web.Tests.Models;

/// <summary>
/// Pins the wire shape of <see cref="EncoderConfigStatusDto"/> (ENC-12).
///
/// <para>
/// <b>Why this file exists.</b> ENC-8 shipped a Settings page that could never deserialize its own
/// API response — two enum properties arrived as JSON strings with no
/// <c>JsonStringEnumConverter</c>, the whole snapshot threw, the client's catch returned null, and
/// every card spun for ever with the only evidence in the Web service's log. No automated gate
/// caught it. This DTO is all-<c>string</c>, so that particular trap does not apply — but "does not
/// apply" is a claim, and this proves it rather than assuming it.
/// </para>
///
/// <para>
/// <b>What this does and does not prove.</b> It pins two things: the DTO's property shape, and that
/// it survives both camelCase and PascalCase payloads under the case-insensitive options SignalR's
/// <c>JsonHubProtocol</c> uses by default. It does <b>not</b> stand in for the live UAT that is the
/// only real proof the hub payload matches — this test constructs the JSON it wants to see, so it
/// cannot catch a server that sends different property names. It catches a regression in the DTO,
/// not a disagreement between the two ends.
/// </para>
/// </summary>
public class EncoderConfigStatusDtoTests
{
  // JsonHubProtocol's default. Constructed here rather than assumed, because case-insensitivity is
  // exactly what makes the camelCase payload below work.
  private static readonly JsonSerializerOptions HubLike = new() { PropertyNameCaseInsensitive = true };

  [Theory]
  [InlineData("""{"status":"Degraded","previousStatus":"Configured"}""")]
  [InlineData("""{"Status":"Degraded","PreviousStatus":"Configured"}""")]
  public void DeserializesFromEitherCasing(string payload)
  {
    var dto = JsonSerializer.Deserialize<EncoderConfigStatusDto>(payload, HubLike);

    dto.Should().NotBeNull();
    dto!.Status.Should().Be("Degraded");
    dto.PreviousStatus.Should().Be("Configured");
  }

  [Fact]
  public void AnUnrecognisedTierDeserializesInsteadOfThrowing()
  {
    // The whole reason the tier crosses the wire as a string. A newer API build sending a tier this
    // kiosk has never heard of must arrive intact and be scored as not-reportable by
    // EncoderFaultRules, rather than throwing during deserialization on a screen nobody is watching.
    var dto = JsonSerializer.Deserialize<EncoderConfigStatusDto>(
      """{"status":"SomeTierFromANewerBuild","previousStatus":"Configured"}""", HubLike);

    dto!.Status.Should().Be("SomeTierFromANewerBuild");
    EncoderFaultRules.Level(dto.Status, isConnected: true).Should().Be(EncoderFaultLevel.None);
  }

  [Fact]
  public void MissingPropertiesFallBackToUnknown()
  {
    // A partial payload must not produce nulls that the rules would then have to defend against.
    var dto = JsonSerializer.Deserialize<EncoderConfigStatusDto>("{}", HubLike);

    dto!.Status.Should().Be("Unknown");
    dto.PreviousStatus.Should().Be("Unknown");
  }
}
