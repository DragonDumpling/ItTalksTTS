using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ItTalksTTS.App.Services;
using ItTalksTTS.App.Views;
using ItTalksTTS.Core;
using ItTalksTTS.Core.Models;
using ItTalksTTS.Core.Services;
using ItTalksTTS.Tts;
using ItTalksTTS.Tts.Preprocess;
using ItTalksTTS.Tts.Protocol;

namespace ItTalksTTS.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    public const string TestVoiceMessage =
        "Thank you for using ItTalks TTS! I am so happy that this is working for you.";

    private readonly AppServices _svc;
    private readonly Window _owner;
    private readonly UpdateCheckService _updateCheck = new();
    private readonly AppUpdateService _appUpdate = new();

    public ObservableCollection<string> Voices { get; } = new();

    /// <summary>Local models offered for speech-friendly preprocessing (bound to the Voice-tab dropdown).</summary>
    public IReadOnlyList<PreprocessModelOption> PreprocessModelOptions { get; } =
        PreprocessModelRegistry.All.Select(PreprocessModelOption.From).ToList();

    [ObservableProperty] private string preprocessStatusText = "Not installed";
    [ObservableProperty] private bool preprocessIsInstalled;
    [ObservableProperty] private bool preprocessIsBusy;
    [ObservableProperty] private string preprocessInstallLabel = "Install";
    [ObservableProperty] private Brush preprocessStatusBrush = Brushes.IndianRed;

    // --- Playback loading/playing phase (drives the inline State-column progress + Now speaking panel) ---
    [ObservableProperty] private bool isPreprocessing;
    [ObservableProperty] private bool isSynthesizing;
    [ObservableProperty] private string activePhaseShort = "";
    [ObservableProperty] private double activePlaybackFraction;
    [ObservableProperty] private bool isSpeaking;

    /// <summary>Document shown in the bottom selected-text area: the spoken text with
    /// the current word highlighted while playing, otherwise the selected queue text.</summary>
    [ObservableProperty] private FlowDocument displayDocument = new();

    /// <summary>True while the active clip is loading (preprocessing or synthesizing).</summary>
    public bool ActiveItemIsLoading => IsPreprocessing || IsSynthesizing;

    public string BuildLabel => AppBuildInfo.ShortLabel;

    [ObservableProperty] private string pasteText = "";

    [ObservableProperty] private string apiListenInfo = "";

    [ObservableProperty] private string voiceStatusLabel = "Stopped";

    [ObservableProperty] private Brush voiceStatusBrush = Brushes.Orange;

    [ObservableProperty] private bool setupRequired;

    [ObservableProperty] private bool isKokoroRunning;

    [ObservableProperty] private string cursorHooksStatus = "";

    [ObservableProperty] private string selectedQueueText = "";

    [ObservableProperty] private bool updateAvailable;

    [ObservableProperty] private string updateAvailableLabel = "Update available";

    [ObservableProperty] private string? updateReleaseUrl;

    [ObservableProperty] private string? updateSetupDownloadUrl;

    [ObservableProperty] private Version? updateLatestVersion;

    [ObservableProperty] private bool updateInProgress;

    public MainViewModel(Window owner, AppServices svc)
    {
        _owner = owner;
        _svc = svc;
        _svc.Playback.ClipFinished += OnClipFinished;
        _svc.Playback.PlaybackStateChanged += OnPlaybackStateChanged;
        _svc.Playback.PhaseChanged += OnPhaseChanged;
        _svc.Playback.ActiveSpokenTextChanged += OnActiveSpokenTextChanged;
        _svc.Playback.SpeakProgress += OnSpeakProgress;
        RefreshSetupGate();
        RefreshCursorHooksStatus();
        RefreshPreprocessStatus();
        RebuildDisplay();
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

    private void OnPhaseChanged() =>
        Application.Current.Dispatcher.Invoke(() =>
        {
            IsPreprocessing = _svc.Playback.Phase == PlaybackPhase.Preprocessing;
            IsSynthesizing = _svc.Playback.Phase == PlaybackPhase.Synthesizing;
            ActivePhaseShort = _svc.Playback.Phase switch
            {
                PlaybackPhase.Preprocessing => "Pre",
                PlaybackPhase.Synthesizing => "TTS",
                PlaybackPhase.Playing => "Playing",
                _ => ""
            };
            if (_svc.Playback.Phase != PlaybackPhase.Playing)
                ActivePlaybackFraction = 0;
            IsSpeaking = _svc.Playback.Phase == PlaybackPhase.Playing;
            OnPropertyChanged(nameof(ActiveItemIsLoading));
        });

    private void OnActiveSpokenTextChanged() =>
        Application.Current.Dispatcher.Invoke(() => RebuildDisplay());

    private readonly List<Run> _wordRuns = new();
    private int _currentWordIndex = -1;

    private static readonly Brush HighlightBrush = new SolidColorBrush(Color.FromRgb(0x4A, 0x9A, 0x4A));
    private static readonly Brush HighlightForeground = new SolidColorBrush(Color.FromRgb(0x0F, 0x1A, 0x0F));

    /// <summary>Rebuild the bottom document: spoken text with per-word Runs while playing
    /// (so we can highlight the current word), otherwise the selected queue text. Newlines
    /// are preserved as LineBreaks so the original layout (paragraphs, separators) is kept.</summary>
    private void RebuildDisplay()
    {
        _wordRuns.Clear();
        _wordWeights = null;
        _currentWordIndex = -1;

        var doc = new FlowDocument
        {
            PagePadding = new Thickness(0),
            Foreground = docForeground
        };
        var para = new Paragraph { Margin = new Thickness(0) };

        if (IsSpeaking && !string.IsNullOrWhiteSpace(_svc.Playback.ActiveSpokenText))
        {
            // Per-word Runs (across lines) so we can highlight the current word, with
            // LineBreaks preserving the spoken text's paragraph structure.
            _wordWeights = _svc.Playback.ActiveSpokenWeights;
            var text = _svc.Playback.ActiveSpokenText!;
            foreach (var line in text.Split('\n'))
            {
                foreach (var w in line.Split(new[] { ' ', '\t', '\r' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var run = new Run(w + " ") { FontSize = 13 };
                    _wordRuns.Add(run);
                    para.Inlines.Add(run);
                }
                para.Inlines.Add(new LineBreak());
            }
        }
        else
        {
            foreach (var line in SelectedQueueText.Split('\n'))
            {
                para.Inlines.Add(new Run(line) { FontSize = 13 });
                para.Inlines.Add(new LineBreak());
            }
        }

        doc.Blocks.Add(para);
        DisplayDocument = doc;
    }

    private List<int>? _wordWeights;

    partial void OnSelectedQueueTextChanged(string value)
    {
        if (!IsSpeaking)
            Application.Current.Dispatcher.Invoke(() => RebuildDisplay());
    }

    partial void OnIsSpeakingChanged(bool value) =>
        Application.Current.Dispatcher.Invoke(() => RebuildDisplay());

    private void OnSpeakProgress(double fraction)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            ActivePlaybackFraction = fraction;
            if (_wordRuns.Count == 0)
                return;
            // Per-word weights: prefer syllable counts baked by the preprocessing pass
            // (they track spoken duration much better than character count); fall back to
            // character length when preprocessing is off and no weights were provided.
            var weights = _wordWeights;
            var totalLen = 0;
            if (weights is { Count: > 0 } && weights.Count == _wordRuns.Count)
            {
                foreach (var wt in weights) totalLen += wt > 0 ? wt : 1;
            }
            else
            {
                weights = null;
                foreach (var r in _wordRuns) totalLen += r.Text.Length;
            }
            if (totalLen == 0)
                return;

            var target = fraction * totalLen;
            var acc = 0;
            var idx = _wordRuns.Count - 1;
            for (var i = 0; i < _wordRuns.Count; i++)
            {
                var w = weights is null ? _wordRuns[i].Text.Length : (weights[i] > 0 ? weights[i] : 1);
                acc += w;
                if (target <= acc) { idx = i; break; }
            }

            if (idx == _currentWordIndex)
                return;
            if (_currentWordIndex >= 0 && _currentWordIndex < _wordRuns.Count)
            {
                _wordRuns[_currentWordIndex].Background = Brushes.Transparent;
                _wordRuns[_currentWordIndex].Foreground = docForeground;
                _wordRuns[_currentWordIndex].FontWeight = FontWeights.Normal;
            }
            _currentWordIndex = idx;
            if (idx >= 0 && idx < _wordRuns.Count)
            {
                _wordRuns[idx].Background = HighlightBrush;
                _wordRuns[idx].Foreground = HighlightForeground;
                _wordRuns[idx].FontWeight = FontWeights.SemiBold;
            }
        });
    }

    private static readonly Brush docForeground = new SolidColorBrush(Color.FromRgb(0xE4, 0xE4, 0xE4));

    /// <summary>True while a clip is loading (preprocessing or synthesizing) but not yet playing.</summary>
    public bool IsProcessingPhase => IsPreprocessing || IsSynthesizing;

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

        if (_svc.Worker.State != TtsServiceState.Running)
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

    // --- Optional speech-friendly preprocessing (local ~3B model) ---

    public void RefreshPreprocessStatus()
    {
        var model = PreprocessModelRegistry.FromId(Settings.PreprocessModelId);
        PreprocessIsInstalled = _svc.PreprocessSetup.IsInstalled(model);
        PreprocessInstallLabel = PreprocessIsBusy
            ? "Installing…"
            : PreprocessIsInstalled ? "Reinstall" : "Install";
        PreprocessStatusText = PreprocessIsBusy
            ? "Installing…"
            : PreprocessIsInstalled
                ? (_svc.Preprocess.State == PreprocessState.Running ? "Running" : "Installed")
                : "Not installed";
        PreprocessStatusBrush = PreprocessIsBusy
            ? Brushes.Gold
            : _svc.Preprocess.State == PreprocessState.Running
                ? Brushes.LimeGreen
                : PreprocessIsInstalled ? Brushes.SteelBlue : Brushes.IndianRed;
        OnPropertyChanged(nameof(CanTogglePreprocess));
    }

    public bool CanTogglePreprocess => PreprocessIsInstalled && !PreprocessIsBusy;

    partial void OnPreprocessIsInstalledChanged(bool value)
    {
        OnPropertyChanged(nameof(CanTogglePreprocess));
    }

    partial void OnPreprocessIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanTogglePreprocess));
        RefreshPreprocessStatus();
    }

    /// <summary>Called from the checkbox toggle: persist and warm/stop the worker.</summary>
    public async Task OnPreprocessEnabledToggledAsync()
    {
        _svc.PersistSettings();
        if (Settings.PreprocessEnabled)
        {
            await _svc.StartPreprocessIfEnabledAsync().ConfigureAwait(true);
        }
        else
        {
            await _svc.Preprocess.StopAsync().ConfigureAwait(true);
        }
        RefreshPreprocessStatus();
    }

    /// <summary>Called when the preprocess model dropdown changes: persist and restart the worker if running.</summary>
    public async Task OnPreprocessModelChangedAsync()
    {
        _svc.PersistSettings();
        if (_svc.Preprocess.State == PreprocessState.Running)
        {
            await _svc.Preprocess.StopAsync().ConfigureAwait(true);
            await _svc.StartPreprocessIfEnabledAsync().ConfigureAwait(true);
        }
        RefreshPreprocessStatus();
    }

    [RelayCommand]
    private async Task InstallPreprocessAsync()
    {
        if (PreprocessIsBusy)
            return;
        var model = PreprocessModelRegistry.FromId(Settings.PreprocessModelId);
        PreprocessIsBusy = true;
        PreprocessInstallLabel = "Installing…";
        try
        {
            var progress = new Progress<(string step, double? fraction)>(p =>
            {
                _svc.Log.Append($"Preprocess: {p.step}");
                PreprocessStatusText = p.step;
            });
            await _svc.PreprocessSetup.InstallAsync(model, progress, CancellationToken.None).ConfigureAwait(true);
            _svc.Log.Append($"Preprocess: {model.DisplayName} ready.");
            // Auto-start the worker if the toggle is on.
            if (Settings.PreprocessEnabled)
                await _svc.StartPreprocessIfEnabledAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _svc.Log.Append("Preprocess install failed: " + ex.Message);
            System.Windows.MessageBox.Show(
                _owner,
                "Could not install the preprocessing model:\n\n" + ex.Message
                    + "\n\nThis needs a system Python 3.10–3.13 on PATH and an internet connection."
                    + " The llama-cpp-python wheel is pulled from its prebuilt Windows index —"
                    + " if your Python is too new or too old, install Python 3.11 or 3.12 from python.org"
                    + " (tick \"Add python.exe to PATH\") and try again.",
                "Preprocess install",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            PreprocessIsBusy = false;
            RefreshPreprocessStatus();
        }
    }

    [RelayCommand]
    private async Task StartEngineAsync()
    {
        // Persist the active engine + its voice settings so next launch is just "Start engine".
        _svc.PersistSettings();
        await _svc.Worker.StartAsync().ConfigureAwait(true);
        IsKokoroRunning = _svc.Worker.State == TtsServiceState.Running;
        RefreshVoiceStatus();
        await RefreshVoicesInternalAsync().ConfigureAwait(true);
        if (IsKokoroRunning && Settings.Autoplay && !_svc.Playback.IsPlaying)
            await _svc.Playback.TryAutoplayChainAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task StopEngineAsync()
    {
        await _svc.Worker.StopAsync().ConfigureAwait(true);
        IsKokoroRunning = false;
        RefreshVoiceStatus();
    }

    /// <summary>Active engine selected in Settings.SelectedModel.</summary>
    public EngineDescriptor ActiveEngine => EngineRegistry.FromKey(Settings.SelectedModel);

    public bool IsNamedVoiceMode => ActiveEngine.VoiceMode == VoiceInputMode.NamedVoices;

    public bool IsReferenceAudioMode => ActiveEngine.VoiceMode == VoiceInputMode.ReferenceAudio;

    /// <summary>Called when the Model dropdown changes: stop the old engine, swap the voice UI, re-gate setup.</summary>
    public async Task OnEngineChangedAsync()
    {
        _svc.PersistSettings();
        await _svc.Worker.StopAsync().ConfigureAwait(true);
        IsKokoroRunning = false;
        Voices.Clear();
        OnPropertyChanged(nameof(ActiveEngine));
        OnPropertyChanged(nameof(IsNamedVoiceMode));
        OnPropertyChanged(nameof(IsReferenceAudioMode));
        RefreshVoiceStatus();
        RefreshSetupGate();
    }

    [RelayCommand]
    private void BrowseRefAudio()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Pick a reference clip (3–12 seconds, clean speech)",
            Filter = "Audio (*.wav;*.flac;*.ogg)|*.wav;*.flac;*.ogg|All files|*.*"
        };
        if (dlg.ShowDialog() == true)
        {
            Settings.F5RefAudioPath = dlg.FileName;
            _svc.PersistSettings();
        }
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

    public void UpdateSelectedQueueText(IEnumerable<QueueItemModel> selected)
    {
        var items = selected.ToList();
        SelectedQueueText = items.Count switch
        {
            0 => "",
            1 => items[0].Text,
            _ => string.Join("\n\n---\n\n", items.Select(i => i.Text))
        };
    }

    [RelayCommand]
    private void CopySelectedQueueText()
    {
        if (string.IsNullOrEmpty(SelectedQueueText))
            return;
        try
        {
            Clipboard.SetDataObject(SelectedQueueText);
        }
        catch (Exception ex)
        {
            _svc.Log.Append("Copy queue text failed: " + ex.Message);
        }
    }

    [RelayCommand]
    private void SendSelectedToPaste()
    {
        if (string.IsNullOrEmpty(SelectedQueueText))
            return;
        PasteText = SelectedQueueText;
        RequestPasteTab?.Invoke();
    }

    public event Action? RequestPasteTab;

    public void SaveAutoplay() => _svc.PersistSettings();

    /// <summary>Persist current settings (e.g. F5 reference clip + transcript) so they survive a restart.</summary>
    public void SaveSettings() => _svc.PersistSettings();

    private void RefreshSetupGate()
    {
        var engine = ActiveEngine;
        if (engine.Id == TtsEngineId.KokoroOnnx)
        {
            var hasModel = File.Exists(AppPaths.KokoroOnnx) && File.Exists(AppPaths.KokoroVoices);
            var hasWorker = File.Exists(_svc.Worker.ResolveWorkerScript(engine));
            var hasPython = File.Exists(Path.Combine(AppPaths.PythonVenv, "Scripts", "python.exe"))
                || File.Exists(Path.Combine(AppPaths.PythonPackages, ".ittalks-ready"));
            SetupRequired = !(hasModel && hasWorker && hasPython);
        }
        else
        {
            SetupRequired = !EngineRegistry.IsInstalled(engine);
        }
    }

    public async Task RunFirstLaunchSetupIfNeededAsync()
    {
        RefreshSetupGate();
        if (!SetupRequired)
            return;
        var dlg = new SetupWindow(_svc) { Owner = _owner, AutoRunOnShown = true };
        dlg.ShowDialog();
        RefreshSetupGate();
        await RefreshVoicesInternalAsync().ConfigureAwait(true);
    }

    public void EnsureCursorHooksInstalled()
    {
        if (CursorHookInstaller.IsConfigured())
        {
            RefreshCursorHooksStatus();
            return;
        }

        var (ok, msg) = CursorHookInstaller.Install();
        _svc.Log.Append(msg);
        RefreshCursorHooksStatus();
        if (ok)
            SfxService.PlaySuccess();
    }

    [RelayCommand]
    private void InstallCursorHooks() => EnsureCursorHooksInstalled();

    [RelayCommand]
    private void OpenCursorHooksFolder()
    {
        try
        {
            var dir = CursorHookInstaller.CursorDirectory;
            Directory.CreateDirectory(dir);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _svc.Log.Append("Open .cursor folder failed: " + ex.Message);
        }
    }

    private void RefreshCursorHooksStatus()
    {
        CursorHooksStatus = CursorHookInstaller.IsConfigured()
            ? "Cursor hooks: on (all projects — use Agent mode)"
            : "Cursor hooks: click Install to enqueue Agent replies to The Q";
    }

    private void RefreshVoiceStatus()
    {
        switch (_svc.Worker.State)
        {
            case TtsServiceState.Running when string.IsNullOrEmpty(_svc.Worker.LastError):
                VoiceStatusLabel = "Ready";
                VoiceStatusBrush = Brushes.LimeGreen;
                break;
            case TtsServiceState.Error:
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
        if (_svc.Worker.State != TtsServiceState.Running)
            return;
        var r = await _svc.Worker.SendAsync(new WorkerRequest { Cmd = "ping" }).ConfigureAwait(true);
        if (r?.Voices is not { Count: > 0 })
            return;
        Application.Current.Dispatcher.Invoke(() =>
        {
            Voices.Clear();
            foreach (var v in r.Voices!)
                Voices.Add(v);
        });
    }

    public async Task CheckForUpdatesAsync()
    {
        var result = await _updateCheck.CheckAsync(AppBuildInfo.CurrentVersion).ConfigureAwait(true);
        if (result.Error is not null)
        {
            _svc.Log.Append("Update check: " + result.Error);
            return;
        }

        UpdateReleaseUrl = result.ReleaseUrl;
        UpdateSetupDownloadUrl = result.SetupDownloadUrl;
        UpdateLatestVersion = result.LatestVersion;
        UpdateAvailable = result.UpdateAvailable;
        if (result.LatestVersion is { } latest)
            UpdateAvailableLabel = $"Update available ({ReleaseVersion.Format(latest)})";
    }

    public bool CanApplyUpdate => UpdateAvailable && !UpdateInProgress;

    partial void OnUpdateAvailableChanged(bool value)
    {
        OnPropertyChanged(nameof(CanApplyUpdate));
        ApplyUpdateCommand.NotifyCanExecuteChanged();
    }

    partial void OnUpdateInProgressChanged(bool value)
    {
        OnPropertyChanged(nameof(CanApplyUpdate));
        ApplyUpdateCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanApplyUpdate))]
    private async Task ApplyUpdateAsync()
    {
        if (UpdateLatestVersion is null)
        {
            await CheckForUpdatesAsync().ConfigureAwait(true);
            if (UpdateLatestVersion is null)
                return;
        }

        var downloadUrl = UpdateSetupDownloadUrl;
        if (string.IsNullOrWhiteSpace(downloadUrl))
        {
            _svc.Log.Append("Update: installer not found on latest release — opening download page.");
            OpenReleasePage();
            return;
        }

        UpdateInProgress = true;
        UpdateAvailableLabel = "Downloading update...";
        try
        {
            _svc.Log.Append($"Update: fetching {ReleaseVersion.Format(UpdateLatestVersion)}...");
            var setupPath = await _appUpdate
                .DownloadSetupAsync(downloadUrl, UpdateLatestVersion, _svc.Log.Append)
                .ConfigureAwait(true);

            UpdateAvailableLabel = "Installing update...";
            _svc.Log.Append("Update: launching installer (approve UAC if prompted)...");
            _appUpdate.LaunchInstaller(setupPath);
            Application.Current.Shutdown();
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            _svc.Log.Append("Update cancelled (UAC prompt declined).");
        }
        catch (Exception ex)
        {
            _svc.Log.Append("Update failed: " + ex.Message);
            if (UpdateLatestVersion is { } latest)
                UpdateAvailableLabel = $"Update available ({ReleaseVersion.Format(latest)})";
        }
        finally
        {
            UpdateInProgress = false;
        }
    }

    private void OpenReleasePage()
    {
        var url = UpdateReleaseUrl ?? UpdateCheckService.LatestReleasePageUrl;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _svc.Log.Append("Open update page failed: " + ex.Message);
        }
    }
}
