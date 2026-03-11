using Bunit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Radzen;
using Radio.Web.Components.Pages;
using Radio.Web.Models;
using Radio.Web.Services.ApiClients;
using Radio.Web.Services.Hub;

namespace Radio.Web.Tests.Components.Pages;

public class PhonePageTests : TestContext
{
  public PhonePageTests()
  {
    JSInterop.Mode = JSRuntimeMode.Loose;
    Services.AddRadzenComponents();

    // Register PhoneApiService with mock handler that returns empty/default data
    Services.AddHttpClient<PhoneApiService>(client =>
    {
      client.BaseAddress = new Uri("http://localhost:5004");
    }).ConfigurePrimaryHttpMessageHandler(() => new EmptyResponseHandler());

    // Register PhoneHubService
    var config = new ConfigurationBuilder()
      .AddInMemoryCollection(new Dictionary<string, string?>
      {
        ["RotaryPhone:HubUrl"] = "http://localhost:5004/hub"
      })
      .Build();
    Services.AddSingleton<IConfiguration>(config);
    Services.AddSingleton(new PhoneHubService(
      NullLogger<PhoneHubService>.Instance, config));
  }

  [Fact]
  public void PhonePage_Renders_WithTabs()
  {
    var cut = RenderComponent<PhonePage>();
    Assert.Contains("Dashboard", cut.Markup);
    Assert.Contains("Contacts", cut.Markup);
    Assert.Contains("Call History", cut.Markup);
  }

  [Fact]
  public void PhonePage_Renders_SystemStatusSection()
  {
    var cut = RenderComponent<PhonePage>();
    Assert.Contains("System Status", cut.Markup);
    Assert.Contains("Bluetooth", cut.Markup);
    Assert.Contains("SIP Server", cut.Markup);
    Assert.Contains("HT801 ATA", cut.Markup);
  }

  [Fact]
  public void PhonePage_Renders_PhoneStatusSection()
  {
    var cut = RenderComponent<PhonePage>();
    Assert.Contains("Phone Status", cut.Markup);
  }

  [Fact]
  public void PhonePage_Renders_DeveloperControls()
  {
    var cut = RenderComponent<PhonePage>();
    Assert.Contains("Developer Controls", cut.Markup);
  }

  private class EmptyResponseHandler : HttpMessageHandler
  {
    protected override Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request, CancellationToken cancellationToken)
    {
      var path = request.RequestUri?.PathAndQuery ?? "";
      string content = path switch
      {
        var p when p.Contains("system-status") =>
          """{"platform":"Linux","sipListening":false,"ht801Reachable":false}""",
        var p when p.Contains("/api/phone/status") =>
          """{"callState":"Idle"}""",
        var p when p.Contains("/api/contacts") => "[]",
        var p when p.Contains("/api/callhistory") => "[]",
        _ => "{}"
      };
      return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
      {
        Content = new StringContent(content, System.Text.Encoding.UTF8, "application/json")
      });
    }
  }
}
