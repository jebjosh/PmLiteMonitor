namespace PmLiteMonitor.Messages;

public interface IMessage
{
    byte   MessageType { get; }
    ushort Size        { get; }
    byte[] RawData     { get; }
    byte[] ToBytes();
}
