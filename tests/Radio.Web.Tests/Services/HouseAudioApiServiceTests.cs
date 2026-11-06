using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Radio.Web.Models;
using Radio.Web.Services;
using Radio.Web.Services.ApiClients;
using Radio.Web.Services.Http;
using Radio.Web.Tests.TestHelpers;

namespace Radio.Web.Tests.Services;

public class HouseAudioApiServiceTests
{
  private readonly FakeHouseController _controller = new();
  private readonly HouseAudioApiService _api;

  public HouseAudioApiServiceTests()
  {
    _api = _controller.CreateClient(FakeHouseController.StoreWithPassword());
  }

  private static HouseAudioApiService ClientWithNoPassword(HttpMessageHandler handler)
  {
    HouseAudioPasswordStore store =
      FakeHouseController.CreateStore(new EphemeralDataProtectionProvider(), null, new RecordingHandler());
    return new HouseAudioApiService(
      new HttpClient(handler) { BaseAddress = new Uri("http://house.test:3001") },
      store,
      NullLogger<HouseAudioApiService>.Instance);
  }

  [Fact]
  public async Task SetVolumeAsync_EscapesTheZoneName()
  {
    HouseAudioResult<bool> result = await _api.SetVolumeAsync("Screen Porch", 40, CancellationToken.None);

    Assert.Equal(new HouseAudioResult<bool>(HouseAudioHealth.Ok, true), result);
    (HttpMethod method, string path) = Assert.Single(_controller.Handler.Requests);
    Assert.Equal(HttpMethod.Put, method);
    Assert.Equal("/Screen%20Porch/volume/40", path);
  }

  [Fact]
  public async Task ZoneName_WithASlash_StaysOneSegment()
  {
    await _api.SetPowerAsync("Bed/Bath", true, CancellationToken.None);

    Assert.Equal("/Bed%2FBath/on", Assert.Single(_controller.Handler.Requests).Path);
  }

  [Fact]
  public async Task Requests_CarryThePasswordHeader_NeverAQueryString()
  {
    await _api.GetZonesAsync(CancellationToken.None);
    await _api.SetPowerAsync("Kitchen", true, CancellationToken.None);

    Assert.Equal(2, _controller.Seen.Count);
    Assert.All(_controller.Seen, seen =>
    {
      Assert.Equal(FakeHouseController.Password, seen.PasswordHeader);
      Assert.Empty(seen.Uri.Query);
      Assert.DoesNotContain(FakeHouseController.Password, seen.Uri.ToString());
    });
  }

  [Fact]
  public async Task GetZones_ReadsTheControllersJson()
  {
    HouseAudioResult<IReadOnlyList<HouseZone>> result = await _api.GetZonesAsync(CancellationToken.None);

    Assert.Equal(HouseAudioHealth.Ok, result.Health);
    HouseZone expected = new(
      ControllerId: 0, ZoneId: 0, Name: "Kitchen", Power: true, Volume: 20, SourceId: 2, Muted: false,
      MaxVolume: 80, Bass: -3, Treble: 4, Loudness: true, Balance: -1, TurnOnVolume: 30,
      DoNotDisturb: true, PartyMode: 2);
    Assert.Equal(expected, result.Value![0]);
    Assert.Equal(2, result.Value.Count);
  }

  [Fact]
  public async Task GetSources_ReadsTheControllersJson()
  {
    HouseAudioResult<IReadOnlyList<HouseSource>> result = await _api.GetSourcesAsync(CancellationToken.None);

    Assert.Equal(HouseAudioHealth.Ok, result.Health);
    Assert.Equal([new HouseSource(0, "Audiocast 1", 7), new HouseSource(2, "Pi Audio", 11)], result.Value!);
  }

  [Fact]
  public async Task GetSystem_ReadsTheControllersJson()
  {
    HouseAudioResult<HouseSystemInfo> result = await _api.GetSystemAsync(CancellationToken.None);

    Assert.Equal(
      new HouseAudioResult<HouseSystemInfo>(
        HouseAudioHealth.Ok,
        new HouseSystemInfo("Mackelhome Controller", "192.168.86.49", true, "1.1.1")),
      result);
  }

