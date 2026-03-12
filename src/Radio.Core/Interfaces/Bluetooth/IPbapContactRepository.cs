using Radio.Core.Models;

namespace Radio.Core.Interfaces.Bluetooth;

public interface IPbapContactRepository
{
  Task UpsertContactsAsync(string deviceAddress, List<PbapContact> contacts, CancellationToken ct = default);
  Task<PbapContact?> FindByPhoneNumberAsync(string deviceAddress, string normalizedNumber, CancellationToken ct = default);
  Task<List<PbapContact>> GetContactsAsync(string deviceAddress, CancellationToken ct = default);
  Task<List<(string DeviceAddress, int ContactCount, DateTime? LastSynced)>> GetSyncSummaryAsync(string? deviceAddress = null, CancellationToken ct = default);
  Task DeleteContactsAsync(string deviceAddress, CancellationToken ct = default);
}
