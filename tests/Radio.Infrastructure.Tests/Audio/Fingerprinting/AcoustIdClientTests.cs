using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using Radio.Core.Configuration;
using Radio.Infrastructure.Audio.Fingerprinting;
using Xunit;

namespace Radio.Infrastructure.Tests.Audio.Fingerprinting
{
    public class AcoustIdClientTests
    {
        private readonly Mock<HttpMessageHandler> _handlerMock;
        private readonly Mock<ILogger<AcoustIdClient>> _loggerMock;
        private readonly IOptionsMonitor<FingerprintingOptions> _optionsMonitor;

        public AcoustIdClientTests()
        {
            _handlerMock = new Mock<HttpMessageHandler>();
            _loggerMock = new Mock<ILogger<AcoustIdClient>>();
            var fingerprintingOptions = new FingerprintingOptions
            {
                AcoustId = new AcoustIdOptions
                {
                    ApiKey = "test_api_key",
                    BaseUrl = "https://api.acoustid.org/v2/"
                }
            };
            var monitorMock = new Mock<IOptionsMonitor<FingerprintingOptions>>();
            monitorMock.Setup(m => m.CurrentValue).Returns(fingerprintingOptions);
            _optionsMonitor = monitorMock.Object;
        }

        [Fact]
        public async Task LookupAsync_WithValidResponse_ReturnsResult()
        {
            // Arrange
            var responseJson = "{\"status\": \"ok\", \"results\": [{\"id\": \"track_id\", \"score\": 0.9, \"recordings\": [{\"title\": \"Test Title\", \"artists\": [{\"name\": \"Test Artist\"}]}]}]}";
            
            _handlerMock.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>()
                )
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent(responseJson)
                });

            var httpClient = new HttpClient(_handlerMock.Object);
            using var client = new AcoustIdClient(httpClient, _loggerMock.Object, _optionsMonitor);

            // Act
            var result = await client.LookupAsync("valid_fingerprint", 120);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("track_id", result.Id);
            Assert.Equal(0.9, result.Score);
            Assert.NotEmpty(result.Recordings);
            Assert.Equal("Test Title", result.Recordings[0].Title);
            Assert.Equal("Test Artist", result.Recordings[0].Artists[0]);
        }

        [Fact]
        public async Task LookupAsync_SendsCorrectParameters()
        {
            // Arrange
            HttpRequestMessage? capturedRequest = null;
            string? capturedContent = null;

            _handlerMock.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>()
                )
                .Callback<HttpRequestMessage, CancellationToken>(async (req, ct) => 
                {
                    capturedRequest = req;
                    if (req.Content != null)
                    {
                        capturedContent = await req.Content.ReadAsStringAsync(ct);
                    }
                })
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent("{\"status\": \"ok\"}")
                });

            var httpClient = new HttpClient(_handlerMock.Object);
            using var client = new AcoustIdClient(httpClient, _loggerMock.Object, _optionsMonitor);

            // Act
            await client.LookupAsync("test_fingerprint", 60);

            // Assert
            Assert.NotNull(capturedRequest);
            Assert.Equal(HttpMethod.Post, capturedRequest!.Method);
            Assert.Equal("https://api.acoustid.org/v2/lookup", capturedRequest.RequestUri!.ToString());
            
            Assert.NotNull(capturedContent);
            Assert.Contains("client=test_api_key", capturedContent);
            Assert.Contains("meta=recordings", capturedContent);
            Assert.Contains("duration=60", capturedContent);
            Assert.Contains("fingerprint=test_fingerprint", capturedContent);
        }
    }
}
