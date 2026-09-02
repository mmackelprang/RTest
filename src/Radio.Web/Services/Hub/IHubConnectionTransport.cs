using Microsoft.AspNetCore.Http.Connections.Client;

namespace Radio.Web.Services.Hub;

/// <summary>
/// Transport seam for SignalR hub connections.
///
/// <para>
/// <c>HubConnectionBuilder.WithUrl(string)</c> accepts no message handler, so a fake
/// <see cref="System.Net.Http.HttpMessageHandler"/> cannot reach it the way it reaches a typed
/// <see cref="System.Net.Http.HttpClient"/>. That asymmetry is why hub connections were the only
/// thing in the unit-test rig that opened real sockets: a measured run of Radio.Web.Tests made
/// 74 TCP connections to <c>127.0.0.1:5000</c>, two of them completed
/// <c>POST /hubs/audio/negotiate</c> calls.
/// </para>
///
/// <para>
/// Tests register an implementation that sets
/// <see cref="HttpConnectionOptions.HttpMessageHandlerFactory"/> to a handler that fails
/// immediately. Production registers nothing, this stays null, and SignalR's own transport is
/// used unchanged — the configure callback becomes a no-op.
/// </para>
///
/// <para>
/// Failing <em>synchronously and immediately</em> matters beyond the socket itself: both hub
/// services start a detached background retry loop when a connection attempt fails slowly, and
/// those loops outlive the test that created them.
/// </para>
/// </summary>
public interface IHubConnectionTransport
{
  /// <summary>Applies transport configuration to a hub connection being built.</summary>
  void Configure(HttpConnectionOptions options);
}
