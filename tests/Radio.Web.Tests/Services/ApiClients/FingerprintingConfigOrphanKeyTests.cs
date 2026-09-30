using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Radio.Web.Models;
using Radio.Web.Services.ApiClients;
using Xunit;

namespace Radio.Web.Tests.Services.ApiClients;

/// <summary>
/// AUD-36: <c>IdentificationIntervalSeconds</c> was removed from <see cref="FingerprintingConfigDto"/>, but
/// the appliance's SQLite config store still holds a <c>fingerprinting:identificationIntervalSeconds</c>
/// row, and <c>POST /api/configuration/{section}</c> only upserts — nothing deletes it. So the section the
/// System Config page loads keeps carrying the orphaned key (and the older orphan <c>fpcalcPath</c>).
/// </summary>
/// <remarks>
/// ⚠ Why this is worth pinning: <see cref="ConfigurationApiService.GetConfigurationAsync{T}"/> returns
/// <c>default</c> on any deserialization failure, and the page then falls back to <c>new()</c>. If an
/// unknown key ever made that read fail, the form would silently show defaults — and saving it would write
/// <c>useShazamForAllSources = false</c> over the box's <c>true</c>, which is what kills Bluetooth album art
/// (AUD-1). These tests prove the orphan is harmless on the read and that the page no longer writes it.
/// </remarks>
public class FingerprintingConfigOrphanKeyTests
{
  // Captured verbatim from the appliance on 2026-09-30: GET /api/configuration/fingerprinting.
  private const string ApplianceSectionJson =
    "{\"enabled\":true,\"sampleDurationSeconds\":15,\"identificationIntervalSeconds\":30," +
    "\"minimumConfidenceThreshold\":0.65,\"duplicateSuppressionMinutes\":5,\"fpcalcPath\":\"\"," +
    "\"databasePath\":\"./data/fingerprints.db\",\"useShazamForAllSources\":true}";

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
    RecordingHandler handler = new(ApplianceSectionJson);
    HttpClient http = new(handler) { BaseAddress = new Uri("http://radio-api.test.invalid") };
    return (new ConfigurationApiService(http, NullLogger<ConfigurationApiService>.Instance), handler);
  }

  [Fact]
  public async Task GetConfiguration_WithOrphanedIntervalKey_StillLoadsTheStoredValues()
  {
    (ConfigurationApiService service, _) = Make();

    FingerprintingConfigDto? dto = await service.GetConfigurationAsync<FingerprintingConfigDto>("fingerprinting");

    // Non-null and carrying the STORED values, not the DTO defaults — the defaults differ on both
    // fields checked here (UseShazamForAllSources false, MinimumConfidenceThreshold 0.5).
    dto.Should().NotBeNull();
    dto!.UseShazamForAllSources.Should().BeTrue();
    dto.MinimumConfidenceThreshold.Should().Be(0.65);
    dto.SampleDurationSeconds.Should().Be(15);
  }

  [Fact]
  public async Task SavingTheLoadedSection_DoesNotWriteTheRemovedIntervalKey()
  {
    (ConfigurationApiService service, RecordingHandler handler) = Make();
    FingerprintingConfigDto? dto = await service.GetConfigurationAsync<FingerprintingConfigDto>("fingerprinting");

    bool ok = await service.UpdateConfigurationAsync("fingerprinting", dto!);

    ok.Should().BeTrue();
    handler.PostBodies.Should().ContainSingle();
    handler.PostBodies[0].Should().NotContainEquivalentOf("identificationInterval");
    handler.PostBodies[0].Should().ContainEquivalentOf("useShazamForAllSources\":true");
  }
}
