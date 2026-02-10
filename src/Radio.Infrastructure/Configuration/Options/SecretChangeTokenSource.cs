using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Radio.Infrastructure.Configuration.Options;

/// <summary>
/// Shared notification source that fires when secrets change.
/// Used to invalidate options caches for types with secret resolution,
/// so that IOptionsMonitor re-evaluates PostConfigure (which resolves secret tags).
/// </summary>
public sealed class SecretChangeTokenSource
{
  private CancellationTokenSource _cts = new();

  /// <summary>
  /// Signals that secrets have changed, invalidating all dependent options caches.
  /// </summary>
  public void SignalChange()
  {
    var oldCts = _cts;
    _cts = new CancellationTokenSource();
    oldCts.Cancel();
    oldCts.Dispose();
  }

  /// <summary>
  /// Gets a change token that fires when secrets change.
  /// </summary>
  public IChangeToken GetChangeToken()
  {
    return new CancellationChangeToken(_cts.Token);
  }
}

/// <summary>
/// Options change token source that invalidates options of type
/// <typeparamref name="TOptions"/> when secrets change.
/// </summary>
public sealed class SecretOptionsChangeTokenSource<TOptions> : IOptionsChangeTokenSource<TOptions>
{
  private readonly SecretChangeTokenSource _source;

  public SecretOptionsChangeTokenSource(SecretChangeTokenSource source)
  {
    _source = source;
  }

  public string? Name => Microsoft.Extensions.Options.Options.DefaultName;

  public IChangeToken GetChangeToken() => _source.GetChangeToken();
}
