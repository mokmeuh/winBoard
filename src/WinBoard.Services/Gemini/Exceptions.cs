namespace WinBoard.Services.Gemini;

/// <summary>Erreur de base du client Gemini.</summary>
public class GeminiException : Exception
{
    public GeminiException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>Erreur renvoyée par l'API (code HTTP et message).</summary>
public sealed class GeminiApiException : GeminiException
{
    public int StatusCode { get; }
    public string? Status { get; }

    public GeminiApiException(int statusCode, string message, string? status = null)
        : base($"Erreur Gemini {statusCode}: {message}")
    {
        StatusCode = statusCode;
        Status = status;
    }
}

/// <summary>Quota atteint, localement ou côté serveur (429 persistant).</summary>
public sealed class GeminiQuotaException : GeminiException
{
    /// <summary>Durée estimée avant de pouvoir réessayer.</summary>
    public TimeSpan? RetryAfter { get; }

    /// <summary>Vrai si la limite est celle du limiteur local (aucun appel réseau effectué).</summary>
    public bool IsLocal { get; }

    public GeminiQuotaException(string message, TimeSpan? retryAfter, bool isLocal) : base(message)
    {
        RetryAfter = retryAfter;
        IsLocal = isLocal;
    }
}

/// <summary>Audio trop volumineux pour un envoi en ligne.</summary>
public sealed class GeminiAudioTooLargeException : GeminiException
{
    public GeminiAudioTooLargeException(long size, long max)
        : base($"Audio trop volumineux ({size / 1048576.0:F1} Mo) : la limite pour un envoi en ligne est d'environ {max / 1048576.0:F0} Mo. Dictez un passage plus court.") { }
}
