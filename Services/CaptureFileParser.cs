using System.IO;
using System.Text;
using PmLiteMonitor.Messages;
using PmLiteMonitor.Networking;

namespace PmLiteMonitor.Services;

// ── Data model ───────────────────────────────────────────────────────────────

/// <summary>One frame entry as loaded from disk.</summary>
public class CaptureEntry
{
    public DateTime  Timestamp { get; init; }
    public byte[]    Frame     { get; init; } = Array.Empty<byte>();
    public IMessage  Message   { get; init; } = new InvalidMessage("Not parsed");

    // Convenience accessors
    public byte   TypeByte   => Frame.Length > 0 ? Frame[0] : (byte)0;
    public ushort SizeField  => Frame.Length >= 3
        ? (ushort)((Frame[2] << 8) | Frame[1]) : (ushort)0;
    public string TypeLabel  => TypeByte switch
    {
        0 => "INVALID", 1 => "START",    2 => "TELEMETRY",
        3 => "DEBUG",   4 => "REQUEST",  5 => "STATUS",
        6 => "TESTDATA",7 => "LOOPBACK", 8 => "CONFIG",
        9 => "NULL",    _ => $"0x{TypeByte:X2}"
    };
    public string HexDump => BitConverter.ToString(Frame).Replace("-", " ");
}

/// <summary>Metadata from the file header.</summary>
public class CaptureFileInfo
{
    public string   FilePath    { get; init; } = string.Empty;
    public string   FileType    { get; init; } = string.Empty;   // "BIN" or "TXT"
    public DateTime CaptureTime { get; init; }
    public int      EntryCount  { get; init; }
    public string   Version     { get; init; } = string.Empty;
}

/// <summary>Complete result returned by the parser.</summary>
public class CaptureParseResult
{
    public CaptureFileInfo          Info    { get; init; } = new();
    public List<CaptureEntry>       Entries { get; init; } = new();
    public List<string>             Warnings{ get; init; } = new();
    public bool                     Success { get; init; }
    public string                   Error   { get; init; } = string.Empty;
}

// ── Parser ───────────────────────────────────────────────────────────────────

/// <summary>
/// Reads .bin and .txt capture files written by TelemetryRecorder
/// and returns a structured CaptureParseResult.
///
/// Binary format (written by BinaryWriter, little-endian):
///
///   FILE HEADER
///   ┌─────────────────────────────────────────────────┐
///   │ 10 bytes  Magic string  "PMLITE_TLM" (ASCII)   │
///   │  1 byte   Version       (currently 1)           │
///   │  4 bytes  Entry count   (Int32)                 │
///   │  8 bytes  Capture time  (Unix ms, Int64)        │
///   └─────────────────────────────────────────────────┘
///
///   REPEATED PER ENTRY
///   ┌─────────────────────────────────────────────────┐
///   │  8 bytes  Timestamp     (Unix ms, Int64)        │
///   │  4 bytes  Frame length  (Int32)                 │
///   │  N bytes  Raw frame     (type + sizeL + sizeH   │
///   │                          + content bytes)       │
///   └─────────────────────────────────────────────────┘
///
/// Text format:
///   Lines 1-6 are the file header (metadata).
///   Lines 7+ are data rows:
///     HH:mm:ss.fff  typeNum  TYPENAME  frameLen  HH-HH-HH...
/// </summary>
public static class CaptureFileParser
{
    private static readonly MessageParser _msgParser = new();
    private const string Magic = "PMLITE_TLM";

    // ── Public entry point ────────────────────────────────────────────────────

    public static CaptureParseResult Parse(string filePath)
    {
        if (!File.Exists(filePath))
            return Fail($"File not found: {filePath}");

        string ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
        {
            ".bin" => ParseBinary(filePath),
            ".txt" => ParseText(filePath),
            _      => Fail($"Unknown extension '{ext}' — expected .bin or .txt")
        };
    }

    // ── Binary parser ─────────────────────────────────────────────────────────

