namespace ONEVO.Agent.Service.IPC;

using System.Text;

/// <summary>
/// Reads UTF-8, newline-terminated IPC lines without ever holding more than
/// <c>maxLineBytes</c> of one line in memory. StreamReader.ReadLineAsync buffers the whole line
/// before any size check can run, so a client sending one huge line with no newline could make
/// the service (LocalSystem) allocate until it ran out of memory. An over-long line is skipped up
/// to its newline and reported through <see cref="OversizedLineSkipped"/>; reading then goes on
/// with the next line.
/// </summary>
public sealed class BoundedLineReader(Stream stream, int maxLineBytes)
{
    private readonly byte[] _buffer = new byte[4096];
    private readonly MemoryStream _line = new();
    private int _pos;
    private int _len;

    /// <summary>Raised with the byte count of each over-long line that was dropped.</summary>
    public event Action<long>? OversizedLineSkipped;

    /// <summary>The next line without its line ending, or null at end of stream.</summary>
    public async Task<string?> ReadLineAsync(CancellationToken ct)
    {
        _line.SetLength(0);
        long skipped = 0;

        while (true)
        {
            if (_pos == _len)
            {
                _len = await stream.ReadAsync(_buffer, ct).ConfigureAwait(false);
                _pos = 0;
                if (_len == 0)
                {
                    if (skipped > 0)
                    {
                        OversizedLineSkipped?.Invoke(skipped);
                        return null;
                    }
                    // Like StreamReader, a final line without a newline is still returned.
                    return _line.Length == 0 ? null : DecodeLine();
                }
            }

            var newline = Array.IndexOf(_buffer, (byte)'\n', _pos, _len - _pos);
            var end = newline < 0 ? _len : newline;
            var count = end - _pos;

            if (skipped > 0 || _line.Length + count > maxLineBytes)
            {
                skipped += _line.Length + count;
                _line.SetLength(0);
            }
            else
            {
                _line.Write(_buffer, _pos, count);
            }

            if (newline < 0)
            {
                _pos = _len;
                continue;
            }

            _pos = newline + 1;
            if (skipped > 0)
            {
                OversizedLineSkipped?.Invoke(skipped);
                skipped = 0;
                continue;
            }
            return DecodeLine();
        }
    }

    private string DecodeLine()
    {
        var length = (int)_line.Length;
        if (length > 0 && _line.GetBuffer()[length - 1] == (byte)'\r')
            length--;
        return Encoding.UTF8.GetString(_line.GetBuffer(), 0, length);
    }
}