  [Fact]
  public async Task GetSystem_ReadsANullAddress()
  {
    _controller.Address = null;

    HouseAudioResult<HouseSystemInfo> result = await _api.GetSystemAsync(CancellationToken.None);

    Assert.Equal(HouseAudioHealth.Ok, result.Health);
    Assert.Null(result.Value!.Address);
  }

  [Fact]
  public async Task GetZones_DropsAZoneWithNoName()
  {
    // Every zone route is by name, so such a zone cannot be commanded, and a null breaks ZonePath.
    _controller.HasUnnamedZone = true;

    HouseAudioResult<IReadOnlyList<HouseZone>> result = await _api.GetZonesAsync(CancellationToken.None);

    Assert.Equal(HouseAudioHealth.Ok, result.Health);
    Assert.Equal(["Kitchen", "Den"], result.Value!.Select(zone => zone.Name));
  }

  [Fact]
  public async Task GetZones_DropsANullEntry()
  {
    _controller.HasNullZoneEntry = true;

    HouseAudioResult<IReadOnlyList<HouseZone>> result = await _api.GetZonesAsync(CancellationToken.None);

    Assert.Equal(HouseAudioHealth.Ok, result.Health);
    Assert.Equal(["Kitchen", "Den"], result.Value!.Select(zone => zone.Name));
  }

  [Fact]
  public async Task RenameController_WorksWhileTheAmpLinkIsDown()
  {
    _controller.AmpLinkDown = true;

    HouseAudioResult<bool> result = await _api.RenameControllerAsync("Den", CancellationToken.None);

    Assert.Equal(new HouseAudioResult<bool>(HouseAudioHealth.Ok, true), result);
  }

  [Fact]
  public async Task GetZones_Maps401_ToWrongPassword()
  {
    _controller.ForcedStatus = HttpStatusCode.Unauthorized;

    HouseAudioResult<IReadOnlyList<HouseZone>> result = await _api.GetZonesAsync(CancellationToken.None);

    Assert.Equal(HouseAudioHealth.WrongPassword, result.Health);
    Assert.Null(result.Value);
  }

  [Fact]
  public async Task GetZones_Maps503_ToAmpDisconnected()
  {
    _controller.AmpLinkDown = true;

    HouseAudioResult<IReadOnlyList<HouseZone>> result = await _api.GetZonesAsync(CancellationToken.None);

    Assert.Equal(HouseAudioHealth.AmpDisconnected, result.Health);
    Assert.Null(result.Value);
  }

  [Fact]
  public async Task GetZones_MapsConnectionFailure_ToUnreachable()
  {
    _controller.Failure = new HttpRequestException("connection refused");

    HouseAudioResult<IReadOnlyList<HouseZone>> result = await _api.GetZonesAsync(CancellationToken.None);

    Assert.Equal(HouseAudioHealth.Unreachable, result.Health);
    Assert.Null(result.Value);
  }

  [Fact]
  public async Task GetZones_MapsATimeout_ToUnreachable()
  {
    // HttpClient reports its own timeout as a cancellation the caller did not ask for.
    _controller.Failure = new TaskCanceledException("timed out", new TimeoutException());

    HouseAudioResult<IReadOnlyList<HouseZone>> result = await _api.GetZonesAsync(CancellationToken.None);

    Assert.Equal(HouseAudioHealth.Unreachable, result.Health);
  }

  [Fact]
  public async Task GetZones_MapsAnUnreadableBody_ToUnreachable()
  {
    HouseAudioApiService api = new(
      new HttpClient(new MockHttpHandler("<html>not json</html>")) { BaseAddress = new Uri("http://house.test:3001") },
      FakeHouseController.StoreWithPassword(),
      NullLogger<HouseAudioApiService>.Instance);

    HouseAudioResult<IReadOnlyList<HouseZone>> result = await api.GetZonesAsync(CancellationToken.None);

    Assert.Equal(HouseAudioHealth.Unreachable, result.Health);
  }

