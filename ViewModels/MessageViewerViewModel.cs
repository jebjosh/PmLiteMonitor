using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PmLiteMonitor.Messages;
using PmLiteMonitor.Services;
using WinForms = System.Windows.Forms;

namespace PmLiteMonitor.ViewModels;

// ── Row model displayed in the DataGrid ──────────────────────────────────────

/// <summary>
/// One row in the message grid. Flat — every column is a string so
/// WPF DataGrid can display it without any converter gymnastics.
/// </summary>
public class MessageRow
{
    public string Timestamp  { get; init; } = string.Empty;
    public string TypeLabel  { get; init; } = string.Empty;
    public string Size       { get; init; } = string.Empty;
    public string Summary    { get; init; } = string.Empty;   // short decoded text
    public string HexDump    { get; init; } = string.Empty;

    // Kept for the detail panel — not shown in the grid directly
    public CaptureEntry Entry { get; init; } = null!;
}

// ── ViewModel ─────────────────────────────────────────────────────────────────

public partial class MessageViewerViewModel : ObservableObject
{
    // ── File info ─────────────────────────────────────────────────────────────
    [ObservableProperty] private string _filePath      = "No file loaded";
    [ObservableProperty] private string _fileType      = string.Empty;
    [ObservableProperty] private string _captureTime   = string.Empty;
    [ObservableProperty] private string _entryCount    = string.Empty;
    [ObservableProperty] private string _statusMessage = "Open a .bin or .txt capture file to begin.";
    [ObservableProperty] private bool   _isLoaded;

    // ── Filter / search ───────────────────────────────────────────────────────
    [ObservableProperty] private string _filterText = string.Empty;
    partial void OnFilterTextChanged(string value) => ApplyFilter();

    [ObservableProperty] private string _selectedTypeFilter = "All";
    partial void OnSelectedTypeFilterChanged(string value) => ApplyFilter();

    public ObservableCollection<string> TypeFilters { get; } = new()
    {
        "All", "TELEMETRY", "STATUS", "START", "LOOPBACK",
        "CONFIG", "NULL", "DEBUG", "INVALID"
    };

    // ── Grid rows ─────────────────────────────────────────────────────────────
    private List<MessageRow> _allRows = new();
    public  ObservableCollection<MessageRow> Rows { get; } = new();

    // ── Selected row → detail panel ───────────────────────────────────────────
    [ObservableProperty] private MessageRow? _selectedRow;
    [ObservableProperty] private string      _detailText = string.Empty;

    partial void OnSelectedRowChanged(MessageRow? value)
    {
        DetailText = value is null ? string.Empty : BuildDetail(value.Entry);
    }

    // ── Stats bar ─────────────────────────────────────────────────────────────
    [ObservableProperty] private string _statsText = string.Empty;

    // ── Commands ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private void OpenFile()
    {
        var dlg = new WinForms.OpenFileDialog
        {
            Title  = "Open PM-LITE Capture File",
            Filter = "Capture files (*.bin;*.txt)|*.bin;*.txt|Binary (*.bin)|*.bin|Text (*.txt)|*.txt|All files (*.*)|*.*"
        };

        if (dlg.ShowDialog() != WinForms.DialogResult.OK) return;

        LoadFile(dlg.FileName);
    }

    [RelayCommand]
    private void ClearFilter()
    {
        FilterText          = string.Empty;
        SelectedTypeFilter  = "All";
    }

    [RelayCommand]
    private void ExportCsv()
    {
        if (!IsLoaded) return;

        var dlg = new WinForms.SaveFileDialog
        {
            Title      = "Export as CSV",
            Filter     = "CSV files (*.csv)|*.csv",
            FileName   = Path.GetFileNameWithoutExtension(FilePath) + "_export.csv"
        };

        if (dlg.ShowDialog() != WinForms.DialogResult.OK) return;

        try
        {
            var lines = new List<string> { "Timestamp,Type,Size,Summary,HexDump" };
            lines.AddRange(Rows.Select(r =>
                $"{r.Timestamp},{r.TypeLabel},{r.Size},\"{r.Summary}\",\"{r.HexDump}\""));
            File.WriteAllLines(dlg.FileName, lines);
            StatusMessage = $"Exported {Rows.Count} rows → {Path.GetFileName(dlg.FileName)}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Export error: {ex.Message}";
        }
    }

    // ── Load ──────────────────────────────────────────────────────────────────

