using System.Text;
using MarkdownViewer.Core.Services;

namespace MarkdownViewer.Core.Tests;

public class TextFileReaderTests
{
    [Fact]
    public void Utf8WithoutBom_IsDetected()
    {
        var result = TextFileReader.Decode(Encoding.UTF8.GetBytes("héllo\nworld\n"));

        Assert.Equal("UTF-8", result.EncodingLabel);
        Assert.Equal("héllo\nworld\n", result.Text);
        Assert.Equal("LF", result.LineEndingLabel);
    }

    [Fact]
    public void Utf8Bom_IsDetected_AndStripped()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("# Title\r\n")).ToArray();

        var result = TextFileReader.Decode(bytes);

        Assert.Equal("UTF-8 BOM", result.EncodingLabel);
        Assert.Equal("# Title\r\n", result.Text);
        Assert.Equal("CRLF", result.LineEndingLabel);
    }

    [Fact]
    public void Utf16Le_IsDetected()
    {
        var bytes = new byte[] { 0xFF, 0xFE }.Concat(Encoding.Unicode.GetBytes("Grüße")).ToArray();

        var result = TextFileReader.Decode(bytes);

        Assert.Equal("UTF-16 LE", result.EncodingLabel);
        Assert.Equal("Grüße", result.Text);
    }

    [Fact]
    public void Utf16Be_IsDetected()
    {
        var bytes = new byte[] { 0xFE, 0xFF }.Concat(Encoding.BigEndianUnicode.GetBytes("Grüße")).ToArray();

        var result = TextFileReader.Decode(bytes);

        Assert.Equal("UTF-16 BE", result.EncodingLabel);
        Assert.Equal("Grüße", result.Text);
    }

    [Fact]
    public void InvalidUtf8_FallsBackToLatin1()
    {
        var bytes = new byte[] { (byte)'c', (byte)'a', (byte)'f', 0xE9 }; // "café" in Latin-1

        var result = TextFileReader.Decode(bytes);

        Assert.Equal("Latin-1", result.EncodingLabel);
        Assert.Equal("café", result.Text);
    }

    [Theory]
    [InlineData("a\r\nb\r\n", "CRLF")]
    [InlineData("a\nb\n", "LF")]
    [InlineData("a\r\nb\n", "Mixed")]
    [InlineData("a\rb", "Mixed")]
    [InlineData("single line", "LF")]
    public void LineEndings_AreDetected(string text, string expected)
    {
        Assert.Equal(expected, TextFileReader.DetectLineEnding(text));
    }

    [Fact]
    public async Task ReadAsync_ReadsFileOpenForWritingByAnotherHandle()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mdv-{Guid.NewGuid():N}.md");
        try
        {
            await File.WriteAllTextAsync(path, "# Shared\n", new UTF8Encoding(false));
            await using var writer = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);

            var result = await new TextFileReader().ReadAsync(path);

            Assert.Equal("# Shared\n", result.Text);
            Assert.Equal("UTF-8", result.EncodingLabel);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
