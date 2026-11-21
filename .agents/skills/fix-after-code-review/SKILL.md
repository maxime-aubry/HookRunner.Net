---
name: fix-after-code-review
description: Corriger une implémentation après une revue de code, ou lancer une boucle de revue et de correction après implémentation, avec le skill code-review de Matt Pocock et trois passes de correction maximum.
---

# Fix after code review

Obtenir une implémentation vérifiée sur les axes `Standards` et `Spec`, en corrigeant les écarts confirmés. Utiliser le skill `mattpocock-skills:code-review` installé via le plugin Matt Pocock pour chaque revue.

## 1. Cadrer la revue

- Lire les instructions applicables du dépôt et les documents qu'elles désignent pour le périmètre concerné.
- Relever la branche courante avec `git branch --show-current` et l'état initial avec `git status --short --untracked-files=all`. Si HEAD est détachée, le signaler. Préserver le travail préexistant et limiter les éditions aux corrections nécessaires.
- Retrouver le point de référence Git et le ticket ou la spécification d'origine dans la demande et le contexte. Demander les éléments manquants nécessaires. Pour un ticket, suivre le workflow du tracker indiqué par le dépôt.
- Justifier la référence par rapport au ticket ou à la spécification ; si elle ne peut pas être déterminée de manière fiable, demander une clarification avant la revue. Vérifier la référence avec `git rev-parse --verify <référence>^{commit}` et figer le SHA de base de comparaison avant la première revue. Si la revue utilise la merge-base, calculer ce SHA une fois avec `git merge-base <référence> HEAD`. Conserver cette base et les mêmes exigences pendant toute la boucle.
- Charger le skill `code-review`. S'il est indisponible ou échoue, arrêter et décrire le blocage.

**Inventaire Git.** Examiner séparément les quatre catégories, même si une comparaison est vide :

| Catégorie | Commandes |
| --- | --- |
| Commits depuis la base figée | `git log --oneline <SHA-base>..HEAD` et `git diff --name-status <SHA-base> HEAD` |
| Modifications non indexées | `git diff --name-status` et `git diff` |
| Modifications indexées | `git diff --cached --name-status` et `git diff --cached` |
| Fichiers non suivis | `git ls-files --others --exclude-standard`, puis lecture de leur contenu |

Constituer la liste des fichiers à examiner par l'union de ces inventaires, en dédoublonnant les chemins et en tenant compte des suppressions et renommages. Un échec de commande laisse l'inventaire incomplet. Une comparaison globale vide ne suffit pas : des changements committés et locaux peuvent se compenser. Consigner les exclusions éventuelles avec leur justification.

**Périmètre de revue.** Fournir explicitement au skill `code-review` une consigne remplaçant son éventuel `git diff <référence>...HEAD` : examiner l'état courant par rapport à la base figée avec `git diff <SHA-base>`, les contenus non suivis et l'inventaire complet ci-dessus. Consulter aussi `git diff <SHA-base> HEAD` pour le contexte committé. Transmettre la même base, les commandes, la liste des commits et les fichiers aux deux axes de revue. Conserver leur méthode ; seule la comparaison Git est adaptée. Actualiser l'inventaire à chaque revue pour inclure les corrections et les nouveaux fichiers, sans réduire le périmètre aux seules dernières corrections.

Le cadrage est terminé quand la branche, la base justifiée, les exigences, les standards applicables et les quatre catégories de changements sont identifiés. Conclure à un périmètre vide uniquement après vérification réussie de ces quatre catégories ; dans ce cas, le signaler et terminer sans prétendre avoir réalisé une revue. Si une source nécessaire manque, signaler la couverture incomplète et demander cette source avant de conclure à la conformité.

## 2. Obtenir et qualifier le rapport

Invoquer explicitement `mattpocock-skills:code-review` avec la base figée, le périmètre courant, la spécification et les standards du dépôt. Récupérer et examiner les rapports complets `Standards` et `Spec`. Un rapport déjà fourni peut servir d'entrée si son périmètre et ses sources correspondent à l'état courant ; il ne remplace pas cette invocation. Une analyse improvisée ne remplace pas le skill : s'il échoue ou si un rapport manque, signaler la vérification incomplète.

Pour chaque constat, conserver son axe, les fichiers ou comportements concernés, sa source (règle, exigence ou heuristique) et les preuves citées. Vérifier sa pertinence par rapport au ticket et aux standards applicables, puis lui attribuer un statut justifié :

