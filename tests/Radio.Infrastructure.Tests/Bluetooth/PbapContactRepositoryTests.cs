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
  public async Task FindByPhoneNumber_Last7Fallback_ShouldMatch()
  {
    var contacts = new List<PbapContact>
    {
      new() { DisplayName = "Jane", PhoneNumbers = new() { "5551234567" } }
    };

    await _repo.UpsertContactsAsync("AA:BB:CC:DD:EE:FF", contacts);
    // Query with different area code prefix, same last 7
    var result = await _repo.FindByPhoneNumberAsync("AA:BB:CC:DD:EE:FF", "9991234567");

    Assert.NotNull(result);
    Assert.Equal("Jane", result!.DisplayName);
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
}
