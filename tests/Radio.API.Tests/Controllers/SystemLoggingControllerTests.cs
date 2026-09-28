using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Radio.API.Controllers;
using Radio.API.Logging;
using Radio.API.Tests.TestSupport;
using Serilog.Events;
using Xunit;

namespace Radio.API.Tests.Controllers;

/// <summary>LOG-5: the runtime log-level endpoint.</summary>
public class SystemLoggingControllerTests : IClassFixture<CustomWebApplicationFactory<Program>>
{
  private readonly CustomWebApplicationFactory<Program> _factory;

  public SystemLoggingControllerTests(CustomWebApplicationFactory<Program> factory)
  {
    _factory = factory;
  }

  private static SystemLoggingController Controller(out LogLevelSwitches switches)
  {
    var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
      ["Serilog:MinimumLevel:Default"] = "Warning",
      ["Serilog:MinimumLevel:Override:Radio"] = "Information",
    }).Build();
    switches = LogLevelSwitches.FromConfiguration(config);
    return new SystemLoggingController(switches, NullLogger<SystemLoggingController>.Instance);
  }

  [Fact]
  public void SetLevel_KnownSource_AppliesAndReturnsNewState()
  {
    var controller = Controller(out var switches);

    var result = controller.SetLevel("Radio", new SetLogLevelRequest("debug"));

    var ok = Assert.IsType<OkObjectResult>(result.Result);
    var state = Assert.IsType<LogLevelState>(ok.Value);
    Assert.Equal(LogEventLevel.Debug, state.Level);
    Assert.Equal(LogEventLevel.Information, state.ConfiguredLevel);
    Assert.Equal(LogEventLevel.Debug, switches.Get("Radio")!.Level);
  }

  [Fact]
  public void SetLevel_UnknownSource_Returns404AndDoesNotEchoIt()
  {
    var controller = Controller(out _);

    var result = controller.SetLevel("Radio.Secret.555-1212", new SetLogLevelRequest("Debug"));

    var notFound = Assert.IsType<NotFoundObjectResult>(result.Result);
    Assert.DoesNotContain("555-1212", JsonSerializer.Serialize(notFound.Value));
  }

  [Theory]
  [InlineData("Loud")]
  [InlineData("2")]
  [InlineData(null)]
  public void SetLevel_InvalidLevel_Returns400AndChangesNothing(string? level)
  {
    var controller = Controller(out var switches);

    var result = controller.SetLevel("Radio", new SetLogLevelRequest(level));

    Assert.IsType<BadRequestObjectResult>(result.Result);
    Assert.Equal(LogEventLevel.Information, switches.Get("Radio")!.Level);
  }

  [Theory]
  [InlineData("Error")]
  [InlineData("Fatal")]
  public void SetLevel_AboveWarning_Returns400(string level)
  {
    // An unauthenticated caller must not be able to silence warnings and errors (and this
    // endpoint's own audit line) until the next restart.
    var controller = Controller(out var switches);

    var result = controller.SetLevel("Radio", new SetLogLevelRequest(level));

    Assert.IsType<BadRequestObjectResult>(result.Result);
    Assert.Equal(LogEventLevel.Information, switches.Get("Radio")!.Level);
  }

  [Fact]
  public void SetLevel_AboveWarning_IsAllowedWhenItIsTheConfiguredLevel()
  {
    var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
      ["Serilog:MinimumLevel:Override:Noisy"] = "Error",
    }).Build();
    var switches = LogLevelSwitches.FromConfiguration(config);
    var controller = new SystemLoggingController(switches, NullLogger<SystemLoggingController>.Instance);
    switches.TrySet("Noisy", LogEventLevel.Debug);

    var result = controller.SetLevel("Noisy", new SetLogLevelRequest("Error"));

    Assert.IsType<OkObjectResult>(result.Result);
    Assert.Equal(LogEventLevel.Error, switches.Get("Noisy")!.Level);
  }

  [Fact]
  public void ResetLevels_RestoresConfiguration()
  {
    var controller = Controller(out var switches);
    switches.TrySet("Radio", LogEventLevel.Verbose);
    switches.TrySet("Default", LogEventLevel.Verbose);

    controller.ResetLevels();

    Assert.All(switches.GetAll(), s => Assert.Equal(s.ConfiguredLevel, s.Level));
  }

  [Fact]
  public async Task Endpoint_IsWiredEndToEnd_AgainstTheHostsOwnSwitches()
  {
    // Proves the DI registration and routing, and that PUT mutates the LogLevelSwitches singleton the
    // host registered. It does not prove the host's logger emits differently: Log.Logger is
    // process-global and the factory replaces it. That half is LogLevelSwitchesTests' job, which
    // drives the same ApiLoggerConfiguration.Build that Program.cs calls.
    var client = _factory.CreateClient();

    var levels = await client.GetFromJsonAsync<List<JsonElement>>("/api/system/logging/levels");
    Assert.NotNull(levels);
    Assert.Equal("Default", levels![0].GetProperty("source").GetString());
    Assert.Contains(levels, l => l.GetProperty("source").GetString() == "Radio");

    try
    {
      var put = await client.PutAsJsonAsync("/api/system/logging/levels/Radio", new { level = "Debug" });
      Assert.Equal(HttpStatusCode.OK, put.StatusCode);

      var switches = _factory.Services.GetRequiredService<LogLevelSwitches>();
      Assert.Equal(LogEventLevel.Debug, switches.Get("Radio")!.Level);

      var missing = await client.PutAsJsonAsync("/api/system/logging/levels/Nope", new { level = "Debug" });
      Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }
    finally
    {
      var reset = await client.PostAsync("/api/system/logging/levels/reset", null);
      Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
    }
  }
}
