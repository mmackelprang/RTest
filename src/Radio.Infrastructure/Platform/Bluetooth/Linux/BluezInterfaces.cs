using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Tmds.DBus;

namespace Radio.Infrastructure.Platform.Bluetooth.Linux;

[DBusInterface("org.freedesktop.DBus.ObjectManager")]
public interface IObjectManager : IDBusObject
{
  Task<IDictionary<ObjectPath, IDictionary<string, IDictionary<string, object>>>> GetManagedObjectsAsync();
  Task<IDisposable> WatchInterfacesAddedAsync(Action<(ObjectPath objectPath, IDictionary<string, IDictionary<string, object>> interfaces)> handler, Action<Exception>? onError = null);
  Task<IDisposable> WatchInterfacesRemovedAsync(Action<(ObjectPath objectPath, string[] interfaces)> handler, Action<Exception>? onError = null);
}

[DBusInterface("org.bluez.Adapter1")]
public interface IAdapter1 : IDBusObject
{
  Task StartDiscoveryAsync();
  Task StopDiscoveryAsync();
  Task RemoveDeviceAsync(ObjectPath device);
  Task<IDisposable> WatchPropertiesAsync(Action<PropertyChanges> handler);
  Task<T> GetAsync<T>(string prop);
  Task SetAsync(string prop, object val);
}

[DBusInterface("org.bluez.Device1")]
public interface IDevice1 : IDBusObject
{
  Task ConnectAsync();
  Task DisconnectAsync();
  Task PairAsync();
  Task CancelPairingAsync();
  Task<IDisposable> WatchPropertiesAsync(Action<PropertyChanges> handler);
  Task<T> GetAsync<T>(string prop);
  Task SetAsync(string prop, object val);
}

[DBusInterface("org.freedesktop.DBus.Properties")]
public interface IProperties : IDBusObject
{
  Task<object> GetAsync(string interfaceName, string prop);
  Task SetAsync(string interfaceName, string prop, object val);
  Task<IDictionary<string, object>> GetAllAsync(string interfaceName);
  Task<IDisposable> WatchPropertiesChangedAsync(Action<(string interfaceName, IDictionary<string, object> changed, string[] invalidated)> handler, Action<Exception>? onError = null);
}

[DBusInterface("org.bluez.MediaPlayer1")]
public interface IMediaPlayer1 : IDBusObject
{
  Task PlayAsync();
  Task PauseAsync();
  Task StopAsync();
  Task NextAsync();
  Task PreviousAsync();
  Task FastForwardAsync();
  Task RewindAsync();
  Task<IDisposable> WatchPropertiesAsync(Action<PropertyChanges> handler);
  Task<T> GetAsync<T>(string prop);
  Task SetAsync(string prop, object val);
}

[DBusInterface("org.bluez.MediaTransport1")]
public interface IMediaTransport1 : IDBusObject
{
  Task<IDisposable> WatchPropertiesAsync(Action<PropertyChanges> handler);
  Task<T> GetAsync<T>(string prop);
  Task SetAsync(string prop, object val);
}

[DBusInterface("org.bluez.AgentManager1")]
public interface IAgentManager1 : IDBusObject
{
  Task RegisterAgentAsync(ObjectPath agent, string capability);
  Task UnregisterAgentAsync(ObjectPath agent);
  Task RequestDefaultAgentAsync(ObjectPath agent);
}

[DBusInterface("org.bluez.Agent1")]
public interface IAgent1 : IDBusObject
{
  Task ReleaseAsync();
  Task<string> RequestPinCodeAsync(ObjectPath device);
  Task DisplayPinCodeAsync(ObjectPath device, string pincode);
  Task<uint> RequestPasskeyAsync(ObjectPath device);
  Task DisplayPasskeyAsync(ObjectPath device, uint passkey, ushort entered);
  Task RequestConfirmationAsync(ObjectPath device, uint passkey);
  Task RequestAuthorizationAsync(ObjectPath device);
  Task AuthorizeServiceAsync(ObjectPath device, string uuid);
  Task CancelAsync();
}

// Helper for property changes
public static class BluezConstants
{
  public const string ServiceName = "org.bluez";
  public const string AdapterInterface = "org.bluez.Adapter1";
  public const string DeviceInterface = "org.bluez.Device1";
  public const string MediaControlInterface = "org.bluez.MediaControl1";
  public const string MediaTransportInterface = "org.bluez.MediaTransport1";
  public const string MediaPlayerInterface = "org.bluez.MediaPlayer1";
  public const string AgentManagerInterface = "org.bluez.AgentManager1";
  public const string AgentPath = "/radio/bluetooth/agent";
}
