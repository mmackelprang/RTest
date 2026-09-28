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

    var btOptions = Options.Create(new BluetoothOptions());

    _service = new PbapSyncService(
      btService.Object, _repo, optionsMonitor.Object, btOptions,
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

  // --- 2026-09-28: the post-sync reconnect must not touch a live link ------------------------

  private static PbapSyncService BuildWith(Mock<IBluetoothService> bt, SqliteConnection conn)
  {
    var repo = new PbapContactRepository(conn);
    var options = new Mock<IOptionsMonitor<PbapOptions>>();
    options.Setup(m => m.CurrentValue).Returns(new PbapOptions());
    return new PbapSyncService(bt.Object, repo, options.Object, Options.Create(new BluetoothOptions()),
      NullLogger<PbapSyncService>.Instance) { ReconnectSettleDelay = TimeSpan.Zero };
  }

  [Fact]
  public async Task ReconnectAfterSync_WhenPhoneStillConnected_DoesNotReconnect()
  {
    // Measured on the box: Connect() on the still-connected phone renegotiated its profiles and
    // the hci0 card came back on the call profile — album art showed, no music played.
    var bt = new Mock<IBluetoothService>();
    bt.Setup(b => b.ConnectedDevice).Returns(new BluetoothDeviceInfo
    {
      Address = "B0:D5:FB:D2:0D:68", Name = "Pixel", IsPaired = true, IsConnected = true
    });
    var svc = BuildWith(bt, _connection);

    await svc.ReconnectAfterSyncAsync("b0:d5:fb:d2:0d:68");

    bt.Verify(b => b.ConnectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
  }

  [Fact]
  public async Task ReconnectAfterSync_WhenPhoneDropped_Reconnects()
  {
    var bt = new Mock<IBluetoothService>();
    bt.Setup(b => b.ConnectedDevice).Returns((BluetoothDeviceInfo?)null);
    var svc = BuildWith(bt, _connection);

    await svc.ReconnectAfterSyncAsync("B0:D5:FB:D2:0D:68");

    bt.Verify(b => b.ConnectAsync("B0:D5:FB:D2:0D:68", It.IsAny<CancellationToken>()), Times.Once);
  }
}
