using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Microsoft.JSInterop;
using Radio.Web.Components.Shared;
using Radio.Web.Models;

namespace Radio.Web.Tests.Components.Shared;

/// <summary>
/// Review M2 on UI-20: a <see cref="PresetBar"/> render that lands while its JS module is still
/// importing must not spend the reveal decision. The parent re-renders the bar on every radio-state
/// tick (~500 ms), so on the box that window is real. bUnit's own interop cannot hold an
/// <c>import</c> pending, so this fixture swaps in an <see cref="IJSRuntime"/> whose import
/// completes only when the test says so.
/// </summary>
public class PresetBarModuleLoadTests : TestContext
{
  private readonly PendingImportRuntime _runtime = new();

  public PresetBarModuleLoadTests()
  {
    Services.AddSingleton<IJSRuntime>(_runtime);
    // UI-21: the bar reads the PRESETS knob's highlight off the HUD singleton.
    Services.AddSingleton(new Radio.Web.Services.EncoderHudService(timeProvider: new FakeTimeProvider()));
  }

  private static RadioPresetDto P(string id, string band, int slot) =>
    new(id, $"Station {id}", 92_300_000 + slot, band, DateTimeOffset.UnixEpoch.AddMinutes(slot), slot);

  /// <summary>
  /// Long enough that only a missing reveal, never a loaded runner, reaches it. A pass returns as
  /// soon as the reveal is recorded; only the failing direction pays the whole wait.
  /// </summary>
  private static readonly TimeSpan RevealTimeout = TimeSpan.FromSeconds(10);

  [Fact]
  public async Task RenderDuringTheImport_DoesNotLoseTheFirstPlacement()
  {
    var presets = PresetBar.OrderLikeKnob(new[] { P("a1", "AM", 1), P("f1", "FM", 1), P("w1", "WB", 1), P("w2", "WB", 2) });

    var cut = RenderComponent<PresetBar>(p => p
      .Add(x => x.Presets, presets)
      .Add(x => x.CurrentBand, "FM")
      .Add(x => x.Clock, new FakeTimeProvider()));

    // A radio-state tick re-renders the bar — now on WB — before the import has returned.
    cut.SetParametersAndRender(p => p.Add(x => x.CurrentBand, "WB"));
    _runtime.Module.Snapshot().Should().BeEmpty();

    await cut.InvokeAsync(() => _runtime.CompleteImport());

    // The import's continuation (init, then reveal) runs on the thread pool and is followed by no
    // render, so bUnit's WaitForAssertion — which re-checks only on a render — checked once, before
    // the continuation, and never again under full-suite load. Rendezvous on the reveal itself
    // (CLAUDE.md § Test Timing). A timeout here means no reveal was made: the failing direction.
    var revealed = await Task.WhenAny(_runtime.Module.FirstReveal, Task.Delay(RevealTimeout));
    revealed.Should().BeSameAs(_runtime.Module.FirstReveal, "the first-render continuation reveals once the module can act");

    var reveal = _runtime.Module.Snapshot().Should().ContainSingle(c => c.Identifier == "reveal").Subject;
    reveal.Args[1].Should().Be(2, "WB's first card — a1 f1 w1 w2 — placed once the module can act");
    reveal.Args[2].Should().Be(false, "it is still the first placement, so not animated");
  }

  [Fact]
  public void DisposedWhileImporting_WiresNothingUp()
  {
    RenderComponent<PresetBar>(p => p
      .Add(x => x.Presets, new List<RadioPresetDto> { P("f1", "FM", 1) })
      .Add(x => x.CurrentBand, "FM")
      .Add(x => x.Clock, new FakeTimeProvider()));

    DisposeComponents();
    _runtime.CompleteImport();

    // The import's continuation runs asynchronously: rendezvous on what it does last (releasing
    // the module), not on elapsed time. Once it has released the module it has already taken the
    // disposed branch, so the empty-calls check below is exact, not a race.
    SpinWait.SpinUntil(() => _runtime.Module.Disposed, TimeSpan.FromSeconds(5))
      .Should().BeTrue("the module that arrived late is released");
    _runtime.Module.Snapshot().Should().BeEmpty("a disposed bar must not register listeners that call back into it");
  }

  /// <summary>An <see cref="IJSRuntime"/> whose <c>import</c> waits for <see cref="CompleteImport"/>.</summary>
  private sealed class PendingImportRuntime : IJSRuntime
  {
    private readonly TaskCompletionSource<IJSObjectReference> _import = new();

    public RecordingModule Module { get; } = new();

    public void CompleteImport() => _import.SetResult(Module);

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
      identifier == "import"
        ? new ValueTask<TValue>(_import.Task.ContinueWith(t => (TValue)t.Result, TaskScheduler.Default))
        : ValueTask.FromResult(default(TValue)!);

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
      InvokeAsync<TValue>(identifier, args);
  }

  /// <summary>
  /// An <see cref="IJSObjectReference"/> that completes every call and records it. Calls arrive on
  /// the import continuation's thread, not the test's, so the list is only ever read through
  /// <see cref="Snapshot"/>, under the same lock that writes it.
  /// </summary>
  private sealed class RecordingModule : IJSObjectReference
  {
    private readonly List<(string Identifier, object?[] Args)> _calls = new();
    private readonly TaskCompletionSource _firstReveal = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private volatile bool _disposed;

    /// <summary>Completes when the first <c>reveal</c> has been recorded.</summary>
    public Task FirstReveal => _firstReveal.Task;

    public bool Disposed => _disposed;

    public List<(string Identifier, object?[] Args)> Snapshot()
    {
      lock (_calls)
      {
        return _calls.ToList();
      }
    }

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
    {
      lock (_calls)
      {
        _calls.Add((identifier, args ?? Array.Empty<object?>()));
      }
      if (identifier == "reveal")
      {
        _firstReveal.TrySetResult();
      }
      return ValueTask.FromResult(default(TValue)!);
    }

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
      InvokeAsync<TValue>(identifier, args);

    public ValueTask DisposeAsync()
    {
      _disposed = true;
      return ValueTask.CompletedTask;
    }
  }
}
