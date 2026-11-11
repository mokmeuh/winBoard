namespace WinBoard.Services.Gemini;

/// <summary>Style de rendu appliqué à une dictée ou à un texte.</summary>
public enum DictationStyle
{
    /// <summary>Transcription fidèle, mot pour mot.</summary>
    Verbatim,
    /// <summary>Texte propre : hésitations et reprises supprimées, sens conservé.</summary>
    Clean,
    /// <summary>Registre soutenu, phrases complètes.</summary>
    Formal,
    /// <summary>Version courte, l'essentiel seulement.</summary>
    Concise,
    /// <summary>Liste à puces.</summary>
    BulletPoints
}

/// <summary>Paramètres du client Gemini.</summary>
public sealed class GeminiOptions
{
    /// <summary>Clé API de l'utilisateur (transmise uniquement dans l'en-tête x-goog-api-key).</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>Modèle utilisé (configurable).</summary>
    public string Model { get; set; } = "gemini-2.5-flash";

    /// <summary>Adresse de base de l'API.</summary>
    public Uri BaseUri { get; set; } = new("https://generativelanguage.googleapis.com/v1beta/");

    /// <summary>Limite locale de requêtes par minute (fenêtre glissante).</summary>
    public int RequestsPerMinute { get; set; } = 10;

    /// <summary>Limite locale de requêtes par jour.</summary>
    public int RequestsPerDay { get; set; } = 250;

    /// <summary>Délai maximal d'une requête.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(60);
}
