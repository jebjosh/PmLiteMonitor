namespace PmLiteMonitor.Messages;

public static class MessageTypes
{
    public const byte Invalid       = 0;
    public const byte Start         = 1;
    public const byte Telemetry     = 2;
    public const byte Debug         = 3;
    public const byte Request       = 4;
    public const byte Status        = 5;
    public const byte TestData      = 6;
    public const byte Loopback      = 7;
    public const byte Configuration = 8;
    public const byte Null          = 9;
}

public abstract class BaseMessage : IMessage
{
    protected const int HeaderSize = 3;

    public byte   MessageType { get; protected set; }
    public ushort Size        { get; protected set; }
    public byte[] RawData     { get; protected set; } = Array.Empty<byte>();

    protected BaseMessage() { }

    protected BaseMessage(byte type, byte[] content)
    {
        MessageType = type;
        RawData     = content;
        Size        = (ushort)(HeaderSize + content.Length);
    }

    /// <summary>Serialize to wire frame: [type][sizeLow][sizeHigh][...content...]</summary>
    public virtual byte[] ToBytes()
    {
        var frame = new byte[Size];
        frame[0] = MessageType;
        frame[1] = (byte)(Size & 0xFF);
        frame[2] = (byte)(Size >> 8);
        if (RawData.Length > 0)
            RawData.CopyTo(frame, HeaderSize);
        return frame;
    }
}
