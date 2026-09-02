using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;

namespace Radio.Infrastructure.External;

/// <summary>
/// A size-bounded, least-recently-used disk cache for fetched GV recordings (ADR-029 D3 §5.3).
///
/// <para>
/// ⚠ This is blackout mitigation, not an optimisation. GV auth is dead roughly 9 minutes in every
/// 20 (punch list XR-3), so a replay 30 seconds later has roughly a 45% chance of 502ing if it goes
/// back to the network. A hit here never touches the network, which is the property that makes
/// replay reliable on a wall clock the user cannot see.
/// </para>
///
/// <para>
/// The cost, owner-accepted at ADR-029 ⟨A1·2⟩: private voicemail audio now sits at rest on disk,
/// where previously it only streamed through a browser. That is why the cap is real, the directory
/// lives under ./data/, and eviction deletes rather than marks.
/// </para>
///
/// <para>
/// Not modelled on <c>AlbumArtCacheService</c>, deliberately: that cache has no size or count bound
/// at all, and hardcodes its directory in the constructor, which is why its own tests scrub the
/// live ./data/albumart directory. The directory here comes from options so tests are hermetic, and
/// the evictor follows <c>DiagnosticCaptureService.PruneCaptureDirectory</c>'s internal-static shape.
/// </para>
/// </summary>
public sealed class GvMediaCache
{
  private readonly ILogger<GvMediaCache> _logger;
  private readonly IOptionsMonitor<GvMediaOptions> _options;

  // Serialises writes and the reclamation that follows them. Deliberately not disposed: this is a
  // process-lifetime singleton, and SemaphoreSlim allocates a disposable resource only once
  // AvailableWaitHandle has been read, which nothing here does.
  private readonly SemaphoreSlim _writeLock = new(1, 1);

  /// <summary>Creates the cache over the GvMedia options section.</summary>
  public GvMediaCache(ILogger<GvMediaCache> logger, IOptionsMonitor<GvMediaOptions> options)
  {
    _logger = logger;
    _options = options;
  }

  /// <summary>
  /// True when a fetch may be served from disk. False at CacheMaxMegabytes = 0, where recordings
  /// are still written — playback needs a path — but never read back (ADR-029 ⟨A1·2⟩: a 0 cap is a
  /// no-cache path, not an infinitely-evicting one).
  /// </summary>
  public bool RetainsEntries => _options.CurrentValue.CacheMaxMegabytes > 0;

  /// <summary>
  /// The cache filename for a media id: 32 hex characters of SHA-256, plus ".mp3".
  /// </summary>
  /// <remarks>
  /// Hashed rather than used raw, even though EventPlaybackRequest.ValidateMediaId already
  /// allow-lists the id to [A-Za-z0-9._~-]. Three reasons the allow-list does not cover:
  /// Windows reserved device names (CON, NUL, PRN, AUX, COM1, ...) are allow-list-clean and are not
  /// creatable as files; a case-insensitive filesystem would collide two ids differing only in
  /// case; and this stays correct if the allow-list is ever loosened for a real GV id that needs it.
  /// The same hash's first 8 characters are the log mask, so a log line and a file on disk
  /// correlate without either carrying the id.
  /// </remarks>
  internal static string FileNameFor(string mediaId)
  {
    var hash = SHA256.HashData(Encoding.UTF8.GetBytes(mediaId));
    return string.Concat(Convert.ToHexString(hash, 0, 16).ToLowerInvariant(), ".mp3");
  }

  /// <summary>The 8-character log mask for a media id. Same hash as <see cref="FileNameFor"/>.</summary>
  internal static string MaskFor(string mediaId)
  {
    var hash = SHA256.HashData(Encoding.UTF8.GetBytes(mediaId));
    return string.Concat("gvm:", Convert.ToHexString(hash, 0, 4).ToLowerInvariant());
  }