    public void LoadFile(string path)
    {
        StatusMessage = $"Parsing {Path.GetFileName(path)}…";
        _allRows.Clear();
        Rows.Clear();
        IsLoaded = false;

        CaptureParseResult result;
        try   { result = CaptureFileParser.Parse(path); }
        catch (Exception ex) { StatusMessage = $"Error: {ex.Message}"; return; }

        if (!result.Success)
        {
            StatusMessage = $"Failed: {result.Error}";
            return;
        }

        // Populate header info
        FilePath    = path;
        FileType    = result.Info.FileType;
        CaptureTime = result.Info.CaptureTime.ToString("yyyy-MM-dd  HH:mm:ss.fff");
        EntryCount  = result.Info.EntryCount.ToString();

        // Build flat row objects from every entry
        _allRows = result.Entries.Select(e => new MessageRow
        {
            Timestamp = e.Timestamp.ToString("HH:mm:ss.fff"),
            TypeLabel = e.TypeLabel,
            Size      = e.Frame.Length.ToString(),
            Summary   = BuildSummary(e),
            HexDump   = e.HexDump,
            Entry     = e
        }).ToList();

        // Rebuild filter dropdown with actual types present
        var typesPresent = _allRows.Select(r => r.TypeLabel).Distinct().OrderBy(x => x).ToList();
        TypeFilters.Clear();
        TypeFilters.Add("All");
        foreach (var t in typesPresent) TypeFilters.Add(t);
        SelectedTypeFilter = "All";

        ApplyFilter();

        // Show any warnings from the parser
        if (result.Warnings.Count > 0)
            StatusMessage = $"Loaded with {result.Warnings.Count} warning(s): {result.Warnings[0]}";
        else
            StatusMessage = $"Loaded {_allRows.Count} messages from {Path.GetFileName(path)}";

        IsLoaded = true;
    }

    // ── Filter ────────────────────────────────────────────────────────────────

    private void ApplyFilter()
    {
        var filtered = _allRows.AsEnumerable();

        // Type dropdown
        if (SelectedTypeFilter != "All")
            filtered = filtered.Where(r => r.TypeLabel == SelectedTypeFilter);

        // Text search — matches timestamp, type, summary, or hex
        string q = FilterText.Trim();
        if (!string.IsNullOrEmpty(q))
        {
            filtered = filtered.Where(r =>
                r.Timestamp.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                r.TypeLabel.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                r.Summary.Contains(q,   StringComparison.OrdinalIgnoreCase) ||
                r.HexDump.Contains(q,   StringComparison.OrdinalIgnoreCase));
        }

        var list = filtered.ToList();

        Rows.Clear();
        foreach (var r in list) Rows.Add(r);

        // Stats: pass/fail only applies to loopback, rest just counts
        int total     = _allRows.Count;
        int shown     = list.Count;
        int telCount  = list.Count(r => r.TypeLabel == "TELEMETRY");
        StatsText = $"Showing {shown} of {total}  •  Telemetry: {telCount}";
    }

    // ── Summary line per message type ─────────────────────────────────────────
    // Short one-liner for the Summary column — enough to scan without opening detail

    private static string BuildSummary(CaptureEntry e) => e.Message switch
    {
        TelemetryMessage t =>
            $"ITM={t.Itm:F2}  MBC={t.Mbc:F2}  CLK={t.Clock:F2}  " +
            $"MRS={t.MrsText}  ITL={t.ItlText}",

        StatusMessage s =>
            $"FW={s.FirmwareVersion}  Serial={s.FirmwareSerial}  Mode={s.PmLiteMode}",

        LoopbackMessage lb =>
            $"Count={lb.Count}  D0=0x{lb.Data0:X2}  D1=0x{lb.Data1:X2}  " +
            $"D2=0x{lb.Data2:X2}  D3=0x{lb.Data3:X2}",

        ConfigurationMessage c =>
            $"PacFlight={c.PacFlight}  PacMissile={c.PacMissile}  " +
            $"TermSafed={c.TermSafed}  TermArmed={c.TermArmed}  " +
            $"Dud={c.DudOverride}  Reset={c.Reset}",

        NullMessage =>
            "Null keepalive",

        StartMessage st =>
            $"Raw content: {BitConverter.ToString(st.Content).Replace("-", " ")}",

        InvalidMessage inv =>
            $"INVALID — {inv.Reason}",

        _ =>
            $"Raw: {e.HexDump}"
    };

