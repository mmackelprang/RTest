using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using Radio.API.Hubs;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Interfaces.External;
using Radio.Infrastructure.External;

namespace Radio.API.Services;

/// <summary>
/// Background service that integrates with the RotaryPhone server.
/// On incoming call: plays ring sound + TTS caller name announcement with ducking.
/// Broadcasts phone call state changes to Web UI via SignalR.
/// </summary>
public class PhoneCallIntegrationService : BackgroundService
{
  private readonly ILogger<PhoneCallIntegrationService> _logger;
  private readonly IPhoneIntegrationService _phoneClient;
  private readonly PhoneContactLookupService _contactLookup;
  private readonly IAnnouncementService _announcementService;
  private readonly IHubContext<AudioStateHub> _hubContext;
  private readonly IOptions<PhoneIntegrationOptions> _options;

  public PhoneCallIntegrationService(
    ILogger<PhoneCallIntegrationService> logger,
    IPhoneIntegrationService phoneClient,
    PhoneContactLookupService contactLookup,
    IAnnouncementService announcementService,
    IHubContext<AudioStateHub> hubContext,
    IOptions<PhoneIntegrationOptions> options)
  {
    _logger = logger;
    _phoneClient = phoneClient;
    _contactLookup = contactLookup;
    _announcementService = announcementService;
    _hubContext = hubContext;
    _options = options;
  }

  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    var opts = _options.Value;
    if (!opts.Enabled)
    {
      _logger.LogInformation("Phone call integration is disabled");
      return;
    }

    _logger.LogInformation("Starting phone call integration (hub: {Url})", opts.HubUrl);

    _phoneClient.CallStateChanged += OnCallStateChanged;

    var retryDelay = TimeSpan.FromSeconds(5);
    var maxRetryDelay = TimeSpan.FromMinutes(2);

    while (!stoppingToken.IsCancellationRequested)
    {
      try
      {
        await _phoneClient.StartAsync(stoppingToken);

        // Keep running until cancellation
        await Task.Delay(Timeout.Infinite, stoppingToken);
      }
      catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
      {
        break;
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Phone call integration service failed, retrying in {Delay}s", retryDelay.TotalSeconds);

        try { await _phoneClient.StopAsync(); } catch { /* cleanup best-effort */ }

        try { await Task.Delay(retryDelay, stoppingToken); }
        catch (OperationCanceledException) { break; }

        retryDelay = TimeSpan.FromSeconds(Math.Min(retryDelay.TotalSeconds * 2, maxRetryDelay.TotalSeconds));
      }
    }

    _phoneClient.CallStateChanged -= OnCallStateChanged;
    try { await _phoneClient.StopAsync(); } catch { /* cleanup best-effort */ }
    _logger.LogInformation("Phone call integration service stopped");
  }

  private async void OnCallStateChanged(object? sender, PhoneCallStateChangedEventArgs e)
  {
    try
    {
      // Broadcast state to Web UI
      await BroadcastPhoneStateAsync(e);

      switch (e.State)
      {
        case PhoneCallState.Ringing:
          await HandleIncomingCallAsync(e);
          break;

        case PhoneCallState.Ended:
        case PhoneCallState.Idle:
          await _announcementService.StopAsync();
          break;
      }
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error handling phone call state change");
    }
  }

  private async Task HandleIncomingCallAsync(PhoneCallStateChangedEventArgs e)
  {
    var opts = _options.Value;

    // Resolve caller name (from RotaryPhone contacts or fall back to number)
    var callerName = e.CallerName;
    if (string.IsNullOrWhiteSpace(callerName) && !string.IsNullOrWhiteSpace(e.PhoneNumber))
    {
      callerName = await _contactLookup.FindCallerNameAsync(e.PhoneNumber);
    }

    callerName ??= "Unknown caller";

    var announcement = $"Incoming call from {callerName}";
    _logger.LogInformation("Phone ringing: {Announcement}", announcement);

    try
    {
      if (opts.PlayRingSound && File.Exists(opts.RingSoundPath))
      {
        await _announcementService.PlaySoundWithAnnouncementAsync(
          opts.RingSoundPath, announcement, opts.RingPriority);
      }
      else
      {
        await _announcementService.AnnounceAsync(announcement, opts.AnnouncementPriority);
      }
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error playing incoming call announcement");
    }

    // Send resolved name back to RotaryPhone for UI/logging
    if (!string.IsNullOrEmpty(e.PhoneNumber))
    {
      try
      {
        await _phoneClient.ReportCallerResolvedAsync(e.PhoneNumber, callerName);
      }
      catch (Exception ex)
      {
        _logger.LogWarning(ex, "Failed to send resolved caller name to RotaryPhone");
      }
    }
  }

  private async Task BroadcastPhoneStateAsync(PhoneCallStateChangedEventArgs e)
  {
    try
    {
      await _hubContext.Clients.All.SendAsync("PhoneCallStateChanged", new
      {
        State = e.State.ToString(),
        e.PhoneNumber,
        e.CallerName
      });
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Error broadcasting phone call state");
    }
  }

  public override void Dispose()
  {
    base.Dispose();
  }
}
