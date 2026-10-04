using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Radio.API.Tests.TestSupport;
using Radio.Configuration.Bridge;

namespace Radio.API.Tests.Controllers;

/// <summary>
/// PHN-13: proves the owner check #1 enable step, end to end, before anyone runs it on the box.
/// <c>POST /api/configuration/RotaryPhone {"DeclineSupported": true}</c> through the real API writes the config
/// store; Radio.Web reads that store through the same bridge (<c>Radio.Web/Program.cs</c>,
/// <c>AddSqliteConfigStore</c> after the JSON layers), so its <c>RotaryPhone:DeclineSupported</c> becomes true over
/// the shipped <c>false</c> — and the same POST with <c>false</c> undoes it.
/// </summary>
/// <remarks>
/// ⚠ The controller lowercases the section (<c>rotaryphone:DeclineSupported</c>) and Radio.Web reads
/// <c>RotaryPhone:DeclineSupported</c>; this pins that the bridge's case-insensitive keys join the two. The
/// banner reads the flag on every use (PHN-13), so a kiosk circuit sees it once radio-web reloads on the API's
/// <c>ConfigChanged</c> push — or after a radio-web restart if that push is missed.
/// </remarks>
public class RotaryPhoneDeclineEnableStepTests : IClassFixture<CustomWebApplicationFactory<Program>>
{
  private const string Key = "RotaryPhone:DeclineSupported";
  private readonly CustomWebApplicationFactory<Program> _factory;

  public RotaryPhoneDeclineEnableStepTests(CustomWebApplicationFactory<Program> factory) => _factory = factory;

  // Radio.Web's view of the same store: the shipped default, then the bridge on top (Program.cs order).
  private bool WebSeesDeclineSupported()
  {
    string db = Path.Combine(_factory.StorageRoot, "data", "config", "configuration.db");
    var web = new ConfigurationBuilder()
      .AddInMemoryCollection(new Dictionary<string, string?> { [Key] = "false" })   // src/Radio.Web/appsettings.json
      .AddSqliteConfigStore(db, "sqlite", new ConfigStoreChangeNotifier())
      .Build();
    return web.GetValue(Key, false);
  }

  [Fact]
  public async Task ThePostTurnsItOn_AndTheSamePostWithFalseTurnsItOff()
  {
    using var client = _factory.CreateClient();

    var on = await client.PostAsJsonAsync("/api/configuration/RotaryPhone", new { DeclineSupported = true });
    bool afterOn = WebSeesDeclineSupported();
    var off = await client.PostAsJsonAsync("/api/configuration/RotaryPhone", new { DeclineSupported = false });
    bool afterOff = WebSeesDeclineSupported();

    Assert.Equal(HttpStatusCode.OK, on.StatusCode);
    Assert.True(afterOn);
    Assert.Equal(HttpStatusCode.OK, off.StatusCode);
    Assert.False(afterOff);
  }
}
