using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Bluetooth;

namespace Radio.Infrastructure.Tests.Bluetooth;

public class PbapSyncServiceTests : IDisposable
{
  private readonly SqliteConnection _connection;
  private readonly PbapContactRepository _repo;
  private readonly PbapSyncService _service;

  public PbapSyncServiceTests()
  {
    _connection = new SqliteConnection("Data Source=:memory:");
    _connection.Open();
    _repo = new PbapContactRepository(_connection);
    _repo.InitializeAsync().GetAwaiter().GetResult();

    var btService = new Mock<IBluetoothService>();
    var optionsMonitor = new Mock<IOptionsMonitor<PbapOptions>>();
    optionsMonitor.Setup(m => m.CurrentValue).Returns(new PbapOptions());

    _service = new PbapSyncService(
      btService.Object, _repo, optionsMonitor.Object,
      NullLogger<PbapSyncService>.Instance);
  }

  public void Dispose() => _connection.Dispose();

  [Fact]
  public async Task ProcessDownloadedVcf_ShouldParseAndStoreContacts()
  {
    // Arrange — write a temp VCF file
    var vcf = "BEGIN:VCARD\r\nVERSION:3.0\r\nFN:Test User\r\nTEL:5551234567\r\nEND:VCARD\r\n";
    var tempFile = Path.GetTempFileName();
    await File.WriteAllTextAsync(tempFile, vcf);

    try
    {
      // Act
      var result = await _service.ProcessDownloadedVcfAsync("AA:BB:CC:DD:EE:FF", tempFile);

      // Assert
      Assert.True(result.Success);
      Assert.Equal(1, result.ContactCount);

      var contact = await _repo.FindByPhoneNumberAsync("AA:BB:CC:DD:EE:FF", "5551234567");
      Assert.NotNull(contact);
      Assert.Equal("Test User", contact!.DisplayName);
    }
    finally
    {
      File.Delete(tempFile);
    }
  }
}
