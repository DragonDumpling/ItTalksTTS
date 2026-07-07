using ItTalksTTS.Core;
using ItTalksTTS.Core.Models;
using ItTalksTTS.Core.Services;
using ItTalksTTS.Tts;
using ItTalksTTS.Tts.Preprocess;
using ItTalksTTS.Tts.Protocol;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace ItTalksTTS.App.Services;

public enum PlaybackPhase
{
    Idle,
    Preprocessing,
    Synthesizing,
    Playing
}

public sealed class PlaybackService
{
    private readonly AppServices _app;
    private readonly Action<string> _log;
    private CancellationTokenSource? _cts;
    private readonly SemaphoreSlim _playLock = new(1, 1);
    private bool _suppressAutoplay;
    private Guid? _lastCompletedId;
    private readonly object _waveLock = new();
    private WaveOutEvent? _activeWaveOut;
    private volatile bool _audioPhase;

    public PlaybackService(AppServices app, Action<string> log)
    {
        _app = app;
        _log = log;
    }

    public bool IsPlaying { get; private set; }

    public bool IsPaused { get; private set; }

    /// <summary>Coarse-grained phase of the current clip — drives the loading/playing status UI.</summary>
    public PlaybackPhase Phase { get; private set; } = PlaybackPhase.Idle;

    /// <summary>Preview text of the item currently being processed (null when idle).</summary>
    public string? ActiveItemPreview { get; private set; }

    /// <summary>The text actually being spoken (preprocessed form when preprocessing is on).
    /// Used for word-by-word highlight during playback.</summary>
    public string? ActiveSpokenText { get; private set; }

    /// <summary>Playback progress of the current clip, 0.0–1.0 (only meaningful during Playing).</summary>
    public double PlaybackFraction { get; private set; }

    public bool IsAudioOutputting
    {
        get
        {
            lock (_waveLock)
                return _audioPhase;
        }
    }

    public event Action? PlaybackStateChanged;

    public event Action? ClipFinished;

    /// <summary>Raised whenever <see cref="Phase"/> or <see cref="ActiveItemPreview"/> changes.</summary>
    public event Action? PhaseChanged;

    /// <summary>Raised when <see cref="ActiveSpokenText"/> changes (a new clip starts loading).</summary>
    public event Action? ActiveSpokenTextChanged;

    /// <summary>Raised repeatedly during audio playback with the current 0.0–1.0 fraction, for word highlighting.</summary>
    public event Action<double>? SpeakProgress;

    private void RaiseTransport() => PlaybackStateChanged?.Invoke();

    private void SetPhase(PlaybackPhase phase, QueueItemModel? item)
    {
        Phase = phase;
        ActiveItemPreview = item?.Preview;
        if (phase == PlaybackPhase.Idle)
        {
            ActiveSpokenText = null;
            PlaybackFraction = 0;
            ActiveSpokenTextChanged?.Invoke();
            SpeakProgress?.Invoke(0.0);
        }
        PhaseChanged?.Invoke();
        RaiseTransport();
    }

    public async Task PauseAsync()
    {
        await Task.Run(() =>
        {
            lock (_waveLock)
            {
                if (_activeWaveOut is null || !_audioPhase)
                    return;
                if (_activeWaveOut.PlaybackState == PlaybackState.Playing)
                    _activeWaveOut.Pause();
                IsPaused = true;
            }
        }).ConfigureAwait(false);
        RaiseTransport();
    }

    public async Task ResumeAsync()
    {
        await Task.Run(() =>
        {
            lock (_waveLock)
            {
                if (_activeWaveOut is null || !_audioPhase)
                    return;
                if (_activeWaveOut.PlaybackState == PlaybackState.Paused)
                    _activeWaveOut.Play();
                IsPaused = false;
            }
        }).ConfigureAwait(false);
        RaiseTransport();
    }

    public async Task StopAsync()
    {
        try
        {
            _cts?.Cancel();
        }
        catch
        {
            /* ignore */
        }

        if (!await _playLock.WaitAsync(TimeSpan.FromSeconds(45)).ConfigureAwait(false))
        {
            _log("Playback: stop timed out waiting for session (lock still held).");
            return;
        }

        try
        {
            lock (_waveLock)
            {
                IsPaused = false;
                try
                {
                    _activeWaveOut?.Stop();
                }
                catch
                {
                    /* ignore */
                }

                _activeWaveOut = null;
                _audioPhase = false;
            }

            _app.Queue.ResetPlayingToPending();
            IsPlaying = false;
            RaiseTransport();
        }
        finally
        {
            _playLock.Release();
        }
    }

    public Task PlayFirstPendingAsync() => PlayInternalAsync(null, false);

    public Task PlayIdsAsync(IReadOnlyList<Guid> ids, bool allowReplay = false) =>
        PlayInternalAsync(ids, allowReplay);

