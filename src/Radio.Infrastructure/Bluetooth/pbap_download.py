#!/usr/bin/env python3
"""Download phonebook contacts from a Bluetooth device via PBAP/OBEX D-Bus.

Usage: pbap_download.py <device_address> <output_vcf_path> [timeout_seconds]

Connects to the BlueZ OBEX daemon on the D-Bus session bus, creates a PBAP
session, selects the internal phonebook, and downloads all contacts as a VCF
file. Exits with code 0 on success, non-zero on failure.

Requires: dbus-python, PyGObject (gi), bluez-obexd running as user service.
"""
import sys
import os
import signal

def main():
  if len(sys.argv) < 3:
    print("Usage: pbap_download.py <device_address> <output_vcf_path> [timeout_seconds]", file=sys.stderr)
    sys.exit(1)

  device_address = sys.argv[1]
  output_path = sys.argv[2]
  timeout = int(sys.argv[3]) if len(sys.argv) > 3 else 30

  import dbus
  import dbus.mainloop.glib
  from gi.repository import GLib

  dbus.mainloop.glib.DBusGMainLoop(set_as_default=True)

  bus = dbus.SessionBus()
  loop = GLib.MainLoop()
  result = {'status': 'pending', 'error': None}
  session_path = None

  def cleanup_and_exit(code):
    """Remove OBEX session and exit."""
    if session_path:
      try:
        client = dbus.Interface(
          bus.get_object('org.bluez.obex', '/org/bluez/obex'),
          'org.bluez.obex.Client1'
        )
        client.RemoveSession(dbus.ObjectPath(session_path))
      except Exception:
        pass
    sys.exit(code)

  def on_timeout():
    result['status'] = 'error'
    result['error'] = 'Transfer timed out'
    loop.quit()
    return False

  try:
    # Create PBAP session
    client = dbus.Interface(
      bus.get_object('org.bluez.obex', '/org/bluez/obex'),
      'org.bluez.obex.Client1'
    )

    session_path = str(client.CreateSession(device_address, {'Target': dbus.String('pbap')}))

    # Get PhonebookAccess1 interface
    session_obj = bus.get_object('org.bluez.obex', session_path)
    pbap = dbus.Interface(session_obj, 'org.bluez.obex.PhonebookAccess1')

    # Select internal phonebook
    pbap.Select('int', 'pb')

    # Download all contacts
    transfer_path, props = pbap.PullAll(output_path, {})

    initial_status = str(props.get('Status', ''))
    if initial_status == 'complete':
      result['status'] = 'complete'
      print(f"OK: 0 bytes (immediate complete)", flush=True)
      cleanup_and_exit(0)

    # Monitor transfer via PropertiesChanged
    transfer_obj = bus.get_object('org.bluez.obex', transfer_path)
    transferred_bytes = [0]

    def on_properties_changed(iface, changed, invalidated):
      status = str(changed.get('Status', ''))
      if 'Transferred' in changed:
        transferred_bytes[0] = int(changed['Transferred'])
      if status == 'complete':
        result['status'] = 'complete'
        loop.quit()
      elif status == 'error':
        result['status'] = 'error'
        result['error'] = 'Transfer reported error'
        loop.quit()

    transfer_obj.connect_to_signal('PropertiesChanged', on_properties_changed)

    # Set timeout
    GLib.timeout_add_seconds(timeout, on_timeout)

    # Run event loop until transfer completes or times out
    loop.run()

    if result['status'] == 'complete':
      size = os.path.getsize(output_path) if os.path.exists(output_path) else 0
      print(f"OK: {size} bytes", flush=True)
      cleanup_and_exit(0)
    else:
      error_msg = result.get('error', 'Unknown error')
      print(f"ERROR: {error_msg}", file=sys.stderr, flush=True)
      cleanup_and_exit(1)

  except dbus.exceptions.DBusException as e:
    error_name = e.get_dbus_name() if hasattr(e, 'get_dbus_name') else ''
    print(f"ERROR: D-Bus error: {error_name}: {e}", file=sys.stderr, flush=True)
    cleanup_and_exit(2)
  except Exception as e:
    print(f"ERROR: {e}", file=sys.stderr, flush=True)
    cleanup_and_exit(3)


if __name__ == '__main__':
  main()
