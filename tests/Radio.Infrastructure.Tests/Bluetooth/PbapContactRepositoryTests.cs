using Microsoft.Data.Sqlite;
using Radio.Core.Models;
using Radio.Infrastructure.Bluetooth;

namespace Radio.Infrastructure.Tests.Bluetooth;

public class PbapContactRepositoryTests : IDisposable
{
  private readonly SqliteConnection _connection;
  private readonly PbapContactRepository _repo;

  public PbapContactRepositoryTests()
  {
    _connection = new SqliteConnection("Data Source=:memory:");
    _connection.Open();
    _repo = new PbapContactRepository(_connection);
    _repo.InitializeAsync().GetAwaiter().GetResult();
  }

  public void Dispose() => _connection.Dispose();

  [Fact]
  public async Task UpsertAndFind_ExactMatch_ShouldReturnContact()
  {
    var contacts = new List<PbapContact>
    {
      new() { DisplayName = "John Smith", PhoneNumbers = new() { "5551234567" } }
    };

    await _repo.UpsertContactsAsync("AA:BB:CC:DD:EE:FF", contacts);
    var result = await _repo.FindByPhoneNumberAsync("AA:BB:CC:DD:EE:FF", "5551234567");

    Assert.NotNull(result);
    Assert.Equal("John Smith", result!.DisplayName);
  }

  [Fact]
  public async Task FindByPhoneNumber_AStrangerWhoSharesTheLastSeven_DoesNotMatchAFullStoredNumber()
  {
    // Owner ruling 2026-10-03 (PHN-14). Until then this test asserted the opposite: a different area code with
    // the same last seven digits matched.
    await _repo.UpsertContactsAsync("AA:BB:CC:DD:EE:FF", [new() { DisplayName = "Jane", PhoneNumbers = ["5551234567"] }]);

    Assert.Null(await _repo.FindByPhoneNumberAsync("AA:BB:CC:DD:EE:FF", "9991234567"));
  }

  [Fact]
  public async Task FindByPhoneNumber_ASevenDigitLocalEntry_MatchesOnTheLastSeven()
  {
    await _repo.UpsertContactsAsync("AA:BB:CC:DD:EE:FF", [new() { DisplayName = "Jane", PhoneNumbers = ["1234567"] }]);

    Assert.Equal("Jane", (await _repo.FindByPhoneNumberAsync("AA:BB:CC:DD:EE:FF", "9991234567"))!.DisplayName);
  }

  [Fact]
  public async Task FindByPhoneNumber_WrongDevice_ShouldNotMatch()
  {
    var contacts = new List<PbapContact>
    {
      new() { DisplayName = "John", PhoneNumbers = new() { "5551234567" } }
    };

    await _repo.UpsertContactsAsync("AA:BB:CC:DD:EE:FF", contacts);
    var result = await _repo.FindByPhoneNumberAsync("11:22:33:44:55:66", "5551234567");

    Assert.Null(result);
  }

  [Fact]
  public async Task Upsert_ShouldReplaceExistingContacts()
  {
    var v1 = new List<PbapContact>
    {
      new() { DisplayName = "Old Name", PhoneNumbers = new() { "5551234567" } }
    };
    var v2 = new List<PbapContact>
    {
      new() { DisplayName = "New Name", PhoneNumbers = new() { "5551234567" } }
    };

    await _repo.UpsertContactsAsync("AA:BB:CC:DD:EE:FF", v1);
    await _repo.UpsertContactsAsync("AA:BB:CC:DD:EE:FF", v2);

    var result = await _repo.FindByPhoneNumberAsync("AA:BB:CC:DD:EE:FF", "5551234567");
    Assert.Equal("New Name", result!.DisplayName);
  }

