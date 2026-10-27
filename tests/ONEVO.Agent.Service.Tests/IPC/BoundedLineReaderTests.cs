using System.Text;
using ONEVO.Agent.Service.IPC;
using Xunit;

namespace ONEVO.Agent.Service.Tests.IPC;

public class BoundedLineReaderTests
{
    private static BoundedLineReader ReaderOver(string text, int maxLineBytes = 16) =>
        new(new MemoryStream(Encoding.UTF8.GetBytes(text)), maxLineBytes);

    [Fact]
    public async Task ReadsLines_StrippingLfAndCrLf()
    {
        var reader = ReaderOver("one\ntwo\r\nthree");

        Assert.Equal("one", await reader.ReadLineAsync(default));
        Assert.Equal("two", await reader.ReadLineAsync(default));
        Assert.Equal("three", await reader.ReadLineAsync(default)); // final line without newline
        Assert.Null(await reader.ReadLineAsync(default));
    }

    [Fact]
    public async Task OversizedLine_IsSkippedAndReported_ThenReadingContinues()
    {
        var reader = ReaderOver(new string('x', 40) + "\nnext\n");
        var skipped = new List<long>();
        reader.OversizedLineSkipped += skipped.Add;

        Assert.Equal("next", await reader.ReadLineAsync(default));
        Assert.Equal([40L], skipped);
    }

    [Fact]
    public async Task LineAtExactlyTheLimit_IsReturned()
    {
        var reader = ReaderOver(new string('y', 16) + "\n");

        Assert.Equal(new string('y', 16), await reader.ReadLineAsync(default));
    }

    [Fact]
    public async Task HugeLineWithoutNewline_NeverBuffersPastTheLimit_AndEndsAsEndOfStream()
    {
        // 8 MB, no newline: StreamReader would hold all of it before any size check.
        var stream = new MemoryStream(new byte[8 * 1024 * 1024]);
        var reader = new BoundedLineReader(stream, 65_536);
        long skipped = 0;
        reader.OversizedLineSkipped += bytes => skipped = bytes;

        Assert.Null(await reader.ReadLineAsync(default));
        Assert.Equal(8 * 1024 * 1024, skipped);
    }

    [Fact]
    public async Task MultiByteUtf8_SplitAcrossReads_IsDecoded()
    {
        var text = new string('a', 4095) + "é\n"; // 'é' straddles the 4096-byte read buffer
        var reader = new BoundedLineReader(new MemoryStream(Encoding.UTF8.GetBytes(text)), 65_536);

        Assert.Equal(text[..^1], await reader.ReadLineAsync(default));
    }
}
