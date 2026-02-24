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
/// ExactSize  — size field must equal this exactly  (null = use Min/Max range)
/// MinSize    — used when ExactSize is null
/// MaxSize    — used when ExactSize is null
/// All sizes include the 3-byte header.
/// </summary>
public class MessageSpec
{
    public int?  ExactSize { get; init; }
    public int   MinSize   { get; init; } = 3;
    public int   MaxSize   { get; init; } = 512;

    public bool IsSizeValid(int size) =>
        ExactSize.HasValue ? size == ExactSize.Value
                           : size >= MinSize && size <= MaxSize;

    public string SizeDescription =>
        ExactSize.HasValue ? $"exactly {ExactSize}" : $"{MinSize}-{MaxSize}";
}

// ── MessageFrameBuffer ───────────────────────────────────────────────────────
/// <summary>
/// Accumulates raw TCP bytes and extracts complete validated frames.
/// An unknown type byte or a size mismatch causes the parser to skip one
/// byte and retry so a corrupt byte never stalls the stream permanently.
/// </summary>
public class MessageFrameBuffer
{
    private readonly List<byte> _buf = new();
    private const int HeaderSize = 3;

    // ── Message specs ─────────────────────────────────────────────────────────
    // ExactSize = the size field in the 3-byte header MUST match exactly.
    // MinSize/MaxSize = acceptable range when exact size is not yet known.
    //
    // TO UPDATE: change MinSize/MaxSize to ExactSize once you confirm the size
    // for Debug, Request, and TestData.
    // All sizes include the 3-byte header (type + sizeL + sizeH).
    private static readonly Dictionary<byte, MessageSpec> Specs = new()
    {
        [MessageTypes.Start]         = new() { ExactSize = 12  },  // [1, 12, 0, ...]
        [MessageTypes.Telemetry]     = new() { ExactSize = 92  },  // [2, 92, 0, ...]
        [MessageTypes.Status]        = new() { ExactSize = 10  },  // [5, 10, 0, ...]
        [MessageTypes.Loopback]      = new() { ExactSize = 8   },  // [7,  8, 0, ...]
        [MessageTypes.Configuration] = new() { ExactSize = 5   },  // [8,  5, 0, ...]
        [MessageTypes.Null]          = new() { ExactSize = 3   },  // [9,  3, 0]

        // Update to ExactSize once you confirm these:
        [MessageTypes.Debug]         = new() { MinSize = 4,  MaxSize = 256 },
        [MessageTypes.Request]       = new() { MinSize = 3,  MaxSize = 64  },
        [MessageTypes.TestData]      = new() { MinSize = 4,  MaxSize = 512 },

        // Type 0 should never arrive but handle gracefully
        [MessageTypes.Invalid]       = new() { MinSize = 3,  MaxSize = 16  },
    };

    public void Append(byte[] data, int count) =>
        _buf.AddRange(new ArraySegment<byte>(data, 0, count));

    public IEnumerable<ParseResult> DrainMessages()
    {
        var results = new List<ParseResult>();
        int offset  = 0;

        while (offset < _buf.Count)
        {
            if (_buf.Count - offset < HeaderSize) break;

            byte   type = _buf[offset];
            ushort size = (ushort)((_buf[offset + 2] << 8) | _buf[offset + 1]);

            // Check 1: type must be a known message type
            if (!Specs.TryGetValue(type, out var spec))
            {
                results.Add(ParseResult.Failure(
                    $"Unknown type 0x{type:X2} at offset {offset} — skipping byte."));
                offset++;
                continue;
            }

            // Check 2: size must match the spec (exact or range)
            if (!spec.IsSizeValid(size))
            {
                results.Add(ParseResult.Failure(
                    $"Type 0x{type:X2} at offset {offset}: size={size} " +
                    $"expected {spec.SizeDescription} — skipping byte."));
                offset++;
                continue;
            }

            // Check 3: wait for the full frame to arrive
            if (_buf.Count - offset < size) break;

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
