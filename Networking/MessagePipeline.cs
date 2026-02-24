using System.Collections.Generic;
using PmLiteMonitor.Messages;

namespace PmLiteMonitor.Networking;

// ── ParseResult ──────────────────────────────────────────────────────────────
public class ParseResult
{
    public bool    IsSuccess    { get; private set; }
    public byte[]? RawFrame     { get; private set; }
    public string? ErrorMessage { get; private set; }

    public static ParseResult Success(byte[] f)   => new() { IsSuccess = true,  RawFrame = f };
    public static ParseResult Failure(string msg) => new() { IsSuccess = false, ErrorMessage = msg };
}

// ── MessageSpec ───────────────────────────────────────────────────────────────
/// <summary>
/// Describes the expected frame size for one message type.
///
/// ExactSize  — the size field must equal this value exactly (null = unknown, use range)
/// MinSize    — used when ExactSize is null; size must be at least this
/// MaxSize    — used when ExactSize is null; size must be no more than this
///
/// All sizes include the 3-byte header.
/// </summary>
public class MessageSpec
{
    public int?  ExactSize { get; init; }
    public int   MinSize   { get; init; } = 3;
    public int   MaxSize   { get; init; } = 512;

    /// <summary>Returns true if the size field from the header is acceptable.</summary>
    public bool IsSizeValid(int size)
    {
        if (ExactSize.HasValue)
            return size == ExactSize.Value;

        return size >= MinSize && size <= MaxSize;
    }

    public string SizeDescription =>
        ExactSize.HasValue
            ? $"exactly {ExactSize}"
            : $"{MinSize}–{MaxSize}";
}

// ── MessageFrameBuffer ───────────────────────────────────────────────────────
/// <summary>
/// Accumulates raw TCP bytes and extracts complete validated frames.
///
/// Each message type has a MessageSpec that defines its expected size.
/// An unknown type byte or a size that doesn't match the spec causes the
/// parser to skip one byte and try again from the next position.
/// </summary>
public class MessageFrameBuffer
{
    private readonly List<byte> _buf = new();
    private const int HeaderSize = 3;

    // ── Message specs ────────────────────────────────────────────────────────
    // ExactSize   → size field in the header MUST equal this value (header+content)
    // MinSize/Max → size field must fall within this range (for unknown-size types)
    //
    // TO ADD A NEW TYPE: add an entry with its known ExactSize, or a Min/Max range.
    // TO UPDATE A SIZE:  change ExactSize to the correct value.
    // UNKNOWN SIZES:     leave ExactSize = null and set a tight Min/Max range.
    private static readonly Dictionary<byte, MessageSpec> Specs = new()
    {
        // type  ExactSize  (header 3 bytes + content bytes)
        [MessageTypes.Start]         = new() { ExactSize = 12  },  // [1,  12, 0, ...]
        [MessageTypes.Telemetry]     = new() { ExactSize = 92  },  // [2,  92, 0, ...]
        [MessageTypes.Status]        = new() { ExactSize = 10  },  // [5,  10, 0, ...]
        [MessageTypes.Loopback]      = new() { ExactSize = 8   },  // [7,   8, 0, ...]
        [MessageTypes.Configuration] = new() { ExactSize = 5   },  // [8,   5, 0, ...]
        [MessageTypes.Null]          = new() { ExactSize = 3   },  // [9,   3, 0]  header only

        // ── Fill these in once you know the sizes ────────────────────────────
        [MessageTypes.Debug]         = new() { MinSize = 4, MaxSize = 256 },
        [MessageTypes.Request]       = new() { MinSize = 3, MaxSize = 64  },
        [MessageTypes.TestData]      = new() { MinSize = 4, MaxSize = 512 },

        // Invalid (type=0) — should never be received, but handle it gracefully
        [MessageTypes.Invalid]       = new() { MinSize = 3, MaxSize = 16  },
    };

    public void Append(byte[] data, int count) =>
        _buf.AddRange(new ArraySegment<byte>(data, 0, count));

