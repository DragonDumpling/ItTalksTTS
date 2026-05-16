using System.Windows;
using ItTalksTTS.App.Services;
using ItTalksTTS.Core.Services;

namespace ItTalksTTS.App;

public partial class App : Application
{
    public static AppServices Services { get; } = new();

    public App()
    {
        InitializeComponent();
        SessionEnding += OnSessionEnding;
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Any(a => string.Equals(a, "/installCursorHooks", StringComparison.OrdinalIgnoreCase)))
        {
            Services.Initialize();
            var (ok, msg) = CursorHookInstaller.Install();
            Services.Log.Append(msg);
            Shutdown(ok ? 0 : 1);
            return;
        }

        Services.Initialize();
        try
        {
            await Services.StartApiAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Services.Log.Append("API start failed: " + ex.Message);
        }

        var w = new MainWindow();
        MainWindow = w;
        w.Show();
    }

    private void OnSessionEnding(object sender, SessionEndingCancelEventArgs e)
    {
        try
        {
            Services.ShutdownAsync().GetAwaiter().GetResult();
        }
        catch
        {
            /* ignore */
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            Services.ShutdownAsync().GetAwaiter().GetResult();
        }
        catch
        {
            /* ignore */
        }

        base.OnExit(e);
    }
}
