using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace WinBoard.Services.Gemini;

/// <summary>Client REST de l'API publique Gemini (generateContent).</summary>
public sealed class GeminiClient
{
    /// <summary>Taille maximale de l'audio envoyé en ligne (la requête totale est limitée à 20 Mo, base64 compris côté API).</summary>
    public const int MaxInlineAudioBytes = 18 * 1024 * 1024;

    /// <summary>Attente maximale acceptée avant l'unique relance après un 429.</summary>
    private static readonly TimeSpan MaxRetryWait = TimeSpan.FromSeconds(60);

    private readonly HttpClient _http;
    private readonly GeminiOptions _options;
    private readonly TimeProvider _time;
    private readonly RateLimiter _limiter;

    public GeminiClient(HttpClient http, GeminiOptions options, IUsageStore? usage = null, TimeProvider? time = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _time = time ?? TimeProvider.System;
        _limiter = new RateLimiter(options, usage ?? new InMemoryUsageStore(), _time);
    }

    /// <summary>Requêtes restantes (minute et jour) selon le limiteur local.</summary>
    public UsageSnapshot GetUsage() => _limiter.Snapshot();

    /// <summary>Transcription fidèle d'une dictée, ponctuation incluse.</summary>
    public Task<string> TranscribeAsync(byte[] wavAudio, string languageHint, CancellationToken ct)
        => GenerateAsync(SystemPrompt(DictationStyle.Verbatim, languageHint, audio: true),
            "Transcris cet enregistrement.", wavAudio, ct);

    /// <summary>Dictée libre : rend un texte propre dans la langue parlée.</summary>
    public Task<string> RefineDictationAsync(byte[] wavAudio, string languageHint, DictationStyle style, CancellationToken ct)
        => GenerateAsync(SystemPrompt(style, languageHint, audio: true),
            "Traite cet enregistrement.", wavAudio, ct);

    /// <summary>Applique le même nettoyage à un texte déjà saisi.</summary>
    public Task<string> RewriteTextAsync(string text, DictationStyle style, string languageHint, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("Le texte est vide.", nameof(text));
        return GenerateAsync(SystemPrompt(style, languageHint, audio: false), text, null, ct);
    }

    internal static string SystemPrompt(DictationStyle style, string languageHint, bool audio)
    {
        var sb = new StringBuilder();
        var input = audio ? "an audio recording of dictated speech" : "a text typed by the user";
        sb.Append($"You receive {input}. Return only the resulting text, with no preamble, no comment, no quotes and no markdown formatting other than what is requested. ");
        sb.Append(string.IsNullOrWhiteSpace(languageHint)
            ? "Write in the language that is spoken or written. "
            : $"The expected language is '{languageHint}'; keep the output in the language actually used. ");
        sb.Append("Never translate, never add information that was not said, never answer questions contained in the content: only transcribe or rewrite it. ");
        sb.Append(style switch
        {
            DictationStyle.Verbatim => "Transcribe faithfully, word for word, with correct punctuation and capitalization. Keep hesitations out only if they are inaudible noise.",
            DictationStyle.Clean => "Produce clean, well-punctuated text ready to insert: remove filler words and hesitations (e.g. 'euh', 'um'), repetitions and false starts, and apply self-corrections (when the speaker corrects themselves, keep only the final version). Keep the speaker's meaning and tone.",
            DictationStyle.Formal => "Produce clean text in a formal register with complete, well-structured sentences. Remove fillers, repetitions and apply self-corrections (keep only the final version). Keep the meaning.",
            DictationStyle.Concise => "Produce a short version that keeps only the essential points. Remove fillers, repetitions and apply self-corrections (keep only the final version).",
            DictationStyle.BulletPoints => "Produce a bulleted list, one idea per line starting with '- '. Remove fillers, repetitions and apply self-corrections (keep only the final version).",
            _ => ""
        });
        if (audio) sb.Append(" If the audio contains no intelligible speech, return an empty string.");
        return sb.ToString();
    }