  [Fact]
  public async Task GetContacts_ShouldReturnAllForDevice()
  {
    var contacts = new List<PbapContact>
    {
      new() { DisplayName = "Alice", PhoneNumbers = new() { "1111111" } },
      new() { DisplayName = "Bob", PhoneNumbers = new() { "2222222", "3333333" } }
    };

    await _repo.UpsertContactsAsync("AA:BB:CC:DD:EE:FF", contacts);
    var result = await _repo.GetContactsAsync("AA:BB:CC:DD:EE:FF");

    Assert.Equal(2, result.Count); // 2 contacts (Bob's numbers grouped)
  }

  [Fact]
  public async Task DeleteContacts_ShouldRemoveAllForDevice()
  {
    var contacts = new List<PbapContact>
    {
      new() { DisplayName = "Alice", PhoneNumbers = new() { "1111111" } }
    };

    await _repo.UpsertContactsAsync("AA:BB:CC:DD:EE:FF", contacts);
    await _repo.DeleteContactsAsync("AA:BB:CC:DD:EE:FF");

    var result = await _repo.GetContactsAsync("AA:BB:CC:DD:EE:FF");
    Assert.Empty(result);
  }

  [Fact]
  public async Task GetSyncSummary_ShouldReturnCountAndTimestamp()
  {
    var contacts = new List<PbapContact>
    {
      new() { DisplayName = "Alice", PhoneNumbers = new() { "1111111", "2222222" } }
    };

    await _repo.UpsertContactsAsync("AA:BB:CC:DD:EE:FF", contacts);
    var summary = await _repo.GetSyncSummaryAsync("AA:BB:CC:DD:EE:FF");

    Assert.Single(summary);
    Assert.Equal("AA:BB:CC:DD:EE:FF", summary[0].DeviceAddress);
    Assert.Equal(2, summary[0].ContactCount); // 2 rows (2 phone numbers)
    Assert.NotNull(summary[0].LastSynced);
  }

  [Fact]
  public async Task GetSyncSummary_EmptyTable_ShouldReturnEmpty()
  {
    var summary = await _repo.GetSyncSummaryAsync();
    Assert.Empty(summary);
  }

  // ── PHN-14: every stored phone book, connected or not ─────────────

  // ⚠ Address order is the REVERSE of sync order (the oldest phone sorts first, and is inserted first), so
  // neither the address tie-break nor row order can pass a test that recency is supposed to decide.
  private const string Pixel = "CC:CC:CC:CC:CC:03";
  private const string OldPhone = "BB:BB:BB:BB:BB:02";
  private const string OlderPhone = "AA:AA:AA:AA:AA:01";

  /// <summary>
  /// A repository whose sync stamps come from a fake clock, and three phone books synced in a known order:
  /// <see cref="OlderPhone"/> first, then <see cref="OldPhone"/>, then <see cref="Pixel"/> (the newest).
  /// </summary>
  private async Task<PbapContactRepository> ThreePhonesAsync(
    string pixelName = "Pixel's name", string oldName = "Old phone's name", string olderName = "Older phone's name",
    string number = "9193718044")
  {
    var time = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
    var repo = new PbapContactRepository(_connection, time);
    await repo.UpsertContactsAsync(OlderPhone, [new() { DisplayName = olderName, PhoneNumbers = [number] }]);
    time.Advance(TimeSpan.FromDays(30));
    await repo.UpsertContactsAsync(OldPhone, [new() { DisplayName = oldName, PhoneNumbers = [number] }]);
    time.Advance(TimeSpan.FromDays(30));
    await repo.UpsertContactsAsync(Pixel, [new() { DisplayName = pixelName, PhoneNumbers = [number] }]);
    return repo;
  }

  [Fact]
  public async Task AnyDevice_WithNoPhoneConnected_FindsTheContactInAStoredPhoneBook()
  {
    // The coordinator's measurement: no phone connected, the number in the stored contacts. Was a 404.
    await _repo.UpsertContactsAsync(OldPhone, [new() { DisplayName = "Owner", PhoneNumbers = ["9193718044"] }]);

    var match = await _repo.FindByPhoneNumberAnyDeviceAsync("9193718044", preferredDeviceAddress: null);

    Assert.NotNull(match);
    Assert.Equal("Owner", match!.DisplayName);
    Assert.Equal(OldPhone, match.DeviceAddress);
    Assert.True(match.IsExactMatch);
  }

