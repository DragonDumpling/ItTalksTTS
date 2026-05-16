using ItTalksTTS.Core;
using ItTalksTTS.Core.Models;
using ItTalksTTS.Core.Services;
using ItTalksTTS.Tts;
using ItTalksTTS.Tts.Protocol;
using NAudio.Wave;

namespace ItTalksTTS.App.Services;

public sealed class PlaybackService
{
    private readonly AppServices _app;
    private readonly Action<string> _log;
    private CancellationTokenSource? _cts;
    private readonly SemaphoreSlim _playLock = new(1, 1);
    private bool _suppressAutoplay;
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

    private void RaiseTransport() => PlaybackStateChanged?.Invoke();

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
        string? wav = null;
        try
        {
            if (_app.Kokoro.State != KokoroServiceState.Running)
            {
                _app.Queue.SetState(id, QueueItemState.Error, "Kokoro worker not running.");
                return;
            }

            var resp = await _app.Kokoro.SendAsync(
                new WorkerRequest
                {
                    Cmd = "synthesize",
                    Text = item.Text,
                    Voice = _app.Settings.SelectedVoice,
                    Lang = "en-us",
                    Speed = 1.0
                },
                token).ConfigureAwait(false);
            if (resp is not { Ok: true } || string.IsNullOrEmpty(resp.Wav))
            {
                _app.Queue.SetState(id, QueueItemState.Error, resp?.Error ?? "Synthesis failed.");
                return;
            }

            wav = resp.Wav;
            await PlayWavFileAsync(wav, token).ConfigureAwait(false);
            _app.Queue.SetState(id, QueueItemState.Played);
            if (!_suppressAutoplay)
                ClipFinished?.Invoke();
        }
        catch (OperationCanceledException)
        {
            _app.Queue.SetState(id, QueueItemState.Pending);
            throw;
        }
        catch (Exception ex)
        {
            _app.Queue.SetState(id, QueueItemState.Error, ex.Message);
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
        }
    }

    private async Task PlayWavFileAsync(string path, CancellationToken token)
    {
        await Task.Run(
                () =>
                {
                    using var reader = new AudioFileReader(path);
                    using var wo = new WaveOutEvent();
                    lock (_waveLock)
                    {
                        _activeWaveOut = wo;
                        _audioPhase = true;
                        IsPaused = false;
                    }

                    RaiseTransport();
                    try
                    {
                        wo.Init(reader);
                        wo.Play();
                        while (wo.PlaybackState == PlaybackState.Playing || wo.PlaybackState == PlaybackState.Paused)
                        {
                            token.ThrowIfCancellationRequested();
                            Thread.Sleep(40);
                        }
                    }
                    finally
                    {
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
        var next = _app.Queue.FirstPending();
        if (next is null)
            return;
        await PlayFirstPendingAsync().ConfigureAwait(false);
    }
}
