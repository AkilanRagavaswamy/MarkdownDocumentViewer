using System.Text;
using MarkdownViewer.Core.Contracts;

namespace MarkdownViewer.Core.Services;

/// <summary>Reads text files while other processes keep them open, detecting encoding and line endings.</summary>
public sealed class TextFileReader : ITextFileReader
{
    /// <summary>Label for UTF-8 without BOM.</summary>
    public const string Utf8Label = "UTF-8";

    /// <summary>Label for UTF-8 with BOM.</summary>
    public const string Utf8BomLabel = "UTF-8 BOM";

    /// <summary>Label for UTF-16 little endian.</summary>
    public const string Utf16LeLabel = "UTF-16 LE";

    /// <summary>Label for UTF-16 big endian.</summary>
    public const string Utf16BeLabel = "UTF-16 BE";

    /// <summary>Label for the single-byte fallback.</summary>
    public const string Latin1Label = "Latin-1";

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <inheritdoc />
    public async Task<LoadedText> ReadAsync(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 81920,
            useAsync: true);

        using var buffer = stream.CanSeek && stream.Length <= int.MaxValue
            ? new MemoryStream((int)stream.Length)
            : new MemoryStream();
        await stream.CopyToAsync(buffer).ConfigureAwait(false);
        return Decode(buffer.ToArray());
    }

    /// <summary>Decodes raw bytes: BOM detection, then strict UTF-8, then Latin-1.</summary>
    public static LoadedText Decode(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        string text;
        string label;
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
            label = Utf8BomLabel;
        }
        else if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            text = Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            label = Utf16LeLabel;
        }
        else if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            text = Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
            label = Utf16BeLabel;
        }
        else
        {
            try
            {
                text = StrictUtf8.GetString(bytes);
                label = Utf8Label;
            }
            catch (DecoderFallbackException)
            {
                text = Encoding.Latin1.GetString(bytes);
                label = Latin1Label;
            }
        }

        return new LoadedText(text, label, DetectLineEnding(text));
    }

    /// <summary>Returns "CRLF", "LF" or "Mixed" ("LF" when the text has no line breaks).</summary>
    public static string DetectLineEnding(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        int crlf = 0, lf = 0, cr = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n')
                {
                    crlf++;
                    i++;
                }
                else
                {
                    cr++;
                }
            }
            else if (ch == '\n')
            {
                lf++;
            }
        }

        if (cr > 0)
        {
            return "Mixed";
        }

        if (crlf > 0 && lf > 0)
        {
            return "Mixed";
        }

        return crlf > 0 ? "CRLF" : "LF";
    }
}
