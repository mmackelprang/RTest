using Bunit;
using Radio.Web.Models;
using Radio.Web.Components.Pages;

namespace Radio.Web.Tests.Components;

public class MessageBubbleTests : TestContext
{
  private SmsMessageDto Msg(string direction = "Inbound", string? text = "hi") =>
    new("m1", "t1", direction, "+15551234567", text, DateTime.UtcNow, false);

  [Fact]
  public void Inbound_AlignsLeft()
  {
    var cut = RenderComponent<MessageBubble>(p => p.Add(x => x.Message, Msg("Inbound")));
    Assert.Contains("inbound", cut.Find(".msg-bubble").ClassList);
  }

  [Fact]
  public void Outbound_AlignsRight()
  {
    var cut = RenderComponent<MessageBubble>(p => p.Add(x => x.Message, Msg("Outbound")));
    Assert.Contains("outbound", cut.Find(".msg-bubble").ClassList);
  }

  [Fact]
  public void UnknownDirection_TreatedAsInbound()
  {
    var cut = RenderComponent<MessageBubble>(p => p.Add(x => x.Message, Msg("garbage")));
    Assert.Contains("inbound", cut.Find(".msg-bubble").ClassList);
  }

  [Fact]
  public void NullText_RendersPlaceholder()
  {
    var cut = RenderComponent<MessageBubble>(p => p.Add(x => x.Message, Msg("Inbound", null)));
    Assert.Contains("(no text)", cut.Markup);
  }

  [Fact]
  public void Sending_ShowsDimAndSpinner()
  {
    var cut = RenderComponent<MessageBubble>(p => p
      .Add(x => x.Message, Msg("Outbound"))
      .Add(x => x.Status, MessageBubble.SendStatus.Sending));
    Assert.Contains("sending", cut.Find(".msg-bubble").ClassList);
  }

  [Fact]
  public void Failed_ShowsRetryAffordance()
  {
    var cut = RenderComponent<MessageBubble>(p => p
      .Add(x => x.Message, Msg("Outbound"))
      .Add(x => x.Status, MessageBubble.SendStatus.Failed));
    Assert.Contains("failed", cut.Find(".msg-bubble").ClassList);
    Assert.Contains("Failed to send", cut.Markup);
  }
}
