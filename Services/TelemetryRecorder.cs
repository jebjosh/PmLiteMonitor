using System.Collections.Concurrent;
using System.IO;
using System.Text;
using PmLiteMonitor.Messages;

namespace PmLiteMonitor.Services;

/// <summary>
/// Watches incoming TelemetryMessages for MRS ON transitions.
/// On each transition it captures:
///   • 100 ms of pre-MRS messages (circular buffer)
///   • 4 seconds of post-MRS messages
/// Then writes both a .bin and a .txt file to the configured output path.
///
/// Each MRS event produces two unique files:
///   telemetry_MRS_<timestamp>.bin
///   telemetry_MRS_<timestamp>.txt
/// </summary>
public class TelemetryRecorder
{
    // ── Configuration ────────────────────────────────────────────────────────
    private const double PreWindowMs  = 100.0;   // 100 ms before MRS ON
    private const double PostWindowMs = 4000.0;  // 4 seconds after MRS ON

    private string _outputPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

    public string OutputPath
    {
        get => _outputPath;
        set { _outputPath = value; Directory.CreateDirectory(value); }
    }

    // ── State ────────────────────────────────────────────────────────────────
    private readonly object _lock = new();

    // Circular pre-buffer: timestamped raw frames
    private readonly ConcurrentQueue<(DateTime ts, byte[] frame)> _preBuffer = new();

    // Snapshot of pre-buffer taken at the moment MRS fires
    private (DateTime ts, byte[] frame)[]? _preSnapshot;

    // Post-buffer: filled after MRS fires
    private List<(DateTime ts, byte[] frame)>? _postBuffer;
    private DateTime _mrsOnTime;
    private bool     _recording;
    private bool     _lastMrsOn;

    // Timer fires every 100ms to check if 4 seconds has elapsed
    // independent of whether messages are still arriving
    private System.Threading.Timer? _stopTimer;

    public bool IsRecording => _recording;

    // Raised on the thread pool whenever a file pair is written
    public event Action<string>? OnRecordingComplete;
    public event Action<string>? OnStatusMessage;

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>Call this for every TelemetryMessage received from the TCP client.</summary>
    public void Feed(TelemetryMessage msg)
    {
        var ts    = DateTime.Now;
        var frame = msg.ToBytes();

        lock (_lock)
        {
            // --- always push into the pre-buffer circular queue ---
            _preBuffer.Enqueue((ts, frame));
            PrunePreBuffer(ts);

            // --- MRS rising edge ---
            if (msg.MrsOn && !_lastMrsOn && !_recording)
            {
                _recording   = true;
                _mrsOnTime   = ts;
                _postBuffer  = new List<(DateTime, byte[])>();

                // Snapshot the pre-buffer NOW at the moment of MRS ON
                // so we get the 100ms before, not 100ms before the 4-second mark
                _preSnapshot = _preBuffer.ToArray();

                OnStatusMessage?.Invoke($"MRS ON detected at {ts:HH:mm:ss.fff} — recording started.");

                // Start a timer that checks every 100ms whether 4 seconds has elapsed.
                // This fires independently of incoming messages so recording always stops.
                _stopTimer = new System.Threading.Timer(
                    CheckStopTimer,
                    state: null,
                    dueTime:  100,
                    period:   100);
            }

            _lastMrsOn = msg.MrsOn;

            // --- accumulate post-MRS data ---
            if (_recording && _postBuffer is not null)
            {
                _postBuffer.Add((ts, frame));
            }
        }
    }

    // ── Timer callback ───────────────────────────────────────────────────────