    private async Task PlayInternalAsync(IReadOnlyList<Guid>? idsOrNull, bool allowReplayFromCaller)
    {
        if (!await _playLock.WaitAsync(0).ConfigureAwait(false))
            return;
        _suppressAutoplay = idsOrNull is { Count: > 1 };
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        try
        {
            IsPlaying = true;
            IsPaused = false;
            RaiseTransport();
            if (idsOrNull is { Count: > 0 })
            {
                foreach (var id in idsOrNull)
                {
                    token.ThrowIfCancellationRequested();
                    await PlayOneAsync(id, token, allowReplayFromCaller).ConfigureAwait(false);
                }

                if (idsOrNull.Count > 1)
                    ClipFinished?.Invoke();
            }
            else
            {
                var next = _app.Queue.FirstPending() ?? _app.Queue.FirstError();
                if (next is not null)
                {
                    var allowReplay = allowReplayFromCaller || next.State == QueueItemState.Error;
                    await PlayOneAsync(next.Id, token, allowReplay).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _log("Playback stopped.");
        }
        finally
        {
            _suppressAutoplay = false;
            lock (_waveLock)
            {
                IsPaused = false;
                _activeWaveOut = null;
                _audioPhase = false;
            }

            IsPlaying = false;
            RaiseTransport();
            _playLock.Release();
        }
    }

    private async Task PlayOneAsync(Guid id, CancellationToken token, bool allowReplay)
    {
        var item = _app.Queue.GetById(id);
        if (item is null)
            return;
        if (item.State == QueueItemState.Playing)
            return;
        if (item.State is QueueItemState.Played or QueueItemState.Error)
        {
            if (!allowReplay)
                return;
            _app.Queue.SetState(id, QueueItemState.Pending, null);
        }

        if (item.State != QueueItemState.Pending)
            return;
        _app.Queue.SetState(id, QueueItemState.Playing);
        SetPhase(_app.Settings.PreprocessEnabled ? PlaybackPhase.Preprocessing : PlaybackPhase.Synthesizing, item);
        string? wav = null;
        // Whether to fire ClipFinished after this clip to advance the autoplay chain.
        // Stays false on Stop/cancel and when the engine is down; true on success and on
        // mid-clip errors so a single bad row doesn't stall the rest of the queue.
        var advanceChain = false;
        try
        {
            if (_app.Worker.State != TtsServiceState.Running)
            {
                _app.Queue.SetState(id, QueueItemState.Pending, null);
                _log("Playback skipped: start the TTS engine on the Voice tab to hear speech.");
                return; // engine down — TryAutoplayChainAsync will stop on its own
            }

            var prepText = await PreprocessTextAsync(item.Text, token).ConfigureAwait(false);
            if (token.IsCancellationRequested)
            {
                _app.Queue.SetState(id, QueueItemState.Pending, null);
                return; // Stop — don't chain
            }

            // The text actually being spoken (post-preprocess) — drives word highlighting.
            ActiveSpokenText = prepText;
            ActiveSpokenTextChanged?.Invoke();

            SetPhase(PlaybackPhase.Synthesizing, item);
            var resp = await _app.Worker.SendAsync(
                BuildSynthesizeRequest(prepText),
                token).ConfigureAwait(false);

            if (token.IsCancellationRequested)
            {
                _app.Queue.SetState(id, QueueItemState.Pending, null);
                return; // Stop — don't chain (was cancelled mid-synth)
            }

            if (resp is not { Ok: true } || string.IsNullOrEmpty(resp.Wav))
            {
                var reason = resp?.Error ?? "Synthesis failed.";
                _log($"Synthesis failed (len {item.Text.Length}): {Summarize(reason)}");
                _app.Queue.SetState(id, QueueItemState.Error, reason);
                // Skip past the errored row so the rest of the queue still plays.
                advanceChain = _app.Worker.State == TtsServiceState.Running;
                return;
            }

            wav = resp.Wav;
            SetPhase(PlaybackPhase.Playing, item);
            await PlayWavFileAsync(wav, token).ConfigureAwait(false);
            _app.Queue.SetState(id, QueueItemState.Played);
            _lastCompletedId = id;
            advanceChain = !_suppressAutoplay;
        }
        catch (OperationCanceledException)
        {
            _app.Queue.SetState(id, QueueItemState.Pending);
            throw;
        }
        catch (Exception ex)
        {
            if (token.IsCancellationRequested)
            {
                _app.Queue.SetState(id, QueueItemState.Pending);
                throw new OperationCanceledException(token);
            }
            _log($"Playback error (len {item.Text.Length}): {Summarize(ex.Message)}");
            _app.Queue.SetState(id, QueueItemState.Error, ex.Message);
            advanceChain = _app.Worker.State == TtsServiceState.Running;
        }
        finally
        {
            if (!string.IsNullOrEmpty(wav))
            {
                try
                {
                    File.Delete(wav);
                }
                catch
                {
                    /* ignore */
                }
            }

            SetPhase(PlaybackPhase.Idle, null);
            if (advanceChain)
                ClipFinished?.Invoke();
        }
    }

    private WorkerRequest BuildSynthesizeRequest(string text)
    {
        var engine = _app.Worker.ActiveEngine;
        if (engine.VoiceMode == VoiceInputMode.ReferenceAudio)
        {
            // Reference audio and its transcript MUST stay paired — F5 derives the
            // speaking rate from len(gen_text)/len(ref_text), so a transcript that
            // doesn't match the clip wrecks the timing. With no custom clip we use the
            // bundled clip with ITS transcript; with a custom clip we use the user's
            // transcript, or blank to let the worker auto-transcribe it.
            string refAudio, refText;
            if (string.IsNullOrWhiteSpace(_app.Settings.F5RefAudioPath))
            {
                refAudio = EngineRegistry.F5DefaultRefAudioPath;
                refText = EngineRegistry.F5DefaultRefText;
            }
            else
            {
                refAudio = _app.Settings.F5RefAudioPath;
                refText = _app.Settings.F5RefText ?? "";
            }

            return new WorkerRequest
            {
                Cmd = "synthesize",
                Text = text,
                RefAudio = refAudio,
                RefText = refText,
                Lang = "en-us",
                // F5 clones the reference cadence and tends to run fast; 0.85 reads at a natural pace.
                Speed = 0.85
            };
        }

        return new WorkerRequest
        {
            Cmd = "synthesize",
            Text = text,
            Voice = _app.Settings.SelectedVoice,
            Lang = "en-us",
            Speed = 1.0
        };
    }

    private static string Summarize(string message)
    {
        // Collapse multi-line worker tracebacks to one readable log line.
        var lines = message.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var text = lines.Length == 0 ? message.Trim() : string.Join(" | ", lines[^Math.Min(2, lines.Length)..]);
        return text.Length > 400 ? text[..400] + "…" : text;
    }

    /// <summary>
    /// Rewrite text into a speech-friendly form when preprocessing is enabled. Falls
    /// back to the original text on any failure or timeout so speech is never blocked.
    /// The original queue text is left untouched — only what's spoken changes.
    /// </summary>
    private async Task<string> PreprocessTextAsync(string text, CancellationToken token)
    {
        if (!_app.Settings.PreprocessEnabled)
            return text;
        if (string.IsNullOrWhiteSpace(text))
            return text;
        if (_app.Preprocess.State != PreprocessState.Running)
            return text;

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            cts.CancelAfter(TimeSpan.FromSeconds(20));
            var rewritten = await _app.Preprocess.PreprocessAsync(text, cts.Token).ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(rewritten) ? text : rewritten;
        }
        catch (Exception ex)
        {
            _log($"Preprocess skipped (using original): {Summarize(ex.Message)}");
            return text;
        }
    }

    private async Task PlayWavFileAsync(string path, CancellationToken token)
    {
        await Task.Run(
                () =>
                {
                    using var reader = new AudioFileReader(path);
                    var gain = (float)Math.Clamp(_app.Settings.VoiceVolume, 0.25, 5.0);
                    ISampleProvider output = reader;
                    if (Math.Abs(gain - 1f) > 0.001f)
                        output = new VolumeSampleProvider(reader) { Volume = gain };

                    using var wo = new WaveOutEvent();
                    var totalTime = reader.TotalTime;
                    lock (_waveLock)
                    {
                        _activeWaveOut = wo;
                        _audioPhase = true;
                        IsPaused = false;
                    }

                    RaiseTransport();
                    SpeakProgress?.Invoke(0.0);
                    try
                    {
                        wo.Init(output);
                        wo.Play();
                        while (wo.PlaybackState == PlaybackState.Playing || wo.PlaybackState == PlaybackState.Paused)
                        {
                            token.ThrowIfCancellationRequested();
                            if (wo.PlaybackState == PlaybackState.Playing && totalTime > TimeSpan.Zero)
                            {
                                var ct = reader.CurrentTime;
                                var f = ct / totalTime;
                                if (f < 0) f = 0;
                                if (f > 1) f = 1;
                                PlaybackFraction = f;
                                SpeakProgress?.Invoke(f);
                            }
                            Thread.Sleep(40);
                        }
                    }
                    finally
                    {
                        SpeakProgress?.Invoke(1.0);
                        PlaybackFraction = 0;
                        lock (_waveLock)
                        {
                            if (ReferenceEquals(_activeWaveOut, wo))
                            {
                                _activeWaveOut = null;
                                _audioPhase = false;
                                IsPaused = false;
                            }
                        }

                        RaiseTransport();
                        try
                        {
                            wo.Stop();
                        }
                        catch
                        {
                            /* ignore */
                        }
                    }
                },
                token)
            .ConfigureAwait(false);
    }

    public async Task TryAutoplayChainAsync()
    {
        if (!_app.Settings.Autoplay)
            return;
        if (IsPlaying)
            return;
        if (_app.Worker.State != TtsServiceState.Running)
            return;

        var next = _lastCompletedId is { } last
            ? _app.Queue.NextPendingAfter(last)
            : null;
        next ??= _app.Queue.FirstPending();
        if (next is null)
            return;
        await PlayIdsAsync(new[] { next.Id }).ConfigureAwait(false);
    }
}