  [Theory]
  [InlineData(HttpStatusCode.NotFound)]
  [InlineData(HttpStatusCode.InternalServerError)]
  public async Task GetZones_MapsAnyOtherFailure_ToUnreachable(HttpStatusCode status)
  {
    _controller.ForcedStatus = status;

    HouseAudioResult<IReadOnlyList<HouseZone>> result = await _api.GetZonesAsync(CancellationToken.None);

    Assert.Equal(HouseAudioHealth.Unreachable, result.Health);
    Assert.Null(result.Value);
  }

  [Fact]
  public async Task GetSystem_AnswersWhileTheAmpLinkIsDown()
  {
    _controller.AmpLinkDown = true;

    HouseAudioResult<HouseSystemInfo> result = await _api.GetSystemAsync(CancellationToken.None);

    Assert.Equal(HouseAudioHealth.Ok, result.Health);
    Assert.False(result.Value!.SerialConnected);
  }

  [Fact]
  public async Task Get_WithNoStoredPassword_ReturnsNoPassword_WithoutCallingOut()
  {
    MockHttpHandler handler = new();
    HouseAudioApiService api = ClientWithNoPassword(handler);

    HouseAudioResult<IReadOnlyList<HouseZone>> result = await api.GetZonesAsync(CancellationToken.None);

    Assert.Equal(HouseAudioHealth.NoPassword, result.Health);
    Assert.Equal(0, handler.RequestCount);
  }

  [Fact]
  public async Task Write_WithNoStoredPassword_ReturnsNoPassword_WithoutCallingOut()
  {
    MockHttpHandler handler = new();
    HouseAudioApiService api = ClientWithNoPassword(handler);

    HouseAudioResult<bool> result = await api.SetAllPowerAsync(false, CancellationToken.None);

    Assert.Equal(new HouseAudioResult<bool>(HouseAudioHealth.NoPassword, false), result);
    Assert.Equal(0, handler.RequestCount);
  }

  public static TheoryData<string, string, string> WriteRoutes => new()
  {
    { "power on", "PUT", "/Kitchen/on" },
    { "power off", "PUT", "/Kitchen/off" },
    { "mute", "PUT", "/Kitchen/mute" },
    { "unmute", "PUT", "/Kitchen/unmute" },
    { "volume", "PUT", "/Kitchen/volume/44" },
    { "source", "PUT", "/Kitchen/source/2" },
    { "parameter", "PUT", "/Kitchen/parameter/7/1" },
    { "negative parameter", "PUT", "/Kitchen/parameter/0/-5" },
    { "max volume", "PUT", "/Kitchen/maxvolume/80" },
    { "delete zone", "DELETE", "/Kitchen" },
    { "all on", "PUT", "/on" },
    { "all off", "PUT", "/off" },
    { "mute all", "PUT", "/mute" },
    { "unmute all", "PUT", "/unmute" },
    { "rename", "PUT", "/system/name" },
    { "add source", "POST", "/sources" },
    { "update source", "PUT", "/sources/3" },
    { "delete source", "DELETE", "/sources/3" },
  };

