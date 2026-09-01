using System.Net;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Radzen;
using Radio.Web.Components.Shared;
using Radio.Web.Models;
using Radio.Web.Services.ApiClients;
using Radio.Web.Tests.TestHelpers;

namespace Radio.Web.Tests.Components.Shared;

/// <summary>
/// bUnit tests for <see cref="CastDeviceDropdown"/> — specifically the new
/// explicit "Stop Casting" action row added per
/// <c>docs/design-handoffs/HANDOFF-stop-casting-menu-item.md</c>.
///
/// The component is mostly presentational + drives DevicesApiService for
/// scan/connect/disconnect. We give it a real DevicesApiService backed by a
/// stub HttpMessageHandler so:
/// <list type="bullet">
///   <item>scans return an empty cached + discovery list (no Cast devices
///         leak into the row count, keeping assertions tight),</item>
///   <item>the disconnect call can be observed (success vs. failure) and
///         delayed so we can assert the loading state mid-flight.</item>
/// </list>
///
/// Radzen + JS interop pattern matches <see cref="OutputPickerDropdownTests"/>:
/// register Radzen components and put the JSRuntime in Loose mode (same gotcha
/// MEMORY documents for MudBlazor).
/// </summary>
public class CastDeviceDropdownTests : TestContext
{
  public CastDeviceDropdownTests()
  {
    // Hermetic rig: fails every outbound HTTP request and every SignalR
    // negotiate without touching the network, so this fixture's result never
    // depends on whether radio-api happens to be running locally.
    Services.AddHermeticTestRig();

    Services.AddRadzenComponents();
    JSInterop.Mode = JSRuntimeMode.Loose;
  }

  /// <summary>
  /// When no Cast device is connected, the Stop Casting action row must NOT
  /// render — the visibility rule (spec §3) gates it on
  /// <c>_connectedDevice != null</c> so the popover only shows the action
  /// when there's actually something to stop.
  /// </summary>
  [Fact]
  public void StopCastingRow_NotRendered_WhenNoDeviceConnected()
  {
    var handler = new StubCastDevicesHandler();
    var api = BuildApi(handler);

    var cut = RenderComponent<CastDeviceDropdown>(p => p
      .Add(c => c.IsOpen, true)
      .Add(c => c.DevicesApi, api)
      .Add(c => c.ConnectedDevice, null));

    Assert.Empty(cut.FindAll("button.cast-stop-row"));
    // Sanity: the old inline Disconnect button was removed, so no
    // "Disconnect" text should appear when no device is connected either.
    Assert.DoesNotContain("Disconnect", cut.Markup);
  }

  /// <summary>
  /// When a Cast device is connected, the Stop Casting row IS rendered,
  /// labelled "Stop Casting", with the cast_off icon, and an aria-label
  /// that names the connected device (per spec §4 Copy table).
  /// </summary>
  [Fact]
  public void StopCastingRow_Rendered_WhenDeviceConnected()
  {
    var handler = new StubCastDevicesHandler();
    var api = BuildApi(handler);
    var device = new CastDeviceDto("device-1", "Living Room", "10.0.0.5", 8009, "Speaker");

    var cut = RenderComponent<CastDeviceDropdown>(p => p
      .Add(c => c.IsOpen, true)
      .Add(c => c.DevicesApi, api)
      .Add(c => c.ConnectedDevice, device));

    var stopRows = cut.FindAll("button.cast-stop-row");
    Assert.Single(stopRows);
    Assert.Contains("Stop Casting", stopRows[0].TextContent);
    // aria-label should name the device for screen readers.
    Assert.Equal("Disconnect from Living Room", stopRows[0].GetAttribute("aria-label"));
    // Default state: not busy.
    Assert.Equal("false", stopRows[0].GetAttribute("aria-busy"));
  }

