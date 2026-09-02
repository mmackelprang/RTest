using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Radio.Web.Services.ApiClients;
using Xunit;

namespace Radio.Web.Tests.Services;

/// <summary>
/// Tests for <see cref="SecretsApiService"/> — the client half of the "send only what changed"
/// contract that keeps a masked placeholder from being written over a real secret.
/// </summary>
public class SecretsApiServiceTests
{
  private sealed class RecordingHandler(string json = "{}", HttpStatusCode status = HttpStatusCode.OK)
    : HttpMessageHandler
  {
    public List<HttpRequestMessage> Requests { get; } = new();
    public List<string> Bodies { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
      Requests.Add(request);
      Bodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct));
      return new HttpResponseMessage(status)
      {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
      };
    }
  }

  private static (SecretsApiService Service, RecordingHandler Handler) Make(string json = "{}")
  {
    var handler = new RecordingHandler(json);
    var http = new HttpClient(handler) { BaseAddress = new Uri("http://radio-api.test.invalid") };
    return (new SecretsApiService(http, NullLogger<SecretsApiService>.Instance), handler);
  }

  private sealed class TtsSecrets
  {
    public string GoogleAPIKey { get; set; } = "";
    public string AzureAPIKey { get; set; } = "";
    public string AzureRegion { get; set; } = "";
  }

  // ---- OnlyProvided: the rule that stops an untouched field being transmitted at all ----

  [Fact]
  public void OnlyProvided_KeepsEnteredValues_AndDropsBlankOnes()
  {
    var payload = SecretsApiService.OnlyProvided(new Dictionary<string, string?>
    {
      ["GoogleAPIKey"] = "",
      ["AzureAPIKey"] = "a-new-azure-key",
      ["AzureRegion"] = "  "
    });

    payload.Should().ContainSingle();
    payload["AzureAPIKey"].Should().Be("a-new-azure-key");
    payload.Should().NotContainKey("GoogleAPIKey");
    payload.Should().NotContainKey("AzureRegion");
  }

  [Fact]
  public void OnlyProvided_DropsNullValues()
  {
    var payload = SecretsApiService.OnlyProvided(new Dictionary<string, string?>
    {
      ["GoogleAPIKey"] = null
    });

    payload.Should().BeEmpty();
  }

  [Fact]
  public void OnlyProvided_ReturnsEmpty_WhenNothingWasEntered()
  {
    // The page uses this to say "nothing to save" instead of POSTing a body of placeholders,
    // which is what the Secrets form used to do on every save.
    var payload = SecretsApiService.OnlyProvided(new Dictionary<string, string?>
    {
      ["GoogleAPIKey"] = "",
      ["AzureAPIKey"] = "",
      ["AzureRegion"] = ""
    });

    payload.Should().BeEmpty();
  }

  [Fact]
  public void OnlyProvided_KeepsAValueThatLooksLikeAMask()
  {
    // Filtering here is by "did the user type anything", never by shape. A secret that happens to
    // look like a mask is still the user's input and must be sent; deciding whether it is really a
    // round-trip needs the stored value, which only the API has.
    var payload = SecretsApiService.OnlyProvided(new Dictionary<string, string?>
    {
      ["GoogleAPIKey"] = "abcd...wxyz"
    });

    payload.Should().ContainKey("GoogleAPIKey");
  }

  // ---- Request shapes ----

  [Fact]
  public async Task GetSecretsAsync_DoesNotAskForRawValues()
  {
    // The API dropped its `raw=true` mode; the client kept sending it and silently received masks
    // while its own doc comment claimed the values were real. Nothing may ask for raw again.
    var (service, handler) = Make("""{"GoogleAPIKey":"abcd...wxyz","AzureAPIKey":"","AzureRegion":"********"}""");

    await service.GetSecretsAsync<TtsSecrets>("tts");

    handler.Requests.Should().ContainSingle();
    var uri = handler.Requests[0].RequestUri!.ToString();
    uri.Should().EndWith("/api/secrets/tts");
    uri.Should().NotContain("raw");
  }

  [Fact]
  public async Task GetSecretsAsync_ReturnsTheMaskedStatus()
  {
    var (service, _) = Make("""{"GoogleAPIKey":"abcd...wxyz","AzureAPIKey":"","AzureRegion":"********"}""");

    var status = await service.GetSecretsAsync<TtsSecrets>("tts");

    status!.GoogleAPIKey.Should().Be("abcd...wxyz");
    status.AzureAPIKey.Should().BeEmpty();
    status.AzureRegion.Should().Be("********");
  }

  [Fact]
  public async Task SaveSecretsAsync_SendsOnlyTheSuppliedProperties()
  {
    var (service, handler) = Make();

    var payload = SecretsApiService.OnlyProvided(new Dictionary<string, string?>
    {
      ["GoogleAPIKey"] = "",
      ["AzureAPIKey"] = "a-new-azure-key",
      ["AzureRegion"] = ""
    });
    var ok = await service.SaveSecretsAsync("tts", payload);

    ok.Should().BeTrue();
    handler.Bodies[0].Should().Contain("AzureAPIKey");
    handler.Bodies[0].Should().NotContain("GoogleAPIKey");
    handler.Bodies[0].Should().NotContain("AzureRegion");
  }

  [Fact]
  public async Task ClearSecretAsync_DeletesThatOnePropertyOnly()
  {
    var (service, handler) = Make();

    var ok = await service.ClearSecretAsync("tts", "AzureAPIKey");

    ok.Should().BeTrue();
    handler.Requests[0].Method.Should().Be(HttpMethod.Delete);
    handler.Requests[0].RequestUri!.ToString().Should().EndWith("/api/secrets/tts/AzureAPIKey");
  }

  [Fact]
  public async Task ClearSecretsAsync_DeletesTheWholeSection()
  {
    var (service, handler) = Make();

    var ok = await service.ClearSecretsAsync("tts");

    ok.Should().BeTrue();
    handler.Requests[0].Method.Should().Be(HttpMethod.Delete);
    handler.Requests[0].RequestUri!.ToString().Should().EndWith("/api/secrets/tts");
  }
}
