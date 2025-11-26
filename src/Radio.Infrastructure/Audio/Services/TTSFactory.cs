using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
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
  private IReadOnlyList<TTSEngineInfo>? _cachedEngines;

  /// <summary>
  /// Initializes a new instance of the <see cref="TTSFactory"/> class.
  /// </summary>
  /// <param name="logger">The factory logger.</param>
  /// <param name="ttsSourceLogger">The TTS source logger.</param>
  /// <param name="options">The TTS options.</param>
  /// <param name="secrets">The TTS secrets (API keys).</param>
  public TTSFactory(
    ILogger<TTSFactory> logger,
    ILogger<TTSEventSource> ttsSourceLogger,
    IOptionsMonitor<TTSOptions> options,
    IOptionsMonitor<TTSSecrets> secrets)
  {
    _logger = logger;
    _ttsSourceLogger = ttsSourceLogger;
    _options = options;
    _secrets = secrets;
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

    // Generate audio based on engine
    var (audioStream, duration) = engine switch
    {
      TTSEngine.ESpeak => await GenerateESpeakAsync(text, voice, speed, pitch, cancellationToken),
      TTSEngine.Google => await GenerateGoogleTTSAsync(text, voice, speed, pitch, cancellationToken),
      TTSEngine.Azure => await GenerateAzureTTSAsync(text, voice, speed, pitch, cancellationToken),
      _ => throw new NotSupportedException($"TTS engine '{engine}' is not supported")
    };

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

    // Google Cloud TTS integration requires the Google.Cloud.TextToSpeech.V1 NuGet package.
    // This is a placeholder for when the dependency is added.
    throw new NotSupportedException(
      "Google Cloud TTS integration is not yet implemented. " +
      "Add the Google.Cloud.TextToSpeech.V1 NuGet package and implement the API call.");
  }

  private Task<(Stream audioStream, TimeSpan duration)> GenerateAzureTTSAsync(
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

    // Azure TTS integration requires the Microsoft.CognitiveServices.Speech NuGet package.
    // This is a placeholder for when the dependency is added.
    throw new NotSupportedException(
      "Azure TTS integration is not yet implemented. " +
      "Add the Microsoft.CognitiveServices.Speech NuGet package and implement the API call.");
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
}
