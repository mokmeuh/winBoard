using System.IO;
using WinBoard.Core;
using WinBoard.Input;
using WinBoard.Language;
using WinBoard.Services.Settings;

namespace WinBoard.App.Services;

/// <summary>
/// Branche <see cref="TextEngine"/> et le modèle utilisateur sur le contrôleur de saisie.
/// Le moteur se charge en tâche de fond ; tant qu'il n'est pas prêt, aucune suggestion n'est renvoyée.
/// Le modèle appris est sauvegardé périodiquement et à la fermeture.
/// </summary>
public sealed class TextEngineAdapter : ISuggestionEngine, IDisposable
{
    private static readonly TimeSpan SaveInterval = TimeSpan.FromMinutes(2);

    private readonly UserModel _user = new();
    private readonly string _modelPath;
    private readonly System.Threading.Timer _saveTimer;
    private volatile TextEngine? _engine;
    private volatile bool _dirty;
    private bool _disposed;

    public TextEngineAdapter(string language, KeyboardLayout layout)
    {
        Language = language;
        _modelPath = Path.Combine(AppPaths.Root, $"user-model-{language}.json");
        Ready = Task.Run(() => Initialize(language, layout));
        _saveTimer = new System.Threading.Timer(_ => SaveIfDirty(), null, SaveInterval, SaveInterval);
    }

    public string Language { get; }

    /// <summary>Terminée quand le moteur est utilisable (jamais en échec : les erreurs sont journalisées).</summary>
    public Task Ready { get; }

    /// <summary>Dictionnaire de la langue, ou null tant que le moteur n'est pas chargé.</summary>
    public ILexicon? Lexicon => _engine?.Lexicon;

    private void Initialize(string language, KeyboardLayout layout)
    {
        try
        {
            try { _user.Load(_modelPath); }
            catch (Exception e) { Log.Error("Modèle utilisateur illisible, démarrage à vide.", e); }
            var engine = TextEngine.Create(language, layout, _user);
            engine.Corrector.Warmup();
            _engine = engine;
        }
        catch (Exception e)
        {
            Log.Error("Chargement du moteur de texte.", e);
        }
    }

    public IReadOnlyList<string> GetSuggestions(string textBeforeCursor, int max)
    {
        var engine = _engine;
        if (engine == null) return Array.Empty<string>();
        try { return engine.GetSuggestions(textBeforeCursor, max); }
        catch (Exception e) { Log.Error("GetSuggestions", e); return Array.Empty<string>(); }
    }

    public string? GetAutoCorrection(string word)
    {
        var engine = _engine;
        if (engine == null) return null;
        try { return engine.GetAutoCorrection(word); }
        catch (Exception e) { Log.Error("GetAutoCorrection", e); return null; }
    }

    public void Learn(string text)
    {
        var engine = _engine;
        if (engine == null || string.IsNullOrWhiteSpace(text)) return;
        try
        {
            engine.Learn(text);
            _dirty = true;
        }
        catch (Exception e) { Log.Error("Learn", e); }
    }

    private void SaveIfDirty()
    {
        if (!_dirty || _engine == null) return;
        try
        {
            _dirty = false;
            _user.Save(_modelPath);
        }
        catch (Exception e)
        {
            _dirty = true;
            Log.Error("Sauvegarde du modèle utilisateur.", e);
        }
    }

    /// <summary>Sauvegarde finale ; à appeler à la fermeture ou au changement de langue.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _saveTimer.Dispose();
        // On attend brièvement le chargement pour ne pas écraser le fichier avec un modèle vide.
        try { Ready.Wait(TimeSpan.FromSeconds(3)); } catch { }
        SaveIfDirty();
    }
}
