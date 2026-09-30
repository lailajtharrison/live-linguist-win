using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Whisper.net;

namespace LiveLinguistWinUI.Services;

/// Captures SYSTEM PLAYBACK (WASAPI loopback) — i.e. what's coming out of the
/// speakers: a Teams/Zoom partner's voice or a video — and transcribes it to
/// French text with Whisper. This is the exchange-call use case the microphone
/// can't serve: a mic in a call hears the local student, not the remote partner.
///
/// Pipeline: WasapiLoopbackCapture -> downmix to mono -> resample to 16 kHz ->
/// UtteranceChunker (energy VAD, utterances of at most 5 s) -> Whisper (fr) -> Phrase
/// events, with Hypothesis events (a live partial transcript) while an utterance grows.
public sealed class LoopbackTranscriber : ISpeechSource
{
    private const int WhisperRate = UtteranceChunker.SampleRate;   // Whisper wants 16 kHz mono
    private const int FrameSamples = UtteranceChunker.FrameSamples;
    private const int MaxUtteranceMs = 5_000;

    // Live partial transcript of the utterance still being heard: every PartialEveryMs,
    // but only while Whisper is fast enough on this machine that the extra runs do not
    // delay the final transcript (on a slow laptop they are skipped).
    private const int PartialEveryMs = 1_000;
    private const int PartialMaxWhisperMs = 600;

    private readonly string _modelPath;
    private readonly UtteranceChunker _chunker = new() { MaxMs = MaxUtteranceMs };
    private double _lastWhisperMs;

    /// When the most recent utterance ended (was cut for Whisper). Read it when a Phrase
    /// arrives to measure the delay from the end of speech to the caption.
    public DateTime LastUtteranceEndUtc { get; private set; }
    private WasapiLoopbackCapture? _capture;
    // Swapped when the Windows default output changes; the pump reads whichever is current.
    private volatile ISampleProvider? _pipeline;
    private readonly object _captureLock = new();
    private MMDeviceEnumerator? _devices;
    private DefaultOutputWatcher? _watcher;
    private WhisperFactory? _whisperFactory;
    private WhisperProcessor? _whisper;
    private CancellationTokenSource? _cts;
    private Task? _pump;

    public event Action<string>? Hypothesis;
    public event Action<string>? Phrase;

    /// When audio above the speech threshold was last heard. The UI uses it to tell the
    /// student when nothing is reaching the app (e.g. Zoom playing on non-default headphones).
    public DateTime LastSoundUtc { get; private set; } = DateTime.UtcNow;

    public LoopbackTranscriber(string whisperModelPath) => _modelPath = whisperModelPath;

    /// Returns false if the Whisper model or a render device is unavailable.
    public async Task<bool> StartAsync()
    {
        if (!File.Exists(_modelPath)) return false;
        try
        {
            _whisperFactory = WhisperFactory.FromPath(_modelPath);
            _whisper = _whisperFactory.CreateBuilder()
                .WithLanguage("fr")
                // More threads was not faster (i9-14900K: 8 threads 363 ms vs 30 threads
                // 400 ms on a 5 s utterance) and starves the simplifier running alongside.
                .WithThreads(Math.Clamp(Environment.ProcessorCount / 2, 2, 8))
                // Size Whisper's encoder to the longest utterance instead of its default
                // 30 s window: 3.5x faster on 5 s utterances (1,254 -> 363 ms) with the
                // same transcript. 50 encoder frames per second, plus a margin.
                .WithAudioContextSize(MaxUtteranceMs / 20 + 64)
                .Build();

            StartCapture();

            // Follow the Windows default output. Plugging in headphones or joining a call
            // often changes it, and a capture left on the old device hears nothing: the
            // student sees "No sound is reaching the app" while their video plays fine.
            _devices = new MMDeviceEnumerator();
            _watcher = new DefaultOutputWatcher(OnDefaultOutputChanged);
            _devices.RegisterEndpointNotificationCallback(_watcher);

            _cts = new CancellationTokenSource();
            _pump = Task.Run(() => PumpAsync(_cts.Token));
            return true;
        }
        catch
        {
            Cleanup();
            return false;
        }
    }

    // (Re)start loopback capture on the current default output.
    private void StartCapture()
    {
        lock (_captureLock)
        {
            StopCapture();
            var capture = new WasapiLoopbackCapture(); // default render endpoint
            var buffer = new BufferedWaveProvider(capture.WaveFormat)
            {
                BufferDuration = TimeSpan.FromSeconds(30),
                DiscardOnBufferOverflow = true,
                // NAudio defaults ReadFully to true, which makes Read() pad with
                // silence and never return 0. The pump then never waits: it spins at
                // ~43,000 iterations/s (measured) instead of the ~33/s real time
                // supplies, burning a full core and shredding each utterance into
                // fragments padded with synthetic silence — 193 chunks out of a 16 s
                // clip instead of 4, so Whisper only ever sees near-silence and no
                // caption is produced. Returning 0 on an empty buffer is what paces
                // the loop against the audio clock.
                ReadFully = false,
            };
            capture.DataAvailable += (_, e) => buffer.AddSamples(e.Buffer, 0, e.BytesRecorded);

            // capture format (float, N-ch, 44.1/48 kHz) -> mono -> 16 kHz
            ISampleProvider mono = new DownmixToMonoSampleProvider(buffer.ToSampleProvider());
            _pipeline = new WdlResamplingSampleProvider(mono, WhisperRate);

            capture.StartRecording();
            _capture = capture;
        }
    }

    private void StopCapture()
    {
        try { _capture?.StopRecording(); } catch { }
        try { _capture?.Dispose(); } catch { }
        _capture = null;
    }