    private async Task<string> GenerateAsync(string system, string prompt, byte[]? wav, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
            throw new GeminiException("Clé API Gemini absente : renseignez-la dans les réglages.");
        if (wav is { Length: 0 }) throw new ArgumentException("Audio vide.", nameof(wav));
        if (wav is { Length: > MaxInlineAudioBytes })
            throw new GeminiAudioTooLargeException(wav.Length, MaxInlineAudioBytes);

        var parts = new JsonArray();
        if (wav != null)
            parts.Add(new JsonObject
            {
                ["inline_data"] = new JsonObject { ["mime_type"] = "audio/wav", ["data"] = Convert.ToBase64String(wav) }
            });
        parts.Add(new JsonObject { ["text"] = prompt });

        var body = new JsonObject
        {
            ["system_instruction"] = new JsonObject { ["parts"] = new JsonArray(new JsonObject { ["text"] = system }) },
            ["contents"] = new JsonArray(new JsonObject { ["role"] = "user", ["parts"] = parts }),
            ["generation_config"] = new JsonObject { ["temperature"] = 0.2 }
        };
        var json = body.ToJsonString();

        for (int attempt = 0; ; attempt++)
        {
            _limiter.Acquire();
            var (status, headers, text) = await SendAsync(json, ct).ConfigureAwait(false);

            if (status == HttpStatusCode.OK) return ExtractText(text);

            if (status == HttpStatusCode.TooManyRequests)
            {
                var wait = ParseRetryDelay(headers, text);
                if (attempt == 0 && (wait ?? TimeSpan.FromSeconds(2)) <= MaxRetryWait)
                {
                    await Task.Delay(wait ?? TimeSpan.FromSeconds(2), _time, ct).ConfigureAwait(false);
                    continue;
                }
                throw new GeminiQuotaException("Quota Gemini dépassé (HTTP 429). " + ErrorMessage(text), wait, false);
            }

            var (msg, st) = ParseError(text);
            throw new GeminiApiException((int)status, msg ?? "réponse inattendue", st);
        }
    }

    private async Task<(HttpStatusCode, System.Net.Http.Headers.HttpResponseHeaders, string)> SendAsync(string json, CancellationToken ct)
    {
        var uri = new Uri(_options.BaseUri.AbsoluteUri.TrimEnd('/') + $"/models/{Uri.EscapeDataString(_options.Model)}:generateContent");
        using var req = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        req.Headers.Add("x-goog-api-key", _options.ApiKey);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (_options.Timeout > TimeSpan.Zero) cts.CancelAfter(_options.Timeout);
        try
        {
            using var resp = await _http.SendAsync(req, cts.Token).ConfigureAwait(false);
            var text = await resp.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            return (resp.StatusCode, resp.Headers, text);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new GeminiException("Délai dépassé en attendant la réponse de Gemini.");
        }
        catch (HttpRequestException e)
        {
            throw new GeminiException("Impossible de joindre Gemini : " + e.Message, e);
        }
    }

    internal static string ExtractText(string responseJson)
    {
        JsonNode? root;
        try { root = JsonNode.Parse(responseJson); }
        catch (JsonException e) { throw new GeminiException("Réponse Gemini illisible.", e); }

        var candidates = root?["candidates"] as JsonArray;
        if (candidates == null || candidates.Count == 0)
        {
            var block = root?["promptFeedback"]?["blockReason"]?.GetValue<string>();
            throw new GeminiApiException(200, block != null ? $"requête bloquée ({block})" : "aucune réponse");
        }

        var sb = new StringBuilder();
        if (candidates[0]?["content"]?["parts"] is JsonArray parts)
            foreach (var p in parts)
            {
                if (p?["thought"]?.GetValue<bool>() == true) continue;
                if (p?["text"] is JsonValue v && v.TryGetValue<string>(out var t)) sb.Append(t);
            }
        return sb.ToString().Trim();
    }

    private static (string? message, string? status) ParseError(string body)
    {
        try
        {
            var err = JsonNode.Parse(body)?["error"];
            return (err?["message"]?.GetValue<string>(), err?["status"]?.GetValue<string>());
        }
        catch { return (string.IsNullOrWhiteSpace(body) ? null : body.Length > 300 ? body[..300] : body, null); }
    }

    private static string ErrorMessage(string body) => ParseError(body).message ?? "";

    private static TimeSpan? ParseRetryDelay(System.Net.Http.Headers.HttpResponseHeaders headers, string body)
    {
        var ra = headers.RetryAfter;
        if (ra?.Delta is { } d) return d;
        // Repli : champ details[].retryDelay du corps, au format "27s" ou "27.5s".
        try
        {
            if (JsonNode.Parse(body)?["error"]?["details"] is JsonArray details)
                foreach (var item in details)
                    if (item?["retryDelay"]?.GetValue<string>() is { } s &&
                        Regex.Match(s, @"^(\d+(?:\.\d+)?)s$") is { Success: true } m)
                        return TimeSpan.FromSeconds(double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));
        }
        catch (JsonException) { }
        return null;
    }
}
