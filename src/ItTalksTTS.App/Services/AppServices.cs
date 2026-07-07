using System.IO;
using System.Threading;
using System.Windows;
using ItTalksTTS.Api;
using ItTalksTTS.Core;
using ItTalksTTS.Core.Models;
using ItTalksTTS.Core.Services;
using ItTalksTTS.Tts;
using ItTalksTTS.Tts.Preprocess;

namespace ItTalksTTS.App.Services;

public sealed class AppServices
{
    private int _shutdownGate;

    public SettingsStore SettingsStore { get; } = new();

    public AppSettingsModel Settings { get; private set; } = null!;

    public QueueManager Queue { get; } = new();

    public ServiceLogBuffer Log { get; } = new();

    public FileLogSink LogFile { get; } = new(AppPaths.LogFilePath);

    public WorkerSupervisor Worker { get; }

    public KokoroSetupService Setup { get; }

    /// <summary>Optional local-LLM speech-friendly text preprocessor (independent of TTS engine).</summary>
    public PreprocessSetupService PreprocessSetup { get; }

    public PreprocessSupervisor Preprocess { get; }

    public PlaybackService Playback { get; }

    public LocalApiServer Api { get; }

    public AppServices()
    {
        // Mirror every in-memory log line to disk so logs survive after the app closes.
        Log.OnAppend = LogFile.Append;
        void LogLine(string s) => Log.Append(s);
        Worker = new WorkerSupervisor(LogLine, () => EngineRegistry.FromKey(Settings?.SelectedModel));
        Setup = new KokoroSetupService(LogLine);
        PreprocessSetup = new PreprocessSetupService(LogLine);
        Preprocess = new PreprocessSupervisor(
            LogLine,
            () => Settings is null ? null : PreprocessModelRegistry.FromId(Settings.PreprocessModelId),
            () => PreprocessSetup.ResolvePythonExe(),
            () => PreprocessSetupService.ResolveWorkerScript());
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
        // If preprocessing was on last session and the model is still installed, warm it up.
        _ = StartPreprocessIfEnabledAsync();
    }

    /// <summary>Starts the preprocessing worker when enabled + installed; no-op otherwise.</summary>
    public async Task StartPreprocessIfEnabledAsync(CancellationToken cancellationToken = default)
    {
        if (!Settings.PreprocessEnabled)
            return;
        if (Preprocess.State == PreprocessState.Running || Preprocess.State == PreprocessState.Starting)
            return;
        var model = PreprocessModelRegistry.FromId(Settings.PreprocessModelId);
        if (!PreprocessSetup.IsInstalled(model))
        {
            Log.Append("Preprocess: enabled but not installed — use Install on the Voice tab.");
            return;
        }
        await Preprocess.StartAsync(cancellationToken).ConfigureAwait(false);
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
            await Worker.StopAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            try
            {
                Log.Append("Shutdown: worker stop failed: " + ex.Message);
            }
            catch
            {
                /* ignore */
            }
        }

        try
        {
            await Preprocess.StopAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            try
            {
                Log.Append("Shutdown: preprocess stop failed: " + ex.Message);
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
