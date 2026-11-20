using System.Text;

namespace ConfigSync;

public static class OriginalFileSaver
{
    public static string Save(ConfigDocument document, string content)
    {
        _ = new ConfigDocument(document.Path, content);
        using var stream = new FileStream(document.Path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
        using var reader = new StreamReader(stream, Encoding.UTF8, true, 4096, leaveOpen: true);
        if (reader.ReadToEnd() != document.Original)
            throw new InvalidOperationException("The original file changed. Reload before saving.");
        string backup = document.Path + "." + DateTime.Now.ToString("yyyyMMdd-HHmmss-fffffff") + ".bak";
        stream.Position = 0;
        using (var backupStream = new FileStream(backup, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            stream.CopyTo(backupStream);
            backupStream.Flush(true);
        }
        byte[] bytes = new UTF8Encoding(false).GetBytes(content);
        stream.Position = 0;
        stream.Write(bytes);
        stream.SetLength(bytes.Length);
        stream.Flush(true);
        stream.Position = 0;
        reader.DiscardBufferedData();
        if (reader.ReadToEnd() != content)
            throw new IOException("Save verification failed. The original backup is at " + backup);
        return backup;
    }
}
