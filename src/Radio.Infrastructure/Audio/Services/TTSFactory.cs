using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Interfaces;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Audio.Sources.Events;

namespace Radio.Infrastructure.Audio.Services;

/// <summary>
/// Factory for creating TTS audio sources.
/// Supports eSpeak (offline), Google Cloud TTS, and Azure TTS engines.
/// </summary>
public class TTSFactory : ITTSFactory
{
  private readonly ILogger<TTSFactory> _logger;
  private readonly ILogger<TTSEventSource> _ttsSourceLogger;
  private readonly IOptionsMonitor<TTSOptions> _options;
  private readonly IOptionsMonitor<TTSSecrets> _secrets;
  private readonly IMetricsCollector? _metricsCollector;
  private IReadOnlyList<TTSEngineInfo>? _cachedEngines;

  /// <summary>
  /// Initializes a new instance of the <see cref="TTSFactory"/> class.
  /// </summary>
  /// <param name="logger">The factory logger.</param>
  /// <param name="ttsSourceLogger">The TTS source logger.</param>
  /// <param name="options">The TTS options.</param>
  /// <param name="secrets">The TTS secrets (API keys).</param>
  /// <param name="metricsCollector">Optional metrics collector for tracking TTS operations.</param>
  public TTSFactory(
    ILogger<TTSFactory> logger,
    ILogger<TTSEventSource> ttsSourceLogger,
    IOptionsMonitor<TTSOptions> options,
    IOptionsMonitor<TTSSecrets> secrets,
    IMetricsCollector? metricsCollector = null)
  {
    _logger = logger;
    _ttsSourceLogger = ttsSourceLogger;
    _options = options;
    _secrets = secrets;
    _metricsCollector = metricsCollector;
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

    // Check for cached audio (simplified - in real implementation would check actual cache)
    var cacheKey = $"{engine}_{voice}_{text.GetHashCode()}";
    var isCached = false; // In real implementation, check if audio file exists

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

    return new TTSEventSource(text, effectiveParams, audioStream, duration, _ttsSourceLogger);
  }

  /// <inheritdoc/>
  public Task<IReadOnlyList<TTSVoiceInfo>> GetVoicesAsync(
    TTSEngine engine,
    CancellationToken cancellationToken = default)
  {
    return engine switch
    {
      TTSEngine.ESpeak => GetESpeakVoicesAsync(cancellationToken),
      TTSEngine.Google => GetGoogleVoicesAsync(cancellationToken),
      TTSEngine.Azure => GetAzureVoicesAsync(cancellationToken),
      _ => throw new NotSupportedException($"TTS engine '{engine}' is not supported")
    };
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
    var secrets = _secrets.CurrentValue;
    if (string.IsNullOrEmpty(secrets.GoogleAPIKey))
    {
      throw new InvalidOperationException("Google TTS API key is not configured");
    }

    _logger.LogDebug("Generating Google TTS audio for voice: {Voice}", voice);

    using var httpClient = new HttpClient();

    // Google Cloud Text-to-Speech REST API
    var endpoint = $"https://texttospeech.googleapis.com/v1/text:synthesize?key={secrets.GoogleAPIKey}";

    // Parse voice to extract language code and voice name
    // Voice format is typically like "en-US-Standard-A"
    var languageCode = voice.Contains('-') ? string.Join("-", voice.Split('-').Take(2)) : "en-US";

    var requestBody = new
    {
      input = new { text },
      voice = new
      {
        languageCode,
        name = voice
      },
      audioConfig = new
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
      var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
      _logger.LogError("Google TTS API error: {StatusCode} - {Error}", response.StatusCode, errorContent);
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

    // Azure Cognitive Services Speech REST API
    var endpoint = $"https://{secrets.AzureRegion}.tts.speech.microsoft.com/cognitiveservices/v1";

    // Add required headers
    httpClient.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", secrets.AzureAPIKey);
    httpClient.DefaultRequestHeaders.Add("X-Microsoft-OutputFormat", "riff-24khz-16bit-mono-pcm");

    // Create SSML payload for Azure TTS
    // Rate: Percentage of default rate (e.g., "+50%" = 1.5x, "-50%" = 0.5x, "0%" = normal)
    // Pitch: Percentage adjustment (e.g., "+20%" = higher pitch, "-20%" = lower pitch)
    // Both use percentage format for consistency
    var ratePercent = (int)((speed - 1.0) * 100);
    var pitchPercent = (int)((pitch - 1.0) * 100);

    var ssml = $@"<speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xml:lang='en-US'>
  <voice name='{voice}'>
    <prosody rate='{ratePercent:+#;-#;0}%' pitch='{pitchPercent:+#;-#;0}%'>
      {System.Security.SecurityElement.Escape(text)}
    </prosody>
  </voice>
</speak>";

    var content = new StringContent(ssml, System.Text.Encoding.UTF8, "application/ssml+xml");

    var response = await httpClient.PostAsync(endpoint, content, cancellationToken);

    if (!response.IsSuccessStatusCode)
    {
      var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
      _logger.LogError("Azure TTS API error: {StatusCode} - {Error}", response.StatusCode, errorContent);
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

  private Task<IReadOnlyList<TTSVoiceInfo>> GetGoogleVoicesAsync(CancellationToken cancellationToken)
  {
    // In a full implementation, this would call the Google Cloud TTS API
    // For now, return some common Google TTS voice identifiers
    var voices = new List<TTSVoiceInfo>
    {
      new() { Id = "en-US-Standard-A", Name = "English US Standard A", Language = "en-US", Gender = TTSVoiceGender.Male },
      new() { Id = "en-US-Standard-B", Name = "English US Standard B", Language = "en-US", Gender = TTSVoiceGender.Male },
      new() { Id = "en-US-Standard-C", Name = "English US Standard C", Language = "en-US", Gender = TTSVoiceGender.Female },
      new() { Id = "en-US-Standard-D", Name = "English US Standard D", Language = "en-US", Gender = TTSVoiceGender.Male },
      new() { Id = "en-GB-Standard-A", Name = "English UK Standard A", Language = "en-GB", Gender = TTSVoiceGender.Female },
      new() { Id = "en-GB-Standard-B", Name = "English UK Standard B", Language = "en-GB", Gender = TTSVoiceGender.Male }
    };

    return Task.FromResult<IReadOnlyList<TTSVoiceInfo>>(voices.AsReadOnly());
  }

  private Task<IReadOnlyList<TTSVoiceInfo>> GetAzureVoicesAsync(CancellationToken cancellationToken)
  {
    // In a full implementation, this would call the Azure Speech API
    // For now, return some common Azure TTS voice identifiers
    var voices = new List<TTSVoiceInfo>
    {
      new() { Id = "en-US-JennyNeural", Name = "Jenny (US)", Language = "en-US", Gender = TTSVoiceGender.Female },
      new() { Id = "en-US-GuyNeural", Name = "Guy (US)", Language = "en-US", Gender = TTSVoiceGender.Male },
      new() { Id = "en-US-AriaNeural", Name = "Aria (US)", Language = "en-US", Gender = TTSVoiceGender.Female },
      new() { Id = "en-GB-SoniaNeural", Name = "Sonia (UK)", Language = "en-GB", Gender = TTSVoiceGender.Female },
      new() { Id = "en-GB-RyanNeural", Name = "Ryan (UK)", Language = "en-GB", Gender = TTSVoiceGender.Male }
    };

    return Task.FromResult<IReadOnlyList<TTSVoiceInfo>>(voices.AsReadOnly());
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
}
