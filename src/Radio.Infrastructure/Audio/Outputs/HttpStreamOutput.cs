using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;

namespace Radio.Infrastructure.Audio.Outputs;

/// <summary>
/// HTTP audio stream server output implementation.
/// Provides an HTTP endpoint that streams mixed audio to connected clients.
/// This is used to provide audio data to Chromecast devices and other network clients.
/// </summary>
public class HttpStreamOutput : IAudioOutput
{
  private readonly ILogger<HttpStreamOutput> _logger;
  private readonly HttpStreamOutputOptions _options;
  private readonly IAudioEngine _audioEngine;
  private readonly object _stateLock = new();

  private AudioOutputState _state = AudioOutputState.Created;
  private float _volume = 1.0f;
  private bool _isMuted;
  private bool _isEnabled;
  private bool _disposed;
  private HttpListener? _httpListener;
  private CancellationTokenSource? _serverCts;
  private Task? _serverTask;
  private readonly ConcurrentDictionary<string, HttpStreamClient> _connectedClients = new();

  /// <inheritdoc />
  public string Id { get; }

  /// <inheritdoc />
  public string Name { get; private set; }

  /// <inheritdoc />
  public AudioOutputType Type => AudioOutputType.HttpStream;

  /// <inheritdoc />
  public AudioOutputState State
  {
    get
    {
      lock (_stateLock)
      {
        return _state;
      }
    }
    private set
    {
      AudioOutputState previousState;
      lock (_stateLock)
      {
        previousState = _state;
        _state = value;
      }

      if (previousState != value)
      {
        _logger.LogInformation(
          "HTTP stream output state changed from {PreviousState} to {NewState}",
          previousState, value);

        StateChanged?.Invoke(this, new AudioOutputStateChangedEventArgs
        {
          PreviousState = previousState,
          NewState = value,
          OutputId = Id
        });
      }
    }
  }

  /// <inheritdoc />
  public float Volume
  {
    get => _volume;
    set
    {
      var clamped = Math.Clamp(value, 0f, 1f);
      if (Math.Abs(_volume - clamped) > 0.0001f)
      {
        _volume = clamped;
        _logger.LogDebug("HTTP stream output volume set to {Volume:P0}", _volume);
      }
    }
  }

  /// <inheritdoc />
  public bool IsMuted
  {
    get => _isMuted;
    set
    {
      if (_isMuted != value)
      {
        _isMuted = value;
        _logger.LogDebug("HTTP stream output mute set to {IsMuted}", _isMuted);
      }
    }
  }

  /// <inheritdoc />
  public bool IsEnabled => _isEnabled;

  /// <inheritdoc />
  public event EventHandler<AudioOutputStateChangedEventArgs>? StateChanged;

  /// <summary>
  /// Event raised when a client connects to the stream.
  /// </summary>
  public event EventHandler<HttpStreamClientEventArgs>? ClientConnected;

  /// <summary>
  /// Event raised when a client disconnects from the stream.
  /// </summary>
  public event EventHandler<HttpStreamClientEventArgs>? ClientDisconnected;

  /// <summary>
  /// Gets the stream URL for clients to connect to.
  /// </summary>
  public string StreamUrl { get; private set; } = "";

  /// <summary>
  /// Gets the number of currently connected clients.
  /// </summary>
  public int ConnectedClientCount => _connectedClients.Count;

  /// <summary>
  /// Gets the configured port for the HTTP stream server.
  /// </summary>
  public int Port => _options.Port;

