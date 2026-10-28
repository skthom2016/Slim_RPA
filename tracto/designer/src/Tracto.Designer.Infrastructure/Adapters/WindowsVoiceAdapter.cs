namespace Tracto.Designer.Infrastructure.Adapters;

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Speech.Recognition;
using Tracto.Designer.Core.Adapters;

public sealed class WindowsVoiceAdapter : IVoiceTranscriptionAdapter, IDisposable
{
    private SpeechRecognitionEngine? _engine;
    private readonly ConcurrentQueue<(DateTimeOffset, string)> _transcripts = new();
    private bool _available;
    private bool _availableTested;
    private bool _running;

    public bool IsAvailable
    {
        get
        {
            if (_availableTested) return _available;
            _availableTested = true;
            try
            {
                using var test = new SpeechRecognitionEngine();
                _available = true;
            }
            catch
            {
                _available = false;
            }
            return _available;
        }
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (!IsAvailable) return;
        try
        {
            _engine = new SpeechRecognitionEngine();
            _engine.LoadGrammar(new DictationGrammar());
            _engine.SpeechRecognized += (_, e) =>
            {
                if (e.Result?.Text is { Length: > 0 } text)
                    _transcripts.Enqueue((DateTimeOffset.Now, text));
            };
            _engine.SetInputToDefaultAudioDevice();
            _engine.RecognizeAsync(RecognizeMode.Multiple);
            _running = true;
        }
        catch
        {
            // Audio device unavailable after availability check (e.g. mic removed).
            // AC-07: StartAsync must not throw on missing mic — swallow and leave _running false.
            _engine?.Dispose();
            _engine = null;
        }
        await Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        _running = false;
        if (_engine is not null)
        {
            try { _engine.RecognizeAsyncStop(); } catch { }
            _engine.Dispose();
            _engine = null;
        }
        await Task.CompletedTask;
    }

    public async IAsyncEnumerable<(DateTimeOffset Timestamp, string Text)> GetTranscriptionsAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        while (!ct.IsCancellationRequested && _running)
        {
            while (_transcripts.TryDequeue(out var item))
                yield return item;
            await Task.Delay(100, ct).ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        _engine?.Dispose();
        _engine = null;
    }
}
