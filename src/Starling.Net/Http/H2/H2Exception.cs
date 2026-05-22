namespace Starling.Net.Http.H2;

// RCS1194: this is a purpose-built internal signal that always carries an
// H2ErrorCode, so the standard parameterless / inner-exception constructors
// don't apply.
#pragma warning disable RCS1194
/// <summary>
/// A connection-level HTTP/2 error (RFC 9113 §5.4.1): fatal to the whole
/// connection. Carries the error code we will report in a GOAWAY frame.
/// </summary>
internal sealed class H2ConnectionException(H2ErrorCode code, string message) : Exception(message)
{
    public H2ErrorCode Code { get; } = code;
}
#pragma warning restore RCS1194