  /// <summary>
  /// Initializes a new instance of the <see cref="HttpStreamOutput"/> class.
  /// </summary>
  /// <param name="logger">The logger instance.</param>
  /// <param name="options">The HTTP stream output options.</param>
  /// <param name="audioEngine">The audio engine for getting the mixed output stream.</param>
  public HttpStreamOutput(
    ILogger<HttpStreamOutput> logger,
    IOptions<AudioOutputOptions> options,
    IAudioEngine audioEngine)
  {
    _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    _options = options?.Value?.HttpStream ?? throw new ArgumentNullException(nameof(options));
    _audioEngine = audioEngine ?? throw new ArgumentNullException(nameof(audioEngine));

    Id = $"http-stream-{Guid.NewGuid():N}";
    Name = $"HTTP Stream :{_options.Port}";
    _isEnabled = _options.Enabled;
  }

  /// <inheritdoc />
  public Task InitializeAsync(CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();

    if (State != AudioOutputState.Created && State != AudioOutputState.Error)
    {
      throw new InvalidOperationException(
        $"Cannot initialize output in state {State}. Output must be in Created or Error state.");
    }

    State = AudioOutputState.Initializing;

    try
    {
      _logger.LogInformation(
        "Initializing HTTP stream output on port {Port}",
        _options.Port);

      // Create the HTTP listener
      _httpListener = new HttpListener();
      var prefix = $"http://+:{_options.Port}{_options.EndpointPath}/";
      _httpListener.Prefixes.Add(prefix);

      // Build the stream URL
      var hostName = Dns.GetHostName();
      StreamUrl = $"http://{hostName}:{_options.Port}{_options.EndpointPath}";

      State = AudioOutputState.Ready;
      _logger.LogInformation(
        "HTTP stream output initialized. Stream URL: {StreamUrl}",
        StreamUrl);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to initialize HTTP stream output");
      State = AudioOutputState.Error;
      throw;
    }

    return Task.CompletedTask;
  }

  /// <inheritdoc />
  public Task StartAsync(CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();

    if (State != AudioOutputState.Ready && State != AudioOutputState.Stopped)
    {
      throw new InvalidOperationException(
        $"Cannot start output in state {State}. Output must be in Ready or Stopped state.");
    }

    if (_httpListener == null)
    {
      throw new InvalidOperationException("Output not initialized. Call InitializeAsync first.");
    }

    try
    {
      _logger.LogInformation("Starting HTTP stream server on port {Port}", _options.Port);

      _serverCts = new CancellationTokenSource();
      _httpListener.Start();

      // Start accepting connections
      _serverTask = AcceptConnectionsAsync(_serverCts.Token);

      _isEnabled = true;
      State = AudioOutputState.Streaming;

      _logger.LogInformation(
        "HTTP stream server started. Stream URL: {StreamUrl}",
        StreamUrl);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to start HTTP stream server");
      State = AudioOutputState.Error;
      throw;
    }

    return Task.CompletedTask;
  }

  /// <inheritdoc />
  public async Task StopAsync(CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();

    if (State != AudioOutputState.Streaming)
    {
      _logger.LogWarning("Stop requested but server is not streaming (state: {State})", State);
      return;
    }

    try
    {
      State = AudioOutputState.Stopping;
      _logger.LogInformation("Stopping HTTP stream server");

      // Cancel all client connections
      _serverCts?.Cancel();

      // Disconnect all clients
      foreach (var client in _connectedClients.Values)
      {
        client.Disconnect();
      }
      _connectedClients.Clear();

      // Stop the HTTP listener
      _httpListener?.Stop();

      // Wait for server task to complete
      if (_serverTask != null)
      {
        try
        {
          await _serverTask.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
        }
        catch (TimeoutException)
        {
          _logger.LogWarning("Server task did not complete within timeout");
        }
        catch (OperationCanceledException)
        {
          // Expected
        }
      }

      _serverCts?.Dispose();
      _serverCts = null;

      _isEnabled = false;
      State = AudioOutputState.Stopped;

      _logger.LogInformation("HTTP stream server stopped");
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error stopping HTTP stream server");
      State = AudioOutputState.Error;
      throw;
    }
  }

