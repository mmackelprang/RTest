namespace Radio.Infrastructure.Audio.Services;

/// <summary>
/// The stations Scan Up/Down may hop between, from a band's swept map (AUD-100).
/// </summary>
/// <param name="StationsHz">Station frequencies, ascending.</param>
/// <param name="MinGapHz">
/// Half the map's channel spacing: a station closer than this to the radio's frequency is the one
/// it is on.
/// </param>
/// <param name="ScannedAtUtc">When the sweep behind the list finished, UTC.</param>
public sealed record ScanStationList(IReadOnlyList<long> StationsHz, long MinGapHz, DateTimeOffset ScannedAtUtc);

/// <summary>
/// What the radio source's Scan Up/Down reads from and writes to the band maps (AUD-100):
/// the stations to hop between when a band's map is fresh, and the stations a live seek finds.
/// </summary>
public interface IScanStationMap
{
  /// <summary>
  /// The stations Scan may hop between on <paramref name="band"/> while the radio is at
  /// <paramref name="frequencyHz"/>, or null when Scan must seek live instead: the band has no
  /// swept map, the map is older than <c>BandMap:ScanMapMaxAgeMinutes</c> (or that is 0 or less),
  /// the map does not cover <paramref name="frequencyHz"/>, or no station other than the one the
  /// radio is on is listed.
  /// </summary>
  /// <param name="band">Band code, e.g. <c>"FM"</c>. Anything that is not the code of a mappable band yields null.</param>
  /// <param name="frequencyHz">The radio's frequency, in Hz.</param>
  /// <param name="whyNot">When the result is null, which of the reasons above, for the log; otherwise null.</param>
  ScanStationList? GetScanStations(string band, long frequencyHz, out string? whyNot);

  /// <summary>
  /// Records a station a live seek stopped on as seek-observed on <paramref name="band"/>'s map.
  /// Ignored for a band that is not mappable. Never changes the band's swept channels or the time
  /// of its sweep.
  /// </summary>
  /// <param name="band">Band code, e.g. <c>"FM"</c>.</param>
  /// <param name="frequencyHz">Where the seek stopped, in Hz.</param>
  /// <param name="seekStrength">The seek's signal reading there.</param>
  void RecordSeekStation(string band, long frequencyHz, float seekStrength);
}
