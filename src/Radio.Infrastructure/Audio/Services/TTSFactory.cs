using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Interfaces;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Audio.Sources.Events;
using Radio.Infrastructure.Audio.SoundFlow;
using Radio.Metrics;

namespace Radio.Infrastructure.Audio.Services;

/// <summary>
/// Factory for creating TTS audio sources.
/// Supports eSpeak (offline), Google Cloud TTS, and Azure TTS engines.
/// </summary>
public class TTSFactory : ITTSFactory, IDisposable
{
  /// <summary>Audio format requested from Azure synthesis, matching <see cref="EstimateWavDuration24kHz"/>.</summary>
  internal const string AzureOutputFormat = "riff-24khz-16bit-mono-pcm";

  /// <summary>Value sent as the <c>User-Agent</c> Azure requires on synthesis requests.</summary>
  internal const string AzureUserAgent = "RadioConsole";

  private readonly ILogger<TTSFactory> _logger;
  private readonly ILogger<TTSEventSource> _ttsSourceLogger;
  private readonly IOptionsMonitor<TTSOptions> _options;
  private readonly IOptionsMonitor<TTSSecrets> _secrets;
  private readonly IMetricsCollector? _metricsCollector;
  private readonly SoundFlowPlaybackService? _playbackService;
  private readonly ITTSVoiceRepository? _voiceRepository;
  private IReadOnlyList<TTSEngineInfo>? _cachedEngines;

  /// <summary>
  /// Initializes a new instance of the <see cref="TTSFactory"/> class.
  /// </summary>
  public TTSFactory(
    ILogger<TTSFactory> logger,
    ILogger<TTSEventSource> ttsSourceLogger,
    IOptionsMonitor<TTSOptions> options,
    IOptionsMonitor<TTSSecrets> secrets,
    IMetricsCollector? metricsCollector = null,
    SoundFlowPlaybackService? playbackService = null,
    ITTSVoiceRepository? voiceRepository = null)
  {
    _logger = logger;
    _ttsSourceLogger = ttsSourceLogger;
    _options = options;
    _secrets = secrets;
    _metricsCollector = metricsCollector;
    _playbackService = playbackService;
    _voiceRepository = voiceRepository;
  }

  /// <inheritdoc/>
  public IReadOnlyList<TTSEngineInfo> AvailableEngines
  {
    get
    {
      _cachedEngines ??= DetectAvailableEngines();
      return _cachedEngines;
    }
  }

  /// <inheritdoc/>
  public async Task<IEventAudioSource> CreateAsync(
    string text,
    TTSParameters? parameters = null,
    CancellationToken cancellationToken = default)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(text);

    var stopwatch = Stopwatch.StartNew();
    var opts = _options.CurrentValue;

    // Use provided parameters or fall back to defaults
    var engine = parameters?.Engine ?? ParseEngine(opts.DefaultEngine);
    var voice = parameters?.Voice ?? opts.DefaultVoice;
    var speed = parameters?.Speed ?? opts.DefaultSpeed;
    var pitch = parameters?.Pitch ?? opts.DefaultPitch;

    var effectiveParams = new TTSParameters
    {
      Engine = engine,
      Voice = voice,
      Speed = speed,
      Pitch = pitch
    };

    _logger.LogInformation("Creating TTS audio for text: '{Text}' with engine {Engine}",
      text.Length > 50 ? text[..50] + "..." : text, engine);

    // Track TTS request metrics
    var providerTag = new Dictionary<string, string> { { "provider", engine.ToString().ToLowerInvariant() } };
    _metricsCollector?.Increment("tts.requests_total", 1.0, providerTag);
    _metricsCollector?.Increment("tts.characters_processed", text.Length);

    // Check for cached audio (simplified - TODO: implement actual cache checking)
    var cacheKey = $"{engine}_{voice}_{text.GetHashCode()}";
    var isCached = false; // In production, this would check if audio file exists in cache

    if (isCached)
    {
      _metricsCollector?.Increment("tts.cache_hits", 1.0, providerTag);
    }
    else
    {
      _metricsCollector?.Increment("tts.cache_misses", 1.0, providerTag);
    }

