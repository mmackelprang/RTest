using System.Management;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Windows.Devices.Enumeration;
using Windows.Media.Audio;

namespace Radio.Tools.BtSender;

/// <summary>
/// Sends a diagnostic tone (200Hz L / 300Hz R) to a Bluetooth audio device via WASAPI.
/// Automatically connects the BT A2DP profile if needed.
/// Usage: dotnet run --project tools/Radio.Tools.BtSender "Grandpas Radio"
/// </summary>
public static class Program
{
  private const int SampleRate = 48000;
  private const int Channels = 2;
  private const int LeftHz = 200;
  private const int RightHz = 300;
  private const float Amplitude = 0.8f;

  public static async Task<int> Main(string[] args)
  {
    if (args.Length == 0)
    {
      Console.WriteLine("BT Diagnostic Tone Sender");
      Console.WriteLine("Usage: Radio.Tools.BtSender <device-name-substring> [--duration <seconds>]");
      Console.WriteLine();
      Console.WriteLine("Available render devices:");
      ListDevices();
      Console.WriteLine();
      Console.WriteLine("Paired BT audio devices:");
      await ListBluetoothDevicesAsync();
      return 1;
    }

    var searchTerm = args[0];
    var duration = ParseDuration(args);

    Console.WriteLine($"Searching for render device matching: \"{searchTerm}\"");

    var device = FindDevice(searchTerm);
    if (device == null)
    {
      Console.WriteLine($"Device not active as WASAPI endpoint. Attempting BT connect...");
      var connected = await TryConnectBluetoothAsync(searchTerm);
      if (connected)
      {
        // Wait for WASAPI endpoint to appear
        Console.WriteLine("Waiting for audio endpoint to become active...");
        for (var i = 0; i < 30; i++)
        {
          await Task.Delay(500);
          device = FindDevice(searchTerm);
          if (device != null) break;
        }
      }

      if (device == null)
      {
        Console.Error.WriteLine($"No render device found matching \"{searchTerm}\" after BT connect attempt.");
        Console.WriteLine();
        Console.WriteLine("Available render devices:");
        ListDevices();
        return 1;
      }
    }

    Console.WriteLine($"Found device: {device.FriendlyName}");
    Console.WriteLine($"Sending {LeftHz}Hz (L) / {RightHz}Hz (R) diagnostic tone at {SampleRate}Hz");
    if (duration.HasValue)
      Console.WriteLine($"Duration: {duration.Value} seconds");
    else
      Console.WriteLine("Press Ctrl+C to stop.");
    Console.WriteLine();

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
      e.Cancel = true;
      cts.Cancel();
    };

    if (duration.HasValue)
      cts.CancelAfter(TimeSpan.FromSeconds(duration.Value));

    PlayDiagnosticTone(device, cts.Token);
    Console.WriteLine("Stopped.");
    return 0;
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

  private static async Task ListBluetoothDevicesAsync()
  {
    try
    {
      // Find paired BT audio devices that support AudioPlaybackConnection
      var selector = AudioPlaybackConnection.GetDeviceSelector();
      var devices = await DeviceInformation.FindAllAsync(selector);

      foreach (var d in devices)
      {
        Console.WriteLine($"  {d.Name} (id: {d.Id[..Math.Min(40, d.Id.Length)]}...)");
      }

      if (devices.Count == 0)
        Console.WriteLine("  (none — ensure BT device is paired)");
    }
    catch (Exception ex)
    {
      Console.WriteLine($"  (error listing BT devices: {ex.Message})");
    }
  }

  private static MMDevice? FindDevice(string searchTerm)
  {
    using var enumerator = new MMDeviceEnumerator();
    var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);

