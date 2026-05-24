namespace Radio.Infrastructure.Weather;

/// <summary>
/// Maps an NWS icon URL (e.g. <c>https://api.weather.gov/icons/land/day/sct?size=medium</c>)
/// to a stable <c>IconKey</c> string (e.g. <c>"mostly-sunny"</c>) the Web layer
/// resolves to a Material Symbol. Keeps NWS-specific knowledge out of the Web
/// layer and out of the Core data contract.
///
/// The full 18-entry mapping is pinned by the Designer handoff §4. Any unmapped
/// URL falls back to <c>"unknown"</c> — the Web layer must render an explicit
/// fallback icon (currently <c>cloud_off</c>) for that case.
///
/// NWS icon URLs follow the documented shape:
///   <c>https://api.weather.gov/icons/{set}/{daypart}/{condition}[/{coverage}][?size=...]</c>
/// where <c>daypart</c> is <c>day</c>|<c>night</c> and <c>condition</c> is one
/// of the controlled-vocabulary tokens listed at
/// https://www.weather.gov/forecast-icons.
/// </summary>
public static class NwsIconMapper
{
  /// <summary>
  /// Maps the NWS icon URL to one of the IconKey strings the Web layer knows
  /// how to render. Returns <c>"unknown"</c> when the URL doesn't match a
  /// known condition prefix; callers SHOULD NOT throw on unknown — the UI
  /// gracefully renders the fallback icon.
  /// </summary>
  /// <param name="iconUrl">
  /// The raw <c>icon</c> field from an NWS forecast period. May be null or
  /// empty when NWS didn't provide one for this period.
  /// </param>
  public static string MapToIconKey(string? iconUrl)
  {
    if (string.IsNullOrWhiteSpace(iconUrl))
    {
      return "unknown";
    }

    // Decompose the URL: pluck the path so we can inspect the daypart and the
    // condition token, ignoring any query string (size=) and host.
    var path = TryGetPath(iconUrl);
    if (path is null)
    {
      return "unknown";
    }

    // Path shape examples:
    //   /icons/land/day/sct?size=medium     → daypart=day, condition=sct
    //   /icons/land/night/skc               → daypart=night, condition=skc
    //   /icons/land/day/tsra,40             → daypart=day, condition=tsra (coverage suffix dropped)
    //   /icons/land/day/rain_showers/rain   → daypart=day, condition=rain_showers (split-icon takes the first half)
    //
    // We isolate the segment AFTER the daypart and strip any "/second"
    // split-icon suffix and any ",coverage" qualifier.
    var (daypart, condition) = ExtractDaypartAndCondition(path);
    if (string.IsNullOrEmpty(condition))
    {
      return "unknown";
    }

    var isNight = string.Equals(daypart, "night", StringComparison.OrdinalIgnoreCase);

    // Mapping table — kept inline rather than in a dictionary because (a) we
    // need ordering (specific conditions before general — e.g. rain_light
    // before rain), (b) night/day disambiguation for sky cover is easiest
    // inline, and (c) it doubles as documentation of the mapping.

    // ---- Sky cover (NWS uses skc/few/sct/bkn/ovc; day/night matters) ----
    if (condition is "skc" or "few")
    {
      return isNight ? "clear-night" : "sunny";
    }
    if (condition == "sct")
    {
      return isNight ? "partly-cloudy-night" : "mostly-sunny";
    }
    if (condition == "bkn")
    {
      return isNight ? "partly-cloudy-night" : "partly-cloudy";
    }
    if (condition == "ovc")
    {
      // "Overcast" is the same icon day and night — fully covered sky has no
      // sun/moon to differentiate.
      return "cloudy";
    }

    // ---- Precipitation (rain — most specific first) ----
    if (condition is "rain_heavy" or "tsra_hi")
    {
      return "rain-heavy";
    }
    if (condition is "rain_light" or "drizzle" or "rain_showers_hi" || condition.StartsWith("rain_showers", StringComparison.Ordinal))
    {
      // rain_showers_hi (high probability) maps to light because "showers" =
      // brief, less-intense rain regardless of probability tier. rain_showers
      // by itself also gets light.
      return "rain-light";
    }
    if (condition is "rain")
    {
      return "rain";
    }

    // ---- Thunderstorm ----
    if (condition.StartsWith("tsra", StringComparison.Ordinal))
    {
      return "thunderstorm";
    }

    // ---- Snow / wintry mix ----
    if (condition is "snow" or "blowing_snow" or "snow_showers" || condition.Contains("snow", StringComparison.Ordinal))
    {
      // "snow_fzra" (snow + freezing rain) and similar combos still read as
      // snow to a glanceable display — the user cares about the headline
      // condition, not the intensity sub-class.
      return "snow";
    }
    if (condition is "sleet" or "fzra" or "rain_sleet" or "snow_sleet" or "rain_fzra" or "snow_fzra")
    {
      return "sleet";
    }

    // ---- Visibility ----
    if (condition is "fog" or "smoke" or "haze" or "dust")
    {
      return "fog";
    }

    // ---- Wind / extreme ----
    if (condition.StartsWith("wind", StringComparison.Ordinal) || condition == "tornado" || condition == "hurricane" || condition == "tropical_storm")
    {
      return "wind";
    }

    // ---- Temperature extremes ----
    if (condition == "hot")
    {
      return "hot";
    }
    if (condition is "cold" or "blizzard")
    {
      return "cold";
    }

    return "unknown";
  }

  /// <summary>
  /// Pulls just the path component of the URL, tolerating relative URLs
  /// (e.g. <c>/icons/land/day/sct</c>) without crashing.
  /// </summary>
  private static string? TryGetPath(string url)
  {
    // Strip any query string up-front so split logic doesn't have to.
    var queryIdx = url.IndexOf('?');
    if (queryIdx >= 0)
    {
      url = url[..queryIdx];
    }

    if (Uri.TryCreate(url, UriKind.Absolute, out var abs))
    {
      return abs.AbsolutePath;
    }

    // Relative URL — treat the raw string as the path.
    return url.StartsWith('/') ? url : "/" + url;
  }

  /// <summary>
  /// Walks the path segments to find the daypart (<c>day</c>|<c>night</c>) and
  /// the immediately-following condition token. Strips coverage suffix
  /// (<c>,40</c>) and split-icon second halves.
  /// </summary>
  private static (string daypart, string condition) ExtractDaypartAndCondition(string path)
  {
    var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

    // Find the daypart segment. It's always "day" or "night" and the
    // condition token is the segment immediately after it.
    for (var i = 0; i < segments.Length - 1; i++)
    {
      var seg = segments[i];
      if (string.Equals(seg, "day", StringComparison.OrdinalIgnoreCase) ||
          string.Equals(seg, "night", StringComparison.OrdinalIgnoreCase))
      {
        var condition = segments[i + 1];

        // Strip coverage suffix: "tsra,40" → "tsra"
        var commaIdx = condition.IndexOf(',');
        if (commaIdx >= 0)
        {
          condition = condition[..commaIdx];
        }

        return (seg.ToLowerInvariant(), condition.ToLowerInvariant());
      }
    }

    // No daypart found — likely a malformed URL.
    return (string.Empty, string.Empty);
  }
}