    public IEnumerable<ParseResult> DrainMessages()
    {
        var results = new List<ParseResult>();
        int offset  = 0;

        while (offset < _buf.Count)
        {
            // Need at least 3 bytes to read the header
            if (_buf.Count - offset < HeaderSize) break;

            byte   type = _buf[offset];
            ushort size = (ushort)((_buf[offset + 2] << 8) | _buf[offset + 1]);

            // ── Check 1: type byte must be a known type ──────────────────────
            if (!Specs.TryGetValue(type, out var spec))
            {
                results.Add(ParseResult.Failure(
                    $"Unknown type 0x{type:X2} at offset {offset} — skipping byte."));
                offset++;
                continue;
            }

            // ── Check 2: size must match the spec for this type ──────────────
            // For known-size types this is an exact match.
            // For unknown-size types this is a range check.
            if (!spec.IsSizeValid(size))
            {
                results.Add(ParseResult.Failure(
                    $"Type 0x{type:X2} at offset {offset}: " +
                    $"size={size} expected {spec.SizeDescription} — skipping byte."));
                offset++;
                continue;
            }

            // ── Check 3: full frame must be in the buffer ────────────────────
            // Header looks valid — wait for all bytes to arrive before consuming
            if (_buf.Count - offset < size) break;

            // Happy path — extract the complete frame
            results.Add(ParseResult.Success(_buf.GetRange(offset, size).ToArray()));
            offset += size;
        }

        if (offset > 0) _buf.RemoveRange(0, offset);
        return results;
    }

    public void Clear() => _buf.Clear();
    public int Count    => _buf.Count;
}

// ── MessageParser ────────────────────────────────────────────────────────────
public class MessageParser
{
    private const int HeaderSize = 3;

    public IMessage Parse(byte[] frame)
    {
        if (frame.Length < HeaderSize)
            return new InvalidMessage("Frame too small", frame);

        byte   type       = frame[0];
        ushort size       = (ushort)((frame[2] << 8) | frame[1]);
        int    contentLen = Math.Max(0, size - HeaderSize);
        byte[] content    = contentLen > 0
            ? frame[HeaderSize..(HeaderSize + contentLen)]
            : Array.Empty<byte>();

        return type switch
        {
            MessageTypes.Invalid       => new InvalidMessage("Received type 0", content),
            MessageTypes.Start         => new StartMessage(size, content),
            MessageTypes.Telemetry     => new TelemetryMessage(size, content),
            MessageTypes.Debug         => new DebugMessage(size, content),
            MessageTypes.Request       => new RequestMessage(size, content),
            MessageTypes.Status        => new StatusMessage(size, content),
            MessageTypes.TestData      => new TestDataMessage(size, content),
            MessageTypes.Loopback      => new LoopbackMessage(size, content),
            MessageTypes.Configuration => new ConfigurationMessage(size, content),
            MessageTypes.Null          => new NullMessage(size, content),
            _                          => new InvalidMessage($"Unknown type 0x{type:X2}", content)
        };
    }
}


namespace PmLiteMonitor.Networking;

// ── ParseResult ──────────────────────────────────────────────────────────────
public class ParseResult
{
    public bool    IsSuccess    { get; private set; }
    public byte[]? RawFrame     { get; private set; }
    public string? ErrorMessage { get; private set; }

    public static ParseResult Success(byte[] f)    => new() { IsSuccess = true,  RawFrame = f };
    public static ParseResult Failure(string msg)  => new() { IsSuccess = false, ErrorMessage = msg };
}

// ── MessageFrameBuffer ───────────────────────────────────────────────────────
/// <summary>
/// Accumulates raw TCP bytes and extracts complete, validated frames.
/// Never throws — bad bytes produce a Failure result and re-sync by one byte.
/// </summary>
public class MessageFrameBuffer
{
    private readonly List<byte> _buf = new();
    private const int HeaderSize = 3;

