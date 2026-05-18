using Microsoft.Extensions.Logging;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;

namespace Radio.Infrastructure.Audio.Services;

/// <summary>
/// Service for managing radio station presets.
/// </summary>
public sealed class RadioPresetService : IRadioPresetService
{
  private readonly ILogger<RadioPresetService> _logger;
  private readonly IRadioPresetRepository _repository;

  /// <summary>
  /// Maximum number of presets allowed.
  /// </summary>
  public int MaxPresets => 50;

  /// <summary>
  /// Initializes a new instance of the <see cref="RadioPresetService"/> class.
  /// </summary>
  /// <param name="logger">The logger instance.</param>
  /// <param name="repository">The preset repository.</param>
  public RadioPresetService(
    ILogger<RadioPresetService> logger,
    IRadioPresetRepository repository)
  {
    _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    _repository = repository ?? throw new ArgumentNullException(nameof(repository));
  }

  /// <inheritdoc/>
  public async Task<IReadOnlyList<RadioPreset>> GetAllPresetsAsync(CancellationToken cancellationToken = default)
  {
    return await _repository.GetAllAsync(cancellationToken);
  }

  /// <inheritdoc/>
  public async Task<RadioPreset?> GetPresetByIdAsync(string id, CancellationToken cancellationToken = default)
  {
    return await _repository.GetByIdAsync(id, cancellationToken);
  }

  /// <inheritdoc/>
  public async Task<RadioPreset> AddPresetAsync(
    string? name,
    RadioBand band,
    double frequency,
    CancellationToken cancellationToken = default)
  {
    // Check if preset limit reached
    var count = await _repository.GetCountAsync(cancellationToken);
    if (count >= MaxPresets)
    {
      _logger.LogWarning("Cannot add preset: maximum of {MaxPresets} presets reached", MaxPresets);
      throw new InvalidOperationException($"Maximum of {MaxPresets} presets reached. Please delete an existing preset first.");
    }

    // Check if preset already exists (collision detection)
    var existing = await _repository.GetByBandAndFrequencyAsync(band, frequency, cancellationToken);
    if (existing != null)
    {
      _logger.LogWarning("Preset already exists for {Band} - {Frequency}: {Name}", band, frequency, existing.Name);
      throw new InvalidOperationException($"A preset already exists for {band} - {frequency}: {existing.Name}");
    }

    // Generate name if not provided
    var presetName = string.IsNullOrWhiteSpace(name)
      ? RadioPreset.GetDefaultName(band, frequency)
      : name.Trim();

    // Create preset
    var preset = new RadioPreset
    {
      Id = Guid.NewGuid().ToString("N"),
      Name = presetName,
      Band = band,
      Frequency = frequency,
      CreatedAt = DateTimeOffset.UtcNow,
      LastModifiedAt = DateTimeOffset.UtcNow
    };

    await _repository.AddAsync(preset, cancellationToken);

    _logger.LogInformation("Added radio preset {Id}: {Name} ({Band} - {Frequency})",
      preset.Id, preset.Name, preset.Band, preset.Frequency);

    return preset;
  }

  /// <inheritdoc/>
  public async Task<bool> DeletePresetAsync(string id, CancellationToken cancellationToken = default)
  {
    var deleted = await _repository.DeleteAsync(id, cancellationToken);

    if (deleted)
    {
      _logger.LogInformation("Deleted radio preset {Id}", id);
    }
    else
    {
      _logger.LogWarning("Radio preset {Id} not found for deletion", id);
    }

    return deleted;
  }

  /// <inheritdoc/>
  public async Task<RadioPreset?> RenamePresetAsync(string id, string newName, CancellationToken cancellationToken = default)
  {
    // Added by the LED-font + preset-affordances hot-fix off PR #371. Empty
    // / whitespace names are rejected up-front — the API controller does the
    // same check but defending here keeps callers (tests, future internal
    // callers) from sneaking through a blank rename via a direct service
    // call.
    if (string.IsNullOrWhiteSpace(newName))
    {
      throw new ArgumentException("Preset name must not be empty or whitespace.", nameof(newName));
    }

    var trimmed = newName.Trim();
    var updated = await _repository.UpdateNameAsync(id, trimmed, cancellationToken);
    if (!updated)
    {
      _logger.LogWarning("Radio preset {Id} not found for rename", id);
      return null;
    }

    var renamed = await _repository.GetByIdAsync(id, cancellationToken);
    _logger.LogInformation("Renamed radio preset {Id} → {Name}", id, trimmed);
    return renamed;
  }

  /// <inheritdoc/>
  public async Task<bool> PresetExistsAsync(RadioBand band, double frequency, CancellationToken cancellationToken = default)
  {
    var preset = await _repository.GetByBandAndFrequencyAsync(band, frequency, cancellationToken);
    return preset != null;
  }

  /// <inheritdoc/>
  public async Task<int> GetPresetCountAsync(CancellationToken cancellationToken = default)
  {
    return await _repository.GetCountAsync(cancellationToken);
  }
}
