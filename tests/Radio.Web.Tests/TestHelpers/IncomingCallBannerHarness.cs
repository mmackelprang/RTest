using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Radio.Web.Services;
using Radio.Web.Services.ApiClients;
using Radio.Web.Services.Hub;

namespace Radio.Web.Tests.TestHelpers;

/// <summary>
/// PHN-11: an <see cref="IncomingCallBannerService"/> wired as production wires it — the real hub service,
/// API clients and contact resolution — over a <see cref="ScriptedPhoneHandler"/> and a
/// <see cref="FakeTimeProvider"/>, so nothing touches the network or the wall clock.
/// </summary>
public sealed class IncomingCallBannerHarness
{
  public FakeTimeProvider Time { get; } = new(DateTimeOffset.Parse("2026-10-02T12:00:00Z"));
  public ScriptedPhoneHandler Phone { get; } = new();
  public PhoneHubService Hub { get; }
  public IncomingCallBannerService Service { get; }

  /// <summary>An HttpClient over <see cref="Phone"/>, for other clients a test wants scripted too.</summary>
  public HttpClient Client { get; }

  /// <summary>The configuration the service reads; a test may change it mid-run (PHN-13's live flag).</summary>
  public IConfigurationRoot Config { get; }

  /// <summary>The circuit's contact resolver, shared with the Phone page in production (its local index).</summary>
  public ContactResolutionService Contacts { get; }

  public IncomingCallBannerHarness(bool declineSupported = false, ILoggerFactory? sink = null)
  {
    ILoggerFactory logs = sink ?? NullLoggerFactory.Instance;
    var config = new ConfigurationBuilder()
      .AddInMemoryCollection(new Dictionary<string, string?>
      {
        [IncomingCallBannerService.DeclineSupportedKey] = declineSupported ? "true" : "false",
      })
      .Build();
    Config = config;
    Hub = new PhoneHubService(logs.CreateLogger<PhoneHubService>(), config);
    Client = new HttpClient(Phone) { BaseAddress = new Uri("http://phone.test.invalid") };
    var phoneApi = new PhoneApiService(Client, logs.CreateLogger<PhoneApiService>());
    var pbap = new PbapApiService(Client, logs.CreateLogger<PbapApiService>());
    var contacts = new ContactResolutionService(pbap, logs.CreateLogger<ContactResolutionService>());
    Contacts = contacts;
    Service = new IncomingCallBannerService(
      Hub, phoneApi, config, logs.CreateLogger<IncomingCallBannerService>(), contacts, Time);
  }

  public void Start() => Service.Start();

  /// <summary>One poll period: advances the clock and waits for the status read it triggered.</summary>
  public async Task Tick()
  {
    Time.Advance(IncomingCallBannerService.PollInterval);
    await Service.LastPoll;
  }
}

/// <summary>
/// Plays RotaryPhone (<c>/api/phone/*</c>, <c>/api/contacts</c>) and the API's PBAP lookup and sleep-screen
/// report, recording every request (with its body) so a test can assert on what was and was not sent.
/// </summary>
public sealed class ScriptedPhoneHandler : HttpMessageHandler
{
  private readonly List<string> _requests = [];

  public string StatusJson { get; set; } = "{\"callState\":\"Idle\"}";
  public bool StatusFails { get; set; }

  /// <summary>Holds every status read open until completed, so a test can see what overlaps.</summary>
  public Task? StatusGate { get; set; }
  public string ContactsJson { get; set; } = "[]";
  public string? PbapName { get; set; }

  /// <summary>The lookup's <c>isExactMatch</c>; false plays a stored 7-digit local entry (PHN-14).</summary>
  public bool PbapExact { get; set; } = true;
  public Task? PbapGate { get; set; }
  public bool LookupThrowsOnce { get; set; }
  public (HttpStatusCode Status, string Body, string ContentType) DeclineResponse { get; set; } =
    (HttpStatusCode.OK, "{\"declined\":true}", "application/json");
  public Task? DeclineGate { get; set; }

  /// <summary>Every request as <c>"METHOD /path?query body"</c>, in arrival order.</summary>
  public IReadOnlyList<string> Requests
  {
    get { lock (_requests) { return _requests.ToArray(); } }
  }

  public int Count(string pathPrefix) => Requests.Count(r => r.Contains(pathPrefix, StringComparison.Ordinal));

  protected override async Task<HttpResponseMessage> SendAsync(
    HttpRequestMessage request, CancellationToken cancellationToken)
  {
    string path = request.RequestUri!.PathAndQuery;
    string body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
    lock (_requests)
    {
      _requests.Add($"{request.Method} {path} {body}".TrimEnd());
    }

    if (path.StartsWith("/api/phone/status", StringComparison.Ordinal))
    {
      string status = StatusJson;
      bool fails = StatusFails;
      var gate = StatusGate;
      if (gate is not null)
      {
        await gate;
      }
      return fails ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : Json(status);
    }
    if (path.StartsWith("/api/contacts", StringComparison.Ordinal))
    {
      return Json(ContactsJson);
    }
    if (path.StartsWith("/api/bluetooth/pbap/lookup", StringComparison.Ordinal))
    {
      // The answer is decided when the request ARRIVES, so a test can change the script for the next
      // call while this one is held at the gate.
      string? name = PbapName;
      bool exact = PbapExact;
      bool throws = LookupThrowsOnce;
      LookupThrowsOnce = false;
      var gate = PbapGate;
      if (gate is not null)
      {
        await gate;
      }
      if (throws)
      {
        // Like a real connection failure: it names no path, so the number is not in it.
        throw new HttpRequestException("pbap unreachable");
      }
      return name is null
        ? new HttpResponseMessage(HttpStatusCode.NotFound)
        : Json($"{{\"displayName\":\"{name}\",\"isExactMatch\":{(exact ? "true" : "false")}}}");
    }
    if (path.StartsWith("/api/phone/decline", StringComparison.Ordinal))
    {
      var gate = DeclineGate;
      if (gate is not null)
      {
        await gate;
      }
      var (status, content, contentType) = DeclineResponse;
      return new HttpResponseMessage(status) { Content = new StringContent(content, Encoding.UTF8, contentType) };
    }
    if (path.StartsWith("/api/system/sleep-screen", StringComparison.Ordinal))
    {
      return Json("{\"isSleeping\":true,\"wakeState\":\"Standby\"}");
    }
    if (path.StartsWith("/api/system/sleep", StringComparison.Ordinal))
    {
      return Json("{\"isSleeping\":false}");
    }
    return new HttpResponseMessage(HttpStatusCode.NotFound);
  }

  private static HttpResponseMessage Json(string body) =>
    new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}
