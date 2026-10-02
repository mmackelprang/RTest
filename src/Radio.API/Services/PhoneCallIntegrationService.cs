using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using Radio.API.Hubs;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Interfaces.External;
using Radio.Core.Utilities;
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

  // AUD-87: the token source for the announcement of the call that is ringing now, or null. Swapped only
  // with Interlocked; see HandleCallStateChangedAsync for who cancels and who disposes it.
  private CancellationTokenSource? _callAnnouncementCts;

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
      await HandleCallStateChangedAsync(e);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error handling phone call state change");
    }
  }

  /// <summary>
  /// Broadcasts the new call state and, on <c>Ringing</c>, announces the caller; on <c>Ended</c> or
  /// <c>Idle</c>, stops that announcement. <c>internal</c> so <c>PhoneCallIntegrationAnnouncementTests</c>
  /// can drive the handler itself; <see cref="OnCallStateChanged"/> is its only production caller.
  /// </summary>
  /// <remarks>
  /// <para>
  /// AUD-87 (owner ruling 2026-10-02): a hang-up stops <b>only the phone's own announcement</b>. It used
  /// to call <see cref="IAnnouncementService.StopAsync"/>, which stops every announcement, so a doorbell
  /// playing alongside the caller's name was silenced by the hang-up too. Each ringing call now owns a
  /// <see cref="CancellationTokenSource"/> whose token is passed to the announcement as its caller
  /// token; the hang-up cancels that token, and <c>AnnouncementService</c> treats a cancelled caller
  /// token as an interruption of that one call — still synthesising, ducking or playing.
  /// </para>
  /// <para>
  /// The CTS is swapped in or out BEFORE this method's first await. The client raises each state as a
  /// separate event and this method returns to it at the first await, so the swap is what fixes the
  /// order: a hang-up that arrives while the ringing event is still broadcasting still reaches the
  /// announcement that ringing event is about to start. Whoever takes the CTS out of the field cancels
  /// (if it is a hang-up or a newer call) and disposes it.
  /// </para>
  /// </remarks>
  internal async Task HandleCallStateChangedAsync(PhoneCallStateChangedEventArgs e)
  {
    CancellationTokenSource? ringingCts = null;
    var callToken = CancellationToken.None;
    switch (e.State)
    {
      case PhoneCallState.Ringing:
        ringingCts = new CancellationTokenSource();
        // Read now: a hang-up may dispose the CTS before the await below returns.
        callToken = ringingCts.Token;
        // A newer call's ring makes the previous call's announcement stale.
        CancelAndDispose(Interlocked.Exchange(ref _callAnnouncementCts, ringingCts));
        break;

      case PhoneCallState.Ended:
      case PhoneCallState.Idle:
        CancelAndDispose(Interlocked.Exchange(ref _callAnnouncementCts, null));
        break;
    }

    // Broadcast state to Web UI
    await BroadcastPhoneStateAsync(e);

    if (ringingCts != null)
    {
      try
      {
        await HandleIncomingCallAsync(e, callToken);
      }
      finally
      {
        // Still ours (no hang-up and no newer call took it): retire it. Otherwise its taker disposed it.
        if (ReferenceEquals(Interlocked.CompareExchange(ref _callAnnouncementCts, null, ringingCts), ringingCts))
        {
          ringingCts.Dispose();
        }
      }
    }
  }

  private static void CancelAndDispose(CancellationTokenSource? cts)
  {
    if (cts == null)
    {
      return;
    }
    cts.Cancel();
    cts.Dispose();
  }

  /// <summary>
  /// Resolves the caller name, logs the masked ringing line, and speaks the announcement.
  /// <c>internal</c> rather than <c>private</c> so <c>PhoneCallIntegrationLogSafetyTests</c> can
  /// drive this method itself instead of a copy of it — <c>Radio.API.csproj</c> already has
  /// <c>InternalsVisibleTo</c> for <c>Radio.API.Tests</c>. Its only production caller is
  /// <see cref="HandleCallStateChangedAsync"/>'s <c>Ringing</c> arm, so the live path and the pinned
  /// path are one method.
  /// </summary>
  /// <remarks>
  /// ⚠ The log-safety pin on this method is doing work no lint can do. <c>LogSafetyLintTests</c>
  /// keys on identifier spellings, and the shape this site leaked through originally was
  /// <c>LogInformation("Phone ringing: {Announcement}", announcement)</c> — a raw number and a
  /// contact name travelling under the name <c>announcement</c>, which is far too generic to write
  /// a rule for. If this method stops being reachable from a test, that coverage is gone.
  /// </remarks>
  internal async Task HandleIncomingCallAsync(
    PhoneCallStateChangedEventArgs e, CancellationToken announcementToken = default)
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

    // ⭐ TWO tokens, and each of them joins this line to something it could not reach before.
    // {Number} is the same phn: token PhoneContactLookupService prints on the lookup lines that
    // produced callerName, so a failed resolution and the announcement it degraded into are now
    // one traceable chain. {Announcement} is the SAME txt: token AnnouncementService prints for
    // this identical string on whichever arm below runs — PlaySoundWithAnnouncementAsync and
    // AnnounceAsync both log their `message` as LogSafeText.For(message). Before PHN-5 the caller
    // printed it in clear and the callee hashed it, which is what a per-row masking rule produces
    // (plan PHN-5 C-95).
    _logger.LogInformation("Phone ringing: announcing to {Number}, announcement {Announcement}",
      LogSafeText.ForPhone(e.PhoneNumber), LogSafeText.For(announcement));

    // announcementToken is cancelled by this call's hang-up (AUD-87). Passed as the caller token, it
    // interrupts this announcement and no other.
    try
    {
      if (announcementToken.IsCancellationRequested)
      {
        // Hung up while the caller's name was being resolved: nothing to announce.
        _logger.LogDebug("Call ended before its announcement started; not announcing");
      }
      else if (opts.PlayRingSound)
      {
        if (File.Exists(opts.RingSoundPath))
        {
          await _announcementService.PlaySoundWithAnnouncementAsync(
            opts.RingSoundPath, announcement, opts.RingPriority, announcementToken);
        }
        else
        {
          _logger.LogWarning("Ring sound enabled but file not found: {Path}. Playing TTS only.", opts.RingSoundPath);
          await _announcementService.AnnounceAsync(announcement, opts.AnnouncementPriority, announcementToken);
        }
      }
      else
      {
        await _announcementService.AnnounceAsync(announcement, opts.AnnouncementPriority, announcementToken);
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
    CancelAndDispose(Interlocked.Exchange(ref _callAnnouncementCts, null));
    base.Dispose();
  }
}
