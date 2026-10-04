using Microsoft.AspNetCore.Mvc;
using Moq;
using Radio.API.Controllers;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Interfaces.Bluetooth;
using Radio.Core.Models;
using Radio.Infrastructure.Bluetooth;

namespace Radio.API.Tests.Controllers;

/// <summary>
/// PHN-14: <c>GET /api/bluetooth/pbap/lookup</c> answers from the stored phone books with no phone connected.
/// Measured on the box 2026-10-03 before the fix: <c>404 "No device currently connected"</c> for every format of
/// a number that was in the stored contacts. Driven over the REAL repository (a temp SQLite file, the production
/// constructor), so the controller and the query are tested together.
/// </summary>
public sealed class PbapControllerLookupTests : IDisposable
{
  private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"pbap-lookup-{Guid.NewGuid():N}.db");
  private readonly PbapContactRepository _repo;
  private readonly Mock<IBluetoothService> _bluetooth = new();

  public PbapControllerLookupTests()
  {
    _repo = new PbapContactRepository($"Data Source={_dbPath};Pooling=False");
    _repo.InitializeAsync().GetAwaiter().GetResult();
  }

  public void Dispose()
  {
    try { File.Delete(_dbPath); } catch (IOException) { /* best effort */ }
  }

  private static string? DisplayNameOf(OkObjectResult ok) =>
    ok.Value!.GetType().GetProperty("DisplayName")!.GetValue(ok.Value) as string;

  private PbapController Controller() =>
    new(new Mock<IPbapSyncService>().Object, _repo, _bluetooth.Object);

  [Theory]
  [InlineData("+19193718044")]
  [InlineData("19193718044")]
  [InlineData("9193718044")]
  [InlineData("(919) 371-8044")]
  public async Task WithNoPhoneConnected_EveryFormatOfAStoredNumberResolves(string phoneNumber)
  {
    await _repo.UpsertContactsAsync("78:20:51:F5:FB:A7",
      [new PbapContact { DisplayName = "Owner", PhoneNumbers = ["9193718044"] }]);
    _bluetooth.SetupGet(b => b.ConnectedDevice).Returns((BluetoothDeviceInfo?)null);

    var result = await Controller().LookupNumber(phoneNumber, CancellationToken.None);

    var ok = Assert.IsType<OkObjectResult>(result);
    Assert.Equal("Owner", DisplayNameOf(ok));
  }

  [Fact]
  public async Task AConnectedPhone_IsPreferredOverAnotherStoredPhoneBook()
  {
    // The connected phone is synced first AND sorts last by address, so recency (if the two stamps differ)
    // and the address tie-break (if they are equal) both pick the other phone: only connected-first picks it.
    await _repo.UpsertContactsAsync("CC:CC:CC:CC:CC:03",
      [new PbapContact { DisplayName = "Connected phone's name", PhoneNumbers = ["9193718044"] }]);
    await _repo.UpsertContactsAsync("AA:AA:AA:AA:AA:01",
      [new PbapContact { DisplayName = "Other phone's name", PhoneNumbers = ["9193718044"] }]);
    _bluetooth.SetupGet(b => b.ConnectedDevice)
      .Returns(new BluetoothDeviceInfo { Address = "CC:CC:CC:CC:CC:03", Name = "Handset" });

    var ok = Assert.IsType<OkObjectResult>(await Controller().LookupNumber("9193718044", CancellationToken.None));

    Assert.Equal("Connected phone's name", DisplayNameOf(ok));
  }

  [Theory]
  [InlineData("5550001111")]
  [InlineData("")]
  public async Task NoStoredContact_Is404_WithoutEchoingTheNumber(string phoneNumber)
  {
    await _repo.UpsertContactsAsync("78:20:51:F5:FB:A7",
      [new PbapContact { DisplayName = "Owner", PhoneNumbers = ["9193718044"] }]);

    var result = await Controller().LookupNumber(phoneNumber, CancellationToken.None);

    var notFound = Assert.IsType<NotFoundObjectResult>(result);
    Assert.Equal("No contact found", notFound.Value);
  }
}
