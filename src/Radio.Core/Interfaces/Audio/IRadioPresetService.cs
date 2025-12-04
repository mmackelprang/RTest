namespace Radio.Core.Interfaces.Audio;

using Radio.Core.Models.Audio;

/// <summary>
/// Service for managing radio station presets.
/// </summary>
public interface IRadioPresetService
{
  /// <summary>
  /// Gets all saved radio presets.
  /// </summary>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>List of all radio presets.</returns>
  Task<IReadOnlyList<RadioPreset>> GetAllPresetsAsync(CancellationToken cancellationToken = default);

  /// <summary>
  /// Gets a specific preset by ID.
  /// </summary>
  /// <param name="id">The preset ID.</param>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>The preset if found, null otherwise.</returns>
  Task<RadioPreset?> GetPresetByIdAsync(string id, CancellationToken cancellationToken = default);

  /// <summary>
  /// Adds a new radio preset.
  /// </summary>
  /// <param name="name">Display name for the preset (optional, will generate default if not provided).</param>
  /// <param name="band">The radio band.</param>
  /// <param name="frequency">The frequency.</param>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>The created preset.</returns>
  /// <exception cref="InvalidOperationException">Thrown when preset limit (50) is reached or preset already exists.</exception>
  Task<RadioPreset> AddPresetAsync(
    string? name,
    RadioBand band,
    double frequency,
    CancellationToken cancellationToken = default);

  /// <summary>
  /// Deletes a radio preset by ID.
  /// </summary>
  /// <param name="id">The preset ID to delete.</param>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>True if deleted, false if not found.</returns>
  Task<bool> DeletePresetAsync(string id, CancellationToken cancellationToken = default);

  /// <summary>
  /// Checks if a preset with the given band and frequency already exists.
  /// </summary>
  /// <param name="band">The radio band.</param>
  /// <param name="frequency">The frequency.</param>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>True if a preset already exists for this band/frequency combination.</returns>
  Task<bool> PresetExistsAsync(RadioBand band, double frequency, CancellationToken cancellationToken = default);

  /// <summary>
  /// Gets the count of saved presets.
  /// </summary>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>Number of saved presets.</returns>
  Task<int> GetPresetCountAsync(CancellationToken cancellationToken = default);

  /// <summary>
  /// Maximum number of presets allowed.
  /// </summary>
  int MaxPresets { get; }
}