    // Windows calls this on its own thread and must not be blocked: switch over in the
    // background, after a short pause so the new device is ready.
    private void OnDefaultOutputChanged()
    {
        if (_cts == null || _cts.IsCancellationRequested) return;
        _ = Task.Run(async () =>
        {
            await Task.Delay(300).ConfigureAwait(false);
            if (_cts == null || _cts.IsCancellationRequested) return;
            try { StartCapture(); } catch { /* no output device right now; the next change retries */ }
        });
    }

    // Pull 16 kHz mono in 30 ms frames, chunk it into utterances, and transcribe them.
    private async Task PumpAsync(CancellationToken ct)
    {
        var frame = new float[FrameSamples];
        int filled = 0;
        int sincePartialSamples = 0;

        while (!ct.IsCancellationRequested)
        {
            var pipeline = _pipeline;
            int got = pipeline == null ? 0 : pipeline.Read(frame, filled, FrameSamples - filled);
            if (got == 0) { await Task.Delay(20, ct).ConfigureAwait(false); continue; }
            filled += got;
            if (filled < FrameSamples) continue;
            filled = 0;

            var utterance = _chunker.Push(frame, out bool voiced);
            if (voiced) LastSoundUtc = DateTime.UtcNow;

            if (utterance != null)
            {
                LastUtteranceEndUtc = DateTime.UtcNow;
                sincePartialSamples = 0;
                var phrase = await TranscribeAsync(utterance, ct).ConfigureAwait(false);
                if (phrase != null) Phrase?.Invoke(phrase);
                continue;
            }

            if (!_chunker.HasSpeech) { sincePartialSamples = 0; continue; }
            sincePartialSamples += FrameSamples;
            if (sincePartialSamples * 1000 / WhisperRate >= PartialEveryMs
                && _chunker.LengthMs >= PartialEveryMs
                && _lastWhisperMs <= PartialMaxWhisperMs)
            {
                sincePartialSamples = 0;
                var partial = await TranscribeAsync(_chunker.Snapshot(), ct).ConfigureAwait(false);
                if (partial != null) Hypothesis?.Invoke(partial);
            }
        }
    }

    // Null when Whisper heard nothing usable.
    private async Task<string?> TranscribeAsync(float[] samples, CancellationToken ct)
    {
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var text = new System.Text.StringBuilder();
            await foreach (var seg in _whisper!.ProcessAsync(samples, ct).ConfigureAwait(false))
                text.Append(seg.Text);
            _lastWhisperMs = sw.Elapsed.TotalMilliseconds;

            var phrase = text.ToString().Trim();
            return string.IsNullOrWhiteSpace(phrase) || IsNoise(phrase) ? null : phrase;
        }
        catch (OperationCanceledException) { return null; }
        catch { return null; /* skip a bad utterance rather than kill the loop */ }
    }

    // Whisper on near-silence sometimes emits stock hallucinations; drop the common ones.
    private static bool IsNoise(string s)
    {
        var t = s.Trim().Trim('.', '!', '?', ' ').ToLowerInvariant();
        return t.Length == 0
            || t is "merci" or "sous-titres réalisés par la communauté d'amara.org"
            || t.Contains("amara.org");
    }

    public async Task StopAsync()
    {
        try
        {
            _cts?.Cancel();
            UnwatchDefaultOutput();
            lock (_captureLock) { try { _capture?.StopRecording(); } catch { } }
            if (_pump != null) await _pump.ConfigureAwait(false);
        }
        catch { /* best effort */ }
    }

    private void UnwatchDefaultOutput()
    {
        try { if (_watcher != null) _devices?.UnregisterEndpointNotificationCallback(_watcher); } catch { }
        try { _devices?.Dispose(); } catch { }
        _watcher = null; _devices = null;
    }

    private void Cleanup()
    {
        UnwatchDefaultOutput();
        lock (_captureLock) StopCapture();
        try { _whisper?.Dispose(); } catch { }
        try { _whisperFactory?.Dispose(); } catch { }
        _whisper = null; _whisperFactory = null;
        _pipeline = null;
    }

    public void Dispose()
    {
        try { _cts?.Cancel(); } catch { }
        Cleanup();
    }
}

/// Tells the transcriber when the Windows default output (what loopback captures) changes.
internal sealed class DefaultOutputWatcher : NAudio.CoreAudioApi.Interfaces.IMMNotificationClient
{
    private readonly Action _changed;
    public DefaultOutputWatcher(Action changed) => _changed = changed;

    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
    {
        // Windows reports each role separately; WasapiLoopbackCapture uses Multimedia.
        if (flow == DataFlow.Render && role == Role.Multimedia) _changed();
    }

    public void OnDeviceStateChanged(string deviceId, DeviceState newState) { }
    public void OnDeviceAdded(string pwstrDeviceId) { }
    public void OnDeviceRemoved(string deviceId) { }
    public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }
}

/// Averages all channels of a source into a single mono channel.
internal sealed class DownmixToMonoSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly int _channels;
    private float[] _scratch = Array.Empty<float>();

    public DownmixToMonoSampleProvider(ISampleProvider source)
    {
        _source = source;
        _channels = source.WaveFormat.Channels;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 1);
    }

    public WaveFormat WaveFormat { get; }

    public int Read(float[] buffer, int offset, int count)
    {
        if (_channels == 1) return _source.Read(buffer, offset, count);

        int need = count * _channels;
        if (_scratch.Length < need) _scratch = new float[need];
        int got = _source.Read(_scratch, 0, need);
        int frames = got / _channels;
        for (int i = 0; i < frames; i++)
        {
            float sum = 0;
            for (int c = 0; c < _channels; c++) sum += _scratch[i * _channels + c];
            buffer[offset + i] = sum / _channels;
        }
        return frames;
    }
}
