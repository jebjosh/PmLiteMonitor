using System.Text;

namespace PmLiteMonitor.Messages;

// ── 0: InvalidMessage ────────────────────────────────────────────────────────
public class InvalidMessage : BaseMessage
{
    public string Reason { get; }
    public InvalidMessage(string reason, byte[]? raw = null)
        : base(MessageTypes.Invalid, raw ?? Array.Empty<byte>())
    {
        Reason = reason;
    }
}

// ── 1: StartMessage ──────────────────────────────────────────────────────────
public class StartMessage : BaseMessage
{
    public byte[] Content { get; }

    // Receive
    public StartMessage(ushort size, byte[] content) : base()
    {
        MessageType = MessageTypes.Start; Size = size; RawData = content; Content = content;
    }
    // Send
    public StartMessage(byte[] content) : base(MessageTypes.Start, content) { Content = content; }
}

// ── 2: TelemetryMessage ──────────────────────────────────────────────────────
/// <summary>
/// Fill ParseContent() with your real firmware byte offsets.
/// Every property here maps directly to a UI binding in MainWindow.
/// </summary>
public class TelemetryMessage : BaseMessage
{
    // --- Power ---
    public float Itm   { get; private set; }
    public float Mbc   { get; private set; }
    public float Clock { get; private set; }
    public float Twt   { get; private set; }
    public float Gyro  { get; private set; }

    // --- Temperature ---
    public float TvmTemp   { get; private set; }
    public float MmpTemp   { get; private set; }
    public float PlloTemp  { get; private set; }
    public float DelayTemp { get; private set; }
    public float GyroTemp  { get; private set; }
    public float IsaTemp   { get; private set; }
    public float CasTemp   { get; private set; }

    // --- HTR on/off ---
    public bool TvmHtr   { get; private set; }
    public bool MmpHtr   { get; private set; }
    public bool PlloHtr  { get; private set; }
    public bool DelayHtr { get; private set; }
    public bool GyroHtr  { get; private set; }
    public bool IsaHtr   { get; private set; }
    public bool CasHtr   { get; private set; }

    // --- Launch Sequence states ---
    public bool MrsOn { get; private set; }
    public bool ItlOn { get; private set; }
    public bool SlsOn { get; private set; }
    public bool MirOn { get; private set; }
    public bool AwyOn { get; private set; }
    public bool LsfOn { get; private set; }

    public string MrsText { get; private set; } = "OFF";
    public string ItlText { get; private set; } = "OFF";
    public string SlsText { get; private set; } = "OFF";
    public string MirText { get; private set; } = "OFF";
    public string AwyText { get; private set; } = "OFF";
    public string LsfText { get; private set; } = "PASS";

    public string MdcText   { get; private set; } = "MDC: 0 0 0 0 0 0 0 0 0 0 0 0 0, Validity: Invalid";
    public string OctalText { get; private set; } = "Octal: 0 0 0 0 0 0 0 0 0 0 0 0 0";

    // --- Safe & Arming ---
    public float ChrgCmd   { get; private set; }
    public float Pafu      { get; private set; }
    public float FiveVdc   { get; private set; }
    public float Condition { get; private set; }
    public float LeftCap   { get; private set; }
    public float LeftLatch { get; private set; }
    public float RightCap  { get; private set; }
    public float Right     { get; private set; }

    // --- One Shots ---
    public float OneTvm  { get; private set; }
    public float OneMmp  { get; private set; }
    public float OneCas  { get; private set; }
    public float OneGas  { get; private set; }
    public float OnePafu { get; private set; }

    // --- Missile info ---
    public string MissileType      { get; private set; } = "Unknown";
    public float  MissileFrequency { get; private set; }
    public float  MissileAddress   { get; private set; }

    // Receive
    public TelemetryMessage(ushort size, byte[] content) : base()
    {
        MessageType = MessageTypes.Telemetry; Size = size; RawData = content;
        ParseContent(content);
    }
    // Send
    public TelemetryMessage(byte[] content) : base(MessageTypes.Telemetry, content)
    { ParseContent(content); }

    /// <summary>
    /// *** FILL IN YOUR REAL BYTE OFFSETS HERE ***
    /// Example patterns shown; replace with actual firmware spec offsets.
    /// </summary>
    private void ParseContent(byte[] c)
    {
        if (c.Length == 0) return;

        // ---- Example stubs — replace these with real offsets ----
        // Power (example: 4-byte floats starting at byte 0)
        if (c.Length >= 4)  Itm   = BitConverter.ToSingle(c, 0);
        if (c.Length >= 8)  Mbc   = BitConverter.ToSingle(c, 4);
        if (c.Length >= 12) Clock = BitConverter.ToSingle(c, 8);
        if (c.Length >= 16) Twt   = BitConverter.ToSingle(c, 12);
        if (c.Length >= 20) Gyro  = BitConverter.ToSingle(c, 16);

        // Launch Sequence states (example: bytes 20-25 as 0/1)
        if (c.Length >= 21) { MrsOn = c[20] != 0; MrsText = MrsOn ? "ON" : "OFF"; }
        if (c.Length >= 22) { ItlOn = c[21] != 0; ItlText = ItlOn ? "ON" : "OFF"; }
        if (c.Length >= 23) { SlsOn = c[22] != 0; SlsText = SlsOn ? "ON" : "OFF"; }
        if (c.Length >= 24) { MirOn = c[23] != 0; MirText = MirOn ? "ON" : "OFF"; }
        if (c.Length >= 25) { AwyOn = c[24] != 0; AwyText = AwyOn ? "ON" : "OFF"; }
        if (c.Length >= 26) { LsfOn = c[25] != 0; LsfText = LsfOn ? "PASS" : "FAIL"; }

        // Add remaining fields using your real firmware offsets ...
    }
}

