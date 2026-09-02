using System.Text.Json;
using Radio.Web.Models;
using Radio.Web.Services.ApiClients;

namespace Radio.Web.Tests.Services.ApiClients;

/// <summary>
/// Pins the wire contract between Radio.API and the Settings page.
///
/// <para>
/// These run against <see cref="IntegrationsApiService.JsonOptions"/> itself rather than a local
/// copy. A copy would let this pass while the real client still failed, which is the whole defect
/// being guarded: the page's cards render a loading spinner indefinitely when deserialization
/// throws, and the hermetic bUnit rig fails every request anyway, so null is its expected result and
/// no page-level test can tell a broken contract from an absent one.
/// </para>
/// </summary>
public class EncoderProvisioningDeserializationTests
{
  /// <summary>
  /// A real response body from <c>GET /api/integrations/encoder/provisioning</c>, captured off the
  /// appliance on 2026-09-02. Enums appear as <b>strings</b> because Radio.API registers a
  /// <c>JsonStringEnumConverter</c> in its MVC options.
  /// </summary>
  private const string RealApiResponse = """
    {
      "enabled": true,
      "isConnected": true,
      "wasEverConnected": true,
      "status": "Configured",
      "lastVerifiedUtc": "2026-09-02T21:34:22.5336188+00:00",
      "lastAttemptedUtc": "2026-09-02T21:34:22.5336188+00:00",
      "lastSavedToDeviceUtc": null,
      "flash": "NeverSaved",
      "fields": [
        {
          "encoderIndex": -1,
          "field": "steps_per_detent",
          "designedValue": "4",
          "readBackValue": "4",
          "isSafetyField": false,
          "agreement": "Agrees"
        },
        {
          "encoderIndex": 0,
          "field": "reverse",
          "designedValue": "False",
          "readBackValue": null,
          "isSafetyField": true,
          "agreement": "NotReadBack"
        }
      ]
    }
    """;

  [Fact]
  public void TheApisRealResponseDeserializes_WithEnumsArrivingAsStrings()
  {
    EncoderProvisioningDto? dto = JsonSerializer.Deserialize<EncoderProvisioningDto>(
      RealApiResponse, IntegrationsApiService.JsonOptions);

    Assert.NotNull(dto);
    Assert.Equal("Configured", dto!.Status);

    // The two properties that actually broke. "NeverSaved" and "Agrees" are strings on the wire; a
    // client without the string-enum converter throws here and the page shows a spinner for ever.
    Assert.Equal(EncoderFlashStateDto.NeverSaved, dto.Flash);
    Assert.Equal(EncoderFieldAgreementDto.Agrees, dto.Fields[0].Agreement);
    Assert.Equal(EncoderFieldAgreementDto.NotReadBack, dto.Fields[1].Agreement);

    // Three states, never two: a field with no read-back must survive the trip as null rather than
    // as an empty string that the page would print as a value the device reported.
    Assert.Null(dto.Fields[1].ReadBackValue);
    Assert.Equal("4", dto.Fields[0].ReadBackValue);
    Assert.True(dto.Fields[1].IsSafetyField);
    Assert.NotNull(dto.LastVerifiedUtc);
  }

  [Fact]
  public void TheMappingResponseDeserializes()
  {
    const string body = """
      [{"encoderIndex":0,"cabinetName":"VOLUME","turnDescription":"Volume up / down","pressDescription":"Mute on / off"}]
      """;

    List<EncoderMappingDto>? rows = JsonSerializer.Deserialize<List<EncoderMappingDto>>(
      body, IntegrationsApiService.JsonOptions);

    Assert.NotNull(rows);
    EncoderMappingDto row = Assert.Single(rows!);
    Assert.Equal(0, row.EncoderIndex);
    Assert.Equal("VOLUME", row.CabinetName);
    Assert.Equal("Volume up / down", row.TurnDescription);
  }
}
