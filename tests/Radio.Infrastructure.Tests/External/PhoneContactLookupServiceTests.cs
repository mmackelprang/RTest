using System.Net;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models;
using Radio.Infrastructure.Bluetooth;
using Radio.Infrastructure.External;

namespace Radio.Infrastructure.Tests.External;

/// <summary>
/// PHN-14: the API announcement's caller-name lookup consults BOTH sources — the stored synced phone books on
/// every phone (a real <see cref="PbapContactRepository"/> over in-memory SQLite), then RotaryPhone's contacts
/// list — with the synced phone book winning when both have the number.
/// </summary>
public sealed class PhoneContactLookupServiceTests : IDisposable
{
  private const string Number = "9195550142";
  private readonly SqliteConnection _connection;
  private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
  private readonly PbapContactRepository _repo;
  private readonly List<string> _requests = [];

  public PhoneContactLookupServiceTests()
  {
    _connection = new SqliteConnection("Data Source=:memory:");
    _connection.Open();
    _repo = new PbapContactRepository(_connection, _time);
    _repo.InitializeAsync().GetAwaiter().GetResult();
  }

  public void Dispose() => _connection.Dispose();

  private PhoneContactLookupService Build(string? rotaryContactsJson, string? connectedAddress = null)
  {
    var options = new Mock<IOptionsMonitor<PhoneIntegrationOptions>>();
    options.SetupGet(o => o.CurrentValue).Returns(new PhoneIntegrationOptions
    {
      ContactsApiBaseUrl = "http://rotaryphone.test.invalid/"
    });
    var bluetooth = new Mock<IBluetoothService>();
    bluetooth.SetupGet(b => b.ConnectedDevice).Returns(connectedAddress is null
      ? null
      : new BluetoothDeviceInfo { Address = connectedAddress, Name = "Handset" });

    var client = new HttpClient(new Handler(_requests, rotaryContactsJson));
    return new PhoneContactLookupService(
      NullLogger<PhoneContactLookupService>.Instance, options.Object, client, _repo, bluetooth.Object);
  }

  private static string Rotary(string name, string phone) =>
    $"[{{\"id\":\"1\",\"name\":\"{name}\",\"phoneNumber\":\"{phone}\",\"email\":null}}]";

  [Fact]
  public async Task WithNoPhoneConnected_TheStoredPhoneBookAnswers()
  {
    await _repo.UpsertContactsAsync("BB:BB:BB:BB:BB:02", [new() { DisplayName = "Owner (synced)", PhoneNumbers = [Number] }]);
    var service = Build("[]", connectedAddress: null);

    Assert.Equal("Owner (synced)", await service.FindCallerNameAsync("+1 919-555-0142"));
    Assert.Empty(_requests);   // resolved locally; RotaryPhone not asked
  }

  [Fact]
  public async Task WhenBothSourcesHaveTheNumber_TheSyncedPhoneBookWins()
  {
    await _repo.UpsertContactsAsync("BB:BB:BB:BB:BB:02", [new() { DisplayName = "Synced name", PhoneNumbers = [Number] }]);
    var service = Build(Rotary("RotaryPhone name", Number));

    Assert.Equal("Synced name", await service.FindCallerNameAsync(Number));
  }

  [Theory]
  [InlineData("+19195550142")]
  [InlineData("19195550142")]
  [InlineData("9195550142")]
  public async Task APhoneBookMiss_FallsBackToRotaryPhonesList_ToleratingACountryCode(string incoming)
  {
    var service = Build(Rotary("RotaryPhone name", "+1 (919) 555-0142"));

    Assert.Equal("RotaryPhone name", await service.FindCallerNameAsync(incoming));
    // The list route, the one RotaryPhone actually has — never the /lookup it never had.
    Assert.Equal(["GET http://rotaryphone.test.invalid/api/contacts"], _requests);
  }

  [Theory]
  [InlineData("+19195550142")]
  [InlineData("19195550142")]
  [InlineData("9195550142")]
  public async Task TheStoredPhoneBook_ToleratesACountryCode(string incoming)
  {
    // VCardParser stores numbers normalized; "+1 919…" in a vCard is stored as 9195550142.
    await _repo.UpsertContactsAsync("BB:BB:BB:BB:BB:02", [new() { DisplayName = "Owner", PhoneNumbers = [Number] }]);
    var service = Build("[]");

    Assert.Equal("Owner", await service.FindCallerNameAsync(incoming));
  }

