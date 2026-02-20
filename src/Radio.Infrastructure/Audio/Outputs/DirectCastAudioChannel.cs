using Microsoft.Extensions.Logging;
using Sharpcaster.Channels;

namespace Radio.Infrastructure.Audio.Outputs;

/// <summary>
/// Custom SharpCaster channel for sending audio data directly over the Cast protocol.
/// Extends <see cref="ChromecastChannel"/> to use a custom namespace for audio streaming.
/// </summary>
/// <remarks>
/// The Cast protocol supports custom message buses identified by namespace URIs.
/// This channel sends Base64-encoded WAV audio chunks as JSON messages, and the
/// custom receiver app decodes and plays them using the Web Audio API.
///
/// Message format sent to receiver:
/// <code>
/// { "type": "audio", "data": "&lt;base64-wav&gt;", "seq": N }
/// </code>
///
/// The receiver can also send back status messages on this namespace, which are
/// handled by <see cref="OnMessageReceived"/>.
/// </remarks>
public class DirectCastAudioChannel : ChromecastChannel
{
  private readonly ILogger _logger;

  /// <summary>
  /// Event raised when the receiver sends a message back on this channel.
  /// </summary>
  public event EventHandler<string>? MessageReceived;

  /// <summary>
  /// Initializes a new instance of the <see cref="DirectCastAudioChannel"/> class.
  /// </summary>
  /// <param name="ns">The custom namespace URI (e.g., "urn:x-cast:com.radioconsole.audio").</param>
  /// <param name="logger">Logger instance.</param>
  public DirectCastAudioChannel(string ns, ILogger logger)
    : base(ns, logger, useBaseNamespace: false)
  {
    _logger = logger;
  }

  /// <summary>
  /// Sends a message payload to the receiver on this channel's namespace.
  /// </summary>
  /// <param name="payload">JSON message payload.</param>
  /// <param name="destinationId">The receiver's transport ID.</param>
  public async Task SendMessageAsync(string payload, string destinationId)
  {
    await SendAsync(payload, destinationId);
  }

  /// <summary>
  /// Called when the receiver sends a message back on this channel's namespace.
  /// </summary>
  /// <param name="messagePayload">The received JSON message.</param>
  /// <param name="type">The message type.</param>
  public override void OnMessageReceived(string messagePayload, string type)
  {
    _logger.LogDebug("DirectCastAudioChannel received: {Type} — {Payload}", type, messagePayload);
    MessageReceived?.Invoke(this, messagePayload);
  }
}
