using CommunityToolkit.Mvvm.ComponentModel;

namespace ItTalksTTS.Core.Models;

public partial class QueueItemModel : ObservableObject
{
    [ObservableProperty] private Guid id;

    [ObservableProperty] private string text = "";

    [ObservableProperty] private string source = "Manual";

    [ObservableProperty] private DateTimeOffset createdAt;

    [ObservableProperty] private QueueItemState state;

    [ObservableProperty] private string? errorMessage;

    public string Preview =>
        Text.Length > 120 ? Text[..117].TrimEnd() + "…" : Text;
}
