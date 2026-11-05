using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Radio.Web.Services;
using Radio.Web.Services.ApiClients;
using Radio.Web.Services.Http;

namespace Radio.Web.Tests.TestHelpers;

/// <summary>What the fake controller saw of one request.</summary>
public sealed record SeenRequest(HttpMethod Method, Uri Uri, string? PasswordHeader, string? Body);

/// <summary>
/// Stands in for the RNET controller on port 3001, with the JSON casing and status codes of
/// <c>ts_src/webHookServer.ts</c>. Built on <see cref="RecordingHandler"/>, so a test rendezvous on
/// <see cref="Handler"/>, and can park a poll on <see cref="SystemGate"/> or <see cref="ZonesGate"/>.
/// </summary>
public sealed class FakeHouseController
{
  public const string Password = "not-a-real-password";

  private readonly object _sync = new();
  private readonly List<SeenRequest> _seen = [];
  private readonly Dictionary<string, int> _zoneVolumes = new() { ["Kitchen"] = 20, ["Den"] = 20 };
  private readonly HashSet<string> _zonesOff = [];
  private readonly HashSet<string> _zonesMuted = [];

  public FakeHouseController()
  {
    Handler = new RecordingHandler(RespondAsync);
  }

  public RecordingHandler Handler { get; }

  /// <summary>When set, every request answers with this status.</summary>
  public HttpStatusCode? ForcedStatus { get; set; }

  /// <summary>When set, every request throws this, as a failed connection does.</summary>
  public Exception? Failure { get; set; }

  /// <summary>
  /// Amp link down: <c>GET /system</c> and <c>PUT /system/name</c> answer, and everything else is 503.
  /// </summary>
  public bool AmpLinkDown { get; set; }

  /// <summary>What <c>GET /system</c> reports as the address. The controller can report null.</summary>
  public string? Address { get; set; } = "192.168.86.49";

  /// <summary>Adds a zone the controller has no name for, which it reports as null.</summary>
  public bool HasUnnamedZone { get; set; }

  /// <summary>Adds a null entry to the zone list, which is more than a zone with a null name.</summary>
  public bool HasNullZoneEntry { get; set; }

  /// <summary>When set, every request other than a GET answers with this status.</summary>
  public HttpStatusCode? WriteStatus { get; set; }

  /// <summary>When set, every request other than a GET waits on this after it is recorded.</summary>
  public TaskCompletionSource? WriteGate { get; set; }

  /// <summary>When set, <c>GET /system</c> waits on this after it is recorded.</summary>
  public TaskCompletionSource? SystemGate { get; set; }

  /// <summary>When set, <c>GET /zones</c> waits on this after it is recorded.</summary>
  public TaskCompletionSource? ZonesGate { get; set; }

  public IReadOnlyList<SeenRequest> Seen
  {
    get
    {
      lock (_sync)
      {
        return _seen.ToList();
      }
    }
  }

  public void SetVolume(string zone, int volume)
  {
    lock (_sync)
    {
      _zoneVolumes[zone] = volume;
    }
  }

  public void SetPower(string zone, bool on)
  {
    lock (_sync)
    {
      if (on)
      {
        _zonesOff.Remove(zone);
      }
      else
      {
        _zonesOff.Add(zone);
      }
    }
  }

  public void SetMuted(string zone, bool muted)
  {
    lock (_sync)
    {
      if (muted)
      {
        _zonesMuted.Add(zone);
      }
      else
      {
        _zonesMuted.Remove(zone);
      }
    }
  }

  /// <summary>An API client wired as <c>Program.cs</c> wires it, ending in this fake.</summary>
  public HouseAudioApiService CreateClient(HouseAudioPasswordStore passwords)
  {
    HttpClient client = new(new HouseAudioAuthHandler(passwords) { InnerHandler = Handler })
    {
      BaseAddress = new Uri("http://house.test:3001")
    };
    return new HouseAudioApiService(client, passwords, NullLogger<HouseAudioApiService>.Instance);
  }

  /// <summary>A password store that already holds <see cref="Password"/>.</summary>
  public static HouseAudioPasswordStore StoreWithPassword()
  {
    IDataProtectionProvider provider = new EphemeralDataProtectionProvider();
    string stored = provider.CreateProtector(HouseAudioPasswordStore.ProtectorPurpose).Protect(Password);
    return CreateStore(provider, stored, new RecordingHandler());
  }

