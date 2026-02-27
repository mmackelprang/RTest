using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Radio.Tools.BtSender;

/// <summary>
/// Sends a diagnostic tone (200Hz L / 300Hz R) to a Bluetooth audio device via WASAPI.
/// Usage: dotnet run --project tools/Radio.Tools.BtSender "Grandpas Radio"
/// </summary>
public static class Program
{
  private const int SampleRate = 48000;
  private const int Channels = 2;
  private const int LeftHz = 200;
  private const int RightHz = 300;
  private const float Amplitude = 0.8f;

  public static int Main(string[] args)
  {
    if (args.Length == 0)
    {
      Console.WriteLine("BT Diagnostic Tone Sender");
      Console.WriteLine("Usage: Radio.Tools.BtSender <device-name-substring>");
      Console.WriteLine();
      Console.WriteLine("Available render devices:");
      ListDevices();
      return 1;
    }

    var searchTerm = args[0];
    Console.WriteLine($"Searching for render device matching: \"{searchTerm}\"");

    var device = FindDevice(searchTerm);
    if (device == null)
    {
      Console.Error.WriteLine($"No render device found matching \"{searchTerm}\".");
      Console.WriteLine();
      Console.WriteLine("Available render devices:");
      ListDevices();
      return 1;
    }

    Console.WriteLine($"Found device: {device.FriendlyName}");
    Console.WriteLine($"Sending {LeftHz}Hz (L) / {RightHz}Hz (R) diagnostic tone at {SampleRate}Hz");
    Console.WriteLine("Press Ctrl+C to stop.");
    Console.WriteLine();

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
      e.Cancel = true;
      cts.Cancel();
    };

    PlayDiagnosticTone(device, cts.Token);
    Console.WriteLine("Stopped.");
    return 0;
  }

  private static void ListDevices()
  {
    using var enumerator = new MMDeviceEnumerator();
    var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
    for (var i = 0; i < devices.Count; i++)
    {
      Console.WriteLine($"  [{i}] {devices[i].FriendlyName}");
    }
  }

  private static MMDevice? FindDevice(string searchTerm)
  {
    using var enumerator = new MMDeviceEnumerator();
    var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);

    return devices.FirstOrDefault(d =>
      d.FriendlyName.Contains(searchTerm, StringComparison.OrdinalIgnoreCase));
  }

  private static void PlayDiagnosticTone(MMDevice device, CancellationToken ct)
  {
    // Generate 1 second of loopable diagnostic tone
    var toneProvider = new DiagnosticToneProvider(SampleRate, Channels, LeftHz, RightHz, Amplitude);

    using var wasapiOut = new WasapiOut(device, AudioClientShareMode.Shared, useEventSync: true, latency: 200);
    wasapiOut.Init(toneProvider);
    wasapiOut.Play();

    try
    {
      while (wasapiOut.PlaybackState == PlaybackState.Playing && !ct.IsCancellationRequested)
      {
        Thread.Sleep(100);
      }
    }
    catch (OperationCanceledException) { }

    wasapiOut.Stop();
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
