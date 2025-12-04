using Radio.Core.Models.Audio;

namespace Radio.API.Models;

/// <summary>
/// DTO for a radio preset.
/// </summary>
public sealed record RadioPresetDto
{
  /// <summary>
  /// Unique identifier for the preset.
  /// </summary>
  public required string Id { get; init; }

  /// <summary>
  /// Display name for the preset.
  /// </summary>
  public required string Name { get; init; }

  /// <summary>
  /// The radio band (AM, FM, WB, VHF, SW).
  /// </summary>
  public required string Band { get; init; }

  /// <summary>
  /// The frequency of the station.
  /// </summary>
  public required double Frequency { get; init; }

  /// <summary>
  /// When this preset was created.
  /// </summary>
  public required DateTimeOffset CreatedAt { get; init; }

  /// <summary>
  /// Maps from domain model to DTO.
  /// </summary>
  public static RadioPresetDto FromModel(RadioPreset preset)
  {
    return new RadioPresetDto
    {
      Id = preset.Id,
      Name = preset.Name,
      Band = preset.Band.ToString(),
      Frequency = preset.Frequency,
      CreatedAt = preset.CreatedAt
    };
  }
}

/// <summary>
/// Request DTO for creating a new radio preset.
/// </summary>
public sealed record CreateRadioPresetRequest
{
  /// <summary>
  /// Display name for the preset (optional, will generate default if not provided).
  /// </summary>
  public string? Name { get; init; }

  /// <summary>
  /// The radio band (AM, FM, WB, VHF, SW).
  /// </summary>
  public required string Band { get; init; }

  /// <summary>
  /// The frequency of the station.
  /// </summary>
  public required double Frequency { get; init; }
}