  private Task<HouseAudioResult<bool>> Invoke(string command) => command switch
  {
    "power on" => _api.SetPowerAsync("Kitchen", true, CancellationToken.None),
    "power off" => _api.SetPowerAsync("Kitchen", false, CancellationToken.None),
    "mute" => _api.SetMuteAsync("Kitchen", true, CancellationToken.None),
    "unmute" => _api.SetMuteAsync("Kitchen", false, CancellationToken.None),
    "volume" => _api.SetVolumeAsync("Kitchen", 44, CancellationToken.None),
    "source" => _api.SetSourceAsync("Kitchen", 2, CancellationToken.None),
    "parameter" => _api.SetParameterAsync("Kitchen", 7, 1, CancellationToken.None),
    "negative parameter" => _api.SetParameterAsync("Kitchen", 0, -5, CancellationToken.None),
    "max volume" => _api.SetMaxVolumeAsync("Kitchen", 80, CancellationToken.None),
    "delete zone" => _api.DeleteZoneAsync("Kitchen", CancellationToken.None),
    "all on" => _api.SetAllPowerAsync(true, CancellationToken.None),
    "all off" => _api.SetAllPowerAsync(false, CancellationToken.None),
    "mute all" => _api.SetAllMuteAsync(true, CancellationToken.None),
    "unmute all" => _api.SetAllMuteAsync(false, CancellationToken.None),
    "rename" => _api.RenameControllerAsync("Den", CancellationToken.None),
    "add source" => _api.AddSourceAsync(3, "Turntable", 13, CancellationToken.None),
    "update source" => _api.UpdateSourceAsync(3, "Turntable", 13, CancellationToken.None),
    "delete source" => _api.DeleteSourceAsync(3, CancellationToken.None),
    _ => throw new ArgumentOutOfRangeException(nameof(command), command, null)
  };

  [Theory]
  [MemberData(nameof(WriteRoutes))]
  public async Task Writes_UseTheControllersRoutes(string command, string method, string path)
  {
    HouseAudioResult<bool> result = await Invoke(command);

    Assert.Equal(new HouseAudioResult<bool>(HouseAudioHealth.Ok, true), result);
    (HttpMethod seenMethod, string seenPath) = Assert.Single(_controller.Handler.Requests);
    Assert.Equal(method, seenMethod.Method);
    Assert.Equal(path, seenPath);
  }

  private static void AssertJson(string? body, params (string Name, object Value)[] expected)
  {
    using JsonDocument document = JsonDocument.Parse(body!);
    Assert.Equal(expected.Length, document.RootElement.EnumerateObject().Count());
    foreach ((string name, object value) in expected)
    {
      // Exact name: the controller reads these case-sensitively.
      JsonElement element = document.RootElement.GetProperty(name);
      Assert.Equal(value, value is int ? element.GetInt32() : element.GetString());
    }
  }

  [Fact]
  public async Task RenameController_SendsTheName()
  {
    await _api.RenameControllerAsync("Mackelhome", CancellationToken.None);

    AssertJson(Assert.Single(_controller.Seen).Body, ("name", "Mackelhome"));
  }

  [Fact]
  public async Task AddSource_SendsTheControllersFieldNames()
  {
    await _api.AddSourceAsync(3, "Turntable", 13, CancellationToken.None);

    AssertJson(Assert.Single(_controller.Seen).Body, ("sourceID", 3), ("name", "Turntable"), ("type", 13));
  }

  [Fact]
  public async Task UpdateSource_SendsNameAndType()
  {
    await _api.UpdateSourceAsync(3, "Turntable", 19, CancellationToken.None);

    AssertJson(Assert.Single(_controller.Seen).Body, ("name", "Turntable"), ("type", 19));
  }

  [Theory]
  [InlineData(HttpStatusCode.Unauthorized, HouseAudioHealth.WrongPassword)]
  [InlineData(HttpStatusCode.ServiceUnavailable, HouseAudioHealth.AmpDisconnected)]
  [InlineData(HttpStatusCode.Conflict, HouseAudioHealth.Ok)]
  [InlineData(HttpStatusCode.BadRequest, HouseAudioHealth.Ok)]
  [InlineData(HttpStatusCode.NotFound, HouseAudioHealth.Ok)]
  [InlineData(HttpStatusCode.InternalServerError, HouseAudioHealth.Unreachable)]
  public async Task Write_Failure_NamesTheHealth_AndReturnsFalse(HttpStatusCode status, HouseAudioHealth health)
  {
    _controller.ForcedStatus = status;

    HouseAudioResult<bool> result = await _api.DeleteSourceAsync(3, CancellationToken.None);

    Assert.Equal(new HouseAudioResult<bool>(health, false), result);
  }

  [Fact]
  public async Task Write_ConnectionFailure_ReturnsUnreachable()
  {
    _controller.Failure = new HttpRequestException("connection refused");

    HouseAudioResult<bool> result = await _api.SetPowerAsync("Kitchen", true, CancellationToken.None);

    Assert.Equal(new HouseAudioResult<bool>(HouseAudioHealth.Unreachable, false), result);
  }

