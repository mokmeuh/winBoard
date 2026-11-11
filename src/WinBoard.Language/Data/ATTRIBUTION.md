# Sources des données linguistiques

## Listes de fréquences (fr.txt, en.txt)

- Source : projet FrequencyWords de Hermit Dave, listes « 2018 » `fr_50k.txt` et `en_50k.txt`
  (https://github.com/hermitdave/FrequencyWords), construites à partir du corpus de sous-titres OpenSubtitles.
- Licence : Creative Commons Attribution-ShareAlike 4.0 (CC BY-SA 4.0)
  (https://creativecommons.org/licenses/by-sa/4.0/).
- Modifications : mise en minuscules, normalisation de l'apostrophe, suppression des entrées
  contenant des chiffres ou d'autres symboles, des fragments parasites et des lettres isolées
  sans signification, conservation d'environ 50 000 mots. Une liste restreinte de formes élidées courantes (j'ai, c'est, l'homme...)
  a été ajoutée au fichier français, avec des fréquences estimées. Les fichiers dérivés restent sous CC BY-SA 4.0.

## Bigrammes anglais (en.bigrams.txt)

- Source : « count_2w.txt » de Peter Norvig (https://norvig.com/ngrams/), données dérivées du
  corpus web de Google (« Web 1T 5-gram »). Le code de l'auteur est sous licence MIT ; la licence
  de ce fichier de données n'est pas précisée explicitement sur la page et reste à confirmer
  avant toute redistribution publique.
- Modifications : minuscules, retrait des paires dont un mot est absent du lexique anglais,
  conservation des 60 000 paires les plus fréquentes.

## Bigrammes français (fr.bigrams.txt)

- Fichier amorce rédigé pour le projet (expressions et enchaînements courants). Les valeurs
  sont des poids relatifs, sans prétention statistique. Même licence que le projet.
