using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using SpotifyAPI.Web;

namespace Radio.Infrastructure.Audio.Services;

/// <summary>
/// Manages the librespot process for integrated Spotify audio streaming.
/// Starts librespot as a child process with pipe backend (stdout) and
/// captures PCM audio data for SoundFlow integration.
/// Handles token refresh and automatic process restart.
/// </summary>
public class LibrespotManager : IAsyncDisposable
{
  private readonly ILogger<LibrespotManager> _logger;
  private readonly IOptionsMonitor<SpotifySecrets> _secrets;
  private readonly IOptionsMonitor<DeviceOptions> _deviceOptions;
  private readonly ConcurrentQueue<byte[]> _audioBuffer;
  private readonly int _maxBufferChunks;
  
  private Process? _librespotProcess;
  private Task? _audioReadTask;
  private CancellationTokenSource? _cts;
  private Timer? _tokenRefreshTimer;
  private SpotifyClient? _spotifyClient;
  
  // Configuration
  private string _deviceName = "Radio Console";
  private readonly int _bitrate = 320;
  private readonly bool _volumeNormalization = true;
  
  // State
  private bool _isDisposed;
  private long _totalSamplesReceived;
  private readonly SemaphoreSlim _refreshLock = new(1, 1);

  /// <summary>
  /// Gets a value indicating whether the librespot process is running.
  /// </summary>
  public bool IsRunning => _librespotProcess != null && !_librespotProcess.HasExited;

  /// <summary>
  /// Gets the current state of the manager.
  /// </summary>
  public DeviceState State { get; private set; } = DeviceState.Stopped;

  /// <summary>
  /// Gets the number of audio chunks currently buffered.
  /// </summary>
  public int BufferedChunks => _audioBuffer.Count;

  /// <summary>
  /// Gets the total number of samples received.
  /// </summary>
  public long TotalSamplesReceived => _totalSamplesReceived;

  /// <summary>
  /// Event raised when audio data is received from librespot.
  /// </summary>
  public event EventHandler<AudioDataEventArgs>? AudioDataReceived;

  /// <summary>
  /// Event raised when the device state changes.
  /// </summary>
  public event EventHandler<DeviceStateChangedEventArgs>? StateChanged;

  /// <summary>
  /// Event raised when a log message is generated.
  /// </summary>
  public event EventHandler<string>? LogMessage;

