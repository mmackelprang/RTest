using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Tmds.DBus;

namespace Radio.Infrastructure.Platform.Bluetooth.Linux;

/// <summary>
/// BlueZ Agent1 implementation that auto-accepts pairing and authorization requests.
/// Registered on D-Bus so BlueZ delegates pairing decisions to this agent instead of
/// prompting via the default agent (which doesn't exist in a headless environment).
/// Uses "NoInputNoOutput" capability to enable "Just Works" pairing (no PIN required).
/// Must implement IAgent1 (not just IDBusObject) so Tmds.DBus exposes all methods
/// on D-Bus — BlueZ calls AuthorizeService to approve A2DP/HFP profiles.
/// </summary>
internal sealed class BluezAgent : IAgent1
{
  private readonly ILogger _logger;
  private readonly bool _autoAccept;
  private readonly HashSet<string> _authorizedServices = new();

  public ObjectPath ObjectPath => new(BluezConstants.AgentPath);

  public BluezAgent(ILogger logger, bool autoAccept)
  {
    _logger = logger;
    _autoAccept = autoAccept;
  }

  /// <summary>Called when BlueZ needs to release this agent.</summary>
  public Task ReleaseAsync()
  {
    _logger.LogInformation("Bluetooth agent released by BlueZ");
    return Task.CompletedTask;
  }

  /// <summary>Called for passkey display (not used with NoInputNoOutput).</summary>
  public Task DisplayPasskeyAsync(ObjectPath device, uint passkey, ushort entered)
  {
    _logger.LogInformation("Bluetooth passkey display for {Device}: {Passkey}", device, passkey);
    return Task.CompletedTask;
  }

  /// <summary>Called for PIN display (legacy pairing).</summary>
  public Task DisplayPinCodeAsync(ObjectPath device, string pincode)
  {
    _logger.LogInformation("Bluetooth PIN display for {Device}: {Pin}", device, pincode);
    return Task.CompletedTask;
  }

  /// <summary>Called when a PIN is requested (legacy pairing). Return "0000" for Just Works.</summary>
  public Task<string> RequestPinCodeAsync(ObjectPath device)
  {
    _logger.LogInformation("Bluetooth PIN requested for {Device}, returning '0000'", device);
    return Task.FromResult("0000");
  }

  /// <summary>Called when a passkey is requested.</summary>
  public Task<uint> RequestPasskeyAsync(ObjectPath device)
  {
    _logger.LogInformation("Bluetooth passkey requested for {Device}, returning 0", device);
    return Task.FromResult(0u);
  }

  /// <summary>Called for numeric comparison SSP pairing — auto-confirm.</summary>
  public Task RequestConfirmationAsync(ObjectPath device, uint passkey)
  {
    if (_autoAccept)
    {
      _logger.LogInformation("Auto-accepting Bluetooth pairing confirmation for {Device} (passkey: {Passkey})",
        device, passkey);
      return Task.CompletedTask; // Returning without exception = accepted
    }

    _logger.LogInformation("Rejecting Bluetooth pairing for {Device} (auto-accept disabled)", device);
    throw new DBusException("org.bluez.Error.Rejected", "Pairing rejected by agent");
  }

  /// <summary>Called to authorize a service connection from a paired device.</summary>
  public Task AuthorizeServiceAsync(ObjectPath device, string uuid)
  {
    if (_autoAccept)
    {
      // Log first authorization per service at Info, subsequent repeats at Debug
      // to avoid flooding the log when BlueZ retries A2DP connections
      var key = $"{device}:{uuid}";
      bool isFirst;
      lock (_authorizedServices) { isFirst = _authorizedServices.Add(key); }

      if (isFirst)
        _logger.LogInformation("Auto-authorizing Bluetooth service {UUID} for {Device}", uuid, device);
      else
        _logger.LogDebug("Re-authorizing Bluetooth service {UUID} for {Device}", uuid, device);
      return Task.CompletedTask;
    }

    _logger.LogInformation("Rejecting Bluetooth service {UUID} for {Device} (auto-accept disabled)", uuid, device);
    throw new DBusException("org.bluez.Error.Rejected", "Service authorization rejected by agent");
  }

  /// <summary>Called to authorize an incoming pairing request.</summary>
  public Task RequestAuthorizationAsync(ObjectPath device)
  {
    if (_autoAccept)
    {
      _logger.LogInformation("Auto-authorizing Bluetooth connection for {Device}", device);
      return Task.CompletedTask;
    }

    _logger.LogInformation("Rejecting Bluetooth authorization for {Device} (auto-accept disabled)", device);
    throw new DBusException("org.bluez.Error.Rejected", "Authorization rejected by agent");
  }

  /// <summary>Called when pairing was cancelled.</summary>
  public Task CancelAsync()
  {
    _logger.LogInformation("Bluetooth pairing cancelled");
    return Task.CompletedTask;
  }
}
