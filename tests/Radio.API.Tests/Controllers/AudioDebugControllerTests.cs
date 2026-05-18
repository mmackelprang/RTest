namespace Radio.API.Tests.Controllers;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Radio.API.Controllers;
using Xunit;

/// <summary>
/// Tests for the AudioDebugController stub (PR D #23). The endpoint currently
/// returns 501 — when the SoundFlow tap exposes a retained buffer, replace
/// the stub with a real WAV serializer and update these tests.
/// </summary>
public class AudioDebugControllerTests
{
  [Fact]
  public void DumpAudioFrame_ReturnsNotImplemented()
  {
    var controller = new AudioDebugController(NullLogger<AudioDebugController>.Instance);

    var result = controller.DumpAudioFrame();

    var objectResult = Assert.IsType<ObjectResult>(result);
    Assert.Equal(StatusCodes.Status501NotImplemented, objectResult.StatusCode);
  }

  [Fact]
  public void DumpAudioFrame_BodyExplainsStubAndTracking()
  {
    var controller = new AudioDebugController(NullLogger<AudioDebugController>.Instance);

    var result = controller.DumpAudioFrame();

    var objectResult = Assert.IsType<ObjectResult>(result);
    var body = objectResult.Value;
    Assert.NotNull(body);
    // Body shape: { error, reason, tracked }
    var json = System.Text.Json.JsonSerializer.Serialize(body);
    Assert.Contains("tracked", json);
    Assert.Contains("#23", json);
  }
}
