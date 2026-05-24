using Radio.Infrastructure.Weather;

namespace Radio.Infrastructure.Tests.Weather;

/// <summary>
/// Coverage for the 18-entry NWS → IconKey mapping table pinned by the
/// Designer handoff §4. The Web layer maps every IconKey produced here to a
/// concrete Material Symbol; any unmapped URL MUST fall through to
/// <c>"unknown"</c> rather than throwing.
/// </summary>
public class NwsIconMapperTests
{
  [Theory]
  // ── Sky cover (day vs night matters for skc/few/sct/bkn) ─────────────────
  [InlineData("https://api.weather.gov/icons/land/day/skc?size=medium", "sunny")]
  [InlineData("https://api.weather.gov/icons/land/day/few?size=medium", "sunny")]
  [InlineData("https://api.weather.gov/icons/land/night/skc", "clear-night")]
  [InlineData("https://api.weather.gov/icons/land/night/few", "clear-night")]
  [InlineData("https://api.weather.gov/icons/land/day/sct", "mostly-sunny")]
  [InlineData("https://api.weather.gov/icons/land/night/sct", "partly-cloudy-night")]
  [InlineData("https://api.weather.gov/icons/land/day/bkn", "partly-cloudy")]
  [InlineData("https://api.weather.gov/icons/land/night/bkn", "partly-cloudy-night")]
  // ovc is the same icon day and night
  [InlineData("https://api.weather.gov/icons/land/day/ovc", "cloudy")]
  [InlineData("https://api.weather.gov/icons/land/night/ovc", "cloudy")]
  // ── Precipitation ─────────────────────────────────────────────────────────
  [InlineData("https://api.weather.gov/icons/land/day/rain", "rain")]
  [InlineData("https://api.weather.gov/icons/land/day/rain_light", "rain-light")]
  [InlineData("https://api.weather.gov/icons/land/day/drizzle", "rain-light")]
  [InlineData("https://api.weather.gov/icons/land/day/rain_showers", "rain-light")]
  [InlineData("https://api.weather.gov/icons/land/day/rain_heavy", "rain-heavy")]
  [InlineData("https://api.weather.gov/icons/land/day/tsra_hi", "rain-heavy")]
  // ── Thunderstorm ──────────────────────────────────────────────────────────
  [InlineData("https://api.weather.gov/icons/land/day/tsra", "thunderstorm")]
  [InlineData("https://api.weather.gov/icons/land/day/tsra_sct", "thunderstorm")]
  // ── Snow (snow-only conditions per NWS docs) ──────────────────────────────
  [InlineData("https://api.weather.gov/icons/land/day/snow", "snow")]
  [InlineData("https://api.weather.gov/icons/land/day/blowing_snow", "snow")]
  [InlineData("https://api.weather.gov/icons/land/day/snow_showers", "snow")]
  // ── Sleet / wintry mix (combos with freezing rain take the mix glyph) ────
  // These were swallowed by the snow branch in the original implementation
  // because of a .Contains("snow") catch-all — see the regression-guard test
  // below. Per ADR §2.6 / HANDOFF §4 they MUST render as sleet so the user
  // sees the visually-distinct icy-mix glyph (weather_mix) instead of pure
  // snow (weather_snowy).
  [InlineData("https://api.weather.gov/icons/land/day/sleet", "sleet")]
  [InlineData("https://api.weather.gov/icons/land/day/fzra", "sleet")]
  [InlineData("https://api.weather.gov/icons/land/day/rain_sleet", "sleet")]
  [InlineData("https://api.weather.gov/icons/land/day/snow_sleet", "sleet")]
  [InlineData("https://api.weather.gov/icons/land/day/rain_fzra", "sleet")]
  [InlineData("https://api.weather.gov/icons/land/day/snow_fzra", "sleet")]
  // ── Visibility ────────────────────────────────────────────────────────────
  [InlineData("https://api.weather.gov/icons/land/day/fog", "fog")]
  [InlineData("https://api.weather.gov/icons/land/day/haze", "fog")]
  [InlineData("https://api.weather.gov/icons/land/day/smoke", "fog")]
  // ── Wind / extremes ───────────────────────────────────────────────────────
  [InlineData("https://api.weather.gov/icons/land/day/wind", "wind")]
  [InlineData("https://api.weather.gov/icons/land/day/wind_skc", "wind")]
  [InlineData("https://api.weather.gov/icons/land/day/tornado", "wind")]
  [InlineData("https://api.weather.gov/icons/land/day/hot", "hot")]
  [InlineData("https://api.weather.gov/icons/land/day/cold", "cold")]
  [InlineData("https://api.weather.gov/icons/land/day/blizzard", "cold")]
  public void MapToIconKey_RecognizedConditions_ProducesExpectedKey(string url, string expected)
  {
    Assert.Equal(expected, NwsIconMapper.MapToIconKey(url));
  }

  [Fact]
  public void MapToIconKey_StripsCoverageSuffix()
  {
    // NWS appends coverage probabilities as ",NN" — must not break parsing.
    Assert.Equal("thunderstorm", NwsIconMapper.MapToIconKey("https://api.weather.gov/icons/land/day/tsra,40?size=medium"));
  }

  [Fact]
  public void MapToIconKey_StripsQuerystring()
  {
    Assert.Equal("sunny", NwsIconMapper.MapToIconKey("https://api.weather.gov/icons/land/day/skc?size=medium"));
  }

  [Fact]
  public void MapToIconKey_AcceptsRelativeUrls()
  {
    Assert.Equal("rain", NwsIconMapper.MapToIconKey("/icons/land/day/rain"));
  }

  [Theory]
  [InlineData(null)]
  [InlineData("")]
  [InlineData("   ")]
  [InlineData("not-a-url")]
  [InlineData("https://api.weather.gov/icons/marine/storm")]   // no day/night segment
  [InlineData("https://api.weather.gov/icons/land/day/asteroid")] // genuine unknown
  public void MapToIconKey_UnmappedOrInvalid_FallsBackToUnknown(string? url)
  {
    Assert.Equal("unknown", NwsIconMapper.MapToIconKey(url));
  }

  [Fact]
  public void MapToIconKey_TakesFirstHalfOfSplitIcons()
  {
    // NWS occasionally renders "split icons" of form .../day/sct/rain — the
    // condition we render is the FIRST half (sct in this case).
    Assert.Equal("mostly-sunny", NwsIconMapper.MapToIconKey("https://api.weather.gov/icons/land/day/sct/rain?size=medium"));
  }

  /// <summary>
  /// Regression guard for the BLOCKER caught in the first review of PR #415:
  /// an earlier version of the snow branch used
  /// <c>condition.Contains("snow")</c> as a catch-all and ran BEFORE the
  /// sleet branch, so <c>snow_fzra</c> / <c>snow_sleet</c> silently took the
  /// snow path and rendered the wrong glyph. ADR §2.6 and HANDOFF §4 both
  /// list those combos under <c>sleet</c>. This test pins the ordering so
  /// the bug can't return without a red CI signal.
  /// </summary>
  [Theory]
  [InlineData("https://api.weather.gov/icons/land/day/snow_fzra", "sleet")]
  [InlineData("https://api.weather.gov/icons/land/day/snow_sleet", "sleet")]
  [InlineData("https://api.weather.gov/icons/land/night/snow_fzra", "sleet")]
  public void MapToIconKey_SnowCombosWithFreezingRainOrSleet_AreSleetNotSnow(string url, string expected)
  {
    Assert.Equal(expected, NwsIconMapper.MapToIconKey(url));
  }
}
