using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Radio.API.Tests.TestSupport;
using Radio.Configuration.Abstractions;

namespace Radio.API.Tests.Controllers;

/// <summary>
/// Integration tests for the SecretsController.
/// </summary>
/// <remarks>
/// The round-trip tests below exist because of a live data loss on 2026-09-02: the Secrets form
/// loaded masked values into editable boxes and POSTed all of them back, so saving one field
/// overwrote every other secret with its own placeholder and killed Google TTS in the cabinet.
/// They assert against <see cref="ISecretsProvider"/> directly rather than against a subsequent
/// GET, because a mask is a fixed point of the masking function - re-masking "abcd...wxyz" yields
/// "abcd...wxyz" - so a read-back cannot tell a preserved secret from a destroyed one. That
/// property is precisely why the bug was invisible for as long as it was.
/// </remarks>
public class SecretsControllerTests : IClassFixture<CustomWebApplicationFactory<Program>>
{
  private readonly HttpClient _client;
  private readonly ISecretsProvider _secrets;

  private const string GoogleTag = "tts_google_api_key";
  private const string AzureKeyTag = "tts_azure_api_key";
  private const string AzureRegionTag = "tts_azure_region";

  public SecretsControllerTests(CustomWebApplicationFactory<Program> factory)
  {
    _client = factory.CreateClient();
    // Resolved after CreateClient so the host is built. ISecretsProvider is a singleton, so this
    // is the same instance the controller writes through.
    _secrets = factory.Services.GetRequiredService<ISecretsProvider>();
  }

  private sealed record SetSecretsResponse(int StoredCount, int UnchangedCount);

  private async Task<Dictionary<string, string>> GetSectionAsync()
  {
    var response = await _client.GetAsync("/api/secrets/tts");
    Assert.True(response.IsSuccessStatusCode);
    var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
    Assert.NotNull(body);
    return body!;
  }

  private async Task<SetSecretsResponse> PostSectionAsync(Dictionary<string, string> data)
  {
    var response = await _client.PostAsJsonAsync("/api/secrets/tts", data);
    Assert.True(response.IsSuccessStatusCode);
    var body = await response.Content.ReadFromJsonAsync<SetSecretsResponse>();
    Assert.NotNull(body);
    return body!;
  }

  [Fact]
  public async Task GetSectionSecrets_ReturnsOk_ForValidSection()
  {
    var response = await _client.GetAsync("/api/secrets/tts");

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);

