using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Radio.Tools.BtSender;

/// <summary>
/// Sends a diagnostic tone (200Hz L / 300Hz R) to a Bluetooth audio device via WASAPI.
/// Automatically reconnects on device loss or WASAPI errors.
/// Usage: dotnet run --project tools/Radio.Tools.BtSender "Grandpas Radio"
/// </summary>
public static class Program
{
  private const int SampleRate = 48000;
  private const int Channels = 2;
  private const int LeftHz = 200;
  private const int RightHz = 300;
  private const float Amplitude = 0.8f;
  private const int ReconnectDelayMs = 3000;
  private const int MaxReconnectAttempts = 120; // 6 minutes at 3s intervals

  public static async Task<int> Main(string[] args)
  {
    if (args.Length == 0)
    {
      Console.WriteLine("BT Diagnostic Tone Sender");
      Console.WriteLine("Usage: Radio.Tools.BtSender <device-name-substring> [--duration <seconds>]");
      Console.WriteLine();
      Console.WriteLine("Available render devices:");
      ListDevices();
      return 1;
    }

    var searchTerm = args[0];
    var duration = ParseDuration(args);

    Console.WriteLine($"BtSender started");
    Console.WriteLine($"Searching for render device matching: \"{searchTerm}\"");

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
      e.Cancel = true;
      cts.Cancel();
    };

    if (duration.HasValue)
      cts.CancelAfter(TimeSpan.FromSeconds(duration.Value));

    // Main resilience loop: find device → play → on failure, wait and retry
    await PlayWithReconnectAsync(searchTerm, cts.Token);

    Console.WriteLine("Stopped.");
    return 0;
  }

  /// <summary>
  /// Outer resilience loop. Finds the WASAPI device, starts playback, and
  /// automatically reconnects if the device disappears or WASAPI errors out.
  /// </summary>
  private static async Task PlayWithReconnectAsync(string searchTerm, CancellationToken ct)
  {
    var reconnectCount = 0;

    while (!ct.IsCancellationRequested)
    {
      var device = FindDevice(searchTerm);
      if (device == null)
      {
        if (reconnectCount == 0)
          Console.WriteLine($"Device not found. Waiting for \"{searchTerm}\" to become available...");

        reconnectCount++;
        if (reconnectCount > MaxReconnectAttempts)
        {
          Console.Error.WriteLine($"Device not found after {MaxReconnectAttempts} attempts. Giving up.");
          Console.WriteLine("Available render devices:");
          ListDevices();
          return;
        }

        try { await Task.Delay(ReconnectDelayMs, ct); }
        catch (OperationCanceledException) { return; }
        continue;
      }

      if (reconnectCount > 0)
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Device recovered after {reconnectCount} retries");
      else
        Console.WriteLine($"Found device: {device.FriendlyName}");

      reconnectCount = 0;
      Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Sending {LeftHz}Hz (L) / {RightHz}Hz (R) diagnostic tone at {SampleRate}Hz");

      var (exitReason, error) = PlayDiagnosticTone(device, ct);

      if (ct.IsCancellationRequested)
        return;

      // Playback stopped unexpectedly — log and retry
      Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Playback stopped: {exitReason}");
      if (error != null)
        Console.WriteLine($"  Error: {error.Message}");
      Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Reconnecting in {ReconnectDelayMs}ms...");

      try { await Task.Delay(ReconnectDelayMs, ct); }
      catch (OperationCanceledException) { return; }
    }
  }

  private static (string Reason, Exception? Error) PlayDiagnosticTone(MMDevice device, CancellationToken ct)
  {
    WasapiOut? wasapiOut = null;
    Exception? playbackError = null;
    var stoppedEvent = new ManualResetEventSlim(false);

    try
    {
      var toneProvider = new DiagnosticToneProvider(SampleRate, Channels, LeftHz, RightHz, Amplitude);

      wasapiOut = new WasapiOut(device, AudioClientShareMode.Shared, useEventSync: true, latency: 200);
      wasapiOut.PlaybackStopped += (_, e) =>
      {
        playbackError = e.Exception;
        stoppedEvent.Set();
      };
      wasapiOut.Init(toneProvider);
      wasapiOut.Play();

      // Wait for either cancellation or unexpected playback stop.
      // Poll periodically so we can also detect device removal.
      while (!ct.IsCancellationRequested)
      {
        if (stoppedEvent.Wait(500))
        {
          // PlaybackStopped fired
          return playbackError != null
            ? ("WASAPI error", playbackError)
            : ("playback ended", null);
        }

        // Check if WASAPI is still in Playing state
        if (wasapiOut.PlaybackState != PlaybackState.Playing)
          return ("state changed to " + wasapiOut.PlaybackState, null);

        // Check if the device is still active
        try
        {
          // Accessing AudioSessionControl will throw if the device is gone
          _ = device.AudioEndpointVolume.MasterVolumeLevelScalar;
        }
        catch (Exception ex)
        {
          return ("device lost", ex);
        }
      }

      return ("cancelled", null);
    }
    catch (Exception ex)
    {
      return ("exception during setup/play", ex);
    }
    finally
    {
      try { wasapiOut?.Stop(); } catch { /* ignore */ }
      try { wasapiOut?.Dispose(); } catch { /* ignore */ }
      stoppedEvent.Dispose();
    }
  }

  private static int? ParseDuration(string[] args)
  {
    for (var i = 0; i < args.Length - 1; i++)
    {
      if (args[i] == "--duration" && int.TryParse(args[i + 1], out var seconds))
        return seconds;
    }
    return null;
  }

  private static void ListDevices()
  {
    using var enumerator = new MMDeviceEnumerator();
    var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
    for (var i = 0; i < devices.Count; i++)
    {
      Console.WriteLine($"  [{i}] {devices[i].FriendlyName}");
    }

    if (devices.Count == 0)
      Console.WriteLine("  (none)");
  }

  private static MMDevice? FindDevice(string searchTerm)
  {
    try
    {
      using var enumerator = new MMDeviceEnumerator();
      var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);

      return devices.FirstOrDefault(d =>
        d.FriendlyName.Contains(searchTerm, StringComparison.OrdinalIgnoreCase));
    }
    catch
    {
      return null;
    }
  }

  /// <summary>
  /// Infinite looping wave provider that generates the diagnostic tone.
  /// </summary>
  private sealed class DiagnosticToneProvider : ISampleProvider
  {
    private readonly int _sampleRate;
    private readonly int _channels;
    private readonly int _leftHz;
    private readonly int _rightHz;
    private readonly float _amplitude;
    private long _sampleIndex;

    public WaveFormat WaveFormat { get; }

    public DiagnosticToneProvider(int sampleRate, int channels, int leftHz, int rightHz, float amplitude)
    {
      _sampleRate = sampleRate;
      _channels = channels;
      _leftHz = leftHz;
      _rightHz = rightHz;
      _amplitude = amplitude;
      WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
    }

    public int Read(float[] buffer, int offset, int count)
    {
      var frames = count / _channels;
      for (var i = 0; i < frames; i++)
      {
        var idx = offset + i * _channels;
        buffer[idx] = (float)(Math.Sin(2.0 * Math.PI * _leftHz * _sampleIndex / _sampleRate) * _amplitude);
        if (_channels > 1)
          buffer[idx + 1] = (float)(Math.Sin(2.0 * Math.PI * _rightHz * _sampleIndex / _sampleRate) * _amplitude);
        _sampleIndex++;
      }
      return count;
    }
  }
}
