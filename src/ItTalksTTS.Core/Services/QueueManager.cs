using System.Collections.ObjectModel;
using ItTalksTTS.Core.Models;

namespace ItTalksTTS.Core.Services;

public sealed class QueueManager
{
    public object SyncRoot { get; } = new();

    public ObservableCollection<QueueItemModel> Items { get; } = new();

    public event Action? Changed;

    public void LoadFromDisk()
    {
        lock (SyncRoot)
        {
            Items.Clear();
            foreach (var item in QueuePersistence.Load())
            {
                TryRepairKokoroNotRunningError(item);
                Items.Add(item);
            }
        }

        Changed?.Invoke();
        SaveToDisk();
    }

    public void SaveToDisk()
    {
        lock (SyncRoot)
        {
            QueuePersistence.Save(Items);
        }
    }

    public Guid Enqueue(string text, string source)
    {
        var item = new QueueItemModel
        {
            Id = Guid.NewGuid(),
            Text = text,
            Source = source,
            CreatedAt = DateTimeOffset.Now,
            State = QueueItemState.Pending
        };
        lock (SyncRoot)
        {
            Items.Add(item);
        }

        Changed?.Invoke();
        SaveToDisk();
        return item.Id;
    }

    public bool HasRecentDuplicate(string text, string source, TimeSpan window)
    {
        if (window <= TimeSpan.Zero)
            return false;

        lock (SyncRoot)
        {
            var cutoff = DateTimeOffset.Now - window;
            return Items.Any(i =>
                string.Equals(i.Text, text, StringComparison.Ordinal)
                && string.Equals(i.Source, source, StringComparison.Ordinal)
                && i.CreatedAt >= cutoff);
        }
    }

    public void Clear()
    {
        lock (SyncRoot)
        {
            Items.Clear();
        }

        Changed?.Invoke();
        SaveToDisk();
    }

    public QueueItemModel? GetById(Guid id)
    {
        lock (SyncRoot)
        {
            return Items.FirstOrDefault(i => i.Id == id);
        }
    }

    public void MoveUp(Guid id)
    {
        lock (SyncRoot)
        {
            var idx = -1;
            for (var i = 0; i < Items.Count; i++)
            {
                if (Items[i].Id != id)
                    continue;
                idx = i;
                break;
            }

            if (idx <= 0)
                return;
            if (Items[idx].State != QueueItemState.Pending)
                return;
            var prev = idx - 1;
            while (prev >= 0 && Items[prev].State != QueueItemState.Pending)
                prev--;
            if (prev < 0)
                return;
            (Items[prev], Items[idx]) = (Items[idx], Items[prev]);
        }

        Changed?.Invoke();
        SaveToDisk();
    }

    public IReadOnlyList<QueueItemModel> GetSelectedPending(IEnumerable<Guid> ids)
    {
        lock (SyncRoot)
        {
            var set = ids.ToHashSet();
            return Items.Where(i => set.Contains(i.Id) && i.State == QueueItemState.Pending).ToList();
        }
    }

    public QueueItemModel? FirstPending()
    {
        lock (SyncRoot)
        {
            return Items.FirstOrDefault(i => i.State == QueueItemState.Pending);
        }
    }

    public QueueItemModel? NextPendingAfter(Guid afterId)
    {
        lock (SyncRoot)
        {
            var idx = -1;
            for (var i = 0; i < Items.Count; i++)
            {
                if (Items[i].Id != afterId)
                    continue;
                idx = i;
                break;
            }

            if (idx < 0)
                return null;

            for (var i = idx + 1; i < Items.Count; i++)
            {
                if (Items[i].State == QueueItemState.Pending)
                    return Items[i];
            }

            return null;
        }
    }

    public QueueItemModel? FirstError()
    {
        lock (SyncRoot)
        {
            return Items.FirstOrDefault(i => i.State == QueueItemState.Error);
        }
    }

    public void SetState(Guid id, QueueItemState state, string? error = null)
    {
        lock (SyncRoot)
        {
            var item = Items.FirstOrDefault(i => i.Id == id);
            if (item is null)
                return;
            item.State = state;
            item.ErrorMessage = error;
        }

        Changed?.Invoke();
        SaveToDisk();
    }

    public void ResetPlayingToPending()
    {
        lock (SyncRoot)
        {
            foreach (var item in Items.Where(i => i.State == QueueItemState.Playing))
            {
                item.State = QueueItemState.Pending;
                item.ErrorMessage = null;
            }
        }

        Changed?.Invoke();
        SaveToDisk();
    }

    /// <summary>Resets items that failed only because Kokoro was stopped (not a synthesis failure).</summary>
    public static bool TryRepairKokoroNotRunningError(QueueItemModel item)
    {
        if (item.State != QueueItemState.Error
            || !string.Equals(item.ErrorMessage, "Kokoro worker not running.", StringComparison.Ordinal))
            return false;
        item.State = QueueItemState.Pending;
        item.ErrorMessage = null;
        return true;
    }
}
