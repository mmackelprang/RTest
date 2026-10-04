using Radio.Core.Models;

namespace Radio.Core.Interfaces.Bluetooth;

public interface IPbapContactRepository
{
  Task UpsertContactsAsync(string deviceAddress, List<PbapContact> contacts, CancellationToken ct = default);
  Task<PbapContact?> FindByPhoneNumberAsync(string deviceAddress, string normalizedNumber, CancellationToken ct = default);

  /// <summary>
  /// PHN-14: finds a caller in EVERY stored phone book, so a name resolves with no phone connected.
  /// An exact digits match on any device beats a last-seven-digits match on any device. Within a tier the
  /// devices are searched <paramref name="preferredDeviceAddress"/> first (the connected phone, when there is
  /// one), then the most recently synced, then the rest by address.
  /// </summary>
  /// <returns>The best match, or null when no stored contact matches.</returns>
  Task<PbapContactMatch?> FindByPhoneNumberAnyDeviceAsync(string normalizedNumber, string? preferredDeviceAddress = null, CancellationToken ct = default);
  Task<List<PbapContact>> GetContactsAsync(string deviceAddress, CancellationToken ct = default);
  Task<List<(string DeviceAddress, int ContactCount, DateTime? LastSynced)>> GetSyncSummaryAsync(string? deviceAddress = null, CancellationToken ct = default);
  Task DeleteContactsAsync(string deviceAddress, CancellationToken ct = default);
}
