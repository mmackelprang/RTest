using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Radio.API.Controllers;
using Radio.Core.Configuration;

namespace Radio.API.Tests.Controllers;

/// <summary>
/// Covers ENC-8's provisioning endpoints.
///
/// <para>
/// These are driven directly rather than through the test host, because the interesting cases are
/// the three "nothing is there" shapes the page renders differently — the subsystem is not
/// registered, the device is not connected, and the index is off the face — and none of them is a
/// 500.
/// </para>
/// </summary>
public class IntegrationsControllerEncoderTests
{
  private static IntegrationsController BuildController(IRotaryEncoderProvisioning? provisioning)
  {
    var services = new ServiceCollection();
    if (provisioning is not null)
    {
      services.AddSingleton(provisioning);
    }

    return new IntegrationsController(
      NullLogger<IntegrationsController>.Instance,
      services.BuildServiceProvider());
  }

  [Fact]
  public void GetEncoderProvisioning_WithNoEncoderSubsystem_ReturnsAnEmptySnapshotRatherThan500()
  {
    IntegrationsController controller = BuildController(provisioning: null);

    var ok = Assert.IsType<OkObjectResult>(controller.GetEncoderProvisioning());
    var snapshot = Assert.IsType<RotaryEncoderProvisioningSnapshot>(ok.Value);

    Assert.False(snapshot.Enabled);
    Assert.False(snapshot.IsConnected);
    Assert.Equal(RotaryEncoderConfigStatus.Unknown, snapshot.Status);
    // Never a ✓ by omission: no fields means no comparison, not agreement.
    Assert.Empty(snapshot.Fields);
    Assert.Equal(RotaryEncoderFlashState.NeverSaved, snapshot.Flash);
  }

  [Fact]
  public async Task Reapply_WhenTheDeviceIsNotConnected_Returns409NotAn500()
  {
    // Nothing failed — the hardware is simply not there, and the page renders that differently.
    var provisioning = new Mock<IRotaryEncoderProvisioning>();
    provisioning
      .Setup(p => p.ReapplyAsync(It.IsAny<CancellationToken>()))
      .ThrowsAsync(new InvalidOperationException("The encoder is not connected."));

    IntegrationsController controller = BuildController(provisioning.Object);

    IActionResult result = await controller.ReapplyEncoderConfig(CancellationToken.None);

    Assert.IsType<ConflictObjectResult>(result);
  }

  [Fact]
  public async Task Reapply_WithNoEncoderSubsystem_Returns409()
  {
    IntegrationsController controller = BuildController(provisioning: null);

    Assert.IsType<ConflictObjectResult>(await controller.ReapplyEncoderConfig(CancellationToken.None));
  }

  [Fact]
  public async Task SetReverse_WithAnIndexOffTheFace_Returns400()
  {
    var provisioning = new Mock<IRotaryEncoderProvisioning>(MockBehavior.Strict);
    IntegrationsController controller = BuildController(provisioning.Object);

    IActionResult tooHigh = await controller.SetEncoderReverse(
      RotaryEncoderDeviceConfig.EncoderCount, new SetEncoderReverseRequest(true), CancellationToken.None);
    IActionResult negative = await controller.SetEncoderReverse(
      -1, new SetEncoderReverseRequest(true), CancellationToken.None);

    Assert.IsType<BadRequestObjectResult>(tooHigh);
    Assert.IsType<BadRequestObjectResult>(negative);
    // Strict mock: rejecting the index must not reach the device at all.
    provisioning.VerifyNoOtherCalls();
  }

  [Fact]
  public async Task SetReverse_WithAValidIndex_CallsTheProvisioningServiceOnce()
  {
    var provisioning = new Mock<IRotaryEncoderProvisioning>();
    provisioning
      .Setup(p => p.SetReverseAsync(2, true, It.IsAny<CancellationToken>()))
      .ReturnsAsync(new RotaryEncoderProvisioningSnapshot { Enabled = true, IsConnected = true });

    IntegrationsController controller = BuildController(provisioning.Object);

    IActionResult result = await controller.SetEncoderReverse(
      2, new SetEncoderReverseRequest(true), CancellationToken.None);

    Assert.IsType<OkObjectResult>(result);
    provisioning.Verify(p => p.SetReverseAsync(2, true, It.IsAny<CancellationToken>()), Times.Once);
  }

  [Fact]
  public async Task ResetCounters_WhenTheDeviceIsNotConnected_Returns409()
  {
    var provisioning = new Mock<IRotaryEncoderProvisioning>();
    provisioning
      .Setup(p => p.ResetCountersAsync(It.IsAny<CancellationToken>()))
      .ReturnsAsync(false);

    IntegrationsController controller = BuildController(provisioning.Object);

    Assert.IsType<ConflictObjectResult>(await controller.ResetEncoderCounters(CancellationToken.None));
  }

  [Fact]
  public void GetEncoderMapping_WithNoRouter_ReturnsAnEmptyListRatherThan500()
  {
    IntegrationsController controller = BuildController(provisioning: null);

    var ok = Assert.IsType<OkObjectResult>(controller.GetEncoderMapping());

    Assert.Empty(Assert.IsAssignableFrom<System.Collections.IEnumerable>(ok.Value!).Cast<object>());
  }
}
