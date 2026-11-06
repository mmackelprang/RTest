using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Radio.Web.Services;
using Radio.Web.Tests.TestHelpers;

namespace Radio.Web.Tests.Services;

public class HouseAudioPasswordStoreTests
{
  private const string Plaintext = FakeHouseController.Password;

  private static bool IsSectionPost(HttpMethod method, string path) =>
    method == HttpMethod.Post && path == "/api/configuration/HouseAudio";

  /// <summary>Records what the save posted, and answers as the API does.</summary>
  private sealed class ConfigApi
  {
    public string? PostedBody { get; private set; }

    public HttpStatusCode PostStatus { get; set; } = HttpStatusCode.OK;

    public RecordingHandler Handler { get; }

    public ConfigApi()
    {
      Handler = new RecordingHandler(RespondAsync);
    }

    public string PostedProtectedValue()
    {
      using JsonDocument document = JsonDocument.Parse(PostedBody!);
      return document.RootElement.GetProperty("ProtectedPassword").GetString()!;
    }

    private async Task<HttpResponseMessage> RespondAsync(HttpRequestMessage request, CancellationToken ct)
    {
      if (request.Method != HttpMethod.Post)
      {
        // The section does not exist yet, as on a first save.
        return new HttpResponseMessage(HttpStatusCode.NotFound);
      }

      PostedBody = await request.Content!.ReadAsStringAsync(ct);
      return new HttpResponseMessage(PostStatus)
      {
        Content = new StringContent("{}", Encoding.UTF8, "application/json")
      };
    }
  }

  [Fact]
  public async Task PasswordStore_RoundTrips_AndStoresCiphertext()
  {
    IDataProtectionProvider provider = new EphemeralDataProtectionProvider();
    ConfigApi api = new();
    HouseAudioPasswordStore store = FakeHouseController.CreateStore(provider, null, api.Handler);

    bool saved = await store.SetAsync(Plaintext, CancellationToken.None);

    Assert.True(saved);
    Assert.Equal(1, api.Handler.Count(IsSectionPost));
    Assert.DoesNotContain(Plaintext, api.PostedBody);
    Assert.NotEqual(Plaintext, api.PostedProtectedValue());

    // A restart: a new store, reading the value the API was given.
    HouseAudioPasswordStore restarted =
      FakeHouseController.CreateStore(provider, api.PostedProtectedValue(), new RecordingHandler());
    Assert.Equal(Plaintext, restarted.Get());
  }

  [Fact]
  public async Task Get_RightAfterASave_ReturnsTheNewPassword_BeforeConfigurationReloads()
  {
    IDataProtectionProvider provider = new EphemeralDataProtectionProvider();
    string oldValue = provider.CreateProtector(HouseAudioPasswordStore.ProtectorPurpose).Protect("old");
    HouseAudioPasswordStore store = FakeHouseController.CreateStore(provider, oldValue, new ConfigApi().Handler);

    await store.SetAsync(Plaintext, CancellationToken.None);

    // This store's IConfiguration never changes, as when the ConfigChanged push has not arrived.
    Assert.Equal(Plaintext, store.Get());
  }

  [Fact]
  public async Task SetAsync_WhenTheApiRefuses_ReturnsFalse_AndKeepsTheOldPassword()
  {
    IDataProtectionProvider provider = new EphemeralDataProtectionProvider();
    string oldValue = provider.CreateProtector(HouseAudioPasswordStore.ProtectorPurpose).Protect("old");
    ConfigApi api = new() { PostStatus = HttpStatusCode.InternalServerError };
    HouseAudioPasswordStore store = FakeHouseController.CreateStore(provider, oldValue, api.Handler);

    bool saved = await store.SetAsync(Plaintext, CancellationToken.None);

    Assert.False(saved);
    Assert.Equal("old", store.Get());
  }

  [Theory]
  [InlineData("")]
  [InlineData("line\nbreak")]
  [InlineData("café")]
  public async Task SetAsync_RefusesAPasswordTheHeaderCannotCarry(string password)
  {
    ConfigApi api = new();
    HouseAudioPasswordStore store =
      FakeHouseController.CreateStore(new EphemeralDataProtectionProvider(), null, api.Handler);

    bool saved = await store.SetAsync(password, CancellationToken.None);

    Assert.False(saved);
    Assert.Equal(0, api.Handler.Count(IsSectionPost));
    Assert.Null(store.Get());
  }

  [Fact]
  public void Get_WithNothingStored_ReturnsNull()
  {
    HouseAudioPasswordStore store =
      FakeHouseController.CreateStore(new EphemeralDataProtectionProvider(), null, new RecordingHandler());

    Assert.Null(store.Get());
  }

  [Fact]
  public void PasswordStore_ReturnsNull_WhenTheRingCannotDecrypt()
  {
    // Protected under another ring, as after the key ring is lost.
    string fromALostRing = new EphemeralDataProtectionProvider()
      .CreateProtector(HouseAudioPasswordStore.ProtectorPurpose)
      .Protect(Plaintext);
    HouseAudioPasswordStore store = FakeHouseController.CreateStore(
      new EphemeralDataProtectionProvider(), fromALostRing, new RecordingHandler());

    Assert.Null(store.Get());
  }

  [Fact]
  public void Get_ReturnsNull_WhenTheStoredValueIsNotAProtectedPayload()
  {
    HouseAudioPasswordStore store = FakeHouseController.CreateStore(
      new EphemeralDataProtectionProvider(), "not a protected payload", new RecordingHandler());

    Assert.Null(store.Get());
  }

  [Fact]
  public void Protector_IsPurposeScoped_SoAnotherPurposeCannotRead()
  {
    IDataProtectionProvider provider = new EphemeralDataProtectionProvider();
    string otherPurpose = provider.CreateProtector("Something.Else").Protect(Plaintext);
    HouseAudioPasswordStore store =
      FakeHouseController.CreateStore(provider, otherPurpose, new RecordingHandler());

    Assert.Null(store.Get());
  }
}
