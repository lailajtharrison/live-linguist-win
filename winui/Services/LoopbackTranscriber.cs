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
/// energy VAD chunks it into utterances -> Whisper (fr) -> Phrase events.
public sealed class LoopbackTranscriber : ISpeechSource
{
    private const int WhisperRate = 16_000;      // Whisper wants 16 kHz mono
    private const int FrameSamples = 480;        // 30 ms @ 16 kHz
    private const float SpeechRms = 0.006f;      // energy gate (normalized float)
    private const int SilenceFlushMs = 700;      // flush an utterance after this much trailing silence
    private const int MaxUtteranceMs = 12_000;   // …or when it gets this long (bounds latency)
    private const int MinUtteranceMs = 400;      // ignore blips shorter than this

    private readonly string _modelPath;
    private WasapiLoopbackCapture? _capture;
    private BufferedWaveProvider? _buffer;
    private ISampleProvider? _pipeline;
    private WhisperFactory? _whisperFactory;
    private WhisperProcessor? _whisper;
    private CancellationTokenSource? _cts;
    private Task? _pump;

    public event Action<string>? Hypothesis;
    public event Action<string>? Phrase;

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
                .WithThreads(Math.Max(1, Environment.ProcessorCount - 2))
                .Build();

            _capture = new WasapiLoopbackCapture(); // default render endpoint
            _buffer = new BufferedWaveProvider(_capture.WaveFormat)
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
            _capture.DataAvailable += (_, e) => _buffer!.AddSamples(e.Buffer, 0, e.BytesRecorded);

            // capture format (float, N-ch, 44.1/48 kHz) -> mono -> 16 kHz
            ISampleProvider mono = new DownmixToMonoSampleProvider(_buffer.ToSampleProvider());
            _pipeline = new WdlResamplingSampleProvider(mono, WhisperRate);

            _capture.StartRecording();
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

    // Pull 16 kHz mono, gate on energy, and flush utterances to Whisper.
    private async Task PumpAsync(CancellationToken ct)
    {
        var frame = new float[FrameSamples];
        var utterance = new List<float>(WhisperRate * 4);
        int trailingSilenceMs = 0;
        bool hasSpeech = false;

        while (!ct.IsCancellationRequested)
        {
            int got = ReadFull(_pipeline!, frame);
            if (got == 0) { await Task.Delay(20, ct).ConfigureAwait(false); continue; }

            float rms = Rms(frame, got);
            bool voiced = rms >= SpeechRms;
            const int frameMs = FrameSamples * 1000 / WhisperRate; // 30 ms

            if (voiced)
            {
                hasSpeech = true;
                trailingSilenceMs = 0;
                utterance.AddRange(new ArraySegment<float>(frame, 0, got));
            }
            else if (hasSpeech)
            {
                trailingSilenceMs += frameMs;
                utterance.AddRange(new ArraySegment<float>(frame, 0, got)); // keep a little tail
            }

            int utterMs = utterance.Count * 1000 / WhisperRate;
            bool flush = hasSpeech && (trailingSilenceMs >= SilenceFlushMs || utterMs >= MaxUtteranceMs);
            if (flush)
            {
                if (utterMs >= MinUtteranceMs)
                    await TranscribeAsync(utterance.ToArray(), ct).ConfigureAwait(false);
                utterance.Clear();
                hasSpeech = false;
                trailingSilenceMs = 0;
            }
        }
    }

    private async Task TranscribeAsync(float[] samples, CancellationToken ct)
    {
        try
        {
            var text = new System.Text.StringBuilder();
            await foreach (var seg in _whisper!.ProcessAsync(samples, ct).ConfigureAwait(false))
                text.Append(seg.Text);

            var phrase = text.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(phrase) && !IsNoise(phrase))
                Phrase?.Invoke(phrase);
        }
        catch (OperationCanceledException) { }
        catch { /* skip a bad utterance rather than kill the loop */ }
    }

    // Whisper on near-silence sometimes emits stock hallucinations; drop the common ones.
    private static bool IsNoise(string s)
    {
        var t = s.Trim().Trim('.', '!', '?', ' ').ToLowerInvariant();
        return t.Length == 0
            || t is "merci" or "sous-titres réalisés par la communauté d'amara.org"
            || t.Contains("amara.org");
    }

    private static int ReadFull(ISampleProvider sp, float[] frame)
    {
        int total = 0;
        while (total < frame.Length)
        {
            int n = sp.Read(frame, total, frame.Length - total);
            if (n == 0) break;
            total += n;
        }
        return total;
    }

    private static float Rms(float[] buf, int count)
    {
        double sum = 0;
        for (int i = 0; i < count; i++) sum += buf[i] * buf[i];
        return count == 0 ? 0f : (float)Math.Sqrt(sum / count);
    }

    public async Task StopAsync()
    {
        try
        {
            _cts?.Cancel();
            _capture?.StopRecording();
            if (_pump != null) await _pump.ConfigureAwait(false);
        }
        catch { /* best effort */ }
    }

    private void Cleanup()
    {
        try { _capture?.Dispose(); } catch { }
        try { _whisper?.Dispose(); } catch { }
        try { _whisperFactory?.Dispose(); } catch { }
        _capture = null; _whisper = null; _whisperFactory = null;
        _buffer = null; _pipeline = null;
    }

    public void Dispose()
    {
        try { _cts?.Cancel(); } catch { }
        Cleanup();
    }
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
