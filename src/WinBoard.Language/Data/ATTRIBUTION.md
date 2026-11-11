# Sources des données linguistiques

## Listes de fréquences (fr.txt, en.txt)

- Source : projet FrequencyWords de Hermit Dave, listes « 2018 » `fr_50k.txt` et `en_50k.txt`
  (https://github.com/hermitdave/FrequencyWords), construites à partir du corpus de sous-titres OpenSubtitles.
- Licence : Creative Commons Attribution-ShareAlike 4.0 (CC BY-SA 4.0)
  (https://creativecommons.org/licenses/by-sa/4.0/).
- Modifications : mise en minuscules, normalisation de l'apostrophe, suppression des entrées
  contenant des chiffres ou d'autres symboles, des fragments parasites et des lettres isolées
  sans signification, conservation d'environ 50 000 mots. Les fichiers dérivés restent sous CC BY-SA 4.0.

## Bigrammes anglais (en.bigrams.txt)

- Source : « count_2w.txt » de Peter Norvig (https://norvig.com/ngrams/), issu du corpus de
  mots de Google Web Trillion Word Corpus, diffusé sous licence MIT
  (https://norvig.com/ngrams/ngrams.zip, voir la page « Natural Language Corpus Data »).
- Modifications : minuscules, retrait des paires dont un mot est absent du lexique anglais,
  conservation des 60 000 paires les plus fréquentes.

## Bigrammes français (fr.bigrams.txt)

- Fichier amorce rédigé pour le projet (expressions et enchaînements courants). Les valeurs
  sont des poids relatifs, sans prétention statistique. Même licence que le projet.