    return devices.FirstOrDefault(d =>
      d.FriendlyName.Contains(searchTerm, StringComparison.OrdinalIgnoreCase));
  }

  /// <summary>
  /// Attempts to connect to a BT device using AudioPlaybackConnection WinRT API.
  /// This establishes an A2DP source connection so audio can be sent to the device.
  /// Note: Requires the app to have MSIX identity or be running on Windows 10 20H1+.
  /// </summary>
  private static async Task<bool> TryConnectBluetoothAsync(string searchTerm)
  {
    try
    {
      var selector = AudioPlaybackConnection.GetDeviceSelector();
      var devices = await DeviceInformation.FindAllAsync(selector);

      var target = devices.FirstOrDefault(d =>
        d.Name.Contains(searchTerm, StringComparison.OrdinalIgnoreCase));

      if (target == null)
      {
        Console.WriteLine($"No paired BT audio device matching \"{searchTerm}\".");
        Console.WriteLine("Paired BT audio devices:");
        foreach (var d in devices)
          Console.WriteLine($"  {d.Name}");
        return false;
      }

      Console.WriteLine($"Found paired BT device: {target.Name}");
      Console.WriteLine("Opening AudioPlaybackConnection...");

      var connection = AudioPlaybackConnection.TryCreateFromId(target.Id);
      if (connection == null)
      {
        Console.WriteLine("Failed to create AudioPlaybackConnection (may need MSIX identity).");
        Console.WriteLine("Falling back to manual connection check...");
        return await TryConnectViaDeviceManagerAsync(searchTerm);
      }

      // Start the A2DP connection
      connection.Start();
      var result = await connection.OpenAsync();
      Console.WriteLine($"AudioPlaybackConnection status: {result.Status}");

      if (result.Status == AudioPlaybackConnectionOpenResultStatus.Success)
      {
        Console.WriteLine("BT A2DP connection established successfully.");
        return true;
      }

      Console.WriteLine($"AudioPlaybackConnection failed. Extended error: {result.ExtendedError?.Message}");
      Console.WriteLine("Falling back to device enable approach...");
      return await TryConnectViaDeviceManagerAsync(searchTerm);
    }
    catch (Exception ex)
    {
      Console.WriteLine($"AudioPlaybackConnection error: {ex.Message}");
      Console.WriteLine("Falling back to device enable approach...");
      return await TryConnectViaDeviceManagerAsync(searchTerm);
    }
  }

  /// <summary>
  /// Fallback: uses WMI to enable the BT audio endpoint device.
  /// </summary>
  private static async Task<bool> TryConnectViaDeviceManagerAsync(string searchTerm)
  {
    try
    {
      // Find the BT audio endpoint that's currently disconnected
      using var enumerator = new MMDeviceEnumerator();
      var allDevices = enumerator.EnumerateAudioEndPoints(DataFlow.Render,
        DeviceState.Active | DeviceState.Unplugged | DeviceState.Disabled);

      var btDevice = allDevices.FirstOrDefault(d =>
        d.FriendlyName.Contains(searchTerm, StringComparison.OrdinalIgnoreCase));

      if (btDevice == null)
      {
        Console.WriteLine("BT device not found in any audio endpoint state.");
        return false;
      }

      Console.WriteLine($"Found endpoint: {btDevice.FriendlyName} (State: {btDevice.State})");

      if (btDevice.State == DeviceState.Active)
        return true;

      // The device exists but is not active. Windows needs a BT profile connection.
      // Try using pnputil / devcon approach via WMI
      Console.WriteLine("Device is not active. Attempting to trigger BT connection via WMI...");

      var query = new SelectQuery("Win32_PnPEntity",
        $"Name LIKE '%{searchTerm.Replace("'", "''")}%' AND PNPClass = 'MEDIA'");
      using var searcher = new ManagementObjectSearcher(query);
      foreach (ManagementObject obj in searcher.Get())
      {
        var name = obj["Name"]?.ToString();
        var deviceId = obj["DeviceID"]?.ToString();
        Console.WriteLine($"  Found PnP device: {name} ({deviceId})");

        // Try to enable the device
        try
        {
          var result = obj.InvokeMethod("Enable", null);
          Console.WriteLine($"  Enable result: {result}");
          await Task.Delay(2000);
          return true;
        }
        catch (Exception ex)
        {
          Console.WriteLine($"  Enable failed: {ex.Message}");
        }
      }

      Console.WriteLine("Could not auto-connect. Please connect the device manually via Windows BT settings.");
      return false;
    }
    catch (Exception ex)
    {
      Console.WriteLine($"Device manager fallback error: {ex.Message}");
      return false;
    }
  }

  private static void PlayDiagnosticTone(MMDevice device, CancellationToken ct)
  {
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
