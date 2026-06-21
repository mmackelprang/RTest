using Bunit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Radzen;
using Radio.Web.Components.Pages;
using Radio.Web.Models;
using Radio.Web.Services.ApiClients;
using Radio.Web.Tests.TestHelpers;
using Xunit;

namespace Radio.Web.Tests.Components;

public class VoicemailPlayerTests : TestContext
{
  private VoicemailItemDto Vm(int duration = 42, string? transcript = "hi",
    DateTime? received = null) =>
    new("vm1", "t1", "+15551234567", "Jane",
      received ?? DateTime.UtcNow, duration, false, transcript,
      "/api/gvbridge/voicemail/vm1/audio");

  private void Register()
  {
    JSInterop.Mode = JSRuntimeMode.Loose;  // MudBlazor/JS-interop components
    Services.AddRadzenComponents();
    var client = new HttpClient(new MockHttpHandler("{}"))
    { BaseAddress = new Uri("http://radio:5004") };
    var config = new ConfigurationBuilder().Build();
    Services.AddSingleton(new GvBridgeApiService(client,
      NullLogger<GvBridgeApiService>.Instance, config));
  }

  [Fact]
  public void Renders_AbsoluteAudioSrc()
  {
    Register();
    var cut = RenderComponent<VoicemailPlayer>(p => p.Add(x => x.Item, Vm()));
    var audio = cut.Find("audio");
    Assert.Equal("http://radio:5004/api/gvbridge/voicemail/vm1/audio",
      audio.GetAttribute("src"));
  }

  [Fact]
  public void TranscriptPresent_RendersBody()
  {
    Register();
    var cut = RenderComponent<VoicemailPlayer>(p => p.Add(x => x.Item, Vm(transcript: "Hello there")));
    Assert.Contains("Hello there", cut.Markup);
  }

  [Fact]
  public void TranscriptPending_WhenNullAndRecent()
  {
    Register();
    var cut = RenderComponent<VoicemailPlayer>(p =>
      p.Add(x => x.Item, Vm(transcript: null, received: DateTime.UtcNow)));
    Assert.Contains("Transcript pending", cut.Markup);
  }

  [Fact]
  public void TranscriptAbsent_WhenNullAndOld()
  {
    Register();
    var cut = RenderComponent<VoicemailPlayer>(p =>
      p.Add(x => x.Item, Vm(transcript: null, received: DateTime.UtcNow.AddHours(-2))));
    Assert.Contains("No transcript available", cut.Markup);
  }

  [Fact]
  public void Duration_RendersEmDash_WhenZero()
  {
    Register();
    var cut = RenderComponent<VoicemailPlayer>(p => p.Add(x => x.Item, Vm(duration: 0)));
    // total shows --:-- not 0:00 when duration unknown
    Assert.Contains("--:--", cut.Markup);
  }
}