  /// <summary>
  /// Returns the path of a cached recording, or null when there is no usable hit.
  /// </summary>
  /// <remarks>
  /// Always null when CacheMaxMegabytes is 0 — that is what "no cache" means here.
  ///
  /// On a hit the file's LastWriteTimeUtc is touched, which is what makes eviction LRU rather than
  /// FIFO. ⚠ Access time is NOT used and must not be substituted: Linux mounts default to relatime,
  /// which updates atime at most once a day for a repeatedly-read file, so an atime-based LRU would
  /// silently degrade on the appliance while behaving differently again on a Windows dev machine.
  /// A failed touch is not a failed hit — the entry is still served, it just keeps its old
  /// eviction rank.
  /// </remarks>
  public string? TryGetPath(string mediaId)
  {
    var options = _options.CurrentValue;
    if (options.CacheMaxMegabytes <= 0)
    {
      return null;
    }

    var path = Path.Combine(options.CacheDirectory, FileNameFor(mediaId));
    if (!File.Exists(path))
    {
      return null;
    }

    try
    {
      File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
    }
    catch (Exception ex)
    {
      _logger.LogDebug(ex, "Could not touch cache entry {MaskedId}; it keeps its old eviction rank",
        MaskFor(mediaId));
    }

    return path;
  }

  /// <summary>
  /// Writes a fetched recording and returns its path. Reclamation runs after the write, so a fetch
  /// never fails because reclamation did.
  /// </summary>
  /// <param name="mediaId">The raw media id — used only to derive the hashed filename.</param>
  /// <param name="content">The fetched recording.</param>
  /// <param name="cancellationToken">Cancellation token.</param>
  public async Task<string> WriteAsync(string mediaId, byte[] content, CancellationToken cancellationToken)
  {
    var options = _options.CurrentValue;
    var directory = options.CacheDirectory;
    var path = Path.Combine(directory, FileNameFor(mediaId));

    await _writeLock.WaitAsync(cancellationToken);
    try
    {
      Directory.CreateDirectory(directory);
      await File.WriteAllBytesAsync(path, content, cancellationToken);

      if (options.CacheMaxMegabytes > 0)
      {
        EvictToCap(directory, (long)options.CacheMaxMegabytes * 1024L * 1024L, path, _logger);
      }
      else
      {
        var window = TimeSpan.FromSeconds(Math.Max(60, options.MaxPlaybackSeconds * 2));
        SweepOlderThan(directory, window, path, _logger);
      }
    }
    finally
    {
      _writeLock.Release();
    }

    return path;
  }

