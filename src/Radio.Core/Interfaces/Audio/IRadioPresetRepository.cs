namespace Radio.Core.Interfaces.Audio;

using Radio.Core.Models.Audio;

/// <summary>
/// Repository for radio preset persistence.
/// </summary>
public interface IRadioPresetRepository
{
  /// <summary>
  /// Gets all saved radio presets.
  /// </summary>
  /// <param name="ct">Cancellation token.</param>
  /// <returns>List of all radio presets.</returns>
  Task<IReadOnlyList<RadioPreset>> GetAllAsync(CancellationToken ct = default);

  /// <summary>
  /// Gets a specific preset by ID.
  /// </summary>
  /// <param name="id">The preset ID.</param>
  /// <param name="ct">Cancellation token.</param>
  /// <returns>The preset if found, null otherwise.</returns>
  Task<RadioPreset?> GetByIdAsync(string id, CancellationToken ct = default);

  /// <summary>
  /// Checks if a preset with the given band and frequency already exists.
  /// </summary>
  /// <param name="band">The radio band.</param>
  /// <param name="frequency">The frequency.</param>
  /// <param name="ct">Cancellation token.</param>
  /// <returns>The existing preset if found, null otherwise.</returns>
  Task<RadioPreset?> GetByBandAndFrequencyAsync(RadioBand band, double frequency, CancellationToken ct = default);

  /// <summary>
  /// Adds a new radio preset.
  /// </summary>
  /// <param name="preset">The preset to add.</param>
  /// <param name="ct">Cancellation token.</param>
  Task AddAsync(RadioPreset preset, CancellationToken ct = default);

  /// <summary>
  /// Deletes a radio preset by ID.
  /// </summary>
  /// <param name="id">The preset ID to delete.</param>
  /// <param name="ct">Cancellation token.</param>
  /// <returns>True if deleted, false if not found.</returns>
  Task<bool> DeleteAsync(string id, CancellationToken ct = default);

  /// <summary>
  /// Updates the display name on an existing preset, refreshing
  /// <see cref="RadioPreset.LastModifiedAt"/>. Band and frequency are
  /// intentionally immutable here — the kebab-menu rename action off the
  /// PR #371 hot-fix only renames; reassigning a band/frequency would be a
  /// new preset.
  /// </summary>
  /// <param name="id">The preset ID to rename.</param>
  /// <param name="newName">The new display name.</param>
  /// <param name="ct">Cancellation token.</param>
  /// <returns>True if the row was updated, false if the ID was not found.</returns>
  Task<bool> UpdateNameAsync(string id, string newName, CancellationToken ct = default);

  /// <summary>
  /// Gets the count of saved presets.
  /// </summary>
  /// <param name="ct">Cancellation token.</param>
  /// <returns>Number of saved presets.</returns>
  Task<int> GetCountAsync(CancellationToken ct = default);
}
