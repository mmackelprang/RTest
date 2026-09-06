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

    // Dedupe concurrent lookups for the same number: publish a placeholder task
    // BEFORE starting the work, so the removal-on-completion can never race ahead
    // of the insert (which would strand a stale task when the request completes
    // synchronously). Only the caller that wins the insert runs the request.
    var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
    var inFlight = _inFlight.GetOrAdd(key, tcs.Task);
    if (!ReferenceEquals(inFlight, tcs.Task))
    {
      return inFlight;   // another call is already resolving this number
    }
    _ = RunLookupAsync(key, number!, tcs, ct);
    return tcs.Task;
  }

  private async Task RunLookupAsync(string key, string number,
    TaskCompletionSource<string?> tcs, CancellationToken ct)
  {
    string? result = null;
    try
    {
      var (outcome, name) = await _pbap.LookupNumberAsync(number, ct);
      switch (outcome)
      {
        case ContactLookupOutcome.Found:
          _cache[key] = name;   // cache the resolved name
          result = name;
          break;
        case ContactLookupOutcome.NotFound:
          _cache[key] = null;   // definitive miss — cache so we don't re-request it
          result = null;
          break;
        default:
          // Transient failure (5xx / timeout / connection error): do NOT cache, so
          // IsResolved stays false and the next poll retries. Otherwise a single
          // backend hiccup would poison this number's name for the whole session.
          result = null;
          break;
      }
    }
    catch (Exception ex)
    {
      // PbapApiService maps its own failures to Unavailable, but guard anyway and
      // treat anything unexpected as transient (do not cache).
      _logger.LogDebug(ex, "Contact resolution failed for {Number}", LogSafeText.ForPhone(number));
      result = null;
    }
    finally
    {
      // Remove our own entry first (so a re-resolve after a transient miss can start
      // a fresh request), then release awaiters. TryRemove(KeyValuePair) only drops
      // the entry if it's still OUR task.
      _inFlight.TryRemove(new KeyValuePair<string, Task<string?>>(key, tcs.Task));
      tcs.TrySetResult(result);
    }
  }
}