  [Fact]
  public async Task CallerCancellation_IsNotReportedAsAnOutage()
  {
    using CancellationTokenSource cancelled = new();
    await cancelled.CancelAsync();

    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _api.GetZonesAsync(cancelled.Token));
    await Assert.ThrowsAnyAsync<OperationCanceledException>(
      () => _api.SetPowerAsync("Kitchen", true, cancelled.Token));
  }
}

/// <summary>
/// Drives the real <c>Program.cs</c> registrations, with only the primary handlers replaced, so these
/// fail if the registration changes rather than a copy of it in a test.
/// </summary>
public class HouseAudioRegistrationTests(RadioWebFactory factory) : IClassFixture<RadioWebFactory>
{
  private const string ClientName = nameof(HouseAudioApiService);

  private sealed class CapturingLoggerFactory : ILoggerFactory
  {
    public ConcurrentQueue<(string Category, string Message)> Entries { get; } = new();

    public ILogger CreateLogger(string categoryName) => new Capturing(this, categoryName);

    public void AddProvider(ILoggerProvider provider)
    {
    }

    public void Dispose()
    {
    }

    private sealed class Capturing(CapturingLoggerFactory owner, string category) : ILogger
    {
      public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

      public bool IsEnabled(LogLevel logLevel) => true;

