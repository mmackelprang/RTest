namespace Radio.Core.Models;

/// <summary>
/// PHN-14: one stored PBAP contact that matched a caller's number, and which synced phone it came from.
/// </summary>
/// <param name="DeviceAddress">The phone whose synced phone book holds the contact.</param>
/// <param name="DisplayName">The contact's name.</param>
/// <param name="PhoneNumber">The stored (normalized) number that matched.</param>
/// <param name="IsExactMatch">True for an exact digits match; false for a stored 7-digit local entry matching the caller's last seven.</param>
public sealed record PbapContactMatch(string DeviceAddress, string DisplayName, string PhoneNumber, bool IsExactMatch);
