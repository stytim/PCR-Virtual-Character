using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using TMPro;
using UnityEngine;

/// <summary>
/// TCP server that listens for incoming connections on any IP address.
/// Receives newline-delimited strings from a Python client and displays
/// them in a TextMeshPro (UI) text element.
///
/// Either this script or the Python client can be started first.
/// The server continuously listens and will accept (re)connections.
/// </summary>
public class TcpTextReceiver : MonoBehaviour
{
    [Header("Network Settings")]
    [Tooltip("Port to listen on. Must match the Python script.")]
    [SerializeField] private int port = 12345;

    [Header("UI")]
    [Tooltip("TextMeshPro UI element to display received text.")]
    [SerializeField] private TextMeshProUGUI displayText;

    // Thread-safe queue so the background thread can pass messages to the main thread.
    private readonly ConcurrentQueue<string> _messageQueue = new ConcurrentQueue<string>();

    private TcpListener _listener;
    private Thread _serverThread;
    private volatile bool _running;

    // ────────────────────────── Unity lifecycle ──────────────────────────

    private void Start()
    {
        _running = true;
        _serverThread = new Thread(ServerLoop)
        {
            IsBackground = true,
            Name = "TcpTextReceiver"
        };
        _serverThread.Start();
        Debug.Log($"[TcpTextReceiver] Server thread started on port {port}.");
    }

    private void Update()
    {
        // Process all queued messages on the main thread (required for UI updates).
        while (_messageQueue.TryDequeue(out string msg))
        {
            if (displayText != null)
            {
                displayText.text = msg;
            }

            Debug.Log($"[TcpTextReceiver] Displayed: {msg}");
        }
    }

    private void OnDestroy()
    {
        _running = false;

        try { _listener?.Stop(); } catch { /* ignored */ }

        if (_serverThread != null && _serverThread.IsAlive)
        {
            _serverThread.Join(2000);
        }

        Debug.Log("[TcpTextReceiver] Server shut down.");
    }

    private void OnApplicationQuit()
    {
        _running = false;
        try { _listener?.Stop(); } catch { /* ignored */ }
    }

    // ──────────────────────── Background thread ─────────────────────────

    private void ServerLoop()
    {
        try
        {
            _listener = new TcpListener(IPAddress.Any, port);
            _listener.Server.SetSocketOption(
                SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _listener.Start();
            Debug.Log($"[TcpTextReceiver] Listening on 0.0.0.0:{port}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[TcpTextReceiver] Failed to start listener: {ex.Message}");
            return;
        }

        while (_running)
        {
            TcpClient client = null;
            try
            {
                // Block until a client connects (will throw on Stop()).
                client = _listener.AcceptTcpClient();
                Debug.Log($"[TcpTextReceiver] Client connected: " +
                          $"{((IPEndPoint)client.Client.RemoteEndPoint).Address}");

                HandleClient(client);
            }
            catch (SocketException) when (!_running)
            {
                // Expected when the listener is stopped during shutdown.
                break;
            }
            catch (ObjectDisposedException) when (!_running)
            {
                break;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[TcpTextReceiver] Connection error: {ex.Message}");
            }
            finally
            {
                try { client?.Close(); } catch { /* ignored */ }
            }

            if (_running)
            {
                Debug.Log("[TcpTextReceiver] Client disconnected. Waiting for new connection...");
            }
        }
    }

    private void HandleClient(TcpClient client)
    {
        NetworkStream stream = client.GetStream();
        byte[] buffer = new byte[4096];
        StringBuilder sb = new StringBuilder();

        while (_running && client.Connected)
        {
            int bytesRead;
            try
            {
                bytesRead = stream.Read(buffer, 0, buffer.Length);
            }
            catch
            {
                break;
            }

            if (bytesRead == 0) break; // Client closed connection.

            sb.Append(Encoding.UTF8.GetString(buffer, 0, bytesRead));

            // Process each complete message (null-byte-delimited protocol).
            string accumulated = sb.ToString();
            int delimIdx;
            while ((delimIdx = accumulated.IndexOf('\0')) >= 0)
            {
                string message = accumulated.Substring(0, delimIdx);
                if (!string.IsNullOrEmpty(message))
                {
                    _messageQueue.Enqueue(message);
                }
                accumulated = accumulated.Substring(delimIdx + 1);
            }
            sb.Clear();
            sb.Append(accumulated);
        }
    }
}
