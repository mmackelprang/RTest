namespace Radio.Web.Models;

/// <summary>
/// LOG-5: one runtime log-level switch on radio-api, as returned by
/// <c>GET /api/system/logging/levels</c>. Levels are Serilog level names.
/// </summary>
public sealed record LogLevelStateDto(string Source, string Level, string ConfiguredLevel);