// ── 3: DebugMessage ──────────────────────────────────────────────────────────
public class DebugMessage : BaseMessage
{
    public string Text { get; }
    public DebugMessage(ushort size, byte[] content) : base()
    {
        MessageType = MessageTypes.Debug; Size = size; RawData = content;
        Text = Encoding.ASCII.GetString(content).TrimEnd('\0');
    }
    public DebugMessage(string text) : base(MessageTypes.Debug, Encoding.ASCII.GetBytes(text))
    { Text = text; }
}

// ── 4: RequestMessage ────────────────────────────────────────────────────────
public class RequestMessage : BaseMessage
{
    public byte RequestedType { get; }
    public RequestMessage(ushort size, byte[] content) : base()
    {
        MessageType = MessageTypes.Request; Size = size; RawData = content;
        RequestedType = content.Length > 0 ? content[0] : (byte)0;
    }
    public RequestMessage(byte requestedType)
        : base(MessageTypes.Request, new[] { requestedType })
    { RequestedType = requestedType; }
}

// ── 5: StatusMessage ─────────────────────────────────────────────────────────
public class StatusMessage : BaseMessage
{
    public string FirmwareVersion    { get; private set; } = "-";
    public string FirmwareSerial     { get; private set; } = "-";
    public string FirmwareBuild      { get; private set; } = "-";
    public string FirmwareDate       { get; private set; } = "-";
    public string FirmwareTime       { get; private set; } = "-";
    public string PmLiteMode         { get; private set; } = "Unknown";

    public StatusMessage(ushort size, byte[] content) : base()
    {
        MessageType = MessageTypes.Status; Size = size; RawData = content;
        ParseContent(content);
    }
    public StatusMessage(byte[] content) : base(MessageTypes.Status, content)
    { ParseContent(content); }

    /// <summary>Fill in your real firmware offsets here.</summary>
    private void ParseContent(byte[] c)
    {
        // Example:
        // if (c.Length >= 16) FirmwareVersion = Encoding.ASCII.GetString(c, 0, 16).TrimEnd('\0');
        // if (c.Length >= 32) FirmwareSerial  = Encoding.ASCII.GetString(c, 16, 16).TrimEnd('\0');
        // if (c.Length >= 64) PmLiteMode      = c[64] == 1 ? "Active" : "Standby";
    }
}

// ── 6: TestDataMessage ───────────────────────────────────────────────────────
public class TestDataMessage : BaseMessage
{
    public byte[] Content { get; }
    public TestDataMessage(ushort size, byte[] content) : base()
    {
        MessageType = MessageTypes.TestData; Size = size; RawData = content; Content = content;
    }
    public TestDataMessage(byte[] content) : base(MessageTypes.TestData, content) { Content = content; }
}

// ── 7: LoopbackMessage ───────────────────────────────────────────────────────
/// <summary>
/// Wire layout (content portion):
/// [count][data0][data1][data2][data3]
/// </summary>
public class LoopbackMessage : BaseMessage
{
    public byte Count { get; }
    public byte Data0 { get; }
    public byte Data1 { get; }
    public byte Data2 { get; }
    public byte Data3 { get; }

    public LoopbackMessage(ushort size, byte[] content) : base()
    {
        MessageType = MessageTypes.Loopback; Size = size; RawData = content;
        if (content.Length >= 5)
        { Count = content[0]; Data0 = content[1]; Data1 = content[2]; Data2 = content[3]; Data3 = content[4]; }
    }
    public LoopbackMessage(byte count, byte d0, byte d1, byte d2, byte d3)
        : base(MessageTypes.Loopback, new[] { count, d0, d1, d2, d3 })
    { Count = count; Data0 = d0; Data1 = d1; Data2 = d2; Data3 = d3; }
}

// ── 8: ConfigurationMessage ──────────────────────────────────────────────────
/// <summary>
/// Content layout: [pacFlight][pacMissile][termSafed][termArmed][dudOverride][reset]
/// Each byte is 0 or 1.
/// </summary>
public class ConfigurationMessage : BaseMessage
{
    public bool PacFlight   { get; }
    public bool PacMissile  { get; }
    public bool TermSafed   { get; }
    public bool TermArmed   { get; }
    public bool DudOverride { get; }
    public bool Reset       { get; }

    public ConfigurationMessage(ushort size, byte[] content) : base()
    {
        MessageType = MessageTypes.Configuration; Size = size; RawData = content;
        if (content.Length >= 6)
        {
            PacFlight   = content[0] != 0;
            PacMissile  = content[1] != 0;
            TermSafed   = content[2] != 0;
            TermArmed   = content[3] != 0;
            DudOverride = content[4] != 0;
            Reset       = content[5] != 0;
        }
    }
    public ConfigurationMessage(bool pacF, bool pacM, bool termS, bool termA, bool dud, bool reset)
        : base(MessageTypes.Configuration, new byte[]
        { (byte)(pacF?1:0),(byte)(pacM?1:0),(byte)(termS?1:0),(byte)(termA?1:0),(byte)(dud?1:0),(byte)(reset?1:0) })
    { PacFlight=pacF; PacMissile=pacM; TermSafed=termS; TermArmed=termA; DudOverride=dud; Reset=reset; }
}

// ── 9: NullMessage ───────────────────────────────────────────────────────────
public class NullMessage : BaseMessage
{
    public NullMessage(ushort size, byte[] content) : base()
    { MessageType = MessageTypes.Null; Size = size; RawData = content; }
    public NullMessage() : base(MessageTypes.Null, Array.Empty<byte>()) { }
}