    /// <summary>
    /// Called every 100ms by the timer. Stops recording once 4 seconds
    /// have elapsed since MRS ON, regardless of whether messages are arriving.
    /// </summary>
    private void CheckStopTimer(object? state)
    {
        lock (_lock)
        {
            if (!_recording) return;

            if ((DateTime.Now - _mrsOnTime).TotalMilliseconds >= PostWindowMs)
            {
                // Stop the timer first
                _stopTimer?.Dispose();
                _stopTimer = null;

                // Capture snapshots and clear recording state
                var pre         = _preSnapshot  ?? Array.Empty<(DateTime, byte[])>();
                var post        = _postBuffer?.ToArray() ?? Array.Empty<(DateTime, byte[])>();
                var captureTime = _mrsOnTime;

                _recording   = false;
                _postBuffer  = null;
                _preSnapshot = null;

                OnStatusMessage?.Invoke($"Recording stopped at {DateTime.Now:HH:mm:ss.fff} — writing files.");

                // Fire-and-forget the async write — never blocks the UI or thread pool
                _ = WriteFilesAsync(pre, post, captureTime);
            }
        }
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    /// <summary>Keep only entries within the pre-window relative to 'now'.</summary>
    private void PrunePreBuffer(DateTime now)
    {
        while (_preBuffer.TryPeek(out var oldest) &&
               (now - oldest.ts).TotalMilliseconds > PreWindowMs)
        {
            _preBuffer.TryDequeue(out _);
        }
    }

    /// <summary>
    /// Builds binary and text content entirely in memory, then flushes to disk
    /// using a single awaited WriteAllBytesAsync / WriteAllTextAsync call.
    ///
    /// Why this doesn't hang the UI:
    ///   - MemoryStream writes are pure RAM — microseconds, no disk involvement
    ///   - File.WriteAllBytesAsync uses true async I/O (overlapped I/O on Windows)
    ///     so the calling thread is released back to the thread pool while the OS
    ///     handles the disk write, then the continuation resumes when it's done
    ///   - The UI thread is never involved at any point
    /// </summary>
    private async Task WriteFilesAsync(
        (DateTime ts, byte[] frame)[] pre,
        (DateTime ts, byte[] frame)[] post,
        DateTime captureTime)
    {
        try
        {
            string stamp   = captureTime.ToString("yyyyMMdd_HHmmss_fff");
            string binPath = Path.Combine(_outputPath, $"telemetry_MRS_{stamp}.bin");
            string txtPath = Path.Combine(_outputPath, $"telemetry_MRS_{stamp}.txt");

            // Merge and sort chronologically
            var all = pre.Concat(post).OrderBy(x => x.ts).ToList();

            // ── Binary — build entirely in MemoryStream, then write async ────
            // MemoryStream is just a byte array in RAM — no disk I/O yet.
            // BinaryWriter wraps it exactly like it would wrap a FileStream,
            // so the write logic is identical — only the destination changes.
            byte[] binBytes;
            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms))
            {
                bw.Write(Encoding.ASCII.GetBytes("PMLITE_TLM")); // 10-byte magic
                bw.Write((byte)1);                                // version
                bw.Write(all.Count);                              // entry count
                bw.Write(new DateTimeOffset(captureTime).ToUnixTimeMilliseconds());

                foreach (var (ts, frame) in all)
                {
                    bw.Write(new DateTimeOffset(ts).ToUnixTimeMilliseconds());
                    bw.Write(frame.Length);
                    bw.Write(frame);
                }

                bw.Flush();
                binBytes = ms.ToArray();  // snapshot the buffer as a plain byte[]
            }

            // Single async write — OS handles disk I/O, this thread is freed
            await File.WriteAllBytesAsync(binPath, binBytes).ConfigureAwait(false);

            // ── Text — build into StringBuilder, then write async ────────────
            // StringBuilder is also pure RAM — no disk I/O until WriteAllTextAsync.
            var sb = new StringBuilder();
            sb.AppendLine($"PM-LITE Telemetry Capture — MRS ON @ {captureTime:yyyy-MM-dd HH:mm:ss.fff}");
            sb.AppendLine($"Pre-window  : {PreWindowMs} ms");
            sb.AppendLine($"Post-window : {PostWindowMs} ms");
            sb.AppendLine($"Total frames: {all.Count}");
            sb.AppendLine(new string('-', 80));
            sb.AppendLine($"{"Timestamp",-26} {"Type",-6} {"Size",-6} {"Hex Bytes"}");
            sb.AppendLine(new string('-', 80));

            foreach (var (ts, frame) in all)
            {
                string label = frame.Length > 0 ? TypeLabel(frame[0]) : "?";
                string hex   = BitConverter.ToString(frame).Replace("-", " ");
                sb.AppendLine($"{ts:HH:mm:ss.fff}  {frame[0],-6} {label,-12} {frame.Length,-6} {hex}");
            }

            // Single async write — OS handles disk I/O, this thread is freed
            await File.WriteAllTextAsync(txtPath, sb.ToString(), Encoding.UTF8).ConfigureAwait(false);

            OnRecordingComplete?.Invoke(
                $"Saved: {Path.GetFileName(binPath)} + {Path.GetFileName(txtPath)}  ({all.Count} frames)");
        }
        catch (Exception ex)
        {
            OnStatusMessage?.Invoke($"[RECORD ERROR] {ex.Message}");
        }
    }

    private static string TypeLabel(byte type) => type switch
    {
        0 => "INVALID",
        1 => "START",
        2 => "TELEMETRY",
        3 => "DEBUG",
        4 => "REQUEST",
        5 => "STATUS",
        6 => "TESTDATA",
        7 => "LOOPBACK",
        8 => "CONFIG",
        9 => "NULL",
        _ => $"0x{type:X2}"
    };
}