    // Generate audio based on engine
    var (audioStream, duration) = engine switch
    {
      TTSEngine.ESpeak => await GenerateESpeakAsync(text, voice, speed, pitch, cancellationToken),
      TTSEngine.Google => await GenerateGoogleTTSAsync(text, voice, speed, pitch, cancellationToken),
      TTSEngine.Azure => await GenerateAzureTTSAsync(text, voice, speed, pitch, cancellationToken),
      _ => throw new NotSupportedException($"TTS engine '{engine}' is not supported")
    };

    stopwatch.Stop();
    _metricsCollector?.Gauge("tts.latency_ms", stopwatch.ElapsedMilliseconds, providerTag);

    return new TTSEventSource(text, effectiveParams, audioStream, duration, _ttsSourceLogger, _playbackService);
  }

  /// <inheritdoc/>
  public async Task<IReadOnlyList<TTSVoiceInfo>> GetVoicesAsync(
    TTSEngine engine,
    CancellationToken cancellationToken = default)
  {
    if (engine == TTSEngine.ESpeak)
    {
      return await GetESpeakVoicesAsync(cancellationToken);
    }

    // For cloud engines, return cached voices from DB (sorted by favorites + price tier)
    if (_voiceRepository != null)
    {
      var cached = await _voiceRepository.GetCachedVoicesAsync(engine, cancellationToken);
      if (cached.Count > 0)
      {
        return SortVoices(cached);
      }
    }

    // No cache — return empty list (user must click "Scan for Voices")
    return Array.Empty<TTSVoiceInfo>();
  }

  /// <inheritdoc/>
  public async Task<int> RefreshVoicesAsync(
    TTSEngine engine,
    CancellationToken cancellationToken = default)
  {
    if (_voiceRepository == null)
    {
      throw new InvalidOperationException("Voice repository not available");
    }

    var voices = engine switch
    {
      TTSEngine.Google => await FetchGoogleVoicesAsync(cancellationToken),
      TTSEngine.Azure => await FetchAzureVoicesAsync(cancellationToken),
      _ => throw new NotSupportedException($"Voice refresh not supported for engine '{engine}'")
    };

    await _voiceRepository.ReplaceCachedVoicesAsync(engine, voices, cancellationToken);
    _logger.LogInformation("Refreshed {Count} voices for {Engine}", voices.Count, engine);
    return voices.Count;
  }

  /// <inheritdoc/>
  public async Task SetVoiceFavoriteAsync(
    TTSEngine engine, string voiceId, CancellationToken cancellationToken = default)
  {
    if (_voiceRepository == null)
    {
      throw new InvalidOperationException("Voice repository not available");
    }
    await _voiceRepository.AddFavoriteAsync(engine, voiceId, cancellationToken);
  }

  /// <inheritdoc/>
  public async Task RemoveVoiceFavoriteAsync(
    TTSEngine engine, string voiceId, CancellationToken cancellationToken = default)
  {
    if (_voiceRepository == null)
    {
      throw new InvalidOperationException("Voice repository not available");
    }
    await _voiceRepository.RemoveFavoriteAsync(engine, voiceId, cancellationToken);
  }

  /// <summary>
  /// Sorts voices: favorites first, then by price tier (cheap first), then by language (US > UK > other).
  /// </summary>
  private static IReadOnlyList<TTSVoiceInfo> SortVoices(IReadOnlyList<TTSVoiceInfo> voices)
  {
    return voices
      .OrderByDescending(v => v.IsFavorite)
      .ThenBy(v => GetPriceTierRank(v.PriceTier))
      .ThenBy(v => GetLanguageRank(v.Language))
      .ThenBy(v => v.Name)
      .ToList();
  }

  private static int GetPriceTierRank(string priceTier) => priceTier switch
  {
    "Standard" => 0,
    "WaveNet" => 1,
    "Neural2" => 1,
    "Neural" => 1,
    _ => 2 // Studio, Journey, Casual, Polyglot, etc.
  };

  private static int GetLanguageRank(string language)
  {
    if (language.StartsWith("en-US", StringComparison.OrdinalIgnoreCase))
    {
      return 0;
    }
    if (language.StartsWith("en-GB", StringComparison.OrdinalIgnoreCase))
    {
      return 1;
    }
    return 2;
  }

  private static TTSEngine ParseEngine(string engineName)
  {
    return Enum.TryParse<TTSEngine>(engineName, ignoreCase: true, out var engine)
      ? engine
      : TTSEngine.ESpeak;
  }

  private IReadOnlyList<TTSEngineInfo> DetectAvailableEngines()
  {
    var engines = new List<TTSEngineInfo>();
    var secrets = _secrets.CurrentValue;

    // Check eSpeak availability
    var espeakAvailable = IsESpeakAvailable();
    engines.Add(new TTSEngineInfo
    {
      Engine = TTSEngine.ESpeak,
      Name = "eSpeak-ng",
      IsAvailable = espeakAvailable,
      RequiresApiKey = false,
      IsOffline = true
    });

    // Check Google TTS availability
    var googleAvailable = !string.IsNullOrEmpty(secrets.GoogleAPIKey);
    engines.Add(new TTSEngineInfo
    {
      Engine = TTSEngine.Google,
      Name = "Google Cloud TTS",
      IsAvailable = googleAvailable,
      RequiresApiKey = true,
      IsOffline = false
    });

    // Check Azure TTS availability
    var azureAvailable = !string.IsNullOrEmpty(secrets.AzureAPIKey) &&
                         !string.IsNullOrEmpty(secrets.AzureRegion);
    engines.Add(new TTSEngineInfo
    {
      Engine = TTSEngine.Azure,
      Name = "Azure Cognitive Services Speech",
      IsAvailable = azureAvailable,
      RequiresApiKey = true,
      IsOffline = false
    });

    return engines.AsReadOnly();
  }

  private bool IsESpeakAvailable()
  {
    try
    {
      var espeakPath = _options.CurrentValue.ESpeakPath;
      using var process = new Process
      {
        StartInfo = new ProcessStartInfo
        {
          FileName = espeakPath,
          Arguments = "--version",
          RedirectStandardOutput = true,
          RedirectStandardError = true,
          UseShellExecute = false,
          CreateNoWindow = true
        }
      };

      process.Start();
      process.WaitForExit(5000);
      return process.ExitCode == 0;
    }
    catch (Exception ex)
    {
      _logger.LogDebug(ex, "eSpeak-ng not available");
      return false;
    }
  }

  private async Task<(Stream audioStream, TimeSpan duration)> GenerateESpeakAsync(
    string text,
    string voice,
    float speed,
    float pitch,
    CancellationToken cancellationToken)
  {
    var opts = _options.CurrentValue;
    var espeakPath = opts.ESpeakPath;

    // Calculate espeak parameters
    // eSpeak speed: 80-450 wpm, default ~175
    var espeakSpeed = (int)(175 * speed);
    // eSpeak pitch: 0-99, default 50
    var espeakPitch = (int)(50 * pitch);

    var arguments = $"-v {voice} -s {espeakSpeed} -p {espeakPitch} --stdout";

    _logger.LogDebug("Running eSpeak-ng: {Arguments}", arguments);

    var memoryStream = new MemoryStream();

    try
    {
      using var process = new Process
      {
        StartInfo = new ProcessStartInfo
        {
          FileName = espeakPath,
          Arguments = arguments,
          RedirectStandardInput = true,
          RedirectStandardOutput = true,
          RedirectStandardError = true,
          UseShellExecute = false,
          CreateNoWindow = true
        }
      };

      process.Start();

      // Write text to stdin
      await process.StandardInput.WriteAsync(text);
      process.StandardInput.Close();

      // Read audio output
      await process.StandardOutput.BaseStream.CopyToAsync(memoryStream, cancellationToken);

      await process.WaitForExitAsync(cancellationToken);

      if (process.ExitCode != 0)
      {
        var error = await process.StandardError.ReadToEndAsync(cancellationToken);
        throw new InvalidOperationException($"eSpeak-ng failed with exit code {process.ExitCode}: {error}");
      }

      memoryStream.Position = 0;

      // Estimate duration based on output size (16-bit, 22050Hz mono WAV)
      // This is a rough estimate; actual duration depends on the audio format
      var estimatedDuration = EstimateWavDuration(memoryStream.Length);

      _logger.LogDebug("Generated TTS audio: {Length} bytes, estimated duration: {Duration}",
        memoryStream.Length, estimatedDuration);

      return (memoryStream, estimatedDuration);
    }
    catch (Exception ex)
    {
      memoryStream.Dispose();
      _logger.LogError(ex, "Failed to generate eSpeak audio");
      throw;
    }
  }

  private async Task<(Stream audioStream, TimeSpan duration)> GenerateGoogleTTSAsync(
    string text,
    string voice,
    float speed,
    float pitch,
    CancellationToken cancellationToken)
  {
    var apiKey = _secrets.CurrentValue.GoogleAPIKey;
    if (string.IsNullOrEmpty(apiKey) || apiKey.Contains("${secret:"))
    {
      throw new InvalidOperationException(
        "Google TTS API key is not configured. Set it via the System Configuration → Secrets → TTS Services page.");
    }

    _logger.LogDebug("Generating Google TTS audio for voice: {Voice}", voice);

    using var httpClient = new HttpClient();

    // Google Cloud Text-to-Speech REST API
    var endpoint = $"https://texttospeech.googleapis.com/v1/text:synthesize?key={apiKey}";

    // Parse voice to extract language code and voice name
    // Voice format is typically like "en-US-Standard-A"
    var languageCode = voice.Contains('-') ? string.Join("-", voice.Split('-').Take(2)) : "en-US";

    // Newer voice types (Journey, Studio, Casual, Chirp3, etc.) require a modelName
    var modelName = GetGoogleModelName(voice);

    var voiceParams = new Dictionary<string, object>
    {
      ["languageCode"] = languageCode,
      ["name"] = voice
    };
    if (modelName != null)
    {
      voiceParams["modelName"] = modelName;
    }

    var requestBody = new Dictionary<string, object>
    {
      ["input"] = new { text },
      ["voice"] = voiceParams,
      ["audioConfig"] = new
      {
        audioEncoding = "LINEAR16",
        speakingRate = speed,
        pitch = (pitch - 1.0) * 20 // Convert 0.5-2.0 range to -20 to +20 semitones
      }
    };

    var json = System.Text.Json.JsonSerializer.Serialize(requestBody);
    var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

    var response = await httpClient.PostAsync(endpoint, content, cancellationToken);

    if (!response.IsSuccessStatusCode)
    {
      var details = await DescribeHttpFailureAsync(response, cancellationToken);
      _logger.LogError("Google TTS API error: {StatusCode} - {Details}", response.StatusCode, details);
      throw new InvalidOperationException($"Google TTS API error: {response.StatusCode}");
    }

    var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
    using var document = System.Text.Json.JsonDocument.Parse(responseJson);

    if (!document.RootElement.TryGetProperty("audioContent", out var audioContentElement))
    {
      throw new InvalidOperationException("Google TTS response did not contain audio content");
    }

    var audioBase64 = audioContentElement.GetString();
    if (string.IsNullOrEmpty(audioBase64))
    {
      throw new InvalidOperationException("Google TTS returned empty audio content");
    }

    var audioBytes = Convert.FromBase64String(audioBase64);
    var memoryStream = new MemoryStream(audioBytes);

    // Estimate duration from audio size (16-bit, 22050Hz mono - Google's LINEAR16 format)
    var estimatedDuration = EstimateLinear16Duration(audioBytes.Length);

    _logger.LogDebug("Generated Google TTS audio: {Length} bytes, estimated duration: {Duration}",
      audioBytes.Length, estimatedDuration);

    return (memoryStream, estimatedDuration);
  }

  private async Task<(Stream audioStream, TimeSpan duration)> GenerateAzureTTSAsync(
    string text,
    string voice,
    float speed,
    float pitch,
    CancellationToken cancellationToken)
  {
    var secrets = _secrets.CurrentValue;
    if (string.IsNullOrEmpty(secrets.AzureAPIKey) || string.IsNullOrEmpty(secrets.AzureRegion))
    {
      throw new InvalidOperationException("Azure TTS API key or region is not configured");
    }

    _logger.LogDebug("Generating Azure TTS audio for voice: {Voice}", voice);

    using var httpClient = new HttpClient();

    using var request = CreateAzureSynthesisRequest(
      secrets.AzureRegion, secrets.AzureAPIKey, voice, text, speed, pitch);

    var response = await httpClient.SendAsync(request, cancellationToken);

    if (!response.IsSuccessStatusCode)
    {
      var details = await DescribeHttpFailureAsync(response, cancellationToken);
      _logger.LogError("Azure TTS API error: {StatusCode} - {Details}", response.StatusCode, details);
      throw new InvalidOperationException($"Azure TTS API error: {response.StatusCode}");
    }

    var audioBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
    var memoryStream = new MemoryStream(audioBytes);

    // Estimate duration from audio size (24kHz, 16-bit, mono WAV)
    var estimatedDuration = EstimateWavDuration24kHz(audioBytes.Length);

    _logger.LogDebug("Generated Azure TTS audio: {Length} bytes, estimated duration: {Duration}",
      audioBytes.Length, estimatedDuration);

    return (memoryStream, estimatedDuration);
  }

  /// <summary>
  /// Builds the Azure speech synthesis request. <c>internal</c> for unit testing via
  /// <c>InternalsVisibleTo</c> so the wire format can be pinned without a network call.
  /// </summary>
  /// <remarks>
  /// Microsoft documents <c>Authorization</c>/<c>Ocp-Apim-Subscription-Key</c>,
  /// <c>Content-Type</c>, <c>X-Microsoft-OutputFormat</c> and <c>User-Agent</c> as required
  /// headers for the <c>/cognitiveservices/v1</c> endpoint. <see cref="HttpClient"/> sends no
  /// <c>User-Agent</c> of its own, so it is set explicitly here. The <c>voices/list</c>
  /// endpoint documents only the authorization header, which is why a key that lists voices
  /// successfully can still be rejected here.
  /// </remarks>
  internal static HttpRequestMessage CreateAzureSynthesisRequest(
    string region,
    string apiKey,
    string voice,
    string text,
    float speed,
    float pitch)
  {
    var endpoint = $"https://{region}.tts.speech.microsoft.com/cognitiveservices/v1";
    var request = new HttpRequestMessage(HttpMethod.Post, endpoint);

    request.Headers.Add("Ocp-Apim-Subscription-Key", apiKey);
    request.Headers.Add("X-Microsoft-OutputFormat", AzureOutputFormat);
    request.Headers.Add("User-Agent", AzureUserAgent);

    request.Content = new StringContent(
      BuildAzureSsml(voice, text, speed, pitch),
      System.Text.Encoding.UTF8,
      "application/ssml+xml");

    return request;
  }

  /// <summary>
  /// Builds the SSML document for an Azure synthesis request. <c>internal</c> for unit testing
  /// via <c>InternalsVisibleTo</c>.
  /// </summary>
  /// <remarks>
  /// Azure accepts <c>rate</c> and <c>pitch</c> as a percentage where the leading "+" is
  /// optional, so a speed or pitch of 1.0 renders as <c>0%</c>. The percentages are rounded
  /// rather than truncated: 0.8f is 0.80000001 as a double, and truncating that yields -19%.
  /// </remarks>
  internal static string BuildAzureSsml(string voice, string text, float speed, float pitch)
  {
    var ratePercent = (int)Math.Round((speed - 1.0) * 100);
    var pitchPercent = (int)Math.Round((pitch - 1.0) * 100);

    return $@"<speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xml:lang='en-US'>
  <voice name='{voice}'>
    <prosody rate='{ratePercent:+#;-#;0}%' pitch='{pitchPercent:+#;-#;0}%'>
      {System.Security.SecurityElement.Escape(text)}
    </prosody>
  </voice>
</speak>";
  }

  /// <summary>
  /// Renders an HTTP failure for logging as the response body plus the response headers.
  /// </summary>
  /// <remarks>
  /// Azure's speech endpoints answer some rejections with an empty body, which leaves the
  /// status code as the only detail unless the headers are logged too. Only response headers
  /// are included — request headers would carry the subscription key.
  /// </remarks>
  private static async Task<string> DescribeHttpFailureAsync(
    HttpResponseMessage response,
    CancellationToken cancellationToken)
  {
    string body;
    try
    {
      body = await response.Content.ReadAsStringAsync(cancellationToken);
    }
    catch (Exception ex)
    {
      body = $"(could not read body: {ex.GetType().Name})";
    }

    var headers = response.Headers
      .Concat(response.Content.Headers)
      .Select(h => $"{h.Key}: {string.Join(", ", h.Value)}");

    return $"body: {(string.IsNullOrWhiteSpace(body) ? "(empty)" : body)} | headers: {string.Join("; ", headers)}";
  }

  private async Task<IReadOnlyList<TTSVoiceInfo>> GetESpeakVoicesAsync(CancellationToken cancellationToken)
  {
    var voices = new List<TTSVoiceInfo>();

    try
    {
      var espeakPath = _options.CurrentValue.ESpeakPath;

      using var process = new Process
      {
        StartInfo = new ProcessStartInfo
        {
          FileName = espeakPath,
          Arguments = "--voices",
          RedirectStandardOutput = true,
          RedirectStandardError = true,
          UseShellExecute = false,
          CreateNoWindow = true
        }
      };

      process.Start();
      var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
      await process.WaitForExitAsync(cancellationToken);

      // Parse the espeak voice list
      // Format: Pty Language Age/Gender VoiceName   File        Other Languages
      var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
      foreach (var line in lines.Skip(1)) // Skip header
      {
        var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 4)
        {
          var language = parts[1];
          var gender = parts[2].Contains('M') ? TTSVoiceGender.Male :
                       parts[2].Contains('F') ? TTSVoiceGender.Female :
                       TTSVoiceGender.Neutral;
          var voiceName = parts[3];

          voices.Add(new TTSVoiceInfo
          {
            Id = voiceName,
            Name = voiceName,
            Language = language,
            Gender = gender
          });
        }
      }
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to enumerate eSpeak voices");

      // Return some common default voices
      voices.Add(new TTSVoiceInfo { Id = "en", Name = "English", Language = "en", Gender = TTSVoiceGender.Male });
      voices.Add(new TTSVoiceInfo { Id = "en-us", Name = "English (US)", Language = "en-US", Gender = TTSVoiceGender.Male });
      voices.Add(new TTSVoiceInfo { Id = "en-gb", Name = "English (UK)", Language = "en-GB", Gender = TTSVoiceGender.Male });
    }

    return voices.AsReadOnly();
  }

  /// <summary>
  /// Fetches English voices from Google Cloud TTS API.
  /// </summary>
  private async Task<List<TTSVoiceInfo>> FetchGoogleVoicesAsync(CancellationToken cancellationToken)
  {
    var apiKey = _secrets.CurrentValue.GoogleAPIKey;
    if (string.IsNullOrEmpty(apiKey) || apiKey.Contains("${secret:"))
    {
      throw new InvalidOperationException(
        "Google TTS API key is not configured. Set it via System Configuration → Secrets.");
    }

    using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

    // Fetch all English voices (languageCode=en returns en-US, en-GB, en-AU, etc.)
    var url = $"https://texttospeech.googleapis.com/v1/voices?key={apiKey}&languageCode=en";
    var response = await httpClient.GetAsync(url, cancellationToken);

    if (!response.IsSuccessStatusCode)
    {
      var details = await DescribeHttpFailureAsync(response, cancellationToken);
      _logger.LogError("Google TTS voices API error: {Status} - {Details}", response.StatusCode, details);
      throw new InvalidOperationException($"Google TTS voices API error: {response.StatusCode}");
    }

    var json = await response.Content.ReadAsStringAsync(cancellationToken);
    using var doc = System.Text.Json.JsonDocument.Parse(json);

    var voices = new List<TTSVoiceInfo>();

    if (doc.RootElement.TryGetProperty("voices", out var voicesArray))
    {
      foreach (var v in voicesArray.EnumerateArray())
      {
        var name = v.GetProperty("name").GetString() ?? "unknown";

        // Language codes array — use the first one
        var langCodes = v.GetProperty("languageCodes");
        var language = langCodes.GetArrayLength() > 0
          ? langCodes[0].GetString() ?? "en-US"
          : "en-US";

        // SSML gender
        var ssmlGender = v.TryGetProperty("ssmlGender", out var genderProp)
          ? genderProp.GetString() ?? "NEUTRAL"
          : "NEUTRAL";
        var gender = ssmlGender switch
        {
          "MALE" => TTSVoiceGender.Male,
          "FEMALE" => TTSVoiceGender.Female,
          _ => TTSVoiceGender.Neutral
        };

        // Extract price tier from voice name pattern: en-US-{Tier}-{Letter}
        var priceTier = ExtractGooglePriceTier(name);

        voices.Add(new TTSVoiceInfo
        {
          Id = name,
          Name = $"{language.Replace("en-", "")} {priceTier} ({name.Split('-').LastOrDefault() ?? ""}) - {ssmlGender.ToLowerInvariant()}",
          Language = language,
          Gender = gender,
          PriceTier = priceTier
        });
      }
    }

    _logger.LogInformation("Fetched {Count} Google TTS voices", voices.Count);
    return voices;
  }

  /// <summary>
  /// Extracts price tier from a Google voice name (e.g., "en-US-Standard-A" → "Standard").
  /// </summary>
  private static string ExtractGooglePriceTier(string voiceName)
  {
    // Voice name format: {locale}-{Tier}-{Letter} or {locale}-{Tier}{Variant}-{Letter}
    var parts = voiceName.Split('-');
    if (parts.Length >= 3)
    {
      // The tier is the third segment (index 2) for names like en-US-Standard-A
      // or en-US-Neural2-A
      return parts[2] switch
      {
        "Standard" => "Standard",
        "WaveNet" => "WaveNet",
        "Neural2" => "Neural2",
        "Studio" => "Studio",
        "Polyglot" => "Polyglot",
        "News" => "News",
        "Casual" => "Casual",
        "Journey" => "Journey",
        _ => parts[2] // Preserve unknown tiers as-is
      };
    }
    return "Standard";
  }

  /// <summary>
  /// Returns the model name required by newer Google TTS voices, or null if not needed.
  /// Voices like Journey, Studio, Casual, and Chirp3 require a modelName in the API request.
  /// Standard, WaveNet, and Neural2 voices do not.
  /// </summary>
  private static string? GetGoogleModelName(string voiceName)
  {
    var parts = voiceName.Split('-');
    if (parts.Length < 3)
    {
      return null;
    }

    // The voice type is the 3rd segment: en-US-{Type}-{Variant}
    var voiceType = parts[2];
    return voiceType switch
    {
      // Classic voices — no model name required
      "Standard" or "WaveNet" or "Wavenet" or "Neural2" => null,

      // Newer voices — require modelName in the API request
      "Journey" => "journey",
      "Studio" => "studio",
      "Casual" => "casual",
      "Polyglot" => "polyglot",
      "News" => "news",
      "Chirp3" => "chirp3",

      _ => null
    };
  }

  /// <summary>
  /// Fetches English voices from Azure Cognitive Services Speech API.
  /// </summary>
  private async Task<List<TTSVoiceInfo>> FetchAzureVoicesAsync(CancellationToken cancellationToken)
  {
    var secrets = _secrets.CurrentValue;
    if (string.IsNullOrEmpty(secrets.AzureAPIKey) || string.IsNullOrEmpty(secrets.AzureRegion))
    {
      throw new InvalidOperationException(
        "Azure TTS API key or region is not configured. Set them via System Configuration → Secrets.");
    }

    using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    httpClient.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", secrets.AzureAPIKey);

    var url = $"https://{secrets.AzureRegion}.tts.speech.microsoft.com/cognitiveservices/voices/list";
    var response = await httpClient.GetAsync(url, cancellationToken);

    if (!response.IsSuccessStatusCode)
    {
      var details = await DescribeHttpFailureAsync(response, cancellationToken);
      _logger.LogError("Azure TTS voices API error: {Status} - {Details}", response.StatusCode, details);
      throw new InvalidOperationException($"Azure TTS voices API error: {response.StatusCode}");
    }

    var json = await response.Content.ReadAsStringAsync(cancellationToken);
    var voiceData = System.Text.Json.JsonSerializer.Deserialize<List<AzureVoiceDto>>(json);

    var voices = new List<TTSVoiceInfo>();

    if (voiceData != null)
    {
      voices = voiceData
        .Where(v => v.Locale?.StartsWith("en-") == true)
        .Select(v => new TTSVoiceInfo
        {
          Id = v.ShortName ?? v.Name ?? "unknown",
          Name = v.LocalName ?? v.DisplayName ?? v.Name ?? "Unknown",
          Language = v.Locale ?? "en-US",
          Gender = v.Gender?.Equals("Male", StringComparison.OrdinalIgnoreCase) == true
            ? TTSVoiceGender.Male
            : TTSVoiceGender.Female,
          PriceTier = v.VoiceType ?? "Standard"
        })
        .ToList();
    }

    _logger.LogInformation("Fetched {Count} Azure TTS voices", voices.Count);
    return voices;
  }

  /// <summary>
  /// Azure Voice API response DTO.
  /// </summary>
  private class AzureVoiceDto
  {
    public string? Name { get; set; }
    public string? DisplayName { get; set; }
    public string? LocalName { get; set; }
    public string? ShortName { get; set; }
    public string? Gender { get; set; }
    public string? Locale { get; set; }
    public string? VoiceType { get; set; }
  }

  private static TimeSpan EstimateWavDuration(long bytes)
  {
    // eSpeak outputs WAV at 22050 Hz, 16-bit, mono by default
    // Bytes per second = 22050 * 2 = 44100
    // Account for WAV header (~44 bytes)
    const int bytesPerSecond = 44100;
    const int headerSize = 44;

    if (bytes <= headerSize)
    {
      return TimeSpan.Zero;
    }

    var audioBytes = bytes - headerSize;
    var seconds = (double)audioBytes / bytesPerSecond;

    return TimeSpan.FromSeconds(seconds);
  }

  private static TimeSpan EstimateLinear16Duration(long bytes)
  {
    // Google TTS LINEAR16 format is 24kHz, 16-bit, mono (as per API docs)
    // Bytes per second = 24000 * 2 = 48000
    const int bytesPerSecond = 48000;

    if (bytes <= 0)
    {
      return TimeSpan.Zero;
    }

    var seconds = (double)bytes / bytesPerSecond;
    return TimeSpan.FromSeconds(seconds);
  }

  private static TimeSpan EstimateWavDuration24kHz(long bytes)
  {
    // Azure riff-24khz-16bit-mono-pcm format
    // Bytes per second = 24000 * 2 = 48000
    // Account for WAV header (~44 bytes)
    const int bytesPerSecond = 48000;
    const int headerSize = 44;

    if (bytes <= headerSize)
    {
      return TimeSpan.Zero;
    }

    var audioBytes = bytes - headerSize;
    var seconds = (double)audioBytes / bytesPerSecond;

    return TimeSpan.FromSeconds(seconds);
  }

  /// <summary>
  /// Disposes the factory resources.
  /// </summary>
  public void Dispose()
  {
    GC.SuppressFinalize(this);
  }
}
