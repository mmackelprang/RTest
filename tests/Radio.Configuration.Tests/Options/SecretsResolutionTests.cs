using Microsoft.Extensions.Options;
using Moq;
using Radio.Configuration.Abstractions;
using Radio.Configuration.Options;
using Xunit;

namespace Radio.Configuration.Tests.Options;

  public class SecretsResolutionTests
  {
      public class TestOptions
      {
          public string SecretValue { get; set; } = "";
          public string NormalValue { get; set; } = "";
          public NestedOptions Nested { get; set; } = new();
      }

      public class NestedOptions
      {
          public string NestedSecret { get; set; } = "";
      }

      [Fact]
      public void PostConfigure_ResolvesSecrets()
      {
          // Arrange
          var secretsProviderMock = new Mock<ISecretsProvider>();
          secretsProviderMock.Setup(x => x.ContainsSecretTag(It.IsAny<string>()))
              .Returns((string s) => s.StartsWith("${secret:"));
          
          secretsProviderMock.Setup(x => x.ResolveTagsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync((string s, CancellationToken ct) => s.Replace("${secret:", "").Replace("}", "") + "_resolved");

          var options = new TestOptions
          {
              SecretValue = "${secret:my_secret}",
              NormalValue = "normal",
              Nested = new NestedOptions
              {
                  NestedSecret = "${secret:nested_secret}"
              }
          };

          var postConfigure = new SecretResolvingPostConfigureOptions<TestOptions>(secretsProviderMock.Object);

          // Act
          postConfigure.PostConfigure("test", options);

          // Assert
          Assert.Equal("my_secret_resolved", options.SecretValue);
          Assert.Equal("normal", options.NormalValue);
          Assert.Equal("nested_secret_resolved", options.Nested.NestedSecret);
      }
  }
