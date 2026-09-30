using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Radio.Web.Models;
using Radio.Web.Services.ApiClients;
using Xunit;

namespace Radio.Web.Tests.Services.ApiClients;

/// <summary>
/// AUD-16: the RF320 USB radio was removed, and with it <c>DeviceOptionsDto.Radio</c>. The appliance's
/// SQLite config store still holds <c>devices:radio|{"usbPort":"/dev/ttyUSB0"}</c> and
/// <c>devices:Radio|{"usbPort":""}</c> beside the live <c>devices:vinyl</c> / <c>devices:Vinyl</c> rows
/// (both <c>{"usbPort":"USB Microphone"}</c>), and <c>POST /api/configuration/{section}</c> only upserts —
/// nothing deletes the radio rows. So the "devices" section the System Config page loads keeps carrying
/// two orphaned radio objects.
/// </summary>
/// <remarks>
/// ⚠ Why this is worth pinning: <see cref="ConfigurationApiService.GetConfigurationAsync{T}"/> returns
/// <c>default</c> on any deserialization failure, and <c>SystemConfigPage.LoadConfigurationAsync</c> then
/// falls back to <c>new DeviceOptionsDto()</c>, whose Vinyl port defaults to <c>/dev/ttyUSB1</c>. If the
/// orphans ever made that read fail, the Devices tab would silently show that default — and pressing
/// Save would write it over the stored <c>devices:vinyl</c> row (<c>USB Microphone</c>). Same shape as
/// <see cref="FingerprintingConfigOrphanKeyTests"/>.
/// </remarks>
public class DeviceConfigOrphanRadioTests
{
  // What GET /api/configuration/devices returns on the appliance — captured verbatim from `radio`
  // (commit a86349f) on 2026-09-30, including the cast/Cast pair this test does not care about.
  // Both casings of each key are present, which makes each pair a duplicate under the
  // case-insensitive deserialization the Web client uses; that is the box's real shape, not noise.
  private const string ApplianceDevicesSectionJson =
    "{\"radio\":{\"usbPort\":\"/dev/ttyUSB0\"}," +
    "\"vinyl\":{\"usbPort\":\"USB Microphone\"}," +
    "\"cast\":{\"defaultDevice\":\"\"}," +
    "\"Radio\":{\"usbPort\":\"\"}," +
    "\"Vinyl\":{\"usbPort\":\"USB Microphone\"}," +
    "\"Cast\":{\"defaultDevice\":\"\"}}";

  private sealed class RecordingHandler(string json) : HttpMessageHandler
  {
    public List<string> PostBodies { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
      if (request.Method == HttpMethod.Post && request.Content is not null)
      {
        PostBodies.Add(await request.Content.ReadAsStringAsync(ct));
      }

      return new HttpResponseMessage(HttpStatusCode.OK)
      {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
      };
    }
  }

  private static (ConfigurationApiService Service, RecordingHandler Handler) Make()
  {
    RecordingHandler handler = new(ApplianceDevicesSectionJson);
    HttpClient http = new(handler) { BaseAddress = new Uri("http://radio-api.test.invalid") };
    return (new ConfigurationApiService(http, NullLogger<ConfigurationApiService>.Instance), handler);
  }

  [Fact]
  public async Task GetDevices_WithOrphanedRadioObjects_StillLoadsTheStoredVinylPort()
  {
    (ConfigurationApiService service, _) = Make();

    DeviceOptionsDto? dto = await service.GetConfigurationAsync<DeviceOptionsDto>("devices");

    // Non-null and carrying the STORED value, not the DTO default (/dev/ttyUSB1).
    dto.Should().NotBeNull();
    dto!.Vinyl.USBPort.Should().Be("USB Microphone");
  }

  [Fact]
  public async Task SavingTheLoadedDevicesSection_KeepsVinyl_AndWritesNoRadioKey()
  {
    (ConfigurationApiService service, RecordingHandler handler) = Make();
    DeviceOptionsDto? dto = await service.GetConfigurationAsync<DeviceOptionsDto>("devices");

    bool ok = await service.UpdateConfigurationAsync("devices", dto!);

    ok.Should().BeTrue();
    handler.PostBodies.Should().ContainSingle();
    handler.PostBodies[0].Should().ContainEquivalentOf("\"usbPort\":\"USB Microphone\"");
    handler.PostBodies[0].Should().NotContainEquivalentOf("\"radio\"");
  }
}