  /// <summary>
  /// Initializes a new instance of the <see cref="LibrespotManager"/> class.
  /// </summary>
  /// <param name="logger">Logger instance.</param>
  /// <param name="secrets">Spotify secrets configuration.</param>
  /// <param name="deviceOptions">Device configuration options.</param>
  /// <param name="maxBufferChunks">Maximum number of audio chunks to buffer.</param>
  public LibrespotManager(
    ILogger<LibrespotManager> logger,
    IOptionsMonitor<SpotifySecrets> secrets,
    IOptionsMonitor<DeviceOptions> deviceOptions,
    int maxBufferChunks = 20)
  {
    _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    _secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));
    _deviceOptions = deviceOptions ?? throw new ArgumentNullException(nameof(deviceOptions));
    _audioBuffer = new ConcurrentQueue<byte[]>();
    _maxBufferChunks = maxBufferChunks;
  }

  /// <summary>
  /// Starts the librespot device.
  /// </summary>
  /// <param name="deviceName">Optional device name override.</param>
  /// <param name="cancellationToken">Cancellation token.</param>
  public async Task StartDeviceAsync(
    string? deviceName = null,
    CancellationToken cancellationToken = default)
  {
    if (IsRunning)
    {
      Log("Device already running");
      return;
    }

    Log("Starting Spotify device...");
    SetState(DeviceState.Starting);

    try
    {
      if (!string.IsNullOrEmpty(deviceName))
      {
        _deviceName = deviceName;
      }

      var secrets = _secrets.CurrentValue;
      if (string.IsNullOrEmpty(secrets.ClientID) ||
          string.IsNullOrEmpty(secrets.ClientSecret) ||
          string.IsNullOrEmpty(secrets.RefreshToken))
      {
        throw new InvalidOperationException("Spotify credentials not configured");
      }

      // Initialize Spotify API client for token management
      var config = SpotifyClientConfig.CreateDefault()
        .WithAuthenticator(new AuthorizationCodeAuthenticator(
          secrets.ClientID,
          secrets.ClientSecret,
          new AuthorizationCodeTokenResponse { RefreshToken = secrets.RefreshToken }
        ));

      _spotifyClient = new SpotifyClient(config);

      // Get initial access token
      var accessToken = await GetAccessTokenAsync(cancellationToken);

      // Start librespot process
      await StartLibrespotProcessAsync(accessToken, cancellationToken);

      // Set up token refresh (every 50 minutes)
      _tokenRefreshTimer = new Timer(
        _ => SafeRefreshTokenAndRestart(),
        null,
        TimeSpan.FromMinutes(50),
        TimeSpan.FromMinutes(50)
      );

      SetState(DeviceState.Running);
      Log($"Spotify device '{_deviceName}' started successfully");
    }
    catch (Exception ex)
    {
      SetState(DeviceState.Error);
      Log($"Failed to start device: {ex.Message}");
      _logger.LogError(ex, "Failed to start Spotify device");
      throw;
    }
  }

  /// <summary>
  /// Stops the librespot device.
  /// </summary>
  public async Task StopDeviceAsync()
  {
    if (!IsRunning)
    {
      Log("Device already stopped");
      return;
    }

    Log("Stopping Spotify device...");
    SetState(DeviceState.Stopping);

    try
    {
      // Stop token refresh
      if (_tokenRefreshTimer != null)
      {
        await _tokenRefreshTimer.DisposeAsync();
        _tokenRefreshTimer = null;
      }

      // Cancel audio reading
      _cts?.Cancel();

      // Give librespot time to clean up gracefully
      if (_librespotProcess != null && !_librespotProcess.HasExited)
      {
        // Try graceful shutdown first
        try
        {
          _librespotProcess.StandardInput?.WriteLine("quit");
          if (!_librespotProcess.WaitForExit(3000))
          {
            // Force kill if it doesn't exit
            _librespotProcess.Kill();
            _librespotProcess.WaitForExit(2000);
          }
        }
        catch (Exception ex)
        {
          _logger.LogWarning(ex, "Failed to gracefully shut down librespot process. Attempting force kill.");
          // If graceful fails, force kill
          try
          {
            _librespotProcess.Kill();
          }
          catch (Exception killEx)
          {
            _logger.LogError(killEx, "Failed to force kill librespot process during shutdown.");
          }
        }
      }

      // Wait for audio read task to complete
      if (_audioReadTask != null)
      {
        var completedTask = await Task.WhenAny(_audioReadTask, Task.Delay(2000));
        if (completedTask != _audioReadTask)
        {
          _logger.LogWarning("Audio read task did not complete within the 2 second timeout during librespot shutdown and may still be running in the background.");
        }
        else
        {
          try
          {
            await _audioReadTask;
          }
          catch (OperationCanceledException)
          {
            // Expected during shutdown when cancellation is requested.
          }
          catch (Exception ex)
          {
            _logger.LogError(ex, "Unexpected error while waiting for the audio read task to complete during librespot shutdown.");
          }
        }
      }

      // Cleanup
      _librespotProcess?.Dispose();
      _librespotProcess = null;
      _cts?.Dispose();
      _cts = null;

      SetState(DeviceState.Stopped);
      Log("Spotify device stopped");
    }
    catch (Exception ex)
    {
      SetState(DeviceState.Error);
      Log($"Error stopping device: {ex.Message}");
      _logger.LogError(ex, "Error stopping Spotify device");
      throw;
    }
  }

  /// <summary>
  /// Restarts the librespot device.
  /// </summary>
  public async Task RestartDeviceAsync()
  {
    Log("Restarting Spotify device...");

    if (IsRunning)
    {
      await StopDeviceAsync();
      await Task.Delay(1000); // Brief pause for cleanup
    }

    await StartDeviceAsync();
  }

  /// <summary>
  /// Tries to dequeue an audio chunk from the buffer.
  /// </summary>
  /// <param name="audioData">The dequeued audio data, or null if buffer is empty.</param>
  /// <returns>True if data was dequeued, false if buffer is empty.</returns>
  public bool TryDequeueAudioData(out byte[]? audioData)
  {
    return _audioBuffer.TryDequeue(out audioData);
  }

  private async Task StartLibrespotProcessAsync(string accessToken, CancellationToken cancellationToken)
  {
    var librespotPath = _deviceOptions.CurrentValue.Spotify?.LibrespotPath;
    if (string.IsNullOrEmpty(librespotPath))
    {
      throw new InvalidOperationException("Librespot path not configured");
    }

    if (!File.Exists(librespotPath))
    {
      throw new FileNotFoundException($"Librespot executable not found at: {librespotPath}");
    }

    _cts = new CancellationTokenSource();

    var args = BuildLibrespotArguments(accessToken);

    var startInfo = new ProcessStartInfo
    {
      FileName = librespotPath,
      Arguments = args,
      UseShellExecute = false,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      RedirectStandardInput = true,
      CreateNoWindow = true,
      WorkingDirectory = Path.GetDirectoryName(librespotPath) ?? "."
    };

    _librespotProcess = new Process { StartInfo = startInfo };

    // Monitor stderr for logs and status
    _librespotProcess.ErrorDataReceived += OnLibrespotLogReceived;

    // Monitor process exit
    _librespotProcess.Exited += OnLibrespotExited;
    _librespotProcess.EnableRaisingEvents = true;

    // Start process
    _librespotProcess.Start();
    _librespotProcess.BeginErrorReadLine();

    Log($"Librespot process started (PID: {_librespotProcess.Id})");

    // Start reading audio stream
    _audioReadTask = Task.Run(() => ReadAudioStreamAsync(_cts.Token), _cts.Token);
  }

  private string BuildLibrespotArguments(string accessToken)
  {
    // Use ProcessStartInfo.ArgumentList to avoid command injection issues
    // Note: This method still returns a string for compatibility, but should be refactored
    // to use ArgumentList directly in StartLibrespotProcessAsync
    
    var args = new System.Text.StringBuilder();
    
    // Escape special characters to prevent command injection
    var escapedDeviceName = _deviceName.Replace("\"", "\\\"");
    var escapedAccessToken = accessToken.Replace("\"", "\\\"");
    
    args.Append($"--name \"{escapedDeviceName}\" ");
    args.Append($"--backend pipe ");
    args.Append($"--device - ");  // stdout
    args.Append($"--access-token \"{escapedAccessToken}\" ");
    args.Append($"--bitrate {_bitrate} ");

    if (_volumeNormalization)
    {
      args.Append($"--enable-volume-normalisation ");
    }

    args.Append($"--initial-volume 100 ");
    args.Append($"--cache-size-limit 1024 ");  // 1GB cache

    return args.ToString();
  }

  private async Task ReadAudioStreamAsync(CancellationToken ct)
  {
    var stream = _librespotProcess?.StandardOutput?.BaseStream;
    if (stream == null)
    {
      _logger.LogWarning("Cannot read audio stream: process or stream is null");
      return;
    }

    // Librespot outputs raw PCM data: 16-bit signed stereo @ 44.1kHz
    // Buffer size: 8192 bytes = 2048 samples = ~23ms of audio at 44.1kHz stereo
    var buffer = new byte[8192];

    try
    {
      while (!ct.IsCancellationRequested)
      {
        int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, ct);

        if (bytesRead == 0)
        {
          Log("Audio stream ended");
          break;
        }

        _totalSamplesReceived = Interlocked.Add(ref _totalSamplesReceived, bytesRead);

        var audioData = new byte[bytesRead];
        Buffer.BlockCopy(buffer, 0, audioData, 0, bytesRead);

        // Queue the audio data
        if (_audioBuffer.Count < _maxBufferChunks)
        {
          _audioBuffer.Enqueue(audioData);
        }
        else
        {
          // Buffer overflow - drop oldest chunk and add new one
          _audioBuffer.TryDequeue(out _);
          _audioBuffer.Enqueue(audioData);
          _logger.LogDebug("Audio buffer overflow, dropped oldest chunk");
        }

        // Raise event for subscribers
        AudioDataReceived?.Invoke(this, new AudioDataEventArgs(audioData));
      }
    }
    catch (OperationCanceledException)
    {
      // Normal cancellation
      Log("Audio stream reading cancelled");
    }
    catch (Exception ex)
    {
      Log($"Audio stream error: {ex.Message}");
      _logger.LogError(ex, "Error reading audio stream from librespot");
      SetState(DeviceState.Error);
    }
  }

  private void SafeRefreshTokenAndRestart()
  {
    // Timer callback must be synchronous, so we fire and forget the async work
    // with proper exception handling to prevent unobserved exceptions
    _ = Task.Run(async () =>
    {
      try
      {
        await RefreshTokenAndRestartAsync();
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Unhandled exception in token refresh timer callback");
        SetState(DeviceState.Error);
      }
    });
  }

  private async Task RefreshTokenAndRestartAsync()
  {
    // Prevent concurrent refresh operations
    if (!await _refreshLock.WaitAsync(0))
    {
      _logger.LogWarning("Token refresh already in progress, skipping");
      return;
    }

    try
    {
      Log("Refreshing access token...");

      var accessToken = await GetAccessTokenAsync(CancellationToken.None);

      // Brief pause in playback for token refresh
      SetState(DeviceState.Reconnecting);

      await StopDeviceAsync();
      await Task.Delay(500);
      await StartLibrespotProcessAsync(accessToken, CancellationToken.None);

      SetState(DeviceState.Running);
      Log("Token refreshed and device reconnected");
    }
    catch (Exception ex)
    {
      Log($"Token refresh failed: {ex.Message}");
      _logger.LogError(ex, "Failed to refresh Spotify access token");
      SetState(DeviceState.Error);
    }
    finally
    {
      _refreshLock.Release();
    }
  }

  private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
  {
    if (_spotifyClient == null)
    {
      throw new InvalidOperationException("Spotify client not initialized");
    }

    try
    {
      var secrets = _secrets.CurrentValue;
      
      // Use the OAuthClient to refresh the token
      var authClient = new OAuthClient();
      var tokenResponse = await authClient.RequestToken(
        new AuthorizationCodeRefreshRequest(
          secrets.ClientID,
          secrets.ClientSecret,
          secrets.RefreshToken
        ),
        cancellationToken
      );

      return tokenResponse.AccessToken;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get Spotify access token");
      throw;
    }
  }

  private void OnLibrespotLogReceived(object? sender, DataReceivedEventArgs e)
  {
    if (!string.IsNullOrEmpty(e.Data))
    {
      Log($"[Librespot] {e.Data}");

      // Parse status from logs
      if (e.Data.Contains("Loading track") || e.Data.Contains("Playing"))
      {
        SetState(DeviceState.Playing);
      }
      else if (e.Data.Contains("Connection error") || e.Data.Contains("Error"))
      {
        SetState(DeviceState.Error);
      }
    }
  }

  private void OnLibrespotExited(object? sender, EventArgs e)
  {
    var exitCode = _librespotProcess?.ExitCode ?? -1;
    Log($"Librespot exited with code {exitCode}");
    _logger.LogWarning("Librespot process exited with code {ExitCode}", exitCode);

    if (State != DeviceState.Stopping && State != DeviceState.Stopped)
    {
      // Unexpected exit
      SetState(DeviceState.Error);
    }
  }

  private void SetState(DeviceState newState)
  {
    if (State != newState)
    {
      var oldState = State;
      State = newState;
      StateChanged?.Invoke(this, new DeviceStateChangedEventArgs(oldState, newState));
    }
  }

  private void Log(string message)
  {
    _logger.LogInformation("[LibrespotManager] {Message}", message);
    LogMessage?.Invoke(this, message);
  }

  /// <inheritdoc/>
  public async ValueTask DisposeAsync()
  {
    if (_isDisposed)
    {
      return;
    }

    _isDisposed = true;

    try
    {
      await StopDeviceAsync();
      
      // Dispose SpotifyClient if it implements IDisposable
      if (_spotifyClient is IDisposable disposableClient)
      {
        disposableClient.Dispose();
        _spotifyClient = null;
      }
      
      _refreshLock?.Dispose();
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error during LibrespotManager disposal");
    }

    GC.SuppressFinalize(this);
  }
}

