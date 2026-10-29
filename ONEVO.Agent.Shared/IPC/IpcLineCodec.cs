namespace ONEVO.Agent.Shared.IPC;

using System.Text;
using System.Text.Json;

/// <summary>
/// Newline-delimited JSON framing shared by Service and Tray.
/// One reader and one writer must be used for the whole lifetime of a pipe connection:
/// a StreamReader buffers ahead, so a second reader on the same stream loses data.
/// </summary>
public static class IpcLineCodec
{
    private const char ByteOrderMark = '﻿';

    /// <summary>UTF-8 without BOM. Encoding.UTF8 writes a BOM per StreamWriter on non-seekable pipes.</summary>
    public static readonly Encoding Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public static StreamReader CreateReader(Stream stream) =>
        new(stream, Encoding, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);

    public static StreamWriter CreateWriter(Stream stream) =>
        new(stream, Encoding, bufferSize: 4096, leaveOpen: true) { AutoFlush = true };

    public static string Serialize(IpcEnvelope envelope) => JsonSerializer.Serialize(envelope);

    /// <summary>
    /// Parses one line. Tolerates a leading BOM (sent by older peers that used Encoding.UTF8),
    /// rejects oversized or malformed input without throwing.
    /// </summary>
    public static bool TryParse(string? line, out IpcEnvelope? envelope)
    {
        envelope = null;
        if (string.IsNullOrEmpty(line) || line.Length > Constants.MaxMessageLengthBytes)
            return false;

        var span = line.AsSpan().TrimStart(ByteOrderMark);
        if (span.IsEmpty)
            return false;

        try
        {
            envelope = JsonSerializer.Deserialize<IpcEnvelope>(span);
        }
        catch (JsonException)
        {
            return false;
        }

        return envelope is not null;
    }
}
