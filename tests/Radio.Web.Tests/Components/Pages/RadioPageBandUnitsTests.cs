using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Radzen;
using Radio.Web.Components.Pages;
using Radio.Web.Models;
using Radio.Web.Services.ApiClients;
using Radio.Web.Services.Hub;
using Radio.Web.Tests.TestHelpers;
using Xunit;

namespace Radio.Web.Tests.Components.Pages;

/// <summary>
/// AUD-102 on the Radio page: SW in kHz in the readout and the frequency dialog, and the step list
/// built from the band's allowed steps.
/// </summary>
public class RadioPageBandUnitsTests : TestContext
{
  public RadioPageBandUnitsTests()
  {
    Services.AddHermeticTestRig();
    JSInterop.Mode = JSRuntimeMode.Loose;

    var configuration = new ConfigurationBuilder()
      .AddInMemoryCollection(new Dictionary<string, string?> { { "ApiBaseUrl", HermeticTestRig.ApiBaseUrl } })
      .Build();

    Services.AddSingleton<IConfiguration>(configuration);
    Services.AddSingleton<ILoggerFactory>(new NullLoggerFactory());
    Services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
    Services.AddRadzenComponents();
    Services.AddHttpClient<ConfigurationApiService>();
    Services.AddSingleton(sp => new AudioStateHubService(
      NullLogger<AudioStateHubService>.Instance, sp.GetRequiredService<IConfiguration>(), transport: new OfflineHubTransport()));
  }

  private static RadioStateDto State(string band, double hz, double step) => new(
    Frequency: hz,
    Band: band,
    Step: step,
    SignalStrength: 50,
    IsScanning: false,
    ScanDirection: null,
    ScanStopThreshold: -36.0,
    Gain: 0,
    AutoGain: false,
    Equalizer: "Normal",
    DeviceVolume: 50);

  private static Radio.Core.Models.RadioBandModel Band(string type, long min, long max, long defaultStep, params long[] steps) => new()
  {
    Type = type,
    Name = type,
    MinFrequencyHz = min,
    MaxFrequencyHz = max,
    DefaultStepHz = defaultStep,
    AllowedStepSizes = steps,
    DefaultModulation = type == "FM" ? "WFM" : "AM",
    Range = "",
    BandPresetCapacity = 16,
  };

  private static readonly Radio.Core.Models.RadioBandModel[] Bands =
  {
    Band("FM", 87_500_000, 108_000_000, 100_000, 50_000, 100_000, 200_000),
    Band("AM", 530_000, 1_710_000, 10_000, 1_000, 9_000, 10_000),
    Band("SW", 1_711_000, 30_000_000, 5_000, 1_000, 2_500, 5_000),
  };

  private StubHandler Use(RadioStateDto state)
  {
    var handler = new StubHandler(state, Bands);
    Services.AddSingleton(_ => new RadioApiService(
      new HttpClient(handler) { BaseAddress = new Uri(HermeticTestRig.ApiBaseUrl) }, NullLogger<RadioApiService>.Instance));
    return handler;
  }

  private IRenderedComponent<RadioPage> Render(RadioStateDto state)
  {
    Use(state);
    var cut = RenderComponent<RadioPage>();
    cut.WaitForAssertion(() => cut.FindAll(".display-frequency").Should().ContainSingle(), TimeSpan.FromSeconds(2));
    return cut;
  }

  [Theory]
  [InlineData("SW", 5_905_000d, "5905 kHz")]
  [InlineData("SW", 5_900_000d, "5900 kHz")]
  [InlineData("SW", 5_910_000d, "5910 kHz")]
  [InlineData("AM", 1_010_000d, "1010 kHz")]
  [InlineData("FM", 101_500_000d, "101.50 MHz")] // unchanged
  public void Readout_SwAndAmInKhz_FmUnchanged(string band, double hz, string expected)
  {
    var cut = Render(State(band, hz, 5_000));

    cut.Find(".display-frequency").TextContent.Trim().Should().Be(expected);
  }

  [Fact]
  public void StepList_IsTheBandsAllowedSteps()
  {
    var cut = Render(State("SW", 5_905_000, 5_000));
    var flags = BindingFlags.NonPublic | BindingFlags.Instance;

    var options = (System.Collections.IEnumerable)typeof(RadioPage).GetProperty("StepSizeOptions", flags)!.GetValue(cut.Instance)!;
    var labels = options.Cast<object>().Select(o => (string)o.GetType().GetProperty("Label")!.GetValue(o)!).ToList();
    var values = options.Cast<object>().Select(o => (string)o.GetType().GetProperty("Value")!.GetValue(o)!).ToList();

    labels.Should().Equal("1 kHz", "2.5 kHz", "5 kHz");
    values.Should().Equal("1000", "2500", "5000");
  }

  [Fact]
  public async Task FrequencyDialog_OnSw_IsInKhz_AndSendsHertz()
  {
    var handler = Use(State("SW", 5_905_000, 5_000));
    var cut = RenderComponent<RadioPage>();
    cut.WaitForAssertion(() => cut.FindAll(".display-frequency").Should().ContainSingle(), TimeSpan.FromSeconds(2));
    var flags = BindingFlags.NonPublic | BindingFlags.Instance;

    cut.Find(".display-frequency").Click();

    cut.Markup.Should().Contain("Frequency (kHz)");
    ((double)typeof(RadioPage).GetField("_newFrequency", flags)!.GetValue(cut.Instance)!).Should().Be(5905);

    typeof(RadioPage).GetField("_newFrequency", flags)!.SetValue(cut.Instance, 9600d);
    await cut.InvokeAsync(() => (Task)typeof(RadioPage).GetMethod("SetFrequencyAsync", flags)!.Invoke(cut.Instance, null)!);

    handler.Posts("/api/radio/frequency").Should().ContainSingle().Which.Should().Contain("9600000");
  }

  private sealed class StubHandler : HttpMessageHandler
  {
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    private readonly RadioStateDto _state;
    private readonly IEnumerable<Radio.Core.Models.RadioBandModel> _bands;
    private readonly List<(string Path, string? Body)> _posts = new();

    public StubHandler(RadioStateDto state, IEnumerable<Radio.Core.Models.RadioBandModel> bands)
    {
      _state = state;
      _bands = bands;
    }

    public List<string?> Posts(string path)
    {
      lock (_posts)
      {
        return _posts.Where(p => p.Path == path).Select(p => p.Body).ToList();
      }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
      string path = request.RequestUri?.AbsolutePath ?? string.Empty;
      if (request.Method == HttpMethod.Post)
      {
        string? body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        lock (_posts)
        {
          _posts.Add((path, body));
        }
        return new HttpResponseMessage(HttpStatusCode.OK);
      }

      return path switch
      {
        "/api/radio/state" => Ok(_state),
        "/api/radio/presets" => Ok(Array.Empty<RadioPresetDto>()),
        "/api/RadioBands" => Ok(_bands),
        _ => new HttpResponseMessage(HttpStatusCode.NotFound),
      };
    }

    private static HttpResponseMessage Ok<T>(T payload) => new(HttpStatusCode.OK)
    {
      Content = new StringContent(JsonSerializer.Serialize(payload, Options), Encoding.UTF8, "application/json"),
    };
  }
}
