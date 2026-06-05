using System.IO;
using System.Threading;
using System.Windows;
using ItTalksTTS.Api;
using ItTalksTTS.Core;
using ItTalksTTS.Core.Models;
using ItTalksTTS.Core.Services;
using ItTalksTTS.Tts;

namespace ItTalksTTS.App.Services;

public sealed class AppServices
{
    private int _shutdownGate;

    public SettingsStore SettingsStore { get; } = new();

    public AppSettingsModel Settings { get; private set; } = null!;

    public QueueManager Queue { get; } = new();

    public ServiceLogBuffer Log { get; } = new();

    public FileLogSink LogFile { get; } = new(AppPaths.LogFilePath);

    public KokoroWorkerSupervisor Kokoro { get; }

    public KokoroSetupService Setup { get; }

    public PlaybackService Playback { get; }

    public LocalApiServer Api { get; }

    public AppServices()
    {
        // Mirror every in-memory log line to disk so logs survive after the app closes.
        Log.OnAppend = LogFile.Append;
        void LogLine(string s) => Log.Append(s);
        Kokoro = new KokoroWorkerSupervisor(LogLine);
        Setup = new KokoroSetupService(LogLine);
        Playback = new PlaybackService(this, LogLine);
        Api = new LocalApiServer(Queue, () => Settings, SettingsStore, OnItemEnqueuedViaApi);
    }

    private void OnItemEnqueuedViaApi(Guid id)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
            return;
        _ = dispatcher.InvokeAsync(async () =>
        {
            if (!Settings.Autoplay || Playback.IsPlaying)
                return;
            await Playback.TryAutoplayChainAsync().ConfigureAwait(true);
        });
    }

    public void Initialize()
    {
        AppPaths.EnsureRoot();
        Log.Append($"Logs: {LogFile.Path}");
        Settings = SettingsStore.Load();
        Queue.LoadFromDisk();
        // Keep the deployed worker script in sync with this build (e.g. after an update).
        Setup.TryRefreshDeployedWorkerScripts();
    }

    public void PersistSettings() => SettingsStore.Save(Settings);

    public async Task StartApiAsync(CancellationToken ct = default)
    {
        await Api.StartAsync(ct).ConfigureAwait(false);
        Log.Append($"API listening on http://127.0.0.1:{Api.Port}/ (see runtime.json for token).");
    }

    /// <summary>Idempotent: safe to call from both SessionEnding and OnExit.</summary>
    public async Task ShutdownAsync()
    {
        if (Interlocked.Exchange(ref _shutdownGate, 1) != 0)
            return;

        try
        {
            await Playback.StopAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            try
            {
                Log.Append("Shutdown: playback stop failed: " + ex.Message);
            }
            catch
            {
                /* ignore */
            }
        }

        try
        {
            await Kokoro.StopAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            try
            {
                Log.Append("Shutdown: Kokoro stop failed: " + ex.Message);
            }
            catch
            {
                /* ignore */
            }
        }

        try
        {
            await Api.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            try
            {
                Log.Append("Shutdown: API stop failed: " + ex.Message);
            }
            catch
            {
                /* ignore */
            }
        }

        try
        {
            if (File.Exists(AppPaths.RuntimePath))
                File.Delete(AppPaths.RuntimePath);
        }
        catch
        {
            /* ignore */
        }
    }
}
