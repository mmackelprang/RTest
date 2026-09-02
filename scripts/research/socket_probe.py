#!/usr/bin/env python3
"""Counts inbound TCP connections on a port while a test suite runs.

Verification instrument for TEST-1(c). The hermetic-rig claim is falsifiable in exactly one
way: with something listening on the port the unit tests used to reach, a hermetic suite
opens ZERO connections to it. Before the fix a measured run of Radio.Web.Tests opened 74.

Usage:  python3 socket_probe.py [port] [seconds]
Prints a running count and a final total; writes nothing else.
"""
import socket
import sys
import threading
import time

PORT = int(sys.argv[1]) if len(sys.argv) > 1 else 5000
DURATION = int(sys.argv[2]) if len(sys.argv) > 2 else 600

count = 0
peers = []
lock = threading.Lock()
stop = threading.Event()


def handle(conn):
  """Read whatever the client sends, record it, answer nothing meaningful."""
  global count
  try:
    conn.settimeout(1.0)
    try:
      data = conn.recv(2048)
    except socket.timeout:
      data = b""
    with lock:
      first = data.split(b"\r\n")[0].decode("latin-1", "replace") if data else "(no payload)"
      peers.append(first)
    # Minimal response so the client fails fast rather than hanging.
    conn.sendall(b"HTTP/1.1 503 Service Unavailable\r\nContent-Length: 0\r\nConnection: close\r\n\r\n")
  except Exception:
    pass
  finally:
    try:
      conn.close()
    except Exception:
      pass


def main():
  global count
  srv = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
  srv.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
  srv.bind(("127.0.0.1", PORT))
  srv.listen(128)
  srv.settimeout(1.0)
  print("PROBE listening on 127.0.0.1:%d for %ds" % (PORT, DURATION), flush=True)

  deadline = time.time() + DURATION
  while time.time() < deadline and not stop.is_set():
    try:
      conn, _ = srv.accept()
    except socket.timeout:
      continue
    except OSError:
      break
    with lock:
      count += 1
    threading.Thread(target=handle, args=(conn,), daemon=True).start()

  srv.close()
  with lock:
    print("PROBE TOTAL CONNECTIONS: %d" % count, flush=True)
    for p in peers[:20]:
      print("  <- %s" % p, flush=True)


if __name__ == "__main__":
  main()