  [Fact]
  public async Task TheConnectedPhone_IsSearchedFirst()
  {
    await _repo.UpsertContactsAsync("AA:AA:AA:AA:AA:01", [new() { DisplayName = "Connected phone's name", PhoneNumbers = [Number] }]);
    _time.Advance(TimeSpan.FromDays(30));
    await _repo.UpsertContactsAsync("CC:CC:CC:CC:CC:03", [new() { DisplayName = "Newer sync's name", PhoneNumbers = [Number] }]);

    Assert.Equal("Connected phone's name",
      await Build("[]", connectedAddress: "AA:AA:AA:AA:AA:01").FindCallerNameAsync(Number));
    Assert.Equal("Newer sync's name", await Build("[]", connectedAddress: null).FindCallerNameAsync(Number));
  }

  // ── Owner ruling 2026-10-03: match quality first, then source ──

  [Fact]
  public async Task ARotaryPhoneExactMatch_BeatsASyncedLocalEntry()
  {
    await _repo.UpsertContactsAsync("BB:BB:BB:BB:BB:02", [new() { DisplayName = "Synced local entry", PhoneNumbers = ["5550142"] }]);
    var service = Build(Rotary("RotaryPhone exact", "+1 919 555 0142"));

    Assert.Equal("RotaryPhone exact", await service.FindCallerNameAsync(Number));
  }

  [Fact]
  public async Task ASyncedLocalEntry_BeatsARotaryPhoneLocalEntry()
  {
    await _repo.UpsertContactsAsync("BB:BB:BB:BB:BB:02", [new() { DisplayName = "Synced local entry", PhoneNumbers = ["5550142"] }]);
    var service = Build(Rotary("RotaryPhone local entry", "555-0142"));

    Assert.Equal("Synced local entry", await service.FindCallerNameAsync(Number));
  }

  [Fact]
  public async Task ASyncedLocalEntry_StandsWhenRotaryPhoneHasNothing()
  {
    await _repo.UpsertContactsAsync("BB:BB:BB:BB:BB:02", [new() { DisplayName = "Synced local entry", PhoneNumbers = ["5550142"] }]);
    var service = Build(Rotary("Someone else", "5550001111"));

    Assert.Equal("Synced local entry", await service.FindCallerNameAsync(Number));
  }

  [Fact]
  public async Task ASyncedLocalEntry_StandsWhenRotaryPhoneIsUnreachable()
  {
    await _repo.UpsertContactsAsync("BB:BB:BB:BB:BB:02", [new() { DisplayName = "Synced local entry", PhoneNumbers = ["5550142"] }]);
    var service = Build(rotaryContactsJson: null);

    Assert.Equal("Synced local entry", await service.FindCallerNameAsync(Number));
  }

  [Fact]
  public async Task AStrangerWhoSharesTheLastSeven_IsNotNamedFromEitherSource()
  {
    await _repo.UpsertContactsAsync("BB:BB:BB:BB:BB:02", [new() { DisplayName = "Synced owner", PhoneNumbers = [Number] }]);
    var service = Build(Rotary("RotaryPhone owner", "+1 919 555 0142"));

    Assert.Equal("5555550142", await service.FindCallerNameAsync("5555550142"));
  }

  [Fact]
  public async Task NeitherSource_ReturnsTheRawNumber()
  {
    var service = Build(Rotary("Someone else", "5550001111"));

    Assert.Equal(Number, await service.FindCallerNameAsync(Number));
  }

  [Fact]
  public async Task ARotaryPhoneContactWithNoName_IsSkipped()
  {
    var service = Build($"[{{\"id\":\"1\",\"name\":\"\",\"phoneNumber\":\"{Number}\"}},{{\"id\":\"2\",\"name\":\"Named\",\"phoneNumber\":\"+1{Number}\"}}]");

    Assert.Equal("Named", await service.FindCallerNameAsync(Number));
  }

  // A null contactsJson plays RotaryPhone unreachable (503 on the list).
  private sealed class Handler(List<string> requests, string? contactsJson) : HttpMessageHandler
  {
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
      requests.Add($"{request.Method} {request.RequestUri}");
      if (contactsJson is null)
      {
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
      }
      return Task.FromResult(request.RequestUri!.AbsolutePath == "/api/contacts"
        ? new HttpResponseMessage(HttpStatusCode.OK)
        {
          Content = new StringContent(contactsJson, Encoding.UTF8, "application/json")
        }
        // Anything else is RotaryPhone's 404 — what the old /api/contacts/lookup always got.
        : new HttpResponseMessage(HttpStatusCode.NotFound));
    }
  }
}
