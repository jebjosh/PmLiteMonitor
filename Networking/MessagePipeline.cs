using PmLiteMonitor.Messages;

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
    private const int HeaderSize    = 3;
    private const int MaxMsgSize    = 65535;

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

            if (size < HeaderSize || size > MaxMsgSize)
            {
                results.Add(ParseResult.Failure(
                    $"Invalid size {size} for type 0x{type:X2} at offset {offset}. Re-syncing."));
                offset++;
                continue;
            }

            if (_buf.Count - offset < size) break; // incomplete — wait for more data

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
