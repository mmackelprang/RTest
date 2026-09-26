namespace Radio.Core.Models.Audio;

/// <summary>
/// The single definition of "the audio source did not supply this field", and the rule for
/// merging a fingerprint result into a source's own metadata.
/// </summary>
/// <remarks>
/// <para>
/// <b>The rule (AUD-1, owner decision 2026-09-08):</b> when metadata is available from the
/// audio source, the source's value wins; where it is missing, fingerprinting fills the gap.
/// <b>Precedence is per FIELD, not per track</b> — a track whose AVRCP supplies title and
/// artist but no cover art keeps both and takes only the art from the identification.
/// </para>
/// <para>
/// ⚠ <b>An empty or whitespace-only string counts as MISSING, not as an authoritative blank.</b>
/// On Bluetooth that is the ordinary case rather than an edge: <c>BluetoothPlaybackMetadata</c>
/// defaults Title/Artist/Album to <c>string.Empty</c>, and <c>BluetoothAudioSource.OnMetadataChanged</c>
/// writes them through unguarded, so a phone that reports no album leaves <c>""</c> under the key.
/// A test that only asked "is the key present" would do nothing at all on Bluetooth.
/// </para>
/// <para>
/// ⚠ <b>Placeholders count as missing too</b> (owner decision 2026-09-08).
/// <see cref="StandardMetadataKeys.DefaultArtist"/> / <see cref="StandardMetadataKeys.DefaultAlbum"/>
/// (<c>"--"</c>), <see cref="StandardMetadataKeys.DefaultTitle"/> and
/// <see cref="StandardMetadataKeys.DefaultAlbumArtUrl"/> exist to mean "nothing here". On
/// <c>FilePlayerAudioSource</c> they are the only form missing takes, because its tag reads skip
/// empty tags and leave the seeded placeholder in place. A caller may add source-specific
/// placeholders for the title (the filename on FilePlayer, the device name on Bluetooth).
/// Placeholders compare <b>ordinal, case-insensitive, after trimming</b> — case-insensitive because
/// that is how FilePlayer's "title equals the filename" test worked before this type existed.
/// </para>
/// <para>
/// This decides and applies text fields. Cover art is only DECIDED here
/// (<see cref="ShouldFillAlbumArt"/>): Bluetooth has to download a remote URL into the local
/// album-art cache before it can be written, while FilePlayer assigns it directly, so each source
/// performs its own write.
/// </para>
/// </remarks>
public static class SourceMetadataPrecedence
{
  /// <summary>
  /// True when <paramref name="value"/> carries nothing the source actually supplied: null, a
  /// value whose text is empty or whitespace-only, or a match for any of
  /// <paramref name="placeholders"/>.
  /// </summary>
  public static bool IsMissing(object? value, params string[] placeholders)
  {
    if (value is null)
    {
      return true;
    }

    var text = value as string ?? value.ToString();
    if (string.IsNullOrWhiteSpace(text))
    {
      return true;
    }

    var trimmed = text.Trim();
    foreach (var placeholder in placeholders)
    {
      if (!string.IsNullOrWhiteSpace(placeholder)
          && string.Equals(trimmed, placeholder.Trim(), StringComparison.OrdinalIgnoreCase))
      {
        return true;
      }
    }

    return false;
  }

  /// <summary>
  /// True when <paramref name="metadata"/> has no usable value under <paramref name="key"/>.
  /// An absent key is missing; so is a present key holding a missing value.
  /// </summary>
  /// <remarks>
  /// Deliberately NOT an overload of <see cref="IsMissing(object?, string[])"/>: a dictionary is an
  /// <c>object</c>, so an overload pair would let <c>IsMissing(metadata, "Album")</c> bind to the
  /// value form with "Album" as a placeholder and silently answer the wrong question.
  /// </remarks>
  public static bool IsFieldMissing(
    IReadOnlyDictionary<string, object> metadata,
    string key,
    params string[] placeholders)
  {
    return !metadata.TryGetValue(key, out var value) || IsMissing(value, placeholders);
  }

  /// <summary>
  /// Writes <paramref name="candidate"/> under <paramref name="key"/> only when the field is
  /// missing and the candidate itself is not. Returns whether it wrote.
  /// </summary>
  public static bool TryFill(
    IDictionary<string, object> metadata,
    string key,
    string? candidate,
    params string[] placeholders)
  {
    if (string.IsNullOrWhiteSpace(candidate))
    {
      return false;
    }

    if (metadata.TryGetValue(key, out var existing) && !IsMissing(existing, placeholders))
    {
      return false;
    }

    metadata[key] = candidate;
    return true;
  }

  /// <summary>
  /// Applies the per-field rule to Title, Artist and Album, each decided independently. Cover
  /// art is not written here — see <see cref="ShouldFillAlbumArt"/>.
  /// </summary>
  /// <param name="metadata">The source's own metadata, mutated in place.</param>
  /// <param name="identified">The fingerprint result.</param>
  /// <param name="extraTitlePlaceholders">
  /// Additional values that mean "no title" for this source, on top of
  /// <see cref="StandardMetadataKeys.DefaultTitle"/>. FilePlayer passes the filename without
  /// extension (it seeds Title with it and replaces it only when a Title tag exists); Bluetooth
  /// passes its own default title and the connected device's name, which it writes as the title
  /// when no track metadata exists.
  /// </param>
  public static MetadataFillResult FillMissingFrom(
    IDictionary<string, object> metadata,
    TrackMetadata identified,
    params string?[] extraTitlePlaceholders)
  {
    var titlePlaceholders = new List<string> { StandardMetadataKeys.DefaultTitle };
    foreach (var extra in extraTitlePlaceholders)
    {
      if (!string.IsNullOrWhiteSpace(extra))
      {
        titlePlaceholders.Add(extra);
      }
    }

    return new MetadataFillResult(
      Title: TryFill(metadata, StandardMetadataKeys.Title, identified.Title, titlePlaceholders.ToArray()),
      Artist: TryFill(metadata, StandardMetadataKeys.Artist, identified.Artist, StandardMetadataKeys.DefaultArtist),
      Album: TryFill(metadata, StandardMetadataKeys.Album, identified.Album, StandardMetadataKeys.DefaultAlbum));
  }

  /// <summary>
  /// The cover-art half of the same rule: true when the source holds no usable art (absent,
  /// empty, or the fallback image path). Returns the DECISION only; the caller performs the write.
  /// </summary>
  public static bool ShouldFillAlbumArt(IReadOnlyDictionary<string, object> metadata)
  {
    return IsFieldMissing(
      metadata,
      StandardMetadataKeys.AlbumArtUrl,
      StandardMetadataKeys.DefaultAlbumArtUrl);
  }
}

/// <summary>Which text fields <see cref="SourceMetadataPrecedence.FillMissingFrom"/> actually wrote.</summary>
public readonly record struct MetadataFillResult(bool Title, bool Artist, bool Album)
{
  /// <summary>True when at least one text field was filled.</summary>
  public bool Any => Title || Artist || Album;

  /// <summary>
  /// A comma-separated list of what was filled, for logging. Returns "nothing" when the source
  /// had supplied every field.
  /// </summary>
  public string Describe(bool filledAlbumArt = false)
  {
    var parts = new List<string>(4);
    if (Title) { parts.Add("title"); }
    if (Artist) { parts.Add("artist"); }
    if (Album) { parts.Add("album"); }
    if (filledAlbumArt) { parts.Add("cover art"); }
    return parts.Count == 0 ? "nothing" : string.Join(", ", parts);
  }
}
