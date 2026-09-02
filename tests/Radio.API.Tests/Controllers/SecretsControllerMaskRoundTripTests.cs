using Microsoft.Extensions.Logging.Abstractions;
using Radio.API.Controllers;
using Radio.Configuration.Abstractions;

namespace Radio.API.Tests.Controllers;

/// <summary>
/// Regression tests for the masked-secret round trip.
/// </summary>
/// <remarks>
/// On 2026-09-02 a save from the System Configuration page destroyed the live Azure Speech
/// credentials: <c>GET /api/secrets/tts</c> returns masked values, the page binds them into its
/// inputs, and saving posted the masks back, which the controller stored verbatim. The stored key
/// became <c>&lt;first 4&gt;...&lt;last 4&gt;</c> and the stored region became eight asterisks.
/// Nothing else holds the plaintext, so the real credentials could not be recovered.
/// </remarks>
public class SecretsControllerMaskRoundTripTests
{
  private const string LongSecret = "abcdefghijklmnop";
  private const string ShortSecret = "eastus";

  private static SecretsController CreateController(InMemorySecretsProvider provider) =>
    new(provider, NullLogger<SecretsController>.Instance);

  /// <summary>Reads back what GetSectionSecrets showed for a property.</summary>
  private static async Task<string> GetMaskAsync(SecretsController controller, string property)
  {
    var result = await controller.GetSectionSecrets("tts");
    var payload = Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(result.Result);
    var map = Assert.IsType<Dictionary<string, string>>(payload.Value);
    return map[property];
  }

  [Theory]
  [InlineData("GoogleAPIKey", "tts_google_api_key", LongSecret)]
  [InlineData("AzureAPIKey", "tts_azure_api_key", LongSecret)]
  [InlineData("AzureRegion", "tts_azure_region", ShortSecret)]
  public async Task PostingBackTheMask_LeavesTheStoredSecretIntact(
    string property, string tag, string realValue)
  {
    var provider = new InMemorySecretsProvider();
    await provider.SetSecretAsync(tag, realValue);
    var controller = CreateController(provider);

    // What the config page loads into its input for this field.
    var mask = await GetMaskAsync(controller, property);
    Assert.NotEqual(realValue, mask);

    // Saving the page without editing the field posts that mask straight back.
    await controller.SetSectionSecrets("tts", new Dictionary<string, string> { [property] = mask });

    Assert.Equal(realValue, await provider.GetSecretAsync(tag));
  }

  [Fact]
  public async Task PostingBackTheMask_DoesNotCountAsStored()
  {
    var provider = new InMemorySecretsProvider();
    await provider.SetSecretAsync("tts_azure_api_key", LongSecret);
    var controller = CreateController(provider);
    var mask = await GetMaskAsync(controller, "AzureAPIKey");

    var result = await controller.SetSectionSecrets(
      "tts", new Dictionary<string, string> { ["AzureAPIKey"] = mask });

    var payload = Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(result);
    var value = payload.Value!;
    var type = value.GetType();
    Assert.Equal(0, type.GetProperty("storedCount")!.GetValue(value));
    Assert.Equal(1, type.GetProperty("unchangedCount")!.GetValue(value));
  }

  [Fact]
  public async Task PostingARealValue_StillOverwritesTheStoredSecret()
  {
    var provider = new InMemorySecretsProvider();
    await provider.SetSecretAsync("tts_azure_api_key", LongSecret);
    var controller = CreateController(provider);

    await controller.SetSectionSecrets(
      "tts", new Dictionary<string, string> { ["AzureAPIKey"] = "a-genuinely-new-key" });

    Assert.Equal("a-genuinely-new-key", await provider.GetSecretAsync("tts_azure_api_key"));
  }

  [Theory]
  [InlineData("********")]
  [InlineData("abcd...wxyz")]
  public async Task PostingAMaskShapedValue_IsNeverStored_EvenForAnEmptySlot(string maskShaped)
  {
    // The guard reads only the submitted string, so it declines a mask even with nothing stored.
    var provider = new InMemorySecretsProvider();
    var controller = CreateController(provider);

    await controller.SetSectionSecrets(
      "tts", new Dictionary<string, string> { ["AzureRegion"] = maskShaped });

    Assert.Null(await provider.GetSecretAsync("tts_azure_region"));
  }

  [Fact]
  public async Task AStaleMask_DoesNotOverwriteASecretThatChangedSinceItWasShown()
  {
    // A form loaded in one place, saved after the secret was changed somewhere else. The posted
    // mask no longer corresponds to the stored value, so a guard that compared the two would let
    // it through and destroy the newer secret.
    var provider = new InMemorySecretsProvider();
    await provider.SetSecretAsync("tts_azure_api_key", "AAAAAAAAAAAAxxxx");
    var controller = CreateController(provider);

    var staleMask = await GetMaskAsync(controller, "AzureAPIKey");

    await provider.SetSecretAsync("tts_azure_api_key", "ZZZZZZZZZZZZyyyy");
    Assert.NotEqual(staleMask, await GetMaskAsync(controller, "AzureAPIKey"));

    await controller.SetSectionSecrets(
      "tts", new Dictionary<string, string> { ["AzureAPIKey"] = staleMask });

    Assert.Equal("ZZZZZZZZZZZZyyyy", await provider.GetSecretAsync("tts_azure_api_key"));
  }

  [Fact]
  public async Task PostingAnEmptyValue_StillClearsTheSecret()
  {
    var provider = new InMemorySecretsProvider();
    await provider.SetSecretAsync("tts_azure_region", ShortSecret);
    var controller = CreateController(provider);

    await controller.SetSectionSecrets(
      "tts", new Dictionary<string, string> { ["AzureRegion"] = "" });

    Assert.Null(await provider.GetSecretAsync("tts_azure_region"));
  }

  private sealed class InMemorySecretsProvider : ISecretsProvider
  {
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

    public Task<string?> GetSecretAsync(string tag, CancellationToken ct = default) =>
      Task.FromResult(_values.TryGetValue(tag, out var v) ? v : null);

    public Task<string> SetSecretAsync(string tag, string value, CancellationToken ct = default)
    {
      _values[tag] = value;
      return Task.FromResult(tag);
    }

    public string GenerateTag(string? hint = null) => hint ?? "tag";

    public Task<bool> DeleteSecretAsync(string tag, CancellationToken ct = default) =>
      Task.FromResult(_values.Remove(tag));

    public Task<IReadOnlyList<string>> ListTagsAsync(CancellationToken ct = default) =>
      Task.FromResult<IReadOnlyList<string>>(_values.Keys.ToList());

    public bool ContainsSecretTag(string value) => false;

    public Task<string> ResolveTagsAsync(string value, CancellationToken ct = default) =>
      Task.FromResult(value);
  }
}
