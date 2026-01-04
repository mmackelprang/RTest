public class SmartSpotifyDevice
{
    private LibrespotManager _device;
    private Timer _idleTimer;
    private readonly TimeSpan _idleTimeout = TimeSpan.FromMinutes(10);
    private DateTime _lastActivity;
    private readonly string _librespotPath;
    private string _clientId;
    private string _clientSecret;
    private string _refreshToken;
    
    public SmartSpotifyDevice(string librespotPath)
    {
        _librespotPath = librespotPath;
    }
    
    public async Task InitializeAsync(string clientId, string clientSecret, string refreshToken)
    {
        _clientId = clientId;
        _clientSecret = clientSecret;
        _refreshToken = refreshToken;
        
        // Device starts disabled - only starts when needed
    }
    
    public async Task EnsureDeviceRunningAsync()
    {
        if (_device?.IsRunning != true)
        {
            Console.WriteLine("Starting Spotify device on-demand...");
            
            _device = new LibrespotManager(_librespotPath);
            _device.AudioDataReceived += OnAudioData;
            
            await _device.StartDeviceAsync(_clientId, _clientSecret, _refreshToken);
            
            // Start idle monitor
            _idleTimer = new Timer(
                _ => CheckIdleTimeout(),
                null,
                TimeSpan.FromMinutes(1),
                TimeSpan.FromMinutes(1)
            );
        }
        
        _lastActivity = DateTime.UtcNow;
    }
    
    private void OnAudioData(object sender, AudioDataEventArgs e)
    {
        _lastActivity = DateTime.UtcNow;
        // Process audio...
    }
    
    private async void CheckIdleTimeout()
    {
        if (_device?.IsRunning == true)
        {
            var idleDuration = DateTime.UtcNow - _lastActivity;
            
            if (idleDuration > _idleTimeout)
            {
                Console.WriteLine($"Shutting down idle Spotify device (idle for {idleDuration.TotalMinutes:F1} min)");
                await _device.StopDeviceAsync();
                _idleTimer?.Dispose();
            }
        }
    }
    
    public async Task ShutdownAsync()
    {
        _idleTimer?.Dispose();
        if (_device != null)
        {
            await _device.StopDeviceAsync();
            _device.Dispose();
        }
    }
}