      public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
        Exception? exception, Func<TState, Exception?, string> formatter) =>
        owner.Entries.Enqueue((category, formatter(state, exception) + " " + exception));
    }
  }

  private WebApplicationFactory<Program> Host(HttpMessageHandler controller, CapturingLoggerFactory? logs = null) =>
    factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
    {
      services.Configure<HttpClientFactoryOptions>(ClientName, options =>
        options.HttpMessageHandlerBuilderActions.Add(handlers => handlers.PrimaryHandler = controller));

      // The password's save goes through the API client; answer that here too.
      services.Configure<HttpClientFactoryOptions>(nameof(ConfigurationApiService), options =>
        options.HttpMessageHandlerBuilderActions.Add(handlers => handlers.PrimaryHandler = new RecordingHandler()));

      if (logs is not null)
      {
        // UseSerilog registered its own; the HttpClient logging filter resolves whichever is last.
        services.AddSingleton<ILoggerFactory>(logs);
      }
    }));

  [Fact]
  public async Task PasswordAndQueryString_NeverReachALog()
  {
    FakeHouseController controller = new();
    CapturingLoggerFactory logs = new();
    await using WebApplicationFactory<Program> host = Host(controller.Transport, logs);

    using IServiceScope scope = host.Services.CreateScope();
    HouseAudioPasswordStore store = scope.ServiceProvider.GetRequiredService<HouseAudioPasswordStore>();
    Assert.True(await store.SetAsync(FakeHouseController.Password, CancellationToken.None));

    HouseAudioResult<IReadOnlyList<HouseZone>> zones = await scope.ServiceProvider
      .GetRequiredService<HouseAudioApiService>()
      .GetZonesAsync(CancellationToken.None);

    // The client never builds a query string. This one stands for the worst case: if a password did
    // get into a URL on this named client, nothing on it may write that URL down.
    HttpClient named = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(ClientName);
    using HttpResponseMessage response = await named.GetAsync($"/zones?pass={FakeHouseController.Password}");

    Assert.Equal(HouseAudioHealth.Ok, zones.Health);
    Assert.All(controller.Seen, seen => Assert.Equal(FakeHouseController.Password, seen.PasswordHeader));
    Assert.Equal(2, controller.Seen.Count);

    Assert.DoesNotContain(logs.Entries, entry => entry.Message.Contains(FakeHouseController.Password));
    Assert.DoesNotContain(logs.Entries, entry => entry.Message.Contains("/zones"));
    Assert.DoesNotContain(
      logs.Entries,
      entry => entry.Category.StartsWith($"System.Net.Http.HttpClient.{ClientName}", StringComparison.Ordinal));
  }

  [Fact]
  public void Client_UsesTheConfiguredBaseUrl_AndAFiveSecondTimeout()
  {
    using WebApplicationFactory<Program> host = Host(new RecordingHandler());

    HttpClient client = host.Services.GetRequiredService<IHttpClientFactory>().CreateClient(ClientName);

    // RadioWebFactory points the setting away from the live controller.
    Assert.Equal(new Uri("http://127.0.0.1:1"), client.BaseAddress);
    Assert.Equal(TimeSpan.FromSeconds(5), client.Timeout);
  }

  private static IConfiguration Settings(string key, string? value) => new ConfigurationBuilder()
    .AddInMemoryCollection(new Dictionary<string, string?> { [key] = value })
    .Build();

  [Theory]
  [InlineData("0", 3)]
  [InlineData("-1", 3)]
  [InlineData("soon", 3)]
  [InlineData("", 3)]
  [InlineData(null, 3)]
  [InlineData("7", 7)]
  public void PollInterval_FallsBack_WhenTheSettingIsUnusable(string? configured, int expectedSeconds)
  {
    TimeSpan interval = HouseAudioSettings.PollInterval(Settings("HouseAudio:PollSeconds", configured));

    Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), interval);
  }

  [Theory]
  [InlineData("", "http://homeaudio.local:3001/")]
  [InlineData("   ", "http://homeaudio.local:3001/")]
  [InlineData("not a url", "http://homeaudio.local:3001/")]
  [InlineData(null, "http://homeaudio.local:3001/")]
  [InlineData("http://192.168.86.49:3001", "http://192.168.86.49:3001/")]
  public void BaseUrl_FallsBack_WhenTheSettingIsUnusable(string? configured, string expected)
  {
    Uri baseUrl = HouseAudioSettings.BaseUrl(Settings("HouseAudio:BaseUrl", configured));

    Assert.Equal(new Uri(expected), baseUrl);
  }

  [Fact]
  public async Task BadSettings_DoNotThrow_WhereThePanelCreatesThePoller()
  {
    await using WebApplicationFactory<Program> host = factory.WithWebHostBuilder(builder =>
      builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
        new Dictionary<string, string?>
        {
          ["HouseAudio:PollSeconds"] = "0",
          ["HouseAudio:BaseUrl"] = ""
        })));
    using IServiceScope circuit = host.Services.CreateScope();

    await using HouseAudioPoller poller = circuit.ServiceProvider.GetRequiredService<Func<HouseAudioPoller>>()();

    // Start is where a zero interval would throw. No password is stored, so this sends nothing.
    poller.Start();
  }

  [Fact]
  public void PasswordStore_IsOneInstance_AcrossCircuits()
  {
    using WebApplicationFactory<Program> host = Host(new RecordingHandler());
    using IServiceScope first = host.Services.CreateScope();
    using IServiceScope second = host.Services.CreateScope();

    Assert.Same(
      first.ServiceProvider.GetRequiredService<HouseAudioPasswordStore>(),
      second.ServiceProvider.GetRequiredService<HouseAudioPasswordStore>());
  }

  [Fact]
  public async Task EachPanelGetsItsOwnPoller_EvenInsideOneCircuit()
  {
    using WebApplicationFactory<Program> host = Host(new RecordingHandler());
    using IServiceScope circuit = host.Services.CreateScope();
    Func<HouseAudioPoller> createPoller = circuit.ServiceProvider.GetRequiredService<Func<HouseAudioPoller>>();

    await using HouseAudioPoller first = createPoller();
    await using HouseAudioPoller second = createPoller();

    Assert.NotSame(first, second);
  }

  [Fact]
  public void PollerIsNotRegisteredDirectly_SoTheContainerCannotKeepOneForTheLifeOfACircuit()
  {
    using WebApplicationFactory<Program> host = Host(new RecordingHandler());

    Assert.Null(host.Services.GetService<HouseAudioPoller>());
  }
}
