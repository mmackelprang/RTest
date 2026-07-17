using System.Collections.Concurrent;
using Radio.Core.Utilities;
using Radio.Web.Models;
using Radio.Web.Services.ApiClients;

namespace Radio.Web.Services;

/// <summary>
/// Resolves a phone number to a contact display name for the Messages feed
/// (Task #6 — contact-name resolution).
///
/// Two tiers, cheapest first so the feed never hammers the API per row:
///   1. A synchronous in-memory index seeded from the already-loaded merged
///      contact set (manual + PBAP) via <see cref="PrimeFromContacts"/>. This
///      covers the common case with zero network calls.
///   2. A deduped, cached async fallback (<see cref="ResolveAsync"/>) against
///      GET /api/bluetooth/pbap/lookup for numbers the seeded index misses.
///      Both positive and negative results are cached, and concurrent lookups
///      for the same number collapse to a single in-flight request, so a feed of
///      N rows sharing a number issues exactly one request.
///
/// Scoped to the Blazor circuit: the cache lives for the connected session and is
/// shared by PhonePage and its child panels.
/// </summary>
public class ContactResolutionService
{
  private readonly PbapApiService _pbap;
  private readonly ILogger<ContactResolutionService> _logger;

  // normalized number → resolved name; a null value is a cached *negative* result
  // (looked up, no match) so we don't re-hit the API for a known miss.
  private readonly ConcurrentDictionary<string, string?> _cache = new(StringComparer.Ordinal);
  // normalized number → in-flight lookup, so concurrent callers share one request.
  private readonly ConcurrentDictionary<string, Task<string?>> _inFlight = new(StringComparer.Ordinal);
  // normalized number → name, seeded from the merged contact set (local, no network).
  private volatile IReadOnlyDictionary<string, string> _index =
    new Dictionary<string, string>(StringComparer.Ordinal);

  public ContactResolutionService(PbapApiService pbap, ILogger<ContactResolutionService> logger)
  {
    _pbap = pbap;
    _logger = logger;
  }

  /// <summary>
  /// Rebuild the local index from the merged contact set. Cheap; call whenever the
  /// contact set changes (page load, PBAP sync, refresh). A number that appears in
  /// the index always wins over a cached negative result, so a late sync self-heals.
  /// </summary>
  public void PrimeFromContacts(IEnumerable<MergedContact>? contacts)
  {
    var index = new Dictionary<string, string>(StringComparer.Ordinal);
    if (contacts != null)
    {
      foreach (var c in contacts)
      {
        var key = PhoneNumberNormalizer.Normalize(c.Phone);
        if (key.Length == 0 || string.IsNullOrWhiteSpace(c.Name))
        {
          continue;
        }
        // First contact wins for a given number (contacts arrive name-sorted).
        index.TryAdd(key, c.Name);
      }
    }
    _index = index;
  }

  /// <summary>
  /// Best-effort synchronous resolution: a caller-supplied attached name (e.g. the
  /// CallerName RotaryPhone already resolved for an incoming call) wins, then the
  /// local index, then a previously-cached lookup. Returns null when we have no
  /// name yet — the caller shows the formatted number and may kick an async lookup.
  /// </summary>
  public string? TryResolve(string? number, string? attachedName = null)
  {
    if (!string.IsNullOrWhiteSpace(attachedName))
    {
      return attachedName;
    }
    var key = PhoneNumberNormalizer.Normalize(number ?? "");
    if (key.Length == 0)
    {
      return null;
    }
    if (_index.TryGetValue(key, out var local))
    {
      return local;
    }
    // A cached negative result surfaces as null (the value stored is null).
    return _cache.TryGetValue(key, out var cached) ? cached : null;
  }

  /// <summary>
  /// Whether this number is already resolved-or-confirmed-missing (in the index or
  /// the cache), so a caller can skip re-dispatching an async lookup for it.
  /// </summary>
  public bool IsResolved(string? number)
  {
    var key = PhoneNumberNormalizer.Normalize(number ?? "");
    if (key.Length == 0)
    {
      return true;   // nothing to resolve
    }
    return _index.ContainsKey(key) || _cache.ContainsKey(key);
  }

  /// <summary>
  /// Async fallback with in-flight dedupe and positive/negative caching. Resolves
  /// from the index/cache without a request when possible; otherwise issues a
  /// single lookup shared by all concurrent callers for the same number. Never
  /// throws; returns the resolved name or null.
  /// </summary>
  public Task<string?> ResolveAsync(string? number, CancellationToken ct = default)
  {
    var key = PhoneNumberNormalizer.Normalize(number ?? "");
    if (key.Length == 0)
    {
      return Task.FromResult<string?>(null);
    }
    if (_index.TryGetValue(key, out var local))
    {
      return Task.FromResult<string?>(local);
    }
    if (_cache.TryGetValue(key, out var cached))
    {
      return Task.FromResult(cached);
    }
    return _inFlight.GetOrAdd(key, k => LookupAndCacheAsync(k, number!, ct));
  }

  private async Task<string?> LookupAndCacheAsync(string key, string number, CancellationToken ct)
  {
    try
    {
      var name = await _pbap.LookupNumberAsync(number, ct);
      _cache[key] = name;   // cache positive AND negative (null) so a miss isn't retried
      return name;
    }
    catch (Exception ex)
    {
      // PbapApiService already swallows its own errors, but guard the cache write.
      _logger.LogDebug(ex, "Contact resolution failed for {Number}", number);
      _cache[key] = null;
      return null;
    }
    finally
    {
      _inFlight.TryRemove(key, out _);
    }
  }
}
