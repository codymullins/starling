namespace Starling.Net.Http.H2;

/// <summary>HTTP/2 frame types (RFC 9113 §6).</summary>
internal enum H2FrameType : byte
{
    Data = 0x0,
    Headers = 0x1,
    Priority = 0x2,
    RstStream = 0x3,
    Settings = 0x4,
    PushPromise = 0x5,
    Ping = 0x6,
    GoAway = 0x7,
    WindowUpdate = 0x8,
    Continuation = 0x9,
}

/// <summary>HTTP/2 frame flag bits (RFC 9113 §6). Meanings are per-frame-type.</summary>
[Flags]
internal enum H2Flags : byte
{
    None = 0,
    Ack = 0x1,          // SETTINGS, PING
    EndStream = 0x1,    // DATA, HEADERS
    EndHeaders = 0x4,   // HEADERS, CONTINUATION, PUSH_PROMISE
    Padded = 0x8,       // DATA, HEADERS, PUSH_PROMISE
    Priority = 0x20,    // HEADERS
}

/// <summary>HTTP/2 error codes (RFC 9113 §7).</summary>
internal enum H2ErrorCode : uint
{
    NoError = 0x0,
    ProtocolError = 0x1,
    InternalError = 0x2,
    FlowControlError = 0x3,
    SettingsTimeout = 0x4,
    StreamClosed = 0x5,
    FrameSizeError = 0x6,
    RefusedStream = 0x7,
    Cancel = 0x8,
    CompressionError = 0x9,
    ConnectError = 0xa,
    EnhanceYourCalm = 0xb,
    InadequateSecurity = 0xc,
    Http11Required = 0xd,
}

/// <summary>SETTINGS parameter identifiers (RFC 9113 §6.5.2).</summary>
internal enum H2SettingId : ushort
{
    HeaderTableSize = 0x1,
    EnablePush = 0x2,
    MaxConcurrentStreams = 0x3,
    InitialWindowSize = 0x4,
    MaxFrameSize = 0x5,
    MaxHeaderListSize = 0x6,
}

/// <summary>Wire constants and protocol defaults (RFC 9113).</summary>
internal static class H2Protocol
{
    /// <summary>Client connection preface (RFC 9113 §3.4), 24 octets.</summary>
    public static ReadOnlySpan<byte> ClientPreface =>
        "PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"u8;

    /// <summary>Fixed frame-header length: 24-bit length + type + flags + 31-bit stream id.</summary>
    public const int FrameHeaderLength = 9;

    /// <summary>Default and minimum SETTINGS_MAX_FRAME_SIZE (RFC 9113 §6.5.2), 2^14.</summary>
    public const int DefaultMaxFrameSize = 16_384;

    /// <summary>Largest permitted SETTINGS_MAX_FRAME_SIZE, 2^24 - 1.</summary>
    public const int MaxAllowedFrameSize = 16_777_215;

    /// <summary>Default SETTINGS_INITIAL_WINDOW_SIZE (RFC 9113 §6.9.2), 2^16 - 1.</summary>
    public const int DefaultInitialWindowSize = 65_535;

    /// <summary>Largest legal flow-control window; exceeding it is a FLOW_CONTROL_ERROR.</summary>
    public const int MaxWindowSize = int.MaxValue; // 2^31 - 1

    /// <summary>Default SETTINGS_HEADER_TABLE_SIZE (RFC 7541 §4.2).</summary>
    public const int DefaultHeaderTableSize = 4_096;
}
