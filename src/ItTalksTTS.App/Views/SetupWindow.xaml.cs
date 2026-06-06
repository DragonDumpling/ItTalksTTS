using System.Windows;
using ItTalksTTS.App.Services;
using ItTalksTTS.Tts;

namespace ItTalksTTS.App.Views;

public partial class SetupWindow : Window
{
    private readonly AppServices _svc;
    private bool _isRunning;

    public bool AutoRunOnShown { get; set; }

    public SetupWindow(AppServices svc)
    {
        InitializeComponent();
        _svc = svc;
    }

    protected override async void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        if (!AutoRunOnShown || _isRunning)
            return;
        AutoRunOnShown = false;
        await RunSetupCoreAsync().ConfigureAwait(true);
    }

    private async void Run_Click(object sender, RoutedEventArgs e) =>
        await RunSetupCoreAsync().ConfigureAwait(true);

    private async Task RunSetupCoreAsync()
    {
        if (_isRunning)
            return;
        _isRunning = true;
        RunBtn.IsEnabled = false;
        CancelBtn.Content = "Please wait…";
        CancelBtn.IsEnabled = false;
        Progress.IsIndeterminate = false;
        Progress.Value = 0;
        try
        {
            void UiLog(string s)
            {
                _svc.Log.Append(s);
                Dispatcher.Invoke(() => Log.Text += s + Environment.NewLine);
            }

            var setup = new KokoroSetupService(UiLog);
            var progress = new Progress<(string step, double? fraction)>(p =>
            {
                Status.Text = p.step;
                if (p.fraction is { } f)
                    Progress.Value = f * 100;
            });
            var engine = EngineRegistry.FromKey(_svc.Settings.SelectedModel);
            await setup.InstallEngineAsync(engine, progress, CancellationToken.None).ConfigureAwait(true);
            Status.Text = $"{engine.DisplayName} setup complete. You can close this window.";
            Progress.Value = 100;
            SfxService.PlaySuccess();
        }
        catch (Exception ex)
        {
            Status.Text = "Failed: " + ex.Message;
            SfxService.PlayButton();
        }
        finally
        {
            _isRunning = false;
            RunBtn.IsEnabled = true;
            CancelBtn.Content = "Close";
            CancelBtn.IsEnabled = true;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
