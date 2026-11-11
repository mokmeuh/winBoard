using System.Net.Http;
using WinBoard.Services.Gemini;
using WinBoard.Services.Settings;

namespace WinBoard.App.Services;

public enum DictationState { Idle, Listening, Processing }

/// <summary>
/// Dictée vocale : enregistrement, envoi à l'API publique Gemini avec la clé de l'utilisateur,
/// puis texte renvoyé à l'appelant. À utiliser depuis le fil d'interface.
/// </summary>
public sealed class DictationService : IDisposable
{
    private static readonly TimeSpan MinDuration = TimeSpan.FromMilliseconds(400);

    private readonly SecretStore _secrets;
    private readonly Func<AppSettings> _settings;
    private readonly GeminiOptions _options = new();
    private readonly GeminiClient _client;
    private readonly VoiceRecorder _recorder = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly SynchronizationContext? _ui = SynchronizationContext.Current;

    public DictationService(HttpClient http, SecretStore secrets, Func<AppSettings> settings)
    {
        _secrets = secrets;
        _settings = settings;
        _client = new GeminiClient(http, _options, new FileUsageStore(AppPaths.UsageFile()));
        _recorder.LimitReached += () =>
        {
            // Fin automatique après 60 s : on revient sur le fil d'interface.
            if (_ui != null) _ui.Post(_ => _ = ToggleAsync(), null);
        };
    }

    public DictationState State { get; private set; }

    public event Action<DictationState>? StateChanged;

    /// <summary>Message d'erreur destiné à l'utilisateur.</summary>
    public event Action<string>? ErrorOccurred;

    /// <summary>Texte obtenu, à insérer.</summary>
    public event Action<string>? TextReady;

    /// <summary>Usage restant selon le limiteur local.</summary>
    public UsageSnapshot GetUsage() => _client.GetUsage();

    private void SetState(DictationState s)
    {
        State = s;
        StateChanged?.Invoke(s);
    }

    /// <summary>Premier appel : démarre l'écoute ; second appel : arrête et traite.</summary>
    public async Task ToggleAsync()
    {
        switch (State)
        {
            case DictationState.Idle: StartListening(); break;
            case DictationState.Listening: await StopAndProcessAsync(); break;
        }
    }

    private void StartListening()
    {
        var key = _secrets.GetApiKey();
        if (string.IsNullOrWhiteSpace(key))
        {
            ErrorOccurred?.Invoke("Clé API Gemini absente : ouvrez les Réglages pour la saisir.");
            return;
        }

        var usage = _client.GetUsage();
        if (usage.DayRemaining <= 0)
        {
            ErrorOccurred?.Invoke("Quota quotidien local atteint : réessayez demain ou augmentez la limite.");
            return;
        }
        if (usage.MinuteRemaining <= 0)
        {
            ErrorOccurred?.Invoke($"Quota par minute atteint : réessayez dans {Format(usage.MinuteResetIn)}.");
            return;
        }

        try
        {
            _recorder.Start();
            SetState(DictationState.Listening);
        }
        catch (MicrophoneNotFoundException)
        {
            ErrorOccurred?.Invoke("Micro introuvable : branchez un micro ou vérifiez les réglages audio de Windows.");
        }
        catch (Exception e)
        {
            Log.Error("Démarrage de l'enregistrement.", e);
            ErrorOccurred?.Invoke("Micro inaccessible : vérifiez qu'une autre application ne l'utilise pas.");
        }
    }

    private async Task StopAndProcessAsync()
    {
        short[] samples;
        try { samples = _recorder.Stop(); }
        catch (Exception e)
        {
            Log.Error("Arrêt de l'enregistrement.", e);
            samples = Array.Empty<short>();
        }

        if (samples.Length < MinDuration.TotalSeconds * VoiceRecorder.SampleRate)
        {
            SetState(DictationState.Idle);
            ErrorOccurred?.Invoke("Enregistrement trop court : parlez après avoir touché le micro.");
            return;
        }

        SetState(DictationState.Processing);
        try
        {
            var s = _settings();
            _options.ApiKey = _secrets.GetApiKey() ?? "";
            _options.Model = string.IsNullOrWhiteSpace(s.GeminiModel) ? "gemini-2.5-flash" : s.GeminiModel.Trim();
            var wav = WavEncoder.Encode(samples, VoiceRecorder.SampleRate, 1);
            var text = s.DictationStyle == DictationStyle.Verbatim
                ? await _client.TranscribeAsync(wav, s.DictationLanguage, _cts.Token)
                : await _client.RefineDictationAsync(wav, s.DictationLanguage, s.DictationStyle, _cts.Token);
            text = text?.Trim() ?? "";
            if (text.Length == 0) ErrorOccurred?.Invoke("Aucun texte reconnu dans l'enregistrement.");
            else TextReady?.Invoke(text);
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested) { }
        catch (Exception e)
        {
            ErrorOccurred?.Invoke(Describe(e));
        }
        finally
        {
            SetState(DictationState.Idle);
        }
    }

    private static string Describe(Exception e)
    {
        switch (e)
        {
            case GeminiQuotaException q when q.IsLocal:
                return $"Quota local atteint : réessayez dans {Format(q.RetryAfter)}.";
            case GeminiQuotaException q:
                return $"Quota Gemini dépassé : réessayez dans {Format(q.RetryAfter)}.";
            case GeminiAudioTooLargeException a:
                return a.Message;
            case GeminiApiException api when api.StatusCode is 401 or 403
                || (api.StatusCode == 400 && api.Message.Contains("API key", StringComparison.OrdinalIgnoreCase)):
                return "Clé API refusée par Gemini : vérifiez-la dans les Réglages.";
            case GeminiApiException api:
                Log.Error("Erreur API Gemini.", api);
                return Truncate(api.Message, 140);
            case GeminiException g when g.InnerException is HttpRequestException:
                Log.Error("Erreur réseau Gemini.", g);
                return "Erreur réseau : impossible de joindre Gemini. Vérifiez la connexion.";
            case GeminiException g:
                Log.Error("Erreur Gemini.", g);
                return Truncate(g.Message, 140);
            case HttpRequestException h:
                Log.Error("Erreur réseau.", h);
                return "Erreur réseau : impossible de joindre Gemini. Vérifiez la connexion.";
            case TaskCanceledException:
                return "Délai dépassé : Gemini n'a pas répondu à temps.";
            default:
                Log.Error("Dictée.", e);
                return "Erreur inattendue pendant la dictée (voir winboard.log).";
        }
    }

    private static string Format(TimeSpan? t)
    {
        if (t is not { } d || d <= TimeSpan.Zero) return "quelques instants";
        if (d.TotalMinutes >= 90) return $"{(int)Math.Ceiling(d.TotalHours)} h";
        if (d.TotalSeconds >= 90) return $"{(int)Math.Ceiling(d.TotalMinutes)} min";
        return $"{(int)Math.Ceiling(d.TotalSeconds)} s";
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    public void Dispose()
    {
        _cts.Cancel();
        _recorder.Dispose();
    }
}
