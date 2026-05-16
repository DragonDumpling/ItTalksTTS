using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ItTalksTTS.App.Services;
using ItTalksTTS.App.Views;
using ItTalksTTS.Core;
using ItTalksTTS.Core.Models;
using ItTalksTTS.Core.Services;
using ItTalksTTS.Tts;
using ItTalksTTS.Tts.Protocol;

namespace ItTalksTTS.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    public const string TestVoiceMessage =
        "Thank you for using ItTalks TTS! I am so happy that this is working for you.";

    private readonly AppServices _svc;
    private readonly Window _owner;

    public ObservableCollection<string> Voices { get; } = new();

    public string BuildLabel => AppBuildInfo.ShortLabel;

    [ObservableProperty] private string pasteText = "";

    [ObservableProperty] private string apiListenInfo = "";

    [ObservableProperty] private string voiceStatusLabel = "Stopped";

    [ObservableProperty] private Brush voiceStatusBrush = Brushes.Orange;

    [ObservableProperty] private bool setupRequired;

    [ObservableProperty] private bool isKokoroRunning;

    public MainViewModel(Window owner, AppServices svc)
    {
        _owner = owner;
        _svc = svc;
        _svc.Playback.ClipFinished += OnClipFinished;
        _svc.Playback.PlaybackStateChanged += OnPlaybackStateChanged;
        RefreshSetupGate();
        _ = RefreshVoicesInternalAsync();
    }

    public ObservableCollection<QueueItemModel> QueueItems => _svc.Queue.Items;

    public AppSettingsModel Settings => _svc.Settings;

    public ServiceLogBuffer Log => _svc.Log;

    private void OnPlaybackStateChanged() =>
        Application.Current.Dispatcher.Invoke(() =>
        {
            OnPropertyChanged(nameof(IsPlaybackActive));
            OnPropertyChanged(nameof(PlayPauseButtonLabel));
        });

    public bool IsPlaybackActive => _svc.Playback.IsPlaying;

    public string PlayPauseButtonLabel =>
        _svc.Playback.IsPaused
            ? "Play"
            : _svc.Playback.IsAudioOutputting
                ? "Pause"
                : "Play";

    private void OnClipFinished()
    {
        if (!Settings.Autoplay)
            return;
        _ = Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            await Task.Yield();
            await _svc.Playback.TryAutoplayChainAsync().ConfigureAwait(true);
        });
    }

    [RelayCommand]
    private async Task AddPasteToQueue()
    {
        if (string.IsNullOrWhiteSpace(PasteText))
            return;
        var filtered = FilterEngine.Apply(PasteText, Settings.FilterRules);
        if (string.IsNullOrWhiteSpace(filtered))
            return;
        var id = _svc.Queue.Enqueue(filtered.Trim(), "Manual");
        PasteText = "";
        SfxService.PlaySuccess();
        if (Settings.Autoplay && !_svc.Playback.IsPlaying)
            await _svc.Playback.PlayIdsAsync(new[] { id }).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task TestVoice()
    {
        if (_svc.Playback.IsPlaying)
        {
            _svc.Log.Append("Test voice skipped (playback in progress).");
            return;
        }

        if (_svc.Kokoro.State != KokoroServiceState.Running)
        {
            _svc.Log.Append("Test voice: start Kokoro first.");
            return;
        }

        var id = _svc.Queue.Enqueue(TestVoiceMessage, "TestVoice");
        await _svc.Playback.PlayIdsAsync(new[] { id }).ConfigureAwait(true);
    }

    [RelayCommand]
    private void ClearServiceLog() => _svc.Log.Clear();

    [RelayCommand]
    private void CopyServiceLog()
    {
        try
        {
            Clipboard.SetDataObject(_svc.Log.GetAllText());
        }
        catch (Exception ex)
        {
            _svc.Log.Append("Copy log failed: " + ex.Message);
        }
    }

    [RelayCommand]
    private void AddFilterRule() => Settings.FilterRules.Add(new FilterRuleModel());

    [RelayCommand]
    private void RemoveFilterRule(FilterRuleModel? rule)
    {
        if (rule is null)
            return;
        Settings.FilterRules.Remove(rule);
        _svc.PersistSettings();
    }

    [RelayCommand]
    private void AddPresetMarkdown()
    {
        foreach (var c in new[] { "**", "*", "`", "#", ">", "|" })
            Settings.FilterRules.Add(new FilterRuleModel { Match = c, Replacement = "" });
        _svc.PersistSettings();
    }

    [RelayCommand]
    private void SaveFilters() => _svc.PersistSettings();

    [RelayCommand]
    private async Task StartKokoroAsync()
    {
        await _svc.Kokoro.StartAsync().ConfigureAwait(true);
        IsKokoroRunning = _svc.Kokoro.State == KokoroServiceState.Running;
        RefreshVoiceStatus();
        await RefreshVoicesInternalAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task StopKokoroAsync()
    {
        await _svc.Kokoro.StopAsync().ConfigureAwait(true);
        IsKokoroRunning = false;
        RefreshVoiceStatus();
    }

    [RelayCommand]
    private async Task PingWorkerAsync()
    {
        await RefreshVoicesInternalAsync().ConfigureAwait(true);
        RefreshVoiceStatus();
    }

    [RelayCommand]
    private async Task OpenSetupAsync()
    {
        var dlg = new SetupWindow(_svc);
        dlg.Owner = _owner;
        dlg.ShowDialog();
        RefreshSetupGate();
        await RefreshVoicesInternalAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task TogglePlayPauseAsync()
    {
        if (_svc.Playback.IsPaused)
            await _svc.Playback.ResumeAsync().ConfigureAwait(true);
        else if (_svc.Playback.IsAudioOutputting)
            await _svc.Playback.PauseAsync().ConfigureAwait(true);
        else
            await _svc.Playback.PlayFirstPendingAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task StopPlaybackAsync() => await _svc.Playback.StopAsync().ConfigureAwait(true);

    [RelayCommand]
    private async Task ClearQueueAsync()
    {
        await _svc.Playback.StopAsync().ConfigureAwait(true);
        _svc.Queue.Clear();
    }

    [RelayCommand]
    private void MoveUp(QueueItemModel? item)
    {
        if (item is null)
            return;
        _svc.Queue.MoveUp(item.Id);
    }

    public async Task PlaySelectedAsync(IEnumerable<QueueItemModel> selected)
    {
        var ids = selected.Select(i => i.Id).ToList();
        if (ids.Count == 0)
            return;
        await _svc.Playback.PlayIdsAsync(ids, allowReplay: true).ConfigureAwait(true);
    }

    public void SaveAutoplay() => _svc.PersistSettings();

    private void RefreshSetupGate()
    {
        var hasModel = File.Exists(AppPaths.KokoroOnnx) && File.Exists(AppPaths.KokoroVoices);
        var hasWorker = File.Exists(_svc.Kokoro.ResolveWorkerScript());
        SetupRequired = !(hasModel && hasWorker);
    }

    private void RefreshVoiceStatus()
    {
        switch (_svc.Kokoro.State)
        {
            case KokoroServiceState.Running when string.IsNullOrEmpty(_svc.Kokoro.LastError):
                VoiceStatusLabel = "Ready";
                VoiceStatusBrush = Brushes.LimeGreen;
                break;
            case KokoroServiceState.Error:
                VoiceStatusLabel = "Error";
                VoiceStatusBrush = Brushes.IndianRed;
                break;
            default:
                VoiceStatusLabel = "Stopped";
                VoiceStatusBrush = Brushes.Orange;
                break;
        }
    }

    private async Task RefreshVoicesInternalAsync()
    {
        if (_svc.Kokoro.State != KokoroServiceState.Running)
            return;
        var r = await _svc.Kokoro.SendAsync(new WorkerRequest { Cmd = "ping" }).ConfigureAwait(true);
        if (r?.Voices is not { Count: > 0 })
            return;
        Application.Current.Dispatcher.Invoke(() =>
        {
            Voices.Clear();
            foreach (var v in r.Voices!)
                Voices.Add(v);
        });
    }
}
