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
                Items.Add(item);
        }

        Changed?.Invoke();
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
}