  /// <summary>
  /// Clicking the Stop Casting row must invoke the DevicesApiService disconnect
  /// flow — verified via the stub handler observing the POST to
  /// /api/devices/cast/disconnect. On success the row also clears the
  /// connected-device banner and fires the OnDisconnect EventCallback.
  /// </summary>
  [Fact]
  public void StopCastingRow_Click_InvokesDisconnectAndFiresCallback()
  {
    var handler = new StubCastDevicesHandler();
    var api = BuildApi(handler);
    var device = new CastDeviceDto("device-1", "Living Room", "10.0.0.5", 8009, "Speaker");
    var disconnectCallbackCount = 0;

    var cut = RenderComponent<CastDeviceDropdown>(p => p
      .Add(c => c.IsOpen, true)
      .Add(c => c.DevicesApi, api)
      .Add(c => c.ConnectedDevice, device)
      .Add(c => c.OnDisconnect, Microsoft.AspNetCore.Components.EventCallback.Factory.Create(
        this, () => disconnectCallbackCount++)));

    cut.Find("button.cast-stop-row").Click();

    // Stub handler observed the disconnect call.
    Assert.True(handler.DisconnectCallCount >= 1,
      "Expected DevicesApiService to POST /api/devices/cast/disconnect at least once.");
    Assert.Equal(1, disconnectCallbackCount);
  }

  /// <summary>
  /// While the disconnect HTTP request is pending the row must show the
  /// loading state: label flips to "Stopping…" (with the ellipsis char,
  /// not three dots — spec §4) and aria-busy goes to true. The stub
  /// handler holds the disconnect response open so we can observe the
  /// in-flight render before completion.
  /// </summary>
  [Fact]
  public async Task StopCastingRow_LoadingState_ShowsStoppingLabelWhilePending()
  {
    var pendingTcs = new TaskCompletionSource<HttpResponseMessage>();
    var handler = new StubCastDevicesHandler { PendingDisconnect = pendingTcs.Task };
    var api = BuildApi(handler);
    var device = new CastDeviceDto("device-1", "Living Room", "10.0.0.5", 8009, "Speaker");

    var cut = RenderComponent<CastDeviceDropdown>(p => p
      .Add(c => c.IsOpen, true)
      .Add(c => c.DevicesApi, api)
      .Add(c => c.ConnectedDevice, device));

    // Click — InvokeAsync so bUnit's renderer dispatches without awaiting
    // the disconnect call (which is parked on pendingTcs).
    _ = cut.InvokeAsync(() => cut.Find("button.cast-stop-row").Click());

    // The component sets _isDisconnecting=true then awaits the HTTP call.
    // Wait until the row reflects the loading state.
    cut.WaitForAssertion(() =>
    {
      var row = cut.Find("button.cast-stop-row");
      Assert.Contains("Stopping", row.TextContent);
      Assert.Equal("true", row.GetAttribute("aria-busy"));
    });

    // Release the disconnect — let the component finish.
    pendingTcs.SetResult(new HttpResponseMessage(HttpStatusCode.OK));
    await Task.Yield();
  }

  private static DevicesApiService BuildApi(HttpMessageHandler handler)
  {
    var client = new HttpClient(handler)
    {
      BaseAddress = new Uri(HermeticTestRig.ApiBaseUrl)
    };
    return new DevicesApiService(client, NullLogger<DevicesApiService>.Instance);
  }

  /// <summary>
  /// Minimal stub of the Cast endpoints the dropdown calls during OnParametersSet
  /// (GET cached + discovery) and the explicit disconnect button. Returns empty
  /// device lists so the device list stays empty (keeps assertions on the
  /// Stop Casting row clean) and lets tests inject a pending Task for the
  /// disconnect call to observe the loading state.
  /// </summary>
  private sealed class StubCastDevicesHandler : HttpMessageHandler
  {
    public int DisconnectCallCount { get; private set; }
    /// <summary>
    /// When non-null, the disconnect response will be deferred until this
    /// Task completes — lets a test observe mid-flight UI state.
    /// </summary>
    public Task<HttpResponseMessage>? PendingDisconnect { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request, CancellationToken cancellationToken)
    {
      var path = request.RequestUri?.AbsolutePath ?? string.Empty;

      if (path.EndsWith("/api/devices/cast/disconnect"))
      {
        DisconnectCallCount++;
        if (PendingDisconnect != null)
        {
          return await PendingDisconnect.WaitAsync(cancellationToken);
        }
        return new HttpResponseMessage(HttpStatusCode.OK);
      }

      if (path.EndsWith("/api/devices/cast/cached") || path.EndsWith("/api/devices/cast"))
      {
        // Empty list — no Cast devices for these tests.
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
          Content = new StringContent("[]", System.Text.Encoding.UTF8, "application/json")
        };
      }

      // Default: 404 — not under test, just don't blow up.
      return new HttpResponseMessage(HttpStatusCode.NotFound);
    }
  }
}