/// <summary>
/// Represents the state of the Spotify device.
/// </summary>
public enum DeviceState
{
  /// <summary>
  /// Device is stopped.
  /// </summary>
  Stopped,

  /// <summary>
  /// Device is starting.
  /// </summary>
  Starting,

  /// <summary>
  /// Device is running and ready.
  /// </summary>
  Running,

  /// <summary>
  /// Device is playing audio.
  /// </summary>
  Playing,

  /// <summary>
  /// Device is stopping.
  /// </summary>
  Stopping,

  /// <summary>
  /// Device is reconnecting (token refresh).
  /// </summary>
  Reconnecting,

  /// <summary>
  /// Device encountered an error.
  /// </summary>
  Error
}

/// <summary>
/// Event arguments for device state change events.
/// </summary>
public class DeviceStateChangedEventArgs : EventArgs
{
  /// <summary>
  /// Gets the old state.
  /// </summary>
  public DeviceState OldState { get; }

  /// <summary>
  /// Gets the new state.
  /// </summary>
  public DeviceState NewState { get; }

  /// <summary>
  /// Initializes a new instance of the <see cref="DeviceStateChangedEventArgs"/> class.
  /// </summary>
  public DeviceStateChangedEventArgs(DeviceState oldState, DeviceState newState)
  {
    OldState = oldState;
    NewState = newState;
  }
}

/// <summary>
/// Event arguments for audio data events.
/// </summary>
public class AudioDataEventArgs : EventArgs
{
  /// <summary>
  /// Gets the audio data.
  /// </summary>
  public byte[] AudioData { get; }

  /// <summary>
  /// Gets the timestamp when the data was received.
  /// </summary>
  public DateTime Timestamp { get; }

  /// <summary>
  /// Initializes a new instance of the <see cref="AudioDataEventArgs"/> class.
  /// </summary>
  public AudioDataEventArgs(byte[] audioData)
  {
    AudioData = audioData;
    Timestamp = DateTime.UtcNow;
  }
}