    // ── Full detail text for the bottom panel ─────────────────────────────────
    // Shows everything decoded — one field per line

    private static string BuildDetail(CaptureEntry e)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Timestamp  : {e.Timestamp:HH:mm:ss.fff}");
        sb.AppendLine($"Type       : {e.TypeLabel}  (byte={e.TypeByte})");
        sb.AppendLine($"Frame size : {e.Frame.Length} bytes");
        sb.AppendLine($"Hex dump   : {e.HexDump}");
        sb.AppendLine();

        switch (e.Message)
        {
            case TelemetryMessage t:
                sb.AppendLine("── Power ─────────────────────────");
                sb.AppendLine($"  ITM   : {t.Itm:F4}");
                sb.AppendLine($"  MBC   : {t.Mbc:F4}");
                sb.AppendLine($"  Clock : {t.Clock:F4}");
                sb.AppendLine($"  TWT   : {t.Twt:F4}");
                sb.AppendLine($"  Gyro  : {t.Gyro:F4}");
                sb.AppendLine();
                sb.AppendLine("── Temperature ───────────────────");
                sb.AppendLine($"  TVM   : {t.TvmTemp:F2}  HTR={t.TvmHtr}");
                sb.AppendLine($"  MMP   : {t.MmpTemp:F2}  HTR={t.MmpHtr}");
                sb.AppendLine($"  PLLO  : {t.PlloTemp:F2}  HTR={t.PlloHtr}");
                sb.AppendLine($"  Delay : {t.DelayTemp:F2}  HTR={t.DelayHtr}");
                sb.AppendLine($"  Gyro  : {t.GyroTemp:F2}  HTR={t.GyroHtr}");
                sb.AppendLine($"  ISA   : {t.IsaTemp:F2}  HTR={t.IsaHtr}");
                sb.AppendLine($"  CAS   : {t.CasTemp:F2}  HTR={t.CasHtr}");
                sb.AppendLine();
                sb.AppendLine("── Launch Sequence ───────────────");
                sb.AppendLine($"  MRS : {t.MrsText}");
                sb.AppendLine($"  ITL : {t.ItlText}");
                sb.AppendLine($"  SLS : {t.SlsText}");
                sb.AppendLine($"  MIR : {t.MirText}");
                sb.AppendLine($"  AWY : {t.AwyText}");
                sb.AppendLine($"  LSF : {t.LsfText}");
                sb.AppendLine();
                sb.AppendLine($"  MDC    : {t.MdcText}");
                sb.AppendLine($"  Octal  : {t.OctalText}");
                break;

            case StatusMessage s:
                sb.AppendLine("── Firmware Info ─────────────────");
                sb.AppendLine($"  Version  : {s.FirmwareVersion}");
                sb.AppendLine($"  Serial   : {s.FirmwareSerial}");
                sb.AppendLine($"  Build    : {s.FirmwareBuild}");
                sb.AppendLine($"  Date     : {s.FirmwareDate}");
                sb.AppendLine($"  Time     : {s.FirmwareTime}");
                sb.AppendLine($"  Mode     : {s.PmLiteMode}");
                break;

            case LoopbackMessage lb:
                sb.AppendLine("── Loopback Fields ───────────────");
                sb.AppendLine($"  Count : {lb.Count}");
                sb.AppendLine($"  Data0 : 0x{lb.Data0:X2}  ({lb.Data0})");
                sb.AppendLine($"  Data1 : 0x{lb.Data1:X2}  ({lb.Data1})");
                sb.AppendLine($"  Data2 : 0x{lb.Data2:X2}  ({lb.Data2})");
                sb.AppendLine($"  Data3 : 0x{lb.Data3:X2}  ({lb.Data3})");
                break;

            case ConfigurationMessage c:
                sb.AppendLine("── Configuration Flags ───────────");
                sb.AppendLine($"  PacFlight   : {c.PacFlight}");
                sb.AppendLine($"  PacMissile  : {c.PacMissile}");
                sb.AppendLine($"  TermSafed   : {c.TermSafed}");
                sb.AppendLine($"  TermArmed   : {c.TermArmed}");
                sb.AppendLine($"  DudOverride : {c.DudOverride}");
                sb.AppendLine($"  Reset       : {c.Reset}");
                break;

            case InvalidMessage inv:
                sb.AppendLine($"  Reason: {inv.Reason}");
                break;

            default:
                sb.AppendLine("  (No detailed decoder for this message type)");
                break;
        }

        return sb.ToString();
    }
}