    /// <summary>
    /// Step-by-step binary parse:
    ///
    ///  1. Open FileStream → wrap in BinaryReader (handles little-endian reads)
    ///  2. Read and verify magic bytes "PMLITE_TLM"
    ///  3. Read version byte
    ///  4. Read Int32 entry count
    ///  5. Read Int64 capture timestamp → convert to DateTime
    ///  6. Loop entry count times:
    ///       a. Read Int64 entry timestamp
    ///       b. Read Int32 frame length
    ///       c. Read exactly that many bytes → raw frame
    ///       d. Feed frame into MessageParser to get a typed IMessage
    ///       e. Wrap in CaptureEntry
    /// </summary>
    private static CaptureParseResult ParseBinary(string filePath)
    {
        var warnings = new List<string>();
        var entries  = new List<CaptureEntry>();

        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            using var br = new BinaryReader(fs, Encoding.ASCII);

            // ── Step 2: Magic ─────────────────────────────────────────────────
            // ReadBytes(10) reads exactly 10 bytes from the current position.
            // GetString converts those bytes back to a string using ASCII encoding.
            byte[] magicBytes = br.ReadBytes(Magic.Length);
            string magic      = Encoding.ASCII.GetString(magicBytes);

            if (magic != Magic)
                return Fail($"Bad magic '{magic}' — not a PM-LITE capture file");

            // ── Step 3: Version ───────────────────────────────────────────────
            // ReadByte() advances the stream by exactly 1 byte.
            byte version = br.ReadByte();

            // ── Step 4: Entry count ───────────────────────────────────────────
            // ReadInt32() reads 4 bytes, interprets as little-endian signed int.
            int entryCount = br.ReadInt32();

            // ── Step 5: Capture timestamp ─────────────────────────────────────
            // ReadInt64() reads 8 bytes → Unix milliseconds since 1970-01-01 UTC.
            // DateTimeOffset.FromUnixTimeMilliseconds converts to a real DateTime.
            long captureMs   = br.ReadInt64();
            var  captureTime = DateTimeOffset.FromUnixTimeMilliseconds(captureMs).LocalDateTime;

            var info = new CaptureFileInfo
            {
                FilePath    = filePath,
                FileType    = "BIN",
                CaptureTime = captureTime,
                EntryCount  = entryCount,
                Version     = $"v{version}"
            };

            // ── Step 6: Entries ───────────────────────────────────────────────
            for (int i = 0; i < entryCount; i++)
            {
                // Check we haven't hit end of file early
                if (fs.Position >= fs.Length)
                {
                    warnings.Add($"Unexpected EOF at entry {i} of {entryCount}");
                    break;
                }

                // 6a: Entry timestamp (8 bytes)
                long entryMs  = br.ReadInt64();
                var  entryTs  = DateTimeOffset.FromUnixTimeMilliseconds(entryMs).LocalDateTime;

                // 6b: Frame length (4 bytes)
                int frameLen  = br.ReadInt32();

                if (frameLen < 0 || frameLen > 65535)
                {
                    warnings.Add($"Entry {i}: suspicious frame length {frameLen}, skipping");
                    continue;
                }

                // 6c: Raw frame bytes (frameLen bytes)
                byte[] frame  = br.ReadBytes(frameLen);

                if (frame.Length != frameLen)
                {
                    warnings.Add($"Entry {i}: expected {frameLen} bytes, got {frame.Length}");
                    continue;
                }

                // 6d: Parse frame → typed IMessage
                IMessage msg;
                try   { msg = _msgParser.Parse(frame); }
                catch { msg = new InvalidMessage("Parse threw exception", frame); }

                // 6e: Wrap and collect
                entries.Add(new CaptureEntry
                {
                    Timestamp = entryTs,
                    Frame     = frame,
                    Message   = msg
                });
            }

            return new CaptureParseResult
            {
                Info     = info,
                Entries  = entries,
                Warnings = warnings,
                Success  = true
            };
        }
        catch (Exception ex)
        {
            return Fail($"Binary parse error: {ex.Message}");
        }
    }

    // ── Text parser ───────────────────────────────────────────────────────────

    /// <summary>
    /// Text file format written by TelemetryRecorder:
    ///
    ///   Line 0: "PM-LITE Telemetry Capture — MRS ON @ yyyy-MM-dd HH:mm:ss.fff"
    ///   Line 1: "Pre-window  : 100 ms"
    ///   Line 2: "Post-window : 4000 ms"
    ///   Line 3: "Total frames: 47"
    ///   Line 4: separator (dashes)
    ///   Line 5: column header
    ///   Line 6: separator (dashes)
    ///   Line 7+: HH:mm:ss.fff  typeNum  TYPENAME  frameLen  HH-HH-HH...
    ///
    /// Parse strategy:
    ///  1. Read all lines
    ///  2. Extract capture time from line 0
    ///  3. Extract frame count from line 3
    ///  4. Skip 7-line header block
    ///  5. For each data line:
    ///       a. Split on whitespace
    ///       b. Parse timestamp (today's date + HH:mm:ss.fff)
    ///       c. Parse hex dump back into byte[]
    ///       d. Feed into MessageParser → typed IMessage
    /// </summary>
    private static CaptureParseResult ParseText(string filePath)
    {
        var warnings = new List<string>();
        var entries  = new List<CaptureEntry>();

        try
        {
            string[] lines = File.ReadAllLines(filePath, Encoding.UTF8);

            if (lines.Length < 7)
                return Fail("File too short — not a valid PM-LITE capture text file");

            // ── Header line 0 — capture time ─────────────────────────────────
            // Format: "PM-LITE Telemetry Capture — MRS ON @ yyyy-MM-dd HH:mm:ss.fff"
            DateTime captureTime = DateTime.Now;
            const string timeSep = "@ ";
            int atIdx = lines[0].IndexOf(timeSep, StringComparison.Ordinal);
            if (atIdx >= 0)
            {
                string timeStr = lines[0][(atIdx + timeSep.Length)..].Trim();
                DateTime.TryParse(timeStr, out captureTime);
            }

            // ── Header line 3 — total frames ─────────────────────────────────
            // Format: "Total frames: 47"
            int entryCount = 0;
            if (lines[3].StartsWith("Total frames:"))
                int.TryParse(lines[3].Split(':')[1].Trim(), out entryCount);

            var info = new CaptureFileInfo
            {
                FilePath    = filePath,
                FileType    = "TXT",
                CaptureTime = captureTime,
                EntryCount  = entryCount,
                Version     = "n/a"
            };

            // ── Data lines — skip the 7-line header block ─────────────────────
            for (int lineNum = 7; lineNum < lines.Length; lineNum++)
            {
                string line = lines[lineNum].Trim();
                if (string.IsNullOrWhiteSpace(line)) continue;

                // Split into tokens: [time] [typeNum] [typeName] [frameLen] [hex...]
                // The hex part may have spaces between bytes so we only split first 4
                string[] tokens = line.Split(' ', 5, StringSplitOptions.RemoveEmptyEntries);

                if (tokens.Length < 5)
                {
                    warnings.Add($"Line {lineNum}: too few tokens, skipping");
                    continue;
                }

                // Token 0: HH:mm:ss.fff — use today's date + captured time
                string timeToken = tokens[0].Trim();
                DateTime entryTs = captureTime.Date;
                if (TimeSpan.TryParse(timeToken, out TimeSpan tod))
                    entryTs = captureTime.Date + tod;

                // Tokens 1,2,3 — typeNum, typeName, frameLen (we don't need these
                // because we reconstruct the message from the hex dump directly)

                // Token 4: space-separated hex bytes "08 05 00 00 05"
                string hexPart = tokens[4].Trim();
                byte[] frame;
                try
                {
                    frame = hexPart.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                                   .Select(h => Convert.ToByte(h, 16))
                                   .ToArray();
                }
                catch
                {
                    warnings.Add($"Line {lineNum}: bad hex '{hexPart}', skipping");
                    continue;
                }

                // Parse frame → typed IMessage (same parser as the live path)
                IMessage msg;
                try   { msg = _msgParser.Parse(frame); }
                catch { msg = new InvalidMessage("Parse threw", frame); }

                entries.Add(new CaptureEntry
                {
                    Timestamp = entryTs,
                    Frame     = frame,
                    Message   = msg
                });
            }

            return new CaptureParseResult
            {
                Info     = info,
                Entries  = entries,
                Warnings = warnings,
                Success  = true
            };
        }
        catch (Exception ex)
        {
            return Fail($"Text parse error: {ex.Message}");
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static CaptureParseResult Fail(string error) => new()
    {
        Success = false,
        Error   = error
    };
}
