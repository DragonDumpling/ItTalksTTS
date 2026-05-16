using CommunityToolkit.Mvvm.ComponentModel;

namespace ItTalksTTS.Core.Models;

public partial class FilterRuleModel : ObservableObject
{
    [ObservableProperty] private string match = "";

    [ObservableProperty] private string replacement = "";
}
