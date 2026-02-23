using System.Collections.Concurrent;
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

    // Post-buffer: filled after MRS fires
    private List<(DateTime ts, byte[] frame)>? _postBuffer;
    private DateTime _mrsOnTime;
    private bool     _recording;
    private bool     _lastMrsOn;

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
                OnStatusMessage?.Invoke($"MRS ON detected at {ts:HH:mm:ss.fff} — recording started.");
            }

            _lastMrsOn = msg.MrsOn;

            // --- collecting post-MRS data ---
            if (_recording && _postBuffer is not null)
            {
                _postBuffer.Add((ts, frame));

                if ((ts - _mrsOnTime).TotalMilliseconds >= PostWindowMs)
                {
                    // Capture everything and hand off to background writer
                    var pre  = _preBuffer.ToArray();   // snapshot
                    var post = _postBuffer.ToArray();
                    var captureTime = _mrsOnTime;

                    _recording  = false;
                    _postBuffer = null;

                    Task.Run(() => WriteFiles(pre, post, captureTime));
                }
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

    private void WriteFiles(
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
            var all = pre
                .Concat(post)
                .OrderBy(x => x.ts)
                .ToList();

            // ── Binary file ──────────────────────────────────────────────────
            // Format per entry:
            //   8  bytes  — Unix ms timestamp (Int64, little-endian)
            //   4  bytes  — frame length (Int32, little-endian)
            //   N  bytes  — raw frame bytes
            using (var fs  = new FileStream(binPath, FileMode.Create, FileAccess.Write))
            using (var bw  = new BinaryWriter(fs))
            {
                // Header
                bw.Write(Encoding.ASCII.GetBytes("PMLITE_TLM"));  // 10-byte magic
                bw.Write((byte)1);                                  // version
                bw.Write(all.Count);                                // entry count
                bw.Write(new DateTimeOffset(captureTime).ToUnixTimeMilliseconds());

                foreach (var (ts, frame) in all)
                {
                    bw.Write(new DateTimeOffset(ts).ToUnixTimeMilliseconds());
                    bw.Write(frame.Length);
                    bw.Write(frame);
                }
            }

            // ── Text file ────────────────────────────────────────────────────
            // Human-readable: timestamp, message type, hex dump
            using (var sw = new StreamWriter(txtPath, append: false, Encoding.UTF8))
            {
                sw.WriteLine($"PM-LITE Telemetry Capture — MRS ON @ {captureTime:yyyy-MM-dd HH:mm:ss.fff}");
                sw.WriteLine($"Pre-window  : {PreWindowMs} ms");
                sw.WriteLine($"Post-window : {PostWindowMs} ms");
                sw.WriteLine($"Total frames: {all.Count}");
                sw.WriteLine(new string('-', 80));
                sw.WriteLine($"{"Timestamp",-26} {"Type",-6} {"Size",-6} {"Hex Bytes"}");
                sw.WriteLine(new string('-', 80));

                foreach (var (ts, frame) in all)
                {
                    string label = frame.Length > 0 ? TypeLabel(frame[0]) : "?";
                    string hex   = BitConverter.ToString(frame).Replace("-", " ");
                    sw.WriteLine($"{ts:HH:mm:ss.fff}  {frame[0],-6} {label,-12} {frame.Length,-6} {hex}");
                }
            }

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
