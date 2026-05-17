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
  /// One-based ordinal slot within the band, ordered by <see cref="CreatedAt"/>
  /// ascending (so slot 1 is the oldest preset, slot 2 the second-oldest, etc.).
  /// Promoted from an implicit ordinal to a real field in PR 3 of the Radio
  /// Controller Polish arc so the UI can render a memory-slot column. Default
  /// 0 means "not assigned" (e.g. when constructed from an isolated
  /// <see cref="FromModel(RadioPreset)"/> call without a band context); the
  /// controller's <c>GetPresets</c> projection fills this in.
  /// </summary>
  public int SlotNumber { get; init; }

  /// <summary>
  /// Maps from domain model to DTO. Does not assign <see cref="SlotNumber"/>;
  /// callers that need a populated slot number must use
  /// <see cref="FromModel(RadioPreset, int)"/> instead so per-band slot
  /// numbering is computed against the entire band's preset set.
  /// </summary>
  public static RadioPresetDto FromModel(RadioPreset preset)
  {
    return new RadioPresetDto
    {
      Id = preset.Id,
      Name = preset.Name,
      Band = preset.Band.ToString(),
      Frequency = preset.Frequency,
      CreatedAt = preset.CreatedAt,
      SlotNumber = 0,
    };
  }

  /// <summary>
  /// Maps from domain model to DTO with an explicit slot number. Used by the
  /// controller projection after computing per-band ordinals.
  /// </summary>
  public static RadioPresetDto FromModel(RadioPreset preset, int slotNumber)
  {
    return new RadioPresetDto
    {
      Id = preset.Id,
      Name = preset.Name,
      Band = preset.Band.ToString(),
      Frequency = preset.Frequency,
      CreatedAt = preset.CreatedAt,
      SlotNumber = slotNumber,
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
