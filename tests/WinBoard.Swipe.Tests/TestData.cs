using WinBoard.Core;

namespace WinBoard.Swipe.Tests;

internal static class TestData
{
    // Mots classés approximativement par fréquence décroissante ; fréquence de type Zipf.
    private const string Fr =
        "le de un et à il avoir ne je son que se qui ce dans en du elle au pour pas que vous par sur faire plus dire me on mon lui nous comme mais pouvoir avec tout y aller voir en bien où sans tu ou leur homme si deux mari moi vouloir te femme venir quand grand celui si notre devoir là jour prendre même votre rien petit encore aussi quelque dont tout mer bonjour merci salut oui non bonsoir bonne nuit été français française france maison monde temps vie fois chose main enfant ville pays travail école année jamais toujours souvent ensemble beaucoup peu trop très assez presque alors donc puis ainsi après avant pendant depuis contre chez entre vers sous autre autres chaque plusieurs rouge bleu vert noir blanc table chaise porte fenêtre livre stylo ordinateur clavier téléphone voiture train avion route rue parc jardin fleur arbre soleil lune étoile pluie neige vent eau feu terre air manger boire dormir marcher courir parler écouter regarder lire écrire jouer chanter danser aimer détester penser savoir connaître comprendre apprendre oublier belle beau nouveau vieux jeune long court haut bas chaud froid bon mauvais meilleur pire facile difficile important possible nécessaire";
    private const string En =
        "the of and to in is you that it he was for on are as with his they at be this have from or one had by word but not what all were we when your can said there use an each which she do how their if will up other about out many then them these so some her would make like him into time has look two more write go see number no way could people my than first water been call who oil its now find long down day did get come made may part hello world keyboard good morning night thanks thank please yes sorry welcome friend family house home school work city country money life love hand eye head heart door window book pen computer phone car train plane road street park garden flower tree sun moon star rain snow wind fire earth air eat drink sleep walk run talk listen read play sing dance think know understand learn forget beautiful new old young short high low hot cold bad better worse easy difficult important possible necessary belle bell well tell hell hall hill help held hold hollow yellow fellow follow hallo hero here hear heal heel heed hole hole";

    public static InMemoryLexicon Build()
    {
        var entries = new List<WordEntry>();
        AddRanked(entries, Fr, 2_000_000);
        AddRanked(entries, En, 3_000_000);
        // Évite les doublons de casse/mots répétés : InMemoryLexicon additionne les fréquences.
        return new InMemoryLexicon("fr", entries);
    }

    private static void AddRanked(List<WordEntry> list, string text, long top)
    {
        var seen = new HashSet<string>();
        int rank = 1;
        foreach (var w in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!seen.Add(w)) continue;
            list.Add(new WordEntry(w, Math.Max(5, top / rank)));
            rank++;
        }
    }

    /// <summary>Bruit déterministe : décalage gaussien tronqué à ±amp appliqué à chaque touche, interpolé entre les touches.</summary>
    public static IReadOnlyList<PointD> NoisyPath(KeyboardLayout layout, string word, int seed, double amp = 0.25)
    {
        var ideal = SwipePathBuilder.IdealPath(layout, word, 8);
        var rnd = new Random(seed);
        double Gauss() // Box-Muller
        {
            double u1 = 1 - rnd.NextDouble(), u2 = rnd.NextDouble();
            return Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
        }
        double Clip(double v) => Math.Clamp(v, -amp, amp);

        // Décalage par point de passage (touche), interpolé linéairement : un doigt trace une courbe lisse.
        int segs = (ideal.Count - 1) / 8;
        var offs = new (double, double)[segs + 1];
        for (int i = 0; i <= segs; i++) offs[i] = (Clip(Gauss() * amp * 0.6), Clip(Gauss() * amp * 0.6));
        var res = new List<PointD>(ideal.Count);
        for (int i = 0; i < ideal.Count; i++)
        {
            int s = Math.Min(i / 8, segs - 1);
            double f = (i - s * 8) / 8.0;
            var (ax, ay) = offs[s];
            var (bx, by) = offs[s + 1];
            res.Add(new PointD(ideal[i].X + ax + (bx - ax) * f + Clip(Gauss() * 0.03),
                               ideal[i].Y + ay + (by - ay) * f + Clip(Gauss() * 0.03)));
        }
        return res;
    }
}
