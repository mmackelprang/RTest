using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Radio.Web.Models;

namespace Radio.Web.Formatting;

/// <summary>
/// Friendly display-name helpers for audio sources, devices, and now-playing tracks.
/// Goals:
/// <list type="bullet">
///   <item><description>Never surface raw GUIDs or driver-name noise to the user.</description></item>
///   <item><description>Apply alias maps first, then heuristic stripping, then length capping.</description></item>
///   <item><description>Stateless: no DI, no configuration lookups inside the helpers.</description></item>
/// </list>
/// </summary>
public static partial class DisplayNames
{
  /// <summary>
  /// Em-dash placeholder for missing subtitle/artist values.
  /// </summary>
  private const string Dash = "—";

  /// <summary>
  /// Ellipsis (U+2026) used when capping long names. A single character so length math is simple.
  /// </summary>
  private const char Ellipsis = '…';

  /// <summary>
  /// Maximum length of a device display name before ellipsis-clipping.
  /// </summary>
  private const int DeviceNameMaxLength = 40;

  /// <summary>
  /// Trailing GUID suffix used by Source IDs (e.g. <c>radio-aabbccdd-1122-3344-5566-778899aabbcc</c>).
  /// Matches a hyphen followed by a canonical 8-4-4-4-12 hex GUID at the end of the string.
  /// </summary>
  [GeneratedRegex(@"-[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$", RegexOptions.Compiled)]
  private static partial Regex GuidSuffixRegex();

  /// <summary>
  /// Leading "<c>N - </c>" enumeration prefix (e.g. <c>"0 - LG TV"</c> → strip <c>"0 - "</c>).
  /// </summary>
  [GeneratedRegex(@"^\d+\s*-\s*", RegexOptions.Compiled)]
  private static partial Regex EnumerationPrefixRegex();

