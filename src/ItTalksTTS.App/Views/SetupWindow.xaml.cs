using System.Windows;
using ItTalksTTS.App.Services;
using ItTalksTTS.Tts;

namespace ItTalksTTS.App.Views;

public partial class SetupWindow : Window
{
    private readonly AppServices _svc;

    public SetupWindow(AppServices svc)
    {
        InitializeComponent();
        _svc = svc;
    }

    private async void Run_Click(object sender, RoutedEventArgs e)
    {
        RunBtn.IsEnabled = false;
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
            await setup.RunSetupAsync(progress, CancellationToken.None).ConfigureAwait(true);
            Status.Text = "Setup complete.";
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
            RunBtn.IsEnabled = true;
            CancelBtn.IsEnabled = true;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
