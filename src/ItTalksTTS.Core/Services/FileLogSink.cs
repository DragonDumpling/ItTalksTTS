namespace ItTalksTTS.Core.Services;

/// <summary>
/// Appends log lines to a file on disk so logs survive after the app closes.
/// Size-based rotation keeps total footprint bounded (default ~3 MB across the
/// active file plus two backups). All writes are best-effort: logging must never
/// throw into the caller.
/// </summary>
public sealed class FileLogSink
{
    private readonly string _path;
    private readonly long _maxBytes;
    private readonly int _maxBackups;
    private readonly object _lock = new();

    public FileLogSink(string path, long maxBytes = 1_000_000, int maxBackups = 2)
    {
        _path = path;
        _maxBytes = maxBytes;
        _maxBackups = maxBackups;
        try
        {
            var dir = System.IO.Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
        }
        catch
        {
            /* ignore */
        }
    }

    public string Path => _path;

    public void Append(string line)
    {
        var stamped = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {line}{Environment.NewLine}";
        lock (_lock)
        {
            try
            {
                RotateIfNeeded(stamped.Length);
                File.AppendAllText(_path, stamped);
            }
            catch
            {
                /* never let logging crash the app */
            }
        }
    }

    private void RotateIfNeeded(int incomingBytes)
    {
        var info = new FileInfo(_path);
        if (!info.Exists || info.Length + incomingBytes < _maxBytes)
            return;

        // Shift backups upward: app.(n-1).log -> app.n.log, oldest dropped.
        for (var i = _maxBackups; i >= 1; i--)
        {
            var src = i == 1 ? _path : BackupPath(i - 1);
            var dst = BackupPath(i);
            if (!File.Exists(src))
                continue;
            try
            {
                if (File.Exists(dst))
                    File.Delete(dst);
                File.Move(src, dst);
            }
            catch
            {
                /* ignore */
            }
        }
    }

    private string BackupPath(int index)
    {
        var dir = System.IO.Path.GetDirectoryName(_path) ?? "";
        var name = System.IO.Path.GetFileNameWithoutExtension(_path);
        var ext = System.IO.Path.GetExtension(_path);
        return System.IO.Path.Combine(dir, $"{name}.{index}{ext}");
    }
}
