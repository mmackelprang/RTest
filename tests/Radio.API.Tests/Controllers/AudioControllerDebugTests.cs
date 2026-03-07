using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Radio.API.Controllers;
using Radio.API.Models;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;
using Xunit;

namespace Radio.API.Tests.Controllers;

  public class AudioControllerDebugTests
  {
      private readonly Mock<ILogger<AudioController>> _loggerMock;
      private readonly Mock<IAudioEngine> _audioEngineMock;
      private readonly Mock<IDuckingService> _duckingServiceMock;
      private readonly Mock<IMasterMixer> _mixerMock;
      private readonly AudioController _controller;

      public AudioControllerDebugTests()
      {
          _loggerMock = new Mock<ILogger<AudioController>>();
          _audioEngineMock = new Mock<IAudioEngine>();
          _duckingServiceMock = new Mock<IDuckingService>();
          _mixerMock = new Mock<IMasterMixer>();

          _audioEngineMock.Setup(x => x.GetMasterMixer()).Returns(_mixerMock.Object);
          
          _controller = new AudioController(_loggerMock.Object, _audioEngineMock.Object, _duckingServiceMock.Object);
      }

      [Fact]
      public async Task Next_WithNoActivePrimarySource_ReturnsBadRequest()
      {
          // Arrange
          _mixerMock.Setup(x => x.GetActiveSources()).Returns(new List<IAudioSource>());

          // Act
          var result = await _controller.Next();

          // Assert
          var actionResult = Assert.IsType<ActionResult<PlaybackStateDto>>(result);
          var badRequest = Assert.IsType<BadRequestObjectResult>(actionResult.Result);
          Assert.Contains("No primary audio source is active", badRequest.Value!.ToString());
      }

      [Fact]
      public async Task Next_WithActiveSourceNotSupportingNext_ReturnsBadRequest()
      {
          // Arrange
          var sourceMock = new Mock<IPrimaryAudioSource>();
          sourceMock.Setup(s => s.Category).Returns(AudioSourceCategory.Primary);
          sourceMock.Setup(s => s.SupportsNext).Returns(false);
          
          _mixerMock.Setup(x => x.GetActiveSources()).Returns(new List<IAudioSource> { sourceMock.Object });

          // Act
          var result = await _controller.Next();

          // Assert
          var actionResult = Assert.IsType<ActionResult<PlaybackStateDto>>(result);
          var badRequest = Assert.IsType<BadRequestObjectResult>(actionResult.Result);
          Assert.Contains("does not support next", badRequest.Value!.ToString());
      }

      [Fact]
      public async Task Next_WithValidSource_CallsNextAsync()
      {
          // Arrange
          var sourceMock = new Mock<IPrimaryAudioSource>();
          sourceMock.Setup(s => s.Category).Returns(AudioSourceCategory.Primary);
          sourceMock.Setup(s => s.SupportsNext).Returns(true);
          sourceMock.Setup(s => s.Metadata).Returns(new Dictionary<string, object>());
          
          _mixerMock.Setup(x => x.GetActiveSources()).Returns(new List<IAudioSource> { sourceMock.Object });

          // Act
          var result = await _controller.Next();

          // Assert
          sourceMock.Verify(s => s.NextAsync(It.IsAny<CancellationToken>()), Times.Once);
          
          // Check that it calls GetPlaybackState (which returns OK)
          var actionResult = Assert.IsType<ActionResult<PlaybackStateDto>>(result);
          // Since we mocked everything, GetPlaybackState inside Next() will probably return Ok with DTO
          var objectResult = Assert.IsType<OkObjectResult>(actionResult.Result);
          Assert.Equal(200, objectResult.StatusCode);
      }
  }
