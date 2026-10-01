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
  }

  private static RadioPresetDto P(string id, string band, int slot) =>
    new(id, $"Station {id}", 92_300_000 + slot, band, DateTimeOffset.UnixEpoch.AddMinutes(slot), slot);

  [Fact]
  public void RenderDuringTheImport_DoesNotLoseTheFirstPlacement()
  {
    var presets = PresetBar.OrderLikeKnob(new[] { P("a1", "AM", 1), P("f1", "FM", 1), P("w1", "WB", 1), P("w2", "WB", 2) });

    var cut = RenderComponent<PresetBar>(p => p
      .Add(x => x.Presets, presets)
      .Add(x => x.CurrentBand, "FM")
      .Add(x => x.Clock, new FakeTimeProvider()));

    // A radio-state tick re-renders the bar — now on WB — before the import has returned.
    cut.SetParametersAndRender(p => p.Add(x => x.CurrentBand, "WB"));
    _runtime.Module.Calls.Should().BeEmpty();

    cut.InvokeAsync(() => _runtime.CompleteImport());

    cut.WaitForAssertion(() => _runtime.Module.Calls.Where(c => c.Identifier == "reveal").Should().ContainSingle());
    var reveal = _runtime.Module.Calls.Single(c => c.Identifier == "reveal");
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
    _runtime.Module.Calls.Should().BeEmpty("a disposed bar must not register listeners that call back into it");
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

  /// <summary>An <see cref="IJSObjectReference"/> that completes every call and records it.</summary>
  private sealed class RecordingModule : IJSObjectReference
  {
    public List<(string Identifier, object?[] Args)> Calls { get; } = new();
    public bool Disposed { get; private set; }

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
    {
      lock (Calls)
      {
        Calls.Add((identifier, args ?? Array.Empty<object?>()));
      }
      return ValueTask.FromResult(default(TValue)!);
    }

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
      InvokeAsync<TValue>(identifier, args);

    public ValueTask DisposeAsync()
    {
      Disposed = true;
      return ValueTask.CompletedTask;
    }
  }
}
