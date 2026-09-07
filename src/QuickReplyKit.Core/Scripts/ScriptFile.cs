using System.IO;
using System.Text;

namespace QuickReplyKit.Core.Scripts;

internal static class ScriptFile
{
    private static readonly UTF8Encoding Utf8 = new(false, true);

    public static string ReadBounded(string path, int maxBytes, int maxChars)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 4096, FileOptions.SequentialScan);
        if (stream.Length > maxBytes) throw new InvalidDataException("file-limit");

        using var reader = new StreamReader(stream, Utf8, detectEncodingFromByteOrderMarks: true);
        var buffer = new char[maxChars + 1];
        int count = 0;
        while (count < buffer.Length)
        {
            int read = reader.Read(buffer, count, buffer.Length - count);
            if (read == 0) break;
            count += read;
        }
        if (count > maxChars || reader.Read() != -1) throw new InvalidDataException("file-limit");
        return new string(buffer, 0, count);
    }

    public static void WriteAtomic(string path, string content)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, content, Utf8);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            try { File.Delete(temp); }
            catch { }
        }
    }
}
