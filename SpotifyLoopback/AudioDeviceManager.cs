public class AudioDeviceManager
{
    private LibrespotManager _spotifyDevice;
    private NAudio.Wave.BufferedWaveProvider _spotifyWaveProvider;
    private NAudio.Wave.WaveOut _spotifyOutput;
    private bool _spotifyEnabled = false;
    
    public bool IsSpotifyDeviceRunning => _spotifyDevice?.IsRunning ?? false;
    
    public async Task EnableSpotifyAsync(
        string librespotPath,
        string clientId,
        string clientSecret,
        string refreshToken,
        string deviceName = "MyAudioApp")
    {
        if (_spotifyEnabled)
            return;
        
        Console.WriteLine("Enabling Spotify device...");
        
        // Initialize manager
        _spotifyDevice = new LibrespotManager(librespotPath);
        
        // Set up event handlers
        _spotifyDevice.StateChanged += OnSpotifyStateChanged;
        _spotifyDevice.LogMessage += (s, msg) => Console.WriteLine($"[Spotify] {msg}");
        _spotifyDevice.AudioDataReceived += OnSpotifyAudioReceived;
        
        // Set up audio pipeline
        SetupSpotifyAudioPipeline();
        
        // Start the device
        await _spotifyDevice.StartDeviceAsync(clientId, clientSecret, refreshToken, deviceName);
        
        _spotifyEnabled = true;
        Console.WriteLine("Spotify device enabled and ready");
    }
    
    public async Task DisableSpotifyAsync()
    {
        if (!_spotifyEnabled)
            return;
        
        Console.WriteLine("Disabling Spotify device...");
        
        // Stop audio output
        _spotifyOutput?.Stop();
        _spotifyOutput?.Dispose();
        _spotifyOutput = null;
        _spotifyWaveProvider = null;
        
        // Stop device
        if (_spotifyDevice != null)
        {
            await _spotifyDevice.StopDeviceAsync();
            _spotifyDevice.Dispose();
            _spotifyDevice = null;
        }
        
        _spotifyEnabled = false;
        Console.WriteLine("Spotify device disabled");
    }
    
    private void SetupSpotifyAudioPipeline()
    {
        var waveFormat = new NAudio.Wave.WaveFormat(44100, 16, 2);
        
        _spotifyWaveProvider = new NAudio.Wave.BufferedWaveProvider(waveFormat)
        {
            BufferDuration = TimeSpan.FromSeconds(3),
            DiscardOnBufferOverflow = true
        };
        
        _spotifyOutput = new NAudio.Wave.WaveOut();
        _spotifyOutput.Init(_spotifyWaveProvider);
        _spotifyOutput.Play();
    }
    
    private void OnSpotifyAudioReceived(object sender, AudioDataEventArgs e)
    {
        // Add to playback buffer
        _spotifyWaveProvider?.AddSamples(e.AudioData, 0, e.AudioData.Length);
        
        // Send to visualization
        ProcessAudioForVisualization(e.AudioData);
        
        // Could also route to different outputs, apply effects, etc.
    }
    
    private void OnSpotifyStateChanged(object sender, DeviceStateChangedEventArgs e)
    {
        Console.WriteLine($"Spotify state: {e.OldState} -> {e.NewState}");
        
        switch (e.NewState)
        {
            case DeviceState.Running:
                // Device is ready - maybe show in UI
                break;
            case DeviceState.Playing:
                // Track is playing
                break;
            case DeviceState.Error:
                // Handle error - maybe try to restart
                Task.Run(async () => await _spotifyDevice?.RestartDeviceAsync());
                break;
            case DeviceState.Stopped:
                // Device stopped
                break;
        }
    }
    
    private void ProcessAudioForVisualization(byte[] audioData)
    {
        // Your visualization logic here
        var sampleCount = audioData.Length / 2;
        var samples = new float[sampleCount];
        
        for (int i = 0; i < sampleCount; i++)
        {
            short sample = BitConverter.ToInt16(audioData, i * 2);
            samples[i] = sample / 32768f;
        }
        
        // UpdateVisualization(samples);
    }
    
    public void RouteSpotifyToOutput(string outputDeviceName)
    {
        if (_spotifyOutput != null)
        {
            // Change output device
            // This depends on your audio routing system
        }
    }
    
    public void SetSpotifyVolume(float volume)
    {
        if (_spotifyOutput != null)
        {
            _spotifyOutput.Volume = Math.Max(0f, Math.Min(1f, volume));
        }
    }
}