    // Set this to the largest message your firmware actually sends.
    // Much smaller than 65535 so corrupt size bytes get caught quickly.
    private const int MaxMsgSize = 512;

    // All valid type bytes your firmware can send.
    // Any other byte as type[0] is immediately treated as garbage.
    private static readonly HashSet<byte> ValidTypes = new()
    {
        MessageTypes.Invalid,
        MessageTypes.Start,
        MessageTypes.Telemetry,
        MessageTypes.Debug,
        MessageTypes.Request,
        MessageTypes.Status,
        MessageTypes.TestData,
        MessageTypes.Loopback,
        MessageTypes.Configuration,
        MessageTypes.Null,
    };

    public void Append(byte[] data, int count) =>
        _buf.AddRange(new ArraySegment<byte>(data, 0, count));

    public IEnumerable<ParseResult> DrainMessages()
    {
        var results = new List<ParseResult>();
        int offset  = 0;

        while (offset < _buf.Count)
        {
            // Need at least 3 bytes to read the header
            if (_buf.Count - offset < HeaderSize) break;

            byte   type = _buf[offset];
            ushort size = (ushort)((_buf[offset + 2] << 8) | _buf[offset + 1]);

            // ── Check 1: type byte must be a known type ──────────────────────
            // If byte[0] isn't a known type it's definitely garbage — skip it.
            if (!ValidTypes.Contains(type))
            {
                results.Add(ParseResult.Failure(
                    $"Unknown type 0x{type:X2} at offset {offset} — skipping byte."));
                offset++;
                continue;
            }

            // ── Check 2: size must be realistic ──────────────────────────────
            // Size includes the 3-byte header so minimum valid value is 3.
            // Anything above MaxMsgSize means we're misaligned — skip this byte.
            if (size < HeaderSize || size > MaxMsgSize)
            {
                results.Add(ParseResult.Failure(
                    $"Suspicious size {size} for type 0x{type:X2} at offset {offset} — skipping byte."));
                offset++;
                continue;
            }

            // ── Check 3: wait for the full frame to arrive ───────────────────
            // Both header checks passed — this looks like a real message.
            // If we don't have all the bytes yet just wait for the next TCP read.
            if (_buf.Count - offset < size) break;

            // Happy path — extract the complete frame
            results.Add(ParseResult.Success(_buf.GetRange(offset, size).ToArray()));
            offset += size;
        }

        if (offset > 0) _buf.RemoveRange(0, offset);
        return results;
    }

    public void Clear() => _buf.Clear();
    public int Count    => _buf.Count;
}

// ── MessageParser ────────────────────────────────────────────────────────────
public class MessageParser
{
    private const int HeaderSize = 3;

    public IMessage Parse(byte[] frame)
    {
        if (frame.Length < HeaderSize)
            return new InvalidMessage("Frame too small", frame);

        byte   type       = frame[0];
        ushort size       = (ushort)((frame[2] << 8) | frame[1]);
        int    contentLen = Math.Max(0, size - HeaderSize);
        byte[] content    = contentLen > 0
            ? frame[HeaderSize..(HeaderSize + contentLen)]
            : Array.Empty<byte>();

        return type switch
        {
            MessageTypes.Invalid       => new InvalidMessage("Received type 0", content),
            MessageTypes.Start         => new StartMessage(size, content),
            MessageTypes.Telemetry     => new TelemetryMessage(size, content),
            MessageTypes.Debug         => new DebugMessage(size, content),
            MessageTypes.Request       => new RequestMessage(size, content),
            MessageTypes.Status        => new StatusMessage(size, content),
            MessageTypes.TestData      => new TestDataMessage(size, content),
            MessageTypes.Loopback      => new LoopbackMessage(size, content),
            MessageTypes.Configuration => new ConfigurationMessage(size, content),
            MessageTypes.Null          => new NullMessage(size, content),
            _                          => new InvalidMessage($"Unknown type 0x{type:X2}", content)
        };
    }
}