  [Fact]
  public async Task AnyDevice_WithNoPhoneConnected_TheMostRecentlySyncedPhoneWins()
  {
    var repo = await ThreePhonesAsync();

    var match = await repo.FindByPhoneNumberAnyDeviceAsync("9193718044", preferredDeviceAddress: null);

    Assert.Equal("Pixel's name", match!.DisplayName);
  }

  [Fact]
  public async Task AnyDevice_TheConnectedPhoneWins_OverAMoreRecentSync()
  {
    var repo = await ThreePhonesAsync();

    var match = await repo.FindByPhoneNumberAnyDeviceAsync("9193718044", preferredDeviceAddress: OlderPhone);

    Assert.Equal("Older phone's name", match!.DisplayName);
  }

  [Fact]
  public async Task AnyDevice_TheConnectedPhoneMatchesCaseInsensitively()
  {
    var repo = await ThreePhonesAsync();

    var match = await repo.FindByPhoneNumberAnyDeviceAsync("9193718044", preferredDeviceAddress: OldPhone.ToLowerInvariant());

    Assert.Equal("Old phone's name", match!.DisplayName);
  }

  [Fact]
  public async Task AnyDevice_AConnectedPhoneWithoutTheNumber_FallsBackToTheOthers()
  {
    var repo = await ThreePhonesAsync();
    await repo.UpsertContactsAsync("DD:DD:DD:DD:DD:04", [new() { DisplayName = "Someone else", PhoneNumbers = ["5550001111"] }]);

    var match = await repo.FindByPhoneNumberAnyDeviceAsync("9193718044", preferredDeviceAddress: "DD:DD:DD:DD:DD:04");

    Assert.Equal("Pixel's name", match!.DisplayName);
  }

  [Fact]
  public async Task AnyDevice_AnExactMatchOnAnOlderPhone_BeatsALast7MatchOnTheConnectedPhone()
  {
    var time = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
    var repo = new PbapContactRepository(_connection, time);
    await repo.UpsertContactsAsync(OlderPhone, [new() { DisplayName = "Exact, old phone", PhoneNumbers = ["9193718044"] }]);
    time.Advance(TimeSpan.FromDays(30));
    await repo.UpsertContactsAsync(Pixel, [new() { DisplayName = "Last seven, connected", PhoneNumbers = ["3718044"] }]);

    var match = await repo.FindByPhoneNumberAnyDeviceAsync("9193718044", preferredDeviceAddress: Pixel);

    Assert.Equal("Exact, old phone", match!.DisplayName);
    Assert.True(match.IsExactMatch);
  }

  [Fact]
  public async Task AnyDevice_FallsBackToTheLastSevenDigits()
  {
    await _repo.UpsertContactsAsync(OldPhone, [new() { DisplayName = "Local", PhoneNumbers = ["3718044"] }]);

    var match = await _repo.FindByPhoneNumberAnyDeviceAsync("9193718044");

    Assert.Equal("Local", match!.DisplayName);
    Assert.False(match.IsExactMatch);
  }

  [Fact]
  public async Task AnyDevice_AStrangerWhoSharesTheLastSeven_MatchesNoFullStoredNumberOnAnyPhone()
  {
    await ThreePhonesAsync();   // every phone stores 9193718044

    Assert.Null(await _repo.FindByPhoneNumberAnyDeviceAsync("5553718044", preferredDeviceAddress: Pixel));
  }

  [Theory]
  [InlineData("")]
  [InlineData("5550001111")]
  public async Task AnyDevice_NoMatchOrNoNumber_ReturnsNull(string number)
  {
    await ThreePhonesAsync();

    Assert.Null(await _repo.FindByPhoneNumberAnyDeviceAsync(number));
  }
}
