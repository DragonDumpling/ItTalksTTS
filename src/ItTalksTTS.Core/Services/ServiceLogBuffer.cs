using System.Collections.ObjectModel;

namespace ItTalksTTS.Core.Services;

public sealed class ServiceLogBuffer
{
    public object SyncRoot { get; } = new();

    private readonly int _max;

    public ObservableCollection<string> Lines { get; } = new();

    public ServiceLogBuffer(int maxLines = 500) => _max = maxLines;

    public string GetAllText()
    {
        lock (SyncRoot)
        {
            return string.Join(Environment.NewLine, Lines);
        }
    }

    public void Clear()
    {
        lock (SyncRoot)
        {
            Lines.Clear();
        }
    }

    public void Append(string line)
    {
        var ts = DateTime.Now.ToString("HH:mm:ss");
        var full = $"[{ts}] {line}";
        lock (SyncRoot)
        {
            Lines.Add(full);
            while (Lines.Count > _max)
                Lines.RemoveAt(0);
        }
    }
}
