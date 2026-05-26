using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using ItTalksTTS.App.Services;
using ItTalksTTS.App.ViewModels;
using ItTalksTTS.Core.Models;

namespace ItTalksTTS.App;

public partial class MainWindow : Window
{
    private bool _tabSoundReady;

    static MainWindow()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            ButtonBase.ClickEvent,
            new RoutedEventHandler(OnRegisteredButtonClick),
            handledEventsToo: true);
    }

    private static void OnRegisteredButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is MainWindow w)
            w.HandleGlobalButtonClick(e);
    }

    public MainWindow()
    {
        InitializeComponent();
        var vm = new MainViewModel(this, App.Services);
        DataContext = vm;
        vm.RequestPasteTab += () => RootTabs.SelectedIndex = 2;
        BindingOperations.EnableCollectionSynchronization(App.Services.Queue.Items, App.Services.Queue.SyncRoot);
        BindingOperations.EnableCollectionSynchronization(App.Services.Log.Lines, App.Services.Log.SyncRoot);
    }

    public MainViewModel Vm => (MainViewModel)DataContext;

    private void HandleGlobalButtonClick(RoutedEventArgs e)
    {
        if (e.OriginalSource is not Button btn)
            return;
        if (btn.Name == "AddToQButton")
            return;
        if (btn.Name == "UpdateAvailableButton")
            return;
        if (btn.Name == "SaveFiltersButton")
            SfxService.PlaySuccess();
        else
            SfxService.PlayButton();
    }

    private async void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        var p = App.Services.Api.Port;
        var tok = App.Services.Settings.ApiToken;
        var preview = tok.Length > 10 ? tok[..10] + "…" : tok;
        Vm.ApiListenInfo = $"POST http://127.0.0.1:{p}/v1/queue  —  Authorization: Bearer {preview}";
        _tabSoundReady = true;
        await Vm.RunFirstLaunchSetupIfNeededAsync().ConfigureAwait(true);
        Vm.EnsureCursorHooksInstalled();
        _ = Vm.CheckForUpdatesAsync();
    }

    private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source is not TabControl)
            return;
        if (!_tabSoundReady)
            return;
        if (e.AddedItems.Count == 0)
            return;
        SfxService.PlayTap();
    }

    private async void PlaySelected_Click(object sender, RoutedEventArgs e)
    {
        var sel = QueueGrid.SelectedItems.Cast<QueueItemModel>().ToList();
        await Vm.PlaySelectedAsync(sel).ConfigureAwait(true);
    }

    private void QueueGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not DataGrid)
            return;
        var sel = QueueGrid.SelectedItems.Cast<QueueItemModel>();
        Vm.UpdateSelectedQueueText(sel);
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is QueueItemModel q)
            Vm.MoveUpCommand.Execute(q);
    }

    private void RemoveFilter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is FilterRuleModel r)
            Vm.RemoveFilterRuleCommand.Execute(r);
    }

    private void Autoplay_Changed(object sender, RoutedEventArgs e)
    {
        SfxService.PlayButton();
        Vm.SaveAutoplay();
    }
}
