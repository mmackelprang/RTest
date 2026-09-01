using Microsoft.AspNetCore.Http.Connections.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Radio.Web.Services.Hub;

namespace Radio.Web.Tests.TestHelpers;

/// <summary>
/// Makes a unit-test service collection hermetic: no test can open a socket, whether it means
/// to or not.
///
/// <para>
/// The defect this closes (TEST-1(c)): the rig pointed at <c>http://localhost:5000</c> and
/// registered real transports against it, so a test's result depended on whether
/// <c>radio-api</c> happened to be running on the machine. A measured run of this project made
/// <b>74 TCP connections</b> to that address, two of them completed SignalR negotiates.
/// </para>
///
/// <para>
/// Two escape routes existed and both are closed here, because they need different tools:
/// typed <see cref="HttpClient"/>s built by <c>IHttpClientFactory</c> (closed by
/// <see cref="NoNetworkHandlerFilter"/>) and SignalR hub connections, whose builder accepts no
/// message handler (closed by <see cref="OfflineHubTransport"/> through the
/// <see cref="IHubConnectionTransport"/> seam).
/// </para>
///
/// <para>
/// Note what this does <b>not</b> rely on: it does not require the absence of a listener. Before
/// this, 28 <c>AddHttpClient&lt;T&gt;()</c> registrations were hermetic only by accident — an
/// <see cref="HttpClient"/> with no <c>BaseAddress</c> throws on a relative URI, and the calling
/// component swallowed it. One added <c>BaseAddress</c> line turned any of them into a live
/// socket, silently. The filter below removes that fragility.
/// </para>
/// </summary>
public static class HermeticTestRig
{
  /// <summary>
  /// Base URL for the Radio API in tests. <c>.invalid</c> is reserved by RFC 2606 and is
  /// guaranteed never to resolve, so an escaped request fails immediately and loudly rather
  /// than silently succeeding against a developer's running service.
  /// </summary>
  public const string ApiBaseUrl = "http://radio-api.test.invalid";

  /// <summary>Base URL for the RotaryPhone API in tests. Same reasoning as
  /// <see cref="ApiBaseUrl"/>; it is a separate service on a separate port (5004).</summary>
  public const string PhoneApiBaseUrl = "http://rotaryphone.test.invalid";

  /// <summary>
  /// Registers the hub transport seam and the outbound-HTTP guard. Safe to call more than once
  /// and safe to call alongside tests that install their own handlers — see
  /// <see cref="NoNetworkHandlerFilter"/> for why those are left alone.
  /// </summary>
  public static IServiceCollection AddHermeticTestRig(this IServiceCollection services)
  {
    services.AddSingleton<IHubConnectionTransport, OfflineHubTransport>();
    services.AddSingleton<IHttpMessageHandlerBuilderFilter, NoNetworkHandlerFilter>();
    return services;
  }
}

/// <summary>
/// Fails every request without touching the network, presenting the same
/// <see cref="HttpRequestException"/> a refused connection would.
///
/// <para>
/// The exception type is deliberate. The point of the rig is that a test's outcome must not
/// depend on ambient state, and "the service is not there" is the state every one of these
/// tests was already written against — components swallow it and fall back to defaults. A novel
/// exception type would change behaviour rather than make it deterministic. The message names
/// the URI so an escape is identifiable in test output rather than merely absent.
/// </para>
/// </summary>
public sealed class NoNetworkHandler : HttpMessageHandler
{
  protected override Task<HttpResponseMessage> SendAsync(
    HttpRequestMessage request, CancellationToken cancellationToken)
  {
    throw new HttpRequestException(
      $"Blocked by the hermetic test rig: a unit test tried to reach '{request.RequestUri}'. " +
      "Stub this dependency instead of relying on a service being absent.");
  }
}

/// <summary>Supplies <see cref="NoNetworkHandler"/> as the transport for SignalR hub
/// connections, via the <see cref="IHubConnectionTransport"/> seam.
///
/// <para>Failing synchronously matters: both hub services start a detached background retry
/// loop on a slow failure, and those loops outlive the test that created them. That is how three
/// classes produced 74 connections.</para>
/// </summary>
public sealed class OfflineHubTransport : IHubConnectionTransport
{
  public void Configure(HttpConnectionOptions options)
  {
    options.HttpMessageHandlerFactory = _ => new NoNetworkHandler();
  }
}

/// <summary>
/// Replaces the socket-opening primary handler on every client built by
/// <c>IHttpClientFactory</c>, covering all current and future
/// <c>AddHttpClient&lt;T&gt;()</c> registrations without each one opting in.
///
/// <para>
/// ⚠ It replaces the handler <b>only</b> when it is still the framework default
/// (<see cref="HttpClientHandler"/> / <see cref="SocketsHttpHandler"/>). A test that installed
/// its own handler through the factory — a <see cref="MockHttpHandler"/> returning canned
/// responses, say — keeps it. Without that check the guard would silently break every test that
/// stubs its transport properly, which is the opposite of the intent.
/// </para>
/// </summary>
public sealed class NoNetworkHandlerFilter : IHttpMessageHandlerBuilderFilter
{
  public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next)
  {
    return builder =>
    {
      next(builder);

      if (builder.PrimaryHandler is HttpClientHandler or SocketsHttpHandler)
      {
        builder.PrimaryHandler = new NoNetworkHandler();
      }
    };
  }
}