  /// <summary>A password store over the given stored value, posting saves to <paramref name="configApi"/>.</summary>
  public static HouseAudioPasswordStore CreateStore(
    IDataProtectionProvider provider, string? storedValue, HttpMessageHandler configApi)
  {
    IConfiguration configuration = new ConfigurationBuilder()
      .AddInMemoryCollection(new Dictionary<string, string?> { [HouseAudioPasswordStore.ConfigKey] = storedValue })
      .Build();

    ServiceCollection services = new();
    services.AddSingleton(new ConfigurationApiService(
      new HttpClient(configApi) { BaseAddress = new Uri("http://api.test:5000") },
      NullLogger<ConfigurationApiService>.Instance));

    return new HouseAudioPasswordStore(
      configuration,
      provider,
      services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
      NullLogger<HouseAudioPasswordStore>.Instance);
  }

  private async Task<HttpResponseMessage> RespondAsync(HttpRequestMessage request, CancellationToken ct)
  {
    string? body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
    string? password = request.Headers.TryGetValues("X-RNet-Password", out IEnumerable<string>? values)
      ? values.Single()
      : null;
    lock (_sync)
    {
      _seen.Add(new SeenRequest(request.Method, request.RequestUri!, password, body));
    }

    string path = request.RequestUri!.AbsolutePath;
    bool isRead = request.Method == HttpMethod.Get;
    if (isRead && path == "/system" && SystemGate is { } systemGate)
    {
      await systemGate.Task.WaitAsync(ct);
    }

    if (isRead && path == "/zones" && ZonesGate is { } zonesGate)
    {
      await zonesGate.Task.WaitAsync(ct);
    }

    if (!isRead && WriteGate is { } writeGate)
    {
      await writeGate.Task.WaitAsync(ct);
    }

    if (Failure is { } failure)
    {
      throw failure;
    }

    if (ForcedStatus is { } status)
    {
      return new HttpResponseMessage(status);
    }

    if (!isRead && WriteStatus is { } writeStatus)
    {
      return new HttpResponseMessage(writeStatus);
    }

    if (isRead && path == "/system")
    {
      return Json(new
      {
        name = "Mackelhome Controller",
        address = Address,
        serialConnected = !AmpLinkDown,
        version = "1.1.1"
      });
    }

    // Both system routes come before the controller's amp check.
    if (AmpLinkDown && path != "/system/name")
    {
      return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
      {
        Content = new StringContent("RNet not connected.", Encoding.UTF8, "text/plain")
      };
    }

    return (isRead, path) switch
    {
      (true, "/zones") => Json(ZonesPayload()),
      (true, "/sources") => Json(new[]
      {
        new { sourceID = 0, name = "Audiocast 1", type = 7 },
        new { sourceID = 2, name = "Pi Audio", type = 11 }
      }),
      _ => new HttpResponseMessage(HttpStatusCode.OK)
    };
  }

  private object?[] ZonesPayload()
  {
    lock (_sync)
    {
      IEnumerable<KeyValuePair<string?, int>> zones = _zoneVolumes
        .Select(zone => new KeyValuePair<string?, int>(zone.Key, zone.Value));
      if (HasUnnamedZone)
      {
        zones = zones.Append(new KeyValuePair<string?, int>(null, 0));
      }

      IEnumerable<object?> payload = zones.Select(zone => (object?)new
      {
        controllerID = 0,
        zoneID = zone.Key switch { "Kitchen" => 0, "Den" => 1, _ => 2 },
        name = zone.Key,
        power = zone.Key is null || !_zonesOff.Contains(zone.Key),
        volume = zone.Value,
        sourceID = 2,
        muted = zone.Key is not null && _zonesMuted.Contains(zone.Key),
        maxVolume = 80,
        bass = -3,
        treble = 4,
        loudness = true,
        balance = -1,
        turnOnVolume = 30,
        doNotDisturb = true,
        partyMode = 2
      });

      return (HasNullZoneEntry ? payload.Append(null) : payload).ToArray();
    }
  }

  private static HttpResponseMessage Json(object payload) => new(HttpStatusCode.OK)
  {
    Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
  };
}