  /// <summary>
  /// Deletes least-recently-used entries until the directory fits inside <paramref name="maxBytes"/>.
  /// </summary>
  /// <remarks>
  /// ⚠ <paramref name="protectedPath"/> is never deleted. It is the file the caller is about to
  /// play, and deleting it here would make a successful fetch unplayable. The consequence is stated
  /// rather than hidden: when the protected file ALONE exceeds the cap, this method leaves the cap
  /// violated by that one file, logs it, and the overage is corrected on the next write, when the
  /// file is no longer protected. The overage is bounded by one recording — with
  /// MaxPlaybackSeconds = 300 the download bound is ~9.6 MB against a 50 MB cap.
  ///
  /// ⚠ Both sides of that protection comparison are absolute before it runs. Directory.EnumerateFiles
  /// returns paths rooted the way its argument was, and CacheDirectory defaults to the RELATIVE
  /// "./data/gvmedia" — so comparing an un-normalised path against Path.GetFullPath(protectedPath)
  /// could never match and the file just fetched would be evicted. Here the left side is
  /// FileInfo.FullName, which is always absolute, and protectedPath is normalised once, before the
  /// loop rather than inside it.
  ///
  /// Recency is LastWriteTimeUtc, touched on read by TryGetPath. See that method for why not atime.
  ///
  /// internal static, taking its directory and cap as parameters, so it can be unit-tested against
  /// a temp dir — the shape of DiagnosticCaptureService.PruneCaptureDirectory.
  /// </remarks>
  internal static void EvictToCap(string directory, long maxBytes, string? protectedPath, ILogger logger)
  {
    if (!Directory.Exists(directory))
    {
      return;
    }

    var protectedFull = protectedPath is null ? null : Path.GetFullPath(protectedPath);

    var entries = new List<(string Path, string Name, long Length, DateTime LastWrite)>();
    foreach (var file in Directory.EnumerateFiles(directory))
    {
      try
      {
        // ⚠ FileInfo's constructor does not stat the file; Length is the member that actually
        // touches the filesystem. It is read HERE, inside the try, which is what makes this catch
        // do what it claims: an entry that vanishes between enumeration and use is skipped rather
        // than thrown out of the method from a later Sum or OrderBy.
        var info = new FileInfo(file);
        entries.Add((info.FullName, info.Name, info.Length, info.LastWriteTimeUtc));
      }
      catch (Exception ex)
      {
        logger.LogDebug(ex, "Could not stat cache entry {File}", file);
      }
    }

    var total = entries.Sum(e => e.Length);
    if (total <= maxBytes)
    {
      return;
    }

    var removed = 0;
    foreach (var entry in entries.OrderBy(e => e.LastWrite))
    {
      if (total <= maxBytes)
      {
        break;
      }
      if (protectedFull is not null
          && string.Equals(entry.Path, protectedFull, StringComparison.OrdinalIgnoreCase))
      {
        continue;
      }

      try
      {
        File.Delete(entry.Path);
        total -= entry.Length;
        removed++;
      }
      catch (Exception ex)
      {
        logger.LogDebug(ex, "Could not evict cache entry {File}", entry.Name);
      }
    }

    if (removed > 0)
    {
      logger.LogInformation(
        "GV media cache: evicted {Count} entries, now {Bytes} bytes against a {Cap} byte cap",
        removed, total, maxBytes);
    }

    if (total > maxBytes)
    {
      logger.LogWarning(
        "GV media cache is {Bytes} bytes against a {Cap} byte cap; the entry in flight is exempt "
        + "from eviction and the overage is corrected on the next write",
        total, maxBytes);
    }
  }

  /// <summary>
  /// The CacheMaxMegabytes = 0 reclamation: deletes entries older than <paramref name="window"/>.
  /// Nothing is evicted per write, so this is a short-retention sweep rather than the
  /// "infinitely-evicting" behaviour ADR-029 ⟨A1·2⟩ forbids.
  /// </summary>
  /// <remarks>
  /// ⚠ As in <see cref="EvictToCap"/>, both sides of the protected-path comparison are made
  /// absolute before comparing. Here the enumerated path is rooted the way
  /// <paramref name="directory"/> was — and the default CacheDirectory is relative — so it is the
  /// side that needs Path.GetFullPath; protectedPath is normalised once, before the loop.
  /// </remarks>
  internal static void SweepOlderThan(string directory, TimeSpan window, string? protectedPath, ILogger logger)
  {
    if (!Directory.Exists(directory))
    {
      return;
    }

    var protectedFull = protectedPath is null ? null : Path.GetFullPath(protectedPath);
    var cutoff = DateTime.UtcNow - window;
    var removed = 0;

    foreach (var file in Directory.EnumerateFiles(directory))
    {
      if (protectedFull is not null
          && string.Equals(Path.GetFullPath(file), protectedFull, StringComparison.OrdinalIgnoreCase))
      {
        continue;
      }

      try
      {
        var info = new FileInfo(file);
        if (info.LastWriteTimeUtc < cutoff)
        {
          info.Delete();
          removed++;
        }
      }
      catch (Exception ex)
      {
        logger.LogDebug(ex, "Could not sweep cache entry {File}", file);
      }
    }

    if (removed > 0)
    {
      logger.LogDebug("GV media cache (no-cache mode): swept {Count} expired entries", removed);
    }
  }
}
