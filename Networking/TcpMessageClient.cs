using System.IO;
using System.Net.Sockets;
using PmLiteMonitor.Messages;

namespace PmLiteMonitor.Networking;

public class TcpMessageClient : IAsyncDisposable
{
    private TcpClient?           _client;
    private NetworkStream?       _stream;
    private readonly MessageFrameBuffer _frameBuffer = new();
    private readonly MessageParser      _parser      = new();
    private readonly SemaphoreSlim      _sendLock    = new(1, 1);
    private CancellationTokenSource?    _cts;

    public event Action<IMessage>?  OnMessageReceived;
    public event Action<string>?    OnWarning;
    public event Action<Exception>? OnError;
    public event Action?            OnDisconnected;

    public bool IsConnected => _client?.Connected ?? false;

    // ── Connect ──────────────────────────────────────────────────────────────
    public async Task ConnectAsync(string host, int port, CancellationToken ct = default)
    {
        _client = new TcpClient();
        await _client.ConnectAsync(host, port, ct);
        _stream = _client.GetStream();
        _cts    = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _ = ReceiveLoopAsync(_cts.Token);
    }

    // ── Send ─────────────────────────────────────────────────────────────────
    public async Task SendAsync(IMessage message, CancellationToken ct = default)
    {
        if (_stream is null) throw new InvalidOperationException("Not connected.");
        byte[] frame = message.ToBytes();
        await _sendLock.WaitAsync(ct);
        try   { await _stream.WriteAsync(frame, ct); }
        finally { _sendLock.Release(); }
    }

    // ── Receive loop ─────────────────────────────────────────────────────────
    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        byte[] chunk = new byte[4096];
        try
        {
            while (!ct.IsCancellationRequested)
            {
                int n;
                try   { n = await _stream!.ReadAsync(chunk, ct); }
                catch (OperationCanceledException) { break; }
                catch (IOException ex)             { OnError?.Invoke(ex); break; }

                if (n == 0) { OnWarning?.Invoke("Server closed the connection."); break; }

                _frameBuffer.Append(chunk, n);
                ProcessFrameBuffer();
            }
        }
        finally
        {
            _frameBuffer.Clear();
            OnDisconnected?.Invoke();
        }
    }

    private void ProcessFrameBuffer()
    {
        foreach (var result in _frameBuffer.DrainMessages())
        {
            if (!result.IsSuccess)
            {
                OnWarning?.Invoke(result.ErrorMessage!);
                continue;
            }

            try
            {
                var msg = _parser.Parse(result.RawFrame!);
                OnMessageReceived?.Invoke(msg);
            }
            catch (Exception ex)
            {
                OnWarning?.Invoke($"Parse error: {ex.Message}");
            }
        }
    }

    // ── Disconnect / Dispose ─────────────────────────────────────────────────
    public async Task DisconnectAsync()
    {
        _cts?.Cancel();
        _stream?.Close();
        _client?.Close();
        await Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        _sendLock.Dispose();
        _cts?.Dispose();
    }
}
