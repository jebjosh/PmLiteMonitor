using System.IO;
using System.Text;

namespace PmLiteMonitor.Services;

/// <summary>
/// Writes every event-log line to a uniquely named .log file.
///
/// File naming:  session_YYYY-MM-DD_HH-mm-ss.log
///               e.g. session_2026-02-25_14-32-07.log
///
/// One file is opened per application session (Start() call).
/// The file stays open and lines are flushed immediately so you
/// can tail it externally while the app runs.
///
/// WHERE TO ADD NEW LOG SOURCES:
///   All log output goes through MainViewModel.AppendLog().
///   That is the ONLY place you need to touch — it already calls
///   SessionLogger.Write() after every line.  If you add a new
///   ViewModel or service that produces log lines, route them
///   through AppendLog() the same way everything else does.
/// </summary>
public sealed class SessionLogger : IDisposable
{
    private StreamWriter? _writer;
    private string        _logPath = string.Empty;

    public string LogPath    => _logPath;
    public bool   IsOpen     => _writer is not null;

    // ── Folder ────────────────────────────────────────────────────────────────

    private string _logFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "PmLiteLogs");

    public string LogFolder
    {
        get => _logFolder;
        set
        {
            if (!string.IsNullOrWhiteSpace(value))
                _logFolder = value;
        }
    }

    // ── Start / Stop ──────────────────────────────────────────────────────────

    /// <summary>
    /// Opens a new log file for this session.
    /// Safe to call at app startup before any log lines are produced.
    /// </summary>
    public void Start()
    {
        Stop();   // close any previous file first

        try
        {
            Directory.CreateDirectory(_logFolder);

            // Unique name: session_2026-02-25_14-32-07.log
            // Colons are illegal in Windows file names — use dashes for time.
            string stamp    = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            _logPath        = Path.Combine(_logFolder, $"session_{stamp}.log");

            // StreamWriter with AutoFlush=true writes each line to disk immediately.
            // leaveOpen=false means the FileStream is owned and closed with the writer.
            _writer = new StreamWriter(
                new FileStream(_logPath, FileMode.Create, FileAccess.Write, FileShare.Read),
                Encoding.UTF8,
                bufferSize: 4096)
            {
                AutoFlush = true
            };

            // Write a header so you can identify the file at a glance
            _writer.WriteLine($"# PM-LITE Monitor — session log");
            _writer.WriteLine($"# Started : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            _writer.WriteLine($"# File    : {_logPath}");
            _writer.WriteLine(new string('-', 72));
        }
        catch (Exception ex)
        {
            // If we can't open the log file, fail silently so the app still runs.
            // The error will appear in the UI log only.
            _writer  = null;
            _logPath = $"(log unavailable: {ex.Message})";
        }
    }

    /// <summary>Flushes and closes the current log file.</summary>
    public void Stop()
    {
        _writer?.Flush();
        _writer?.Dispose();
        _writer  = null;
        _logPath = string.Empty;
    }

    // ── Write ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Writes one line to the log file.
    /// Called by MainViewModel.AppendLog() for every line shown on screen.
    /// Thread-safe — lock ensures concurrent callers don't interleave output.
    /// </summary>
    public void Write(string timestampedLine)
    {
        if (_writer is null) return;

        lock (_writer)
        {
            try   { _writer.WriteLine(timestampedLine); }
            catch { /* silently skip if disk full / file locked */ }
        }
    }

    // ── IDisposable ───────────────────────────────────────────────────────────
    public void Dispose() => Stop();
}
