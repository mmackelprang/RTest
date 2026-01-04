using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

public class LibrespotManager : IDisposable
{
    private Process _librespotProcess;
    private Task _audioReadTask;
    private CancellationTokenSource _cts;
    private SpotifyTokenManager _tokenManager;
    private Timer _tokenRefreshTimer;
    private readonly string _librespotPath;
    
    // Configuration
    private string _deviceName = "MyAudioApp";
    private int _bitrate = 320;
    private bool _volumeNormalization = true;
    
    // State
    public bool IsRunning => _librespotProcess != null && !_librespotProcess.HasExited;
    public DeviceState State { get; private set; } = DeviceState.Stopped;
    
    // Events
    public event EventHandler<AudioDataEventArgs> AudioDataReceived;
    public event EventHandler<DeviceStateChangedEventArgs> StateChanged;
    public event EventHandler<string> LogMessage;
    
    public LibrespotManager(string librespotPath)
    {
        _librespotPath = librespotPath;
    }
    
    public async Task StartDeviceAsync(
        string clientId, 
        string clientSecret, 
        string refreshToken,
        string deviceName = null)
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
                _deviceName = deviceName;
            
            // Initialize token manager
            _tokenManager = new SpotifyTokenManager(clientId, clientSecret, refreshToken);
            
            // Get initial access token
            var accessToken = await _tokenManager.GetAccessTokenAsync();
            
            // Start librespot
            await StartLibrespotProcessAsync(accessToken);
            
            // Set up token refresh (every 50 minutes)
            _tokenRefreshTimer = new Timer(
                async _ => await RefreshTokenAndRestartAsync(),
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
            throw;
        }
    }
    
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
            _tokenRefreshTimer?.Dispose();
            _tokenRefreshTimer = null;
            
            // Cancel audio reading
            _cts?.Cancel();
            
            // Give librespot time to clean up gracefully
            if (_librespotProcess != null && !_librespotProcess.HasExited)
            {
                // Try graceful shutdown first
                try
                {
                    _librespotProcess.StandardInput.WriteLine("quit");
                    if (!_librespotProcess.WaitForExit(3000))
                    {
                        // Force kill if it doesn't exit
                        _librespotProcess.Kill();
                        _librespotProcess.WaitForExit(2000);
                    }
                }
                catch
                {
                    // If graceful fails, force kill
                    try { _librespotProcess.Kill(); } catch { }
                }
            }
            
            // Wait for audio read task to complete
            if (_audioReadTask != null)
            {
                await Task.WhenAny(_audioReadTask, Task.Delay(2000));
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
            throw;
        }
    }
    
    public async Task RestartDeviceAsync()
    {
        Log("Restarting Spotify device...");
        
        var wasRunning = IsRunning;
        if (wasRunning)
        {
            // Store current credentials
            var tokenManager = _tokenManager;
            
            await StopDeviceAsync();
            await Task.Delay(1000); // Brief pause for cleanup
            
            // Restart with same credentials
            if (tokenManager != null)
            {
                var token = await tokenManager.GetAccessTokenAsync();
                await StartLibrespotProcessAsync(token);
                _tokenManager = tokenManager;
                
                // Restart token refresh timer
                _tokenRefreshTimer = new Timer(
                    async _ => await RefreshTokenAndRestartAsync(),
                    null,
                    TimeSpan.FromMinutes(50),
                    TimeSpan.FromMinutes(50)
                );
                
                SetState(DeviceState.Running);
            }
        }
    }
    
    private async Task StartLibrespotProcessAsync(string accessToken)
    {
        _cts = new CancellationTokenSource();
        
        var args = BuildLibrespotArguments(accessToken);
        
        var startInfo = new ProcessStartInfo
        {
            FileName = _librespotPath,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true,
            WorkingDirectory = System.IO.Path.GetDirectoryName(_librespotPath)
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
        
        // Start reading audio stream
        _audioReadTask = Task.Run(() => ReadAudioStreamAsync(_cts.Token), _cts.Token);
    }
    
    private string BuildLibrespotArguments(string accessToken)
    {
        var args = new System.Text.StringBuilder();
        args.Append($"--name \"{_deviceName}\" ");
        args.Append($"--backend pipe ");
        args.Append($"--device - ");  // stdout
        args.Append($"--access-token \"{accessToken}\" ");
        args.Append($"--bitrate {_bitrate} ");
        
        if (_volumeNormalization)
            args.Append($"--enable-volume-normalisation ");
        
        args.Append($"--initial-volume 100 ");
        args.Append($"--cache-size-limit 1024 ");  // 1GB cache
        
        return args.ToString();
    }
    
    private async Task ReadAudioStreamAsync(CancellationToken ct)
    {
        var stream = _librespotProcess?.StandardOutput?.BaseStream;
        if (stream == null) return;
        
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
                
                var audioData = new byte[bytesRead];
                Buffer.BlockCopy(buffer, 0, audioData, 0, bytesRead);
                
                AudioDataReceived?.Invoke(this, new AudioDataEventArgs(audioData));
            }
        }
        catch (OperationCanceledException)
        {
            // Normal cancellation
        }
        catch (Exception ex)
        {
            Log($"Audio stream error: {ex.Message}");
            SetState(DeviceState.Error);
        }
    }
    
    private async Task RefreshTokenAndRestartAsync()
    {
        try
        {
            Log("Refreshing access token...");
            
            var newToken = await _tokenManager.GetAccessTokenAsync();
            
            // Brief pause in playback for token refresh
            SetState(DeviceState.Reconnecting);
            
            await StopDeviceAsync();
            await Task.Delay(500);
            await StartLibrespotProcessAsync(newToken);
            
            SetState(DeviceState.Running);
            Log("Token refreshed and device reconnected");
        }
        catch (Exception ex)
        {
            Log($"Token refresh failed: {ex.Message}");
            SetState(DeviceState.Error);
        }
    }
    
    private void OnLibrespotLogReceived(object sender, DataReceivedEventArgs e)
    {
        if (!string.IsNullOrEmpty(e.Data))
        {
            Log($"[Librespot] {e.Data}");
            
            // Parse status from logs
            if (e.Data.Contains("Loading track"))
                SetState(DeviceState.Playing);
            else if (e.Data.Contains("Connection error") || e.Data.Contains("Error"))
                SetState(DeviceState.Error);
        }
    }
    
    private void OnLibrespotExited(object sender, EventArgs e)
    {
        var exitCode = _librespotProcess?.ExitCode ?? -1;
        Log($"Librespot exited with code {exitCode}");
        
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
        Console.WriteLine($"[LibrespotManager] {message}");
        LogMessage?.Invoke(this, message);
    }
    
    public void Dispose()
    {
        StopDeviceAsync().Wait();
    }
}

public enum DeviceState
{
    Stopped,
    Starting,
    Running,
    Playing,
    Stopping,
    Reconnecting,
    Error
}

public class DeviceStateChangedEventArgs : EventArgs
{
    public DeviceState OldState { get; }
    public DeviceState NewState { get; }
    
    public DeviceStateChangedEventArgs(DeviceState oldState, DeviceState newState)
    {
        OldState = oldState;
        NewState = newState;
    }
}

public class AudioDataEventArgs : EventArgs
{
    public byte[] AudioData { get; }
    public DateTime Timestamp { get; }
    
    public AudioDataEventArgs(byte[] audioData)
    {
        AudioData = audioData;
        Timestamp = DateTime.UtcNow;
    }
}