- **Confirmé** : l'écart est étayé par les exigences ou les standards applicables.
- **Facultatif** : recommandation pertinente qui ne constitue pas une violation démontrée. Vérifier son intérêt et sa proportionnalité au ticket avant de choisir de l'appliquer ; consigner ce choix. Les code smells heuristiques du reviewer restent des jugements, pas des violations automatiques.
- **Rejeté** : constat non étayé, inapplicable ou contredit par une source faisant autorité ; expliquer explicitement le motif et les éléments qui le justifient.

Si la qualification ou la correction nécessite une décision métier ou architecturale absente du contexte, signaler **Décision requise**, avec le point à trancher. Garder le constat ouvert jusqu'à cette décision plutôt que le traiter comme facultatif ou rejeté.

La qualification est terminée quand chaque constat a un statut et une justification. Conserver les exigences d'origine comme référence : une correction porte sur l'implémentation, pas sur une réécriture du ticket destinée à supprimer l'écart.

## 3. Corriger et vérifier

Une **passe** comprend les corrections des écarts confirmés, les contrôles adaptés et une nouvelle revue. Autoriser au maximum **trois passes**, après la revue initiale : cela représente au plus quatre revues, en comptant la vérification finale.

Pour chaque passe :

1. Corriger les écarts confirmés avec le changement minimal cohérent avec les conventions et l'architecture documentées du dépôt.
2. Ajouter ou adapter les tests des comportements concernés. Utiliser le skill `mattpocock-skills:tdd` lorsqu'une régression reproductible ou un changement de comportement justifie une approche test-first.
3. Après chaque correction, exécuter les tests et contrôles adaptés. Conserver les assertions utiles et les contrôles qualité actifs ; traiter leurs échecs au lieu de les affaiblir.
4. Actualiser l'inventaire Git et invoquer de nouveau `mattpocock-skills:code-review` sur la même base et le périmètre complet actualisé, corrections locales comprises. Récupérer et examiner les deux rapports `Spec` et `Standards`. Cette revue clôt la passe, y compris la troisième.
5. Qualifier chaque nouveau constat et comparer les résultats au rapport précédent : écarts résolus, persistants et nouvellement introduits. Un écart est résolu lorsque la vérification du comportement ou de la règle et la nouvelle revue le confirment. Répéter la passe si des écarts confirmés subsistent, dans la limite fixée.

Si la revue initiale ne présente aucun écart confirmé et couvre les deux axes, terminer sans passe de correction.

## 4. Terminer sur un état vérifiable

La boucle réussit lorsque la dernière revue couvre tout le périmètre sur les deux axes, qu'aucun écart confirmé ni décision requise ne reste ouvert et que les contrôles nécessaires réussissent. Les recommandations facultatives peuvent rester signalées. Fonder toute annonce d'absence d'écart sur l'inventaire complet et les deux rapports examinés ; un axe omis ou une source manquante laisse la couverture incomplète.

Arrêter la boucle et rendre compte de l'état réel si :

- les trois passes sont épuisées avec des écarts confirmés restants ;
- une correction nécessite une décision utilisateur encore absente : poser la question avec le constat et les options utiles ;
- les tests échouent sans solution identifiée après investigation ciblée ;
- une passe ne résout aucun écart confirmé, sans progrès vérifiable sur les problèmes restants ;
- une revue échoue, omet une partie nécessaire du périmètre ou ne permet pas de vérifier les corrections.

Distinguer une réussite, une limite de passes atteinte et une vérification bloquée ou incomplète.

## Rapport final

Fournir :

- la branche courante, la référence retenue, le SHA de base figé et la justification du choix ;
- les commandes d'inventaire et de comparaison réellement exécutées, ainsi que les exclusions justifiées ;
- le nombre total de fichiers distincts examinés et le décompte par catégorie Git, notamment les fichiers indexés et non suivis ; préciser que les catégories peuvent se recouper ;
- les écarts confirmés initiaux par axe, les corrections et fichiers concernés, ainsi que les écarts restants ;
- les constats rejetés avec leur justification et les recommandations facultatives non appliquées avec le motif du choix ;
- les commandes de tests et de validation exécutées avec leurs résultats, ainsi que les contrôles nécessaires non exécutés et leur motif ;
- le nombre de passes et de revues, puis les résultats finaux distincts de `Spec` et `Standards`, avec les éventuelles limites de couverture et décisions attendues.

## Limites Git

Travailler dans la branche et le répertoire de travail actuels. Ne créer aucun commit, ne pousser aucune modification et ne créer, changer, renommer ou supprimer aucune branche. Préserver les modifications préexistantes ; rendre compte uniquement des revues et contrôles réellement effectués.
