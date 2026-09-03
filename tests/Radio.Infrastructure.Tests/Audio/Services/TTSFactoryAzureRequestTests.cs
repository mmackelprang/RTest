using Radio.Infrastructure.Audio.Services;

namespace Radio.Infrastructure.Tests.Audio.Services;

/// <summary>
/// Pins the wire format of the Azure speech synthesis request — headers, URI and SSML — so a
/// regression is caught without a network call.
/// </summary>
/// <remarks>
/// Microsoft documents <c>User-Agent</c> as a required header for
/// <c>/cognitiveservices/v1</c> but not for <c>voices/list</c>, and <see cref="HttpClient"/>
/// sends none by default. That asymmetry is why the same subscription key could list voices
/// while synthesis was rejected, so the header has its own assertion here.
/// </remarks>
public class TTSFactoryAzureRequestTests
{
  private const string Region = "eastus";
  private const string ApiKey = "test-key";
  private const string Voice = "en-US-AriaNeural";

  private static HttpRequestMessage CreateRequest(
    string text = "Hello", float speed = 1.0f, float pitch = 1.0f) =>
    TTSFactory.CreateAzureSynthesisRequest(Region, ApiKey, Voice, text, speed, pitch);

  [Fact]
  public void Request_PostsToTheRegionalSynthesisEndpoint()
  {
    using var request = CreateRequest();

    Assert.Equal(HttpMethod.Post, request.Method);
    Assert.Equal(
      "https://eastus.tts.speech.microsoft.com/cognitiveservices/v1",
      request.RequestUri!.ToString());
  }

  [Fact]
  public void Request_SendsAUserAgent()
  {
    using var request = CreateRequest();

    Assert.True(
      request.Headers.Contains("User-Agent"),
      "Azure documents User-Agent as required for /cognitiveservices/v1.");
    Assert.Equal(
      TTSFactory.AzureUserAgent,
      Assert.Single(request.Headers.GetValues("User-Agent")));
  }

  [Fact]
  public void Request_SendsTheSubscriptionKeyAndOutputFormat()
  {
    using var request = CreateRequest();

    Assert.Equal(ApiKey, Assert.Single(request.Headers.GetValues("Ocp-Apim-Subscription-Key")));
    Assert.Equal(
      TTSFactory.AzureOutputFormat,
      Assert.Single(request.Headers.GetValues("X-Microsoft-OutputFormat")));
  }

  [Fact]
  public void Request_SendsSsmlAsUtf8()
  {
    using var request = CreateRequest();

    Assert.Equal("application/ssml+xml", request.Content!.Headers.ContentType!.MediaType);
    Assert.Equal("utf-8", request.Content.Headers.ContentType.CharSet);
  }

  [Fact]
  public void Ssml_AtDefaultSpeedAndPitch_UsesZeroPercent()
  {
    // Azure accepts a percentage with an optional leading "+", so "0%" is the no-change value.
    var ssml = TTSFactory.BuildAzureSsml(Voice, "Hello", 1.0f, 1.0f);

    Assert.Contains("rate='0%'", ssml);
    Assert.Contains("pitch='0%'", ssml);
  }

  [Theory]
  [InlineData(1.5f, "+50%")]
  [InlineData(0.5f, "-50%")]
  public void Ssml_SignsRelativeRates(float speed, string expected)
  {
    var ssml = TTSFactory.BuildAzureSsml(Voice, "Hello", speed, 1.0f);

    Assert.Contains($"rate='{expected}'", ssml);
  }

  [Theory]
  [InlineData(1.2f, "+20%")]
  [InlineData(0.8f, "-20%")]
  public void Ssml_SignsRelativePitches(float pitch, string expected)
  {
    var ssml = TTSFactory.BuildAzureSsml(Voice, "Hello", 1.0f, pitch);

    Assert.Contains($"pitch='{expected}'", ssml);
  }

  [Fact]
  public void Ssml_NamesTheVoiceAndDeclaresTheNamespace()
  {
    var ssml = TTSFactory.BuildAzureSsml(Voice, "Hello", 1.0f, 1.0f);

    Assert.Contains("xmlns='http://www.w3.org/2001/10/synthesis'", ssml);
    Assert.Contains($"<voice name='{Voice}'>", ssml);
  }

  [Fact]
  public void Ssml_EscapesMarkupInTheSpokenText()
  {
    var ssml = TTSFactory.BuildAzureSsml(Voice, "Fish & <chips>", 1.0f, 1.0f);

    Assert.Contains("Fish &amp; &lt;chips&gt;", ssml);
    Assert.DoesNotContain("<chips>", ssml);
  }

  [Fact]
  public void Ssml_IsWellFormedXml()
  {
    var ssml = TTSFactory.BuildAzureSsml(Voice, "Fish & <chips>", 1.0f, 1.0f);

    var document = System.Xml.Linq.XDocument.Parse(ssml);
    Assert.Equal("speak", document.Root!.Name.LocalName);
  }

  /// <summary>
  /// The voice id is caller-supplied and reaches this document from the unauthenticated
  /// <c>POST /api/sources/events/tts</c> route, so it must not be able to close the
  /// single-quoted <c>name</c> attribute and append SSML of the attacker's choosing.
  /// </summary>
  [Fact]
  public void Ssml_EscapesMarkupInTheVoiceId()
  {
    // Closes the name attribute and the voice element, then opens an attacker-chosen one.
    var hostileVoice = "en-US-JennyNeural'></voice><voice name='en-US-GuyNeural";

    var ssml = TTSFactory.BuildAzureSsml(hostileVoice, "Hello", 1.0f, 1.0f);

    // The breakout characters survive only in escaped form.
    Assert.DoesNotContain("</voice><voice", ssml);
    Assert.Contains("&apos;&gt;&lt;/voice&gt;", ssml);

    // And the document still has exactly one voice element, carrying the whole hostile
    // string as a single literal name rather than as markup.
    var document = System.Xml.Linq.XDocument.Parse(ssml);
    System.Xml.Linq.XNamespace ns = "http://www.w3.org/2001/10/synthesis";
    var voices = document.Root!.Elements(ns + "voice").ToList();
    Assert.Single(voices);
    Assert.Equal(hostileVoice, voices[0].Attribute("name")!.Value);
  }
}
