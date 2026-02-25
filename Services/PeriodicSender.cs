using PmLiteMonitor.Messages;
using PmLiteMonitor.Networking;

namespace PmLiteMonitor.Services;

/// <summary>
/// Sends a configurable message to the server on a fixed interval
/// while the TCP connection is active.
///
/// Usage:
///   _sender.Message  = new NullMessage();   // what to send
///   _sender.Interval = TimeSpan.FromSeconds(30);
///   _sender.Start(client);
///   ...
///   _sender.Stop();
///
/// Changing Message or Interval while running takes effect on the next tick.
/// </summary>
public class PeriodicSender
{
    // ── Configuration — change any time, safe while running ──────────────────
    private IMessage?  _message;
    private TimeSpan   _interval = TimeSpan.FromSeconds(30);

    public IMessage? Message
    {
        get => _message;
        set => Interlocked.Exchange(ref _message, value);
    }

    public TimeSpan Interval
    {
        get => _interval;
        set => _interval = value.TotalMilliseconds > 0 ? value : TimeSpan.FromSeconds(30);
    }

    // ── State ─────────────────────────────────────────────────────────────────
    private CancellationTokenSource? _cts;

    public bool IsRunning => _cts is not null && !_cts.IsCancellationRequested;

    // Raised on the thread pool — subscribe to feed into AppendLog
    public event Action<string>? OnStatus;
    public event Action<Exception>? OnError;

    // ── Start / Stop ──────────────────────────────────────────────────────────

    /// <summary>
    /// Starts the periodic send loop. Safe to call multiple times
    /// — calling Start while already running first stops the previous loop.
    /// </summary>
    public void Start(TcpMessageClient client)
    {
        Stop();  // clean up any previous run

        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        // Fire-and-forget on the thread pool
        _ = Task.Run(async () =>
        {
            OnStatus?.Invoke($"[PERIODIC] Started — interval {Interval.TotalSeconds:F0}s");

            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(Interval, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                if (token.IsCancellationRequested) break;

                var msg = Message;
                if (msg is null)
                {
                    OnStatus?.Invoke("[PERIODIC] Skipped — no message configured");
                    continue;
                }

                try
                {
                    await client.SendAsync(msg, token);
                    OnStatus?.Invoke($"[PERIODIC] Sent {msg.GetType().Name.Replace("Message","")} " +
                                     $"at {DateTime.Now:HH:mm:ss}");
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    OnError?.Invoke(ex);
                }
            }

            OnStatus?.Invoke("[PERIODIC] Stopped");
        }, CancellationToken.None);
    }

    /// <summary>Cancels the loop. Returns immediately — the loop exits async.</summary>
    public void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }
}