  /// <summary>
  /// Generic filename-derived title (e.g. <c>"Track 8"</c>) — when the metadata layer
  /// couldn't extract an actual title.
  /// </summary>
  [GeneratedRegex(@"^Track\s+\d+$", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
  private static partial Regex GenericTrackTitleRegex();

  /// <summary>
  /// Leading track-number tokens at the start of a filename (e.g. <c>"08 opening night"</c>,
  /// <c>"01 - intro"</c>, <c>"03_drums"</c>).
  /// </summary>
  [GeneratedRegex(@"^\d+[\s\-_]+", RegexOptions.Compiled)]
  private static partial Regex TrackNumberPrefixRegex();

  /// <summary>
  /// Trailing parenthesized hardware-driver suffixes (e.g. <c>" (AMD High Definition Audio Device)"</c>).
  /// Capture group 1 is the head; group 2 is the parenthesized content.
  /// </summary>
  [GeneratedRegex(@"^(.+?)\s*\(([^)]+)\)\s*$", RegexOptions.Compiled)]
  private static partial Regex ParenthesizedTailRegex();

  /// <summary>
  /// Known driver-name suffixes that should always be stripped.
  /// PR 3 will extend this list as we discover more in the wild.
  /// Match is case-insensitive against the parenthesized content.
  /// </summary>
  private static readonly string[] KnownDriverSuffixes =
  [
    "AMD High Definition Audio Device",
    "NVIDIA High Definition Audio",
    "Intel High Definition Audio",
    "Realtek High Definition Audio",
    "Realtek USB Audio",
    "VB-Audio Virtual Cable",
    "High Definition Audio Device",
    "USB Audio Device",
    "Generic USB Audio",
  ];

  /// <summary>
  /// Pretty source name for UI rows.
  /// <list type="bullet">
  ///   <item><description>If <see cref="AudioSourceDto.Name"/> is non-empty, return it.</description></item>
  ///   <item><description>Else humanize <see cref="AudioSourceDto.Type"/> (CamelCase → space-separated).</description></item>
  ///   <item><description>Never return the raw <see cref="AudioSourceDto.Id"/>; any trailing GUID is stripped.</description></item>
  /// </list>
  /// </summary>
  public static string Source(AudioSourceDto s)
  {
    if (s is null)
    {
      return Dash;
    }

    if (!string.IsNullOrWhiteSpace(s.Name))
    {
      return s.Name.Trim();
    }

    if (!string.IsNullOrWhiteSpace(s.Type))
    {
      return HumanizeCamelCase(s.Type.Trim());
    }

    // Last-resort: derive from Id with the GUID suffix stripped so we never leak raw GUIDs.
    if (!string.IsNullOrWhiteSpace(s.Id))
    {
      var stripped = GuidSuffixRegex().Replace(s.Id, string.Empty);
      return HumanizeCamelCase(stripped);
    }

    return Dash;
  }

  /// <summary>
  /// Pretty device name for selectors and toggles.
  /// <list type="number">
  ///   <item><description>Apply the alias map (whole-string match against the raw <see cref="AudioDeviceDto.Name"/>).</description></item>
  ///   <item><description>Strip a leading <c>"N - "</c> enumeration prefix.</description></item>
  ///   <item><description>Strip a trailing parenthesized driver suffix when (a) it appears in the
  ///     <see cref="KnownDriverSuffixes"/> allow list OR (b) the head is at least 4 characters and contains a space.</description></item>
  ///   <item><description>Cap at <see cref="DeviceNameMaxLength"/> characters with a trailing ellipsis.</description></item>
  /// </list>
  /// </summary>
  public static string Device(AudioDeviceDto d, IDictionary<string, string>? aliasMap = null)
  {
    if (d is null || string.IsNullOrWhiteSpace(d.Name))
    {
      return Dash;
    }

    var raw = d.Name.Trim();

    // 1. Alias map first — whole-string match against the raw name.
    if (aliasMap is not null && aliasMap.TryGetValue(raw, out var alias) && !string.IsNullOrWhiteSpace(alias))
    {
      return Cap(alias);
    }

    var working = raw;

    // 2. Strip leading "N - " enumeration.
    working = EnumerationPrefixRegex().Replace(working, string.Empty);

    // 3. Strip trailing parenthesized driver suffix when allowed.
    var parenMatch = ParenthesizedTailRegex().Match(working);
    if (parenMatch.Success)
    {
      var head = parenMatch.Groups[1].Value.Trim();
      var paren = parenMatch.Groups[2].Value.Trim();

      var driverSuffixKnown = KnownDriverSuffixes.Any(
        s => string.Equals(s, paren, StringComparison.OrdinalIgnoreCase));
      var headDescriptive = head.Length >= 4 && head.Contains(' ');

      if (driverSuffixKnown || headDescriptive)
      {
        working = head;
      }
    }

    return Cap(working);
  }

  /// <summary>
  /// Title + subtitle pair for the now-playing surface.
  /// </summary>
  /// <returns>
  /// <c>Title</c>: the cleanest name available — prefers <see cref="NowPlayingDto.Title"/> unless it is empty
  /// or looks like generic filename fallback (<c>"Track 8"</c>), in which case the file name is parsed.
  /// <c>Subtitle</c>: <see cref="NowPlayingDto.Artist"/> if non-empty, else em-dash.
  /// </returns>
  public static (string Title, string Subtitle) Track(NowPlayingDto np)
  {
    if (np is null)
    {
      return (Dash, Dash);
    }

    var title = (np.Title ?? string.Empty).Trim();
    var isGeneric = string.IsNullOrEmpty(title) || GenericTrackTitleRegex().IsMatch(title);

    string finalTitle;
    if (!isGeneric)
    {
      finalTitle = title;
    }
    else
    {
      var derived = DeriveTitleFromFilePath(GetFilePath(np));
      finalTitle = !string.IsNullOrEmpty(derived)
        ? derived
        : (string.IsNullOrEmpty(title) ? Dash : title);
    }

    var artist = (np.Artist ?? string.Empty).Trim();
    // Treat the existing default placeholder values as missing.
    if (string.IsNullOrEmpty(artist) || artist == "--")
    {
      artist = Dash;
    }

    return (finalTitle, artist);
  }

  /// <summary>
  /// Pulls a file path out of the now-playing payload.
  /// Prefers the typed <see cref="NowPlayingDto.FilePath"/> property (PR 3+); falls back to
  /// the legacy <see cref="NowPlayingDto.ExtendedMetadata"/> dictionary entries so older
  /// payloads (or non-file callers that piggyback on the metadata bag) still resolve.
  /// </summary>
  private static string? GetFilePath(NowPlayingDto np)
  {
    // 1. Typed property is the canonical source from PR 3 onward.
    if (!string.IsNullOrWhiteSpace(np.FilePath))
    {
      return np.FilePath;
    }

    // 2. Legacy / backward-compatible fallback to the typeless metadata dictionary.
    if (np.ExtendedMetadata is null)
    {
      return null;
    }

    foreach (var key in new[] { "FilePath", "filePath", "Path", "path", "SourcePath", "sourcePath" })
    {
      if (np.ExtendedMetadata.TryGetValue(key, out var raw) && raw is not null)
      {
        var s = raw.ToString();
        if (!string.IsNullOrWhiteSpace(s))
        {
          return s;
        }
      }
    }

    return null;
  }

  /// <summary>
  /// Parses a filesystem path into a human title: strips folders, extension, leading track
  /// numbers, and title-cases the result if it looks lowercase.
  /// </summary>
  private static string DeriveTitleFromFilePath(string? path)
  {
    if (string.IsNullOrWhiteSpace(path))
    {
      return string.Empty;
    }

    string fileName;
    try
    {
      fileName = Path.GetFileNameWithoutExtension(path) ?? string.Empty;
    }
    catch (ArgumentException)
    {
      // Path contains invalid chars — bail out gracefully.
      fileName = string.Empty;
    }

    if (string.IsNullOrWhiteSpace(fileName))
    {
      return string.Empty;
    }

    var stripped = TrackNumberPrefixRegex().Replace(fileName, string.Empty).Trim();
    if (stripped.Length == 0)
    {
      return string.Empty;
    }

    // Title-case if it's all lowercase (filename heuristic for case-folded music libraries).
    if (stripped == stripped.ToLowerInvariant())
    {
      var culture = CultureInfo.InvariantCulture;
      stripped = culture.TextInfo.ToTitleCase(stripped);
    }

    return stripped;
  }

  /// <summary>
  /// Splits a CamelCase / PascalCase token into space-separated words
  /// (<c>"FilePlayer"</c> → <c>"File Player"</c>).
  /// </summary>
  private static string HumanizeCamelCase(string token)
  {
    if (string.IsNullOrEmpty(token))
    {
      return string.Empty;
    }

    var sb = new StringBuilder(token.Length + 4);
    for (var i = 0; i < token.Length; i++)
    {
      var c = token[i];
      if (i > 0 && char.IsUpper(c) && !char.IsUpper(token[i - 1]))
      {
        sb.Append(' ');
      }
      sb.Append(c);
    }
    return sb.ToString();
  }

  /// <summary>
  /// Truncates to <see cref="DeviceNameMaxLength"/> with a single ellipsis character.
  /// </summary>
  private static string Cap(string s)
  {
    if (s.Length <= DeviceNameMaxLength)
    {
      return s;
    }
    return s.Substring(0, DeviceNameMaxLength - 1).TrimEnd() + Ellipsis;
  }
}