  /// <summary>
  /// Gets information about all connected clients.
  /// </summary>
  /// <returns>A list of connected client information.</returns>
  public IReadOnlyList<HttpStreamClientInfo> GetConnectedClients()
  {
    return _connectedClients.Values
      .Select(c => new HttpStreamClientInfo
      {
        ClientId = c.ClientId,
        RemoteEndpoint = c.RemoteEndpoint,
        ConnectedAt = c.ConnectedAt,
        BytesSent = c.BytesSent
      })
      .ToList();
  }

  private async Task AcceptConnectionsAsync(CancellationToken cancellationToken)
  {
    _logger.LogDebug("Starting to accept HTTP connections");

    while (!cancellationToken.IsCancellationRequested && _httpListener?.IsListening == true)
    {
      try
      {
        var context = await _httpListener.GetContextAsync().WaitAsync(cancellationToken);

        if (_connectedClients.Count >= _options.MaxConcurrentClients)
        {
          _logger.LogWarning(
            "Maximum clients reached ({Max}), rejecting connection from {Endpoint}",
            _options.MaxConcurrentClients,
            context.Request.RemoteEndPoint);

          context.Response.StatusCode = 503; // Service Unavailable
          context.Response.Close();
          continue;
        }

        // Handle the client connection on a separate task
        _ = HandleClientAsync(context, cancellationToken);
      }
      catch (OperationCanceledException)
      {
        break;
      }
      catch (HttpListenerException ex) when (ex.ErrorCode == 995) // Operation aborted
      {
        break;
      }
      catch (Exception ex)
      {
        _logger.LogWarning(ex, "Error accepting HTTP connection");
      }
    }

    _logger.LogDebug("Stopped accepting HTTP connections");
  }

  private async Task HandleClientAsync(HttpListenerContext context, CancellationToken cancellationToken)
  {
    var clientId = Guid.NewGuid().ToString("N");
    var remoteEndpoint = context.Request.RemoteEndPoint?.ToString() ?? "unknown";

    var client = new HttpStreamClient(clientId, remoteEndpoint, context.Response);

    _logger.LogInformation(
      "Client connected: {ClientId} from {Endpoint}",
      clientId, remoteEndpoint);

    _connectedClients.TryAdd(clientId, client);
    ClientConnected?.Invoke(this, new HttpStreamClientEventArgs { Client = client.ToInfo() });

    try
    {
      // Set up the response headers
      context.Response.ContentType = _options.ContentType;
      context.Response.SendChunked = true;
      context.Response.KeepAlive = true;

      // Write WAV header if using WAV format
      if (_options.ContentType == "audio/wav")
      {
        var header = CreateWavHeader(
          _options.SampleRate,
          _options.Channels,
          _options.BitsPerSample);
        await context.Response.OutputStream.WriteAsync(header, cancellationToken);
      }

      // Stream audio data to the client
      var audioStream = _audioEngine.GetMixedOutputStream();
      var buffer = new byte[_options.ClientBufferSize];

      while (!cancellationToken.IsCancellationRequested && client.IsConnected)
      {
        var bytesRead = await audioStream.ReadAsync(buffer, cancellationToken);

        if (bytesRead == 0)
        {
          // No data available, wait a bit
          await Task.Delay(10, cancellationToken);
          continue;
        }

        try
        {
          await context.Response.OutputStream.WriteAsync(
            buffer.AsMemory(0, bytesRead),
            cancellationToken);

          client.AddBytesSent(bytesRead);
        }
        catch (HttpListenerException)
        {
          // Client disconnected
          break;
        }
      }
    }
    catch (OperationCanceledException)
    {
      // Expected during shutdown
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Error streaming to client {ClientId}", clientId);
    }
    finally
    {
      client.Disconnect();
      _connectedClients.TryRemove(clientId, out _);

      try
      {
        context.Response.Close();
      }
      catch
      {
        // Ignore errors closing response
      }

      _logger.LogInformation(
        "Client disconnected: {ClientId} (sent {Bytes} bytes)",
        clientId, client.BytesSent);

      ClientDisconnected?.Invoke(this, new HttpStreamClientEventArgs { Client = client.ToInfo() });
    }
  }

