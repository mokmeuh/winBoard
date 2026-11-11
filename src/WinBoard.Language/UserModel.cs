using System.Text.Json;

namespace WinBoard.Language;

/// <summary>
/// Modèle appris localement : fréquence des mots tapés, enchaînements de mots et mots ajoutés à la main.
/// Toutes les méthodes sont thread-safe.
/// </summary>
public sealed class UserModel
{
    private const int MaxBigramPairs = 60000;
    private const int PersonalWordWeight = 5;

    private readonly object _gate = new();
    private readonly Dictionary<string, int> _unigrams = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, int>> _bigrams = new(StringComparer.Ordinal);
    private readonly HashSet<string> _personal = new(StringComparer.Ordinal);
    private long _unigramTotal;
    private int _pairCount;

    /// <summary>Nombre d'occurrences à partir duquel un mot appris est considéré comme connu (1 par défaut).</summary>
    public int MinCountToKnow { get; set; } = 1;

    /// <summary>Somme des occurrences de mots appris.</summary>
    public long TotalWords { get { lock (_gate) return _unigramTotal; } }

    /// <summary>Nombre de mots distincts connus (appris ou ajoutés).</summary>
    public int VocabularySize { get { lock (_gate) return _unigrams.Count + _personal.Count(w => !_unigrams.ContainsKey(w)); } }