    var data = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
    Assert.NotNull(data);
    Assert.True(data.ContainsKey("GoogleAPIKey"));
    Assert.True(data.ContainsKey("AzureAPIKey"));
    Assert.True(data.ContainsKey("AzureRegion"));
  }

  [Fact]
  public async Task GetSectionSecrets_ReturnsBadRequest_ForUnknownSection()
  {
    var response = await _client.GetAsync("/api/secrets/nonexistent");
    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
  }

  [Fact]
  public async Task PostSectionSecrets_StoresAndRetrieves_TtsSecrets()
  {
    // Arrange - store a test key
    var data = new Dictionary<string, string>
    {
      ["GoogleAPIKey"] = "test-google-key-12345"
    };

    // Act - store
    var postResponse = await _client.PostAsJsonAsync("/api/secrets/tts", data);
    Assert.True(postResponse.IsSuccessStatusCode);

    // Act - retrieve (always masked, there is no raw mode)
    var getResponse = await _client.GetAsync("/api/secrets/tts");
    Assert.True(getResponse.IsSuccessStatusCode);

    var result = await getResponse.Content.ReadFromJsonAsync<Dictionary<string, string>>();
    Assert.NotNull(result);
    // Value should be masked (contains "..." and preserves first/last 4 chars)
    Assert.Contains("...", result!["GoogleAPIKey"]);
    Assert.StartsWith("test", result["GoogleAPIKey"]);
    Assert.EndsWith("2345", result["GoogleAPIKey"]);
  }

  [Fact]
  public async Task PostSectionSecrets_ReturnsBadRequest_ForUnknownSection()
  {
    var data = new Dictionary<string, string> { ["Key"] = "val" };
    var response = await _client.PostAsJsonAsync("/api/secrets/nonexistent", data);
    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
  }

  [Fact]
  public async Task DeleteSectionSecrets_ReturnsOk()
  {
    // Arrange - store something first
    var data = new Dictionary<string, string> { ["AzureRegion"] = "delete-me" };
    await _client.PostAsJsonAsync("/api/secrets/tts", data);

    // Act
    var response = await _client.DeleteAsync("/api/secrets/tts");
    Assert.True(response.IsSuccessStatusCode);

    // Verify deleted (always masked now)
    var getResponse = await _client.GetAsync("/api/secrets/tts");
    var result = await getResponse.Content.ReadFromJsonAsync<Dictionary<string, string>>();
    Assert.NotNull(result);
    Assert.Equal("", result!["AzureRegion"]);
  }

  [Fact]
  public async Task DeleteSectionSecrets_ReturnsBadRequest_ForUnknownSection()
  {
    var response = await _client.DeleteAsync("/api/secrets/nonexistent");
    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
  }

  [Fact]
  public async Task ListTags_ReturnsOk()
  {
    var response = await _client.GetAsync("/api/secrets/tags");
    Assert.True(response.IsSuccessStatusCode);

    var tags = await response.Content.ReadFromJsonAsync<List<string>>();
    Assert.NotNull(tags);
  }

  [Fact]
  public async Task GetSectionSecrets_MasksValues_ByDefault()
  {
    // Arrange - store a key long enough to be masked
    var data = new Dictionary<string, string> { ["GoogleAPIKey"] = "abcdefghijklmnop" };
    await _client.PostAsJsonAsync("/api/secrets/tts", data);

    // Act
    var response = await _client.GetAsync("/api/secrets/tts");
    var result = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();

    Assert.NotNull(result);
    // Masked value should contain "..."
    Assert.Contains("...", result!["GoogleAPIKey"]);
    Assert.NotEqual("abcdefghijklmnop", result["GoogleAPIKey"]);
  }

  // ================= Mask round-trip guard =================

  [Fact]
  public async Task PostSectionSecrets_IsANoOp_WhenTheWholeMaskedSectionIsPostedBack()
  {
    // The 2026-09-02 sequence exactly: open the Secrets form, change nothing, press Save.
    const string google = "fake-google-key-for-tests-0001";
    const string azureKey = "fake-azure-key-for-tests-0002";
    const string azureRegion = "eastus";

    await PostSectionAsync(new Dictionary<string, string>
    {
      ["GoogleAPIKey"] = google,
      ["AzureAPIKey"] = azureKey,
      ["AzureRegion"] = azureRegion
    });

    var masked = await GetSectionAsync();

    var result = await PostSectionAsync(new Dictionary<string, string>
    {
      ["GoogleAPIKey"] = masked["GoogleAPIKey"],
      ["AzureAPIKey"] = masked["AzureAPIKey"],
      ["AzureRegion"] = masked["AzureRegion"]
    });

    Assert.Equal(0, result.StoredCount);
    Assert.Equal(3, result.UnchangedCount);

    Assert.Equal(google, await _secrets.GetSecretAsync(GoogleTag));
    Assert.Equal(azureKey, await _secrets.GetSecretAsync(AzureKeyTag));
    Assert.Equal(azureRegion, await _secrets.GetSecretAsync(AzureRegionTag));
  }

  [Fact]
  public async Task PostSectionSecrets_LeavesSecretUnchanged_WhenValueIsItsOwnMask()
  {
    const string original = "fake-google-key-for-tests-0003";
    await PostSectionAsync(new Dictionary<string, string> { ["GoogleAPIKey"] = original });

    var masked = (await GetSectionAsync())["GoogleAPIKey"];
    Assert.NotEqual(original, masked);

    var result = await PostSectionAsync(new Dictionary<string, string> { ["GoogleAPIKey"] = masked });

    Assert.Equal(0, result.StoredCount);
    Assert.Equal(1, result.UnchangedCount);
    Assert.Equal(original, await _secrets.GetSecretAsync(GoogleTag));
  }

  [Fact]
  public async Task PostSectionSecrets_LeavesShortSecretUnchanged_WhenValueIsTheAsteriskMask()
  {
    // A value of 8 characters or fewer masks to "********". AzureRegion is the real case: without
    // this branch the same save stores the literal asterisks and the endpoint URL becomes
    // https://********.tts.speech.microsoft.com.
    const string region = "eastus";
    await PostSectionAsync(new Dictionary<string, string> { ["AzureRegion"] = region });

    var masked = (await GetSectionAsync())["AzureRegion"];
    Assert.Equal("********", masked);

    var result = await PostSectionAsync(new Dictionary<string, string> { ["AzureRegion"] = masked });

    Assert.Equal(0, result.StoredCount);
    Assert.Equal(1, result.UnchangedCount);
    Assert.Equal(region, await _secrets.GetSecretAsync(AzureRegionTag));
  }

  [Fact]
  public async Task PostSectionSecrets_ReplacesSecret_WhenValueIsGenuinelyNew()
  {
    const string original = "fake-google-key-for-tests-0004";
    const string replacement = "fake-google-key-for-tests-0005";

    await PostSectionAsync(new Dictionary<string, string> { ["GoogleAPIKey"] = original });
    var result = await PostSectionAsync(new Dictionary<string, string> { ["GoogleAPIKey"] = replacement });

    Assert.Equal(1, result.StoredCount);
    Assert.Equal(0, result.UnchangedCount);
    Assert.Equal(replacement, await _secrets.GetSecretAsync(GoogleTag));
  }

  [Fact]
  public async Task PostSectionSecrets_DeclinesAMaskShapedValue_EvenWhenItIsNotThisSecretsOwnMask()
  {
    // The guard tests the submitted string's shape, so it declines a mask that does not correspond
    // to the value stored now. That is the point: a form loaded before the secret changed
    // elsewhere posts a stale mask, and a guard that compared the two would let it through and
    // destroy the newer secret. The cost - a genuine secret shaped exactly like a mask cannot be
    // saved here - is the safe direction, and is pinned by this test rather than left implicit.
    const string original = "fake-azure-key-for-tests-0006";
    await PostSectionAsync(new Dictionary<string, string> { ["AzureAPIKey"] = original });

    var stale = await PostSectionAsync(new Dictionary<string, string> { ["AzureAPIKey"] = "wxyz...abcd" });

    Assert.Equal(0, stale.StoredCount);
    Assert.Equal(1, stale.UnchangedCount);
    Assert.Equal(original, await _secrets.GetSecretAsync(AzureKeyTag));

    // Same for the short form, against a secret whose own mask is the ellipsis form.
    var asterisks = await PostSectionAsync(new Dictionary<string, string> { ["AzureAPIKey"] = "********" });

    Assert.Equal(0, asterisks.StoredCount);
    Assert.Equal(original, await _secrets.GetSecretAsync(AzureKeyTag));
  }

  // ================= Blank means unchanged, deletion is explicit =================

  [Fact]
  public async Task PostSectionSecrets_LeavesSecretUnchanged_WhenValueIsBlank()
  {
    // Blank used to mean "delete". It now means "unchanged", because the UI presents a configured
    // secret as an empty box; under the old rule an ordinary Save would have erased every secret
    // the user did not retype.
    const string original = "fake-google-key-for-tests-0008";
    await PostSectionAsync(new Dictionary<string, string> { ["GoogleAPIKey"] = original });

    var result = await PostSectionAsync(new Dictionary<string, string> { ["GoogleAPIKey"] = "" });

    Assert.Equal(0, result.StoredCount);
    Assert.Equal(1, result.UnchangedCount);
    Assert.Equal(original, await _secrets.GetSecretAsync(GoogleTag));
  }

  [Theory]
  [InlineData(" ")]
  [InlineData("   ")]
  [InlineData("\t")]
  public async Task PostSectionSecrets_LeavesSecretUnchanged_WhenValueIsWhitespaceOnly(string whitespace)
  {
    // Whitespace is not blank to string.IsNullOrEmpty and is not mask-shaped, so before the blank
    // check was widened it fell through to the write and replaced a live credential with a space -
    // the same silent overwrite, reachable from Swagger or curl rather than from the Blazor page.
    const string original = "fake-google-key-for-tests-0011";
    await PostSectionAsync(new Dictionary<string, string> { ["GoogleAPIKey"] = original });

    var result = await PostSectionAsync(new Dictionary<string, string> { ["GoogleAPIKey"] = whitespace });

    Assert.Equal(0, result.StoredCount);
    Assert.Equal(1, result.UnchangedCount);
    Assert.Equal(original, await _secrets.GetSecretAsync(GoogleTag));
  }

  [Fact]
  public async Task DeleteSectionSecret_RemovesOnlyThatProperty()
  {
    const string google = "fake-google-key-for-tests-0009";
    const string azureKey = "fake-azure-key-for-tests-0010";

    await PostSectionAsync(new Dictionary<string, string>
    {
      ["GoogleAPIKey"] = google,
      ["AzureAPIKey"] = azureKey,
      ["AzureRegion"] = "eastus"
    });

    var response = await _client.DeleteAsync("/api/secrets/tts/AzureRegion");
    Assert.True(response.IsSuccessStatusCode);

    Assert.True(string.IsNullOrEmpty(await _secrets.GetSecretAsync(AzureRegionTag)));
    Assert.Equal(google, await _secrets.GetSecretAsync(GoogleTag));
    Assert.Equal(azureKey, await _secrets.GetSecretAsync(AzureKeyTag));
  }

  [Fact]
  public async Task DeleteSectionSecret_ReturnsBadRequest_ForUnknownProperty()
  {
    var response = await _client.DeleteAsync("/api/secrets/tts/NotAProperty");
    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
  }

  [Fact]
  public async Task DeleteSectionSecret_ReturnsBadRequest_ForUnknownSection()
  {
    var response = await _client.DeleteAsync("/api/secrets/nonexistent/GoogleAPIKey");
    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
  }
}
