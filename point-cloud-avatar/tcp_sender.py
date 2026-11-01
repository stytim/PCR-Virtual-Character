#!/usr/bin/env python3
"""
TCP client that connects to the Unity TcpTextReceiver server and sends
newline-delimited strings.

Either this script or Unity can be started first; the client will keep
retrying until a connection is established.

Usage:
    python tcp_sender.py
"""

import socket
import time

# ──────────────────── Configuration ────────────────────
UNITY_IP   = "10.0.130.151"   # Change this to the Unity machine's IP address
UNITY_PORT = 12345          # Must match the port in TcpTextReceiver
RETRY_INTERVAL = 2          # Seconds between reconnection attempts
# ───────────────────────────────────────────────────────


def connect(ip: str, port: int) -> socket.socket:
    """Keep trying to connect until the Unity server is available."""
    while True:
        try:
            sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
            sock.connect((ip, port))
            print(f"[tcp_sender] Connected to {ip}:{port}")
            return sock
        except (ConnectionRefusedError, OSError) as e:
            print(f"[tcp_sender] Connection failed ({e}). Retrying in {RETRY_INTERVAL}s...")
            sock.close()
            time.sleep(RETRY_INTERVAL)


def send_message(sock: socket.socket, message: str) -> bool:
    """Send a null-byte-terminated string. Returns False on failure."""
    try:
        data = (message + "\0").encode("utf-8")
        sock.sendall(data)
        return True
    except (BrokenPipeError, ConnectionResetError, OSError):
        return False


def main():
    sock = connect(UNITY_IP, UNITY_PORT)

    print("[tcp_sender] Enter your message (can be multiple lines).")
    print("[tcp_sender] Press Enter on an empty line to send.")
    print("[tcp_sender] Type 'quit' on its own line to exit.\n")

    try:
        while True:
            lines = []
            while True:
                line = input(">> " if not lines else ".. ")
                if line.lower() == "quit" and not lines:
                    sock.close()
                    print("[tcp_sender] Connection closed.")
                    return
                if line == "" and lines:
                    break  # Empty line ends the multi-line input
                if line:
                    lines.append(line)

            text = "\n".join(lines)

            if not send_message(sock, text):
                print("[tcp_sender] Connection lost. Reconnecting...")
                sock.close()
                sock = connect(UNITY_IP, UNITY_PORT)
                # Retry sending after reconnection
                send_message(sock, text)
    except (KeyboardInterrupt, EOFError):
        print("\n[tcp_sender] Shutting down.")
    finally:
        sock.close()
        print("[tcp_sender] Connection closed.")


if __name__ == "__main__":
    main()