  private static byte[] CreateWavHeader(int sampleRate, int channels, int bitsPerSample)
  {
    // Create a WAV header for streaming
    // For streaming, we use a very large data size (max int) since we don't know the final size
    var byteRate = sampleRate * channels * (bitsPerSample / 8);
    var blockAlign = channels * (bitsPerSample / 8);

    using var ms = new MemoryStream();
    using var writer = new BinaryWriter(ms);

    // RIFF header
    writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
    writer.Write(int.MaxValue); // File size (unknown for streaming)
    writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));

    // fmt subchunk
    writer.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
    writer.Write(16); // Subchunk size for PCM
    writer.Write((short)1); // Audio format (1 = PCM)
    writer.Write((short)channels);
    writer.Write(sampleRate);
    writer.Write(byteRate);
    writer.Write((short)blockAlign);
    writer.Write((short)bitsPerSample);

    // data subchunk
    writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
    writer.Write(int.MaxValue); // Data size (unknown for streaming)

    return ms.ToArray();
  }

  private void ThrowIfDisposed()
  {
    ObjectDisposedException.ThrowIf(_disposed, this);
  }

  /// <inheritdoc />
  public async ValueTask DisposeAsync()
  {
    if (_disposed)
    {
      return;
    }

    _disposed = true;
    _isEnabled = false;

    if (State == AudioOutputState.Streaming)
    {
      try
      {
        await StopAsync();
      }
      catch (Exception ex)
      {
        _logger.LogWarning(ex, "Error stopping server during dispose");
      }
    }

    _httpListener?.Close();
    _serverCts?.Dispose();

    State = AudioOutputState.Disposed;
    _logger.LogInformation("HTTP stream output disposed");
  }
}

/// <summary>
/// Represents a connected HTTP stream client.
/// </summary>
internal class HttpStreamClient
{
  private long _bytesSent;
  private bool _isConnected = true;

  public string ClientId { get; }
  public string RemoteEndpoint { get; }
  public DateTimeOffset ConnectedAt { get; }
  public HttpListenerResponse Response { get; }

  public long BytesSent => Interlocked.Read(ref _bytesSent);
  public bool IsConnected => _isConnected;

  public HttpStreamClient(string clientId, string remoteEndpoint, HttpListenerResponse response)
  {
    ClientId = clientId;
    RemoteEndpoint = remoteEndpoint;
    Response = response;
    ConnectedAt = DateTimeOffset.UtcNow;
  }

  public void AddBytesSent(int bytes)
  {
    Interlocked.Add(ref _bytesSent, bytes);
  }

  public void Disconnect()
  {
    _isConnected = false;
  }

  public HttpStreamClientInfo ToInfo() => new()
  {
    ClientId = ClientId,
    RemoteEndpoint = RemoteEndpoint,
    ConnectedAt = ConnectedAt,
    BytesSent = BytesSent
  };
}

/// <summary>
/// Information about a connected HTTP stream client.
/// </summary>
public record HttpStreamClientInfo
{
  /// <summary>
  /// Gets the unique client identifier.
  /// </summary>
  public required string ClientId { get; init; }

  /// <summary>
  /// Gets the remote endpoint address.
  /// </summary>
  public required string RemoteEndpoint { get; init; }

  /// <summary>
  /// Gets when the client connected.
  /// </summary>
  public required DateTimeOffset ConnectedAt { get; init; }

  /// <summary>
  /// Gets the number of bytes sent to this client.
  /// </summary>
  public required long BytesSent { get; init; }
}

/// <summary>
/// Event arguments for HTTP stream client events.
/// </summary>
public class HttpStreamClientEventArgs : EventArgs
{
  /// <summary>
  /// Gets the client information.
  /// </summary>
  public required HttpStreamClientInfo Client { get; init; }
}