    /// <summary>Apprend les mots et les enchaînements d'un texte saisi. Une fin de phrase interrompt les bigrammes.</summary>
    public void Learn(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        var sentences = text.Split(new[] { '.', '!', '?', '…', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        lock (_gate)
        {
            foreach (var s in sentences)
            {
                string? prev = null;
                foreach (var w in TextUtil.Tokenize(s))
                {
                    AddUnigram(w, 1);
                    if (prev != null) AddBigram(prev, w, 1);
                    prev = w;
                }
            }
        }
    }

    /// <summary>Ajoute un mot personnel (nom propre, jargon...) : il ne sera plus corrigé et sera proposé en complétion.</summary>
    public void AddWord(string word)
    {
        var w = TextUtil.Normalize(word.Trim());
        if (!TextUtil.IsWordLike(w)) return;
        lock (_gate)
        {
            _personal.Add(w);
            if (!_unigrams.TryGetValue(w, out var c) || c < PersonalWordWeight)
            {
                _unigramTotal += PersonalWordWeight - (c);
                _unigrams[w] = PersonalWordWeight;
            }
        }
    }

    /// <summary>Oublie un mot (apprentissage ou ajout manuel) ainsi que les enchaînements qui le contiennent.</summary>
    public void Forget(string word)
    {
        var w = TextUtil.Normalize(word.Trim());
        lock (_gate)
        {
            if (_unigrams.Remove(w, out var c)) _unigramTotal -= c;
            _personal.Remove(w);
            _bigrams.Remove(w);
            foreach (var d in _bigrams.Values) d.Remove(w);
            RecountPairs();
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _unigrams.Clear(); _bigrams.Clear(); _personal.Clear();
            _unigramTotal = 0; _pairCount = 0;
        }
    }

    /// <summary>Vrai si le mot est connu de l'utilisateur (assez d'occurrences ou ajouté).</summary>
    public bool IsKnown(string word)
    {
        var w = TextUtil.Normalize(word);
        lock (_gate)
            return _personal.Contains(w) || (_unigrams.TryGetValue(w, out var c) && c >= MinCountToKnow);
    }

    public int GetCount(string word)
    {
        lock (_gate) return _unigrams.TryGetValue(TextUtil.Normalize(word), out var c) ? c : 0;
    }

    public int GetBigramCount(string previous, string word)
    {
        lock (_gate)
            return _bigrams.TryGetValue(previous, out var d) && d.TryGetValue(word, out var c) ? c : 0;
    }

    /// <summary>Somme des comptes des suivants de <paramref name="previous"/>.</summary>
    public long GetBigramTotal(string previous)
    {
        lock (_gate)
            return _bigrams.TryGetValue(previous, out var d) ? d.Values.Sum(v => (long)v) : 0;
    }

    /// <summary>Suivants appris de <paramref name="previous"/>, du plus fréquent au moins fréquent.</summary>
    public IReadOnlyList<(string Word, int Count)> GetNext(string previous)
    {
        lock (_gate)
            return _bigrams.TryGetValue(previous, out var d)
                ? d.OrderByDescending(kv => kv.Value).Select(kv => (kv.Key, kv.Value)).ToArray()
                : Array.Empty<(string, int)>();
    }

    /// <summary>Mots connus commençant par le préfixe (déjà normalisé), du plus fréquent au moins fréquent.</summary>
    public IReadOnlyList<(string Word, int Count)> WordsWithPrefix(string prefix, int max)
    {
        lock (_gate)
            return _unigrams.Where(kv => kv.Value >= MinCountToKnow || _personal.Contains(kv.Key))
                .Where(kv => kv.Key.StartsWith(prefix, StringComparison.Ordinal))
                .OrderByDescending(kv => kv.Value).Take(max)
                .Select(kv => (kv.Key, kv.Value)).ToArray();
    }

    /// <summary>Copie des mots connus (utilisée par la correction orthographique).</summary>
    public IReadOnlyList<string> KnownWords()
    {
        lock (_gate)
            return _unigrams.Where(kv => kv.Value >= MinCountToKnow || _personal.Contains(kv.Key))
                .Select(kv => kv.Key).ToArray();
    }

    // Les méthodes suivantes supposent que le verrou est déjà pris.
    private void AddUnigram(string w, int n)
    {
        _unigrams[w] = _unigrams.TryGetValue(w, out var c) ? c + n : n;
        _unigramTotal += n;
    }

    private void AddBigram(string a, string b, int n)
    {
        if (!_bigrams.TryGetValue(a, out var d)) _bigrams[a] = d = new Dictionary<string, int>(StringComparer.Ordinal);
        if (d.TryGetValue(b, out var c)) d[b] = c + n;
        else
        {
            d[b] = n;
            if (++_pairCount > MaxBigramPairs) Prune();
        }
    }

    // Supprime les paires vues une seule fois quand le modèle devient trop gros.
    private void Prune()
    {
        foreach (var key in _bigrams.Keys.ToArray())
        {
            var d = _bigrams[key];
            foreach (var k in d.Where(kv => kv.Value <= 1).Select(kv => kv.Key).ToArray()) d.Remove(k);
            if (d.Count == 0) _bigrams.Remove(key);
        }
        RecountPairs();
    }

    private void RecountPairs() => _pairCount = _bigrams.Values.Sum(d => d.Count);

    private sealed class Dto
    {
        public int Version { get; set; } = 1;
        public Dictionary<string, int> Unigrams { get; set; } = new();
        public Dictionary<string, Dictionary<string, int>> Bigrams { get; set; } = new();
        public List<string> Words { get; set; } = new();
    }

    /// <summary>Enregistre le modèle en JSON (écriture atomique via un fichier temporaire).</summary>
    public void Save(string path)
    {
        Dto dto;
        lock (_gate)
        {
            dto = new Dto
            {
                Unigrams = new Dictionary<string, int>(_unigrams),
                Bigrams = _bigrams.ToDictionary(kv => kv.Key, kv => new Dictionary<string, int>(kv.Value)),
                Words = _personal.OrderBy(w => w, StringComparer.Ordinal).ToList(),
            };
        }
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(dto));
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>
    /// Remplace le contenu du modèle par celui du fichier. Un fichier absent laisse le modèle inchangé ;
    /// un fichier illisible lève une exception sans modifier le modèle.
    /// </summary>
    public void Load(string path)
    {
        if (!File.Exists(path)) return;
        var dto = JsonSerializer.Deserialize<Dto>(File.ReadAllText(path))
                  ?? throw new InvalidDataException("Modèle utilisateur vide ou invalide.");
        lock (_gate)
        {
            _unigrams.Clear(); _bigrams.Clear(); _personal.Clear();
            _unigramTotal = 0;
            foreach (var (w, c) in dto.Unigrams) if (c > 0) AddUnigram(w, c);
            foreach (var (a, d) in dto.Bigrams)
                foreach (var (b, c) in d) if (c > 0) AddBigram(a, b, c);
            foreach (var w in dto.Words) _personal.Add(w);
            RecountPairs();
        }
    }

    /// <summary>Crée un modèle et le charge depuis <paramref name="path"/> s'il existe.</summary>
    public static UserModel FromFile(string path)
    {
        var m = new UserModel();
        m.Load(path);
        return m;
    }
}
