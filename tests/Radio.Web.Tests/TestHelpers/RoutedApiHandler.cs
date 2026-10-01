using System.Net;
using System.Text;
using System.Text.Json;

namespace Radio.Web.Tests.TestHelpers;

/// <summary>
/// A canned Radio API: answers each request from a table keyed by method and path (query string
/// ignored), returns <c>404</c> with no body for anything not in the table, and records every
/// request with its body.
///
/// <para>
/// The <c>404</c> default is chosen because it is what the typed API clients already treat as
/// "nothing there" — <c>GetPrimarySourceAsync</c> and the queue reads return <c>null</c> on it — so
/// a component rendered against an empty table behaves as it does against an absent API, and a test
/// only has to stub the endpoints its assertion is about. Routes can be replaced between renders or
/// between refreshes, which is how a test moves the "server" from one state to another.
/// </para>
/// </summary>
public sealed class RoutedApiHandler : HttpMessageHandler
{
  private readonly object _sync = new();
  private readonly Dictionary<(HttpMethod Method, string Path), (HttpStatusCode Status, string? Body)> _routes = new();
  private readonly List<RecordedRequest> _requests = [];

  /// <summary>One request as the handler saw it.</summary>
  public sealed record RecordedRequest(HttpMethod Method, string Path, string? Body)
  {
    /// <summary>The query string, with its leading <c>?</c>, or empty. Routing ignores it.</summary>
    public string Query { get; init; } = string.Empty;
  }

  /// <summary>Every request received so far, in arrival order.</summary>
  public IReadOnlyList<RecordedRequest> Requests
  {
    get
    {
      lock (_sync)
      {
        return _requests.ToList();
      }
    }
  }

  /// <summary>Answers <c>GET <paramref name="path"/></c> with <paramref name="value"/> as JSON.</summary>
  public RoutedApiHandler Get(string path, object value) =>
    Route(HttpMethod.Get, path, HttpStatusCode.OK, JsonSerializer.Serialize(value));

  /// <summary>Answers <c>POST <paramref name="path"/></c> with <paramref name="status"/> and no body.</summary>
  public RoutedApiHandler Post(string path, HttpStatusCode status = HttpStatusCode.OK) =>
    Route(HttpMethod.Post, path, status, null);

  /// <summary>Sets (or replaces) the answer for one method + path.</summary>
  public RoutedApiHandler Route(HttpMethod method, string path, HttpStatusCode status, string? body)
  {
    lock (_sync)
    {
      _routes[(method, path)] = (status, body);
    }
    return this;
  }

  /// <summary>
  /// Holds every answer to <c><paramref name="method"/> <paramref name="path"/></c> until the returned
  /// source is completed. The request is recorded when it arrives, before the hold, so a test can
  /// wait for it to be in flight. Complete it before the test ends.
  /// </summary>
  public TaskCompletionSource Hold(HttpMethod method, string path)
  {
    TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    lock (_sync)
    {
      _holds[(method, path)] = release.Task;
    }
    return release;
  }

  private readonly Dictionary<(HttpMethod Method, string Path), Task> _holds = new();

  protected override async Task<HttpResponseMessage> SendAsync(
    HttpRequestMessage request, CancellationToken cancellationToken)
  {
    string path = request.RequestUri?.AbsolutePath ?? string.Empty;
    string? body = request.Content == null
      ? null
      : await request.Content.ReadAsStringAsync(cancellationToken);

    (HttpStatusCode Status, string? Body) answer;
    Task? hold;
    lock (_sync)
    {
      _requests.Add(new RecordedRequest(request.Method, path, body) { Query = request.RequestUri?.Query ?? string.Empty });
      _holds.TryGetValue((request.Method, path), out hold);
      if (!_routes.TryGetValue((request.Method, path), out answer))
      {
        answer = (HttpStatusCode.NotFound, null);
      }
    }

    if (hold != null)
    {
      await hold;

      // Read again after the hold, so a test can change the answer while the request is held.
      lock (_sync)
      {
        if (!_routes.TryGetValue((request.Method, path), out answer))
        {
          answer = (HttpStatusCode.NotFound, null);
        }
      }
    }

    HttpResponseMessage response = new(answer.Status);
    if (answer.Body != null)
    {
      response.Content = new StringContent(answer.Body, Encoding.UTF8, "application/json");
    }
    return response;
  }
}
