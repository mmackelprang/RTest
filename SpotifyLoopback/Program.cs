class Program
{
    static async Task Main(string[] args)
    {
        var audioManager = new AudioDeviceManager();
        
        // Start Spotify device when needed
        Console.WriteLine("Starting Spotify device...");
        await audioManager.EnableSpotifyAsync(
            librespotPath: @"C:\path\to\librespot.exe",
            clientId: "your_client_id",
            clientSecret: "your_client_secret",
            refreshToken: "your_refresh_token",
            deviceName: "My Audio App"
        );
        
        Console.WriteLine("Spotify device is now available in the Spotify app!");
        Console.WriteLine("Play music from any Spotify client...");
        Console.WriteLine();
        Console.WriteLine("Press 'D' to disable Spotify");
        Console.WriteLine("Press 'E' to enable Spotify");
        Console.WriteLine("Press 'Q' to quit");
        
        while (true)
        {
            var key = Console.ReadKey(true);
            
            if (key.Key == ConsoleKey.D)
            {
                await audioManager.DisableSpotifyAsync();
                Console.WriteLine("Spotify disabled");
            }
            else if (key.Key == ConsoleKey.E)
            {
                await audioManager.EnableSpotifyAsync(
                    @"C:\path\to\librespot.exe",
                    "your_client_id",
                    "your_client_secret",
                    "your_refresh_token"
                );
                Console.WriteLine("Spotify enabled");
            }
            else if (key.Key == ConsoleKey.Q)
            {
                break;
            }
        }
        
        // Clean shutdown
        Console.WriteLine("Shutting down...");
        await audioManager.DisableSpotifyAsync();
    }
}
